using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Cli.Context;
using Vion.Cli.Infrastructure;
using Vion.Cli.Rendering;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     `vion context use|list|show`, `kubectl`'s shape: named contexts in `config.json`, one current. `use` takes the
    ///     roots from the same global options every call takes, so `--tenant` means one thing whether it is stored or
    ///     passed; a custom environment is defined on the same command by its two URLs.
    /// </summary>
    internal static class ContextCommands
    {
        public static Command Create(CliTree tree, CliServices services)
        {
            var context = new Command("context", "Select, define or show the context a call runs in: an environment and the ids it fills in.");
            context.Subcommands.Add(Use(tree, services));
            context.Subcommands.Add(List(tree, services));
            context.Subcommands.Add(Show(tree, services));
            return context;
        }

        private static Command Use(CliTree tree, CliServices services)
        {
            var name = new Argument<string?>("name") { Description = "The context's name; the environment's name when left out.", Arity = ArgumentArity.ZeroOrOne };
            var apiUrl = new Option<string>("--api-url") { Description = "Define the environment named by --env: its API URL.", HelpName = "url" };
            var authUrl = new Option<string>("--auth-url") { Description = "Define the environment named by --env: its realm URL.", HelpName = "realm url" };
            var command = new Command("use",
                                      "Select a context by name, or create or update one with the environment and ids given and select it. The ids are GUIDs; `whoami` lists them.");
            command.Arguments.Add(name);
            command.Options.Add(apiUrl);
            command.Options.Add(authUrl);
            command.SetAction(result =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals,
                                                        services,
                                                        () => Task.FromResult(UseContext(result.GetValue(name),
                                                                                         result.GetValue(apiUrl),
                                                                                         result.GetValue(authUrl),
                                                                                         globals,
                                                                                         services)));
                              });
            return command;
        }

        private static int UseContext(string? name, string? apiUrl, string? authUrl, GlobalValues globals, CliServices services)
        {
            var config = ConfigStore.Load(services.HomeDirectory);
            var environments = new Dictionary<string, ApiEnvironment>(config.Environments, StringComparer.OrdinalIgnoreCase);
            var contexts = new Dictionary<string, ContextEntry>(config.Contexts, StringComparer.Ordinal);

            if (apiUrl != null || authUrl != null)
            {
                if (apiUrl == null || authUrl == null || globals.Env == null)
                {
                    throw new UsageException("an environment is defined by --env <name> --api-url <url> --auth-url <realm url>, all three together.");
                }

                if (ApiEnvironment.BuiltIn.ContainsKey(globals.Env))
                {
                    throw new UsageException($"{globals.Env} is built in and keeps its URLs; VION_API_URL and VION_AUTH_URL override them for one call.");
                }

                environments[globals.Env] = new ApiEnvironment(globals.Env, apiUrl.TrimEnd('/'), authUrl.TrimEnd('/'), ApiEnvironment.DefaultClientId);
            }

            foreach (var (key, value) in Roots(globals))
            {
                if (value != null && !Guid.TryParse(value, out _))
                {
                    throw new UsageException($"--{key} takes the {key}'s id, a GUID; '{value}' is not one. `vion whoami` lists the ids of your memberships.");
                }
            }

            var contextName = name ?? globals.Env ?? throw new UsageException("name the context, or pass --env <name> to create one named after its environment.");
            var anyValue = globals.Env != null || Roots(globals).Any(root => root.Value != null);
            if (!contexts.TryGetValue(contextName, out var existing))
            {
                if (!anyValue)
                {
                    throw new UsageException($"no context named '{contextName}'; `vion context list` shows them, and `vion context use {contextName} --env <name> …` creates it.");
                }

                if (globals.Env == null)
                {
                    throw new UsageException("a new context needs its environment: pass --env production, --env test, or a name defined with --api-url and --auth-url.");
                }
            }

            var env = globals.Env ?? existing!.Env;
            if (!ApiEnvironment.BuiltIn.ContainsKey(env) && !environments.ContainsKey(env))
            {
                throw new
                    UsageException($"unknown environment '{env}': the built-in ones are production and test; another is defined with `vion context use --env {env} --api-url <url> --auth-url <realm url>`.");
            }

            var entry = new ContextEntry(env,
                                         globals.Integrator ?? existing?.Integrator,
                                         globals.Tenant ?? existing?.Tenant,
                                         globals.Project ?? existing?.Project,
                                         globals.Platform ?? existing?.Platform);
            contexts[contextName] = entry;
            ConfigStore.Save(services.HomeDirectory, new CliConfig(contextName, contexts, environments));

            if (globals.Json)
            {
                var json = ConfigStore.ToJson(entry);
                json.Insert(0, "name", contextName);
                Renderer.WriteJson(json, services);
            }
            else
            {
                var roots = string.Join(", ", RootsOf(entry).Where(root => root.Value != null).Select(root => $"{root.Key} {root.Value}"));
                services.Out.WriteLine($"context {contextName} is current: env {entry.Env}{(roots.Length > 0 ? ", " + roots : ", no ids")}.");
            }

            return ExitCodes.Ok;
        }

        private static Command List(CliTree tree, CliServices services)
        {
            var command = new Command("list", "List the stored contexts, the current one marked.");
            command.SetAction(result =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals,
                                                        services,
                                                        () =>
                                                        {
                                                            var config = ConfigStore.Load(services.HomeDirectory);
                                                            var items = new JsonArray();
                                                            foreach (var (name, entry) in config.Contexts)
                                                            {
                                                                var row = ConfigStore.ToJson(entry);
                                                                var current = string.Equals(name, config.Current, StringComparison.Ordinal);
                                                                row.Insert(0, "current", globals.Json ? current : current ? "*" : "");
                                                                row.Insert(1, "name", name);
                                                                items.Add((JsonNode)row);
                                                            }

                                                            var document = new JsonObject { ["current"] = config.Current, ["items"] = items };
                                                            if (globals.Json)
                                                            {
                                                                Renderer.WriteJson(Fields.Project(document, globals.Fields), services);
                                                            }
                                                            else if (items.Count == 0)
                                                            {
                                                                services.Out.WriteLine("no contexts; `vion context use --env test --tenant <id>` creates one.");
                                                            }
                                                            else
                                                            {
                                                                Renderer.WriteTable(document, globals, services);
                                                            }

                                                            return Task.FromResult(ExitCodes.Ok);
                                                        });
                              });
            return command;
        }

        private static Command Show(CliTree tree, CliServices services)
        {
            var command = new Command("show", "Show the current context as a call would resolve it: the environment's URLs and the ids, overrides applied.");
            command.SetAction(result =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals,
                                                        services,
                                                        () =>
                                                        {
                                                            var config = ConfigStore.Load(services.HomeDirectory);
                                                            var variable = services.Environment.TryGetValue("VION_ENV", out var value) && !string.IsNullOrWhiteSpace(value);
                                                            if (config.CurrentContext == null && globals.Env == null && !variable)
                                                            {
                                                                if (globals.Json)
                                                                {
                                                                    Renderer.WriteJson(null, services);
                                                                }
                                                                else
                                                                {
                                                                    services.Out
                                                                            .WriteLine("no current context; `vion context use --env test --tenant <id>` creates and selects one.");
                                                                }

                                                                return Task.FromResult(ExitCodes.Ok);
                                                            }

                                                            var resolved = ContextResolver.Resolve(globals, services);
                                                            var document = new JsonObject
                                                                           {
                                                                               ["name"] = config.CurrentContext is { } current &&
                                                                                          string.Equals(current.Env,
                                                                                                        resolved.Environment.Name,
                                                                                                        StringComparison.OrdinalIgnoreCase) ? config.Current : null,
                                                                               ["env"] = resolved.Environment.Name,
                                                                               ["apiUrl"] = resolved.Environment.ApiUrl,
                                                                               ["authUrl"] = resolved.Environment.AuthUrl,
                                                                               ["clientId"] = resolved.Environment.ClientId,
                                                                           };
                                                            foreach (var key in ContextResolver.RootKeys)
                                                            {
                                                                document[key] = resolved.Root(key);
                                                            }

                                                            if (globals.Json)
                                                            {
                                                                Renderer.WriteJson(Fields.Project(document, globals.Fields), services);
                                                            }
                                                            else
                                                            {
                                                                Renderer.WriteTable(document, globals, services);
                                                            }

                                                            return Task.FromResult(ExitCodes.Ok);
                                                        });
                              });
            return command;
        }

        private static IEnumerable<KeyValuePair<string, string?>> Roots(GlobalValues globals)
        {
            yield return new KeyValuePair<string, string?>("integrator", globals.Integrator);
            yield return new KeyValuePair<string, string?>("tenant", globals.Tenant);
            yield return new KeyValuePair<string, string?>("project", globals.Project);
            yield return new KeyValuePair<string, string?>("platform", globals.Platform);
        }

        private static IEnumerable<KeyValuePair<string, string?>> RootsOf(ContextEntry entry)
        {
            yield return new KeyValuePair<string, string?>("integrator", entry.Integrator);
            yield return new KeyValuePair<string, string?>("tenant", entry.Tenant);
            yield return new KeyValuePair<string, string?>("project", entry.Project);
            yield return new KeyValuePair<string, string?>("platform", entry.Platform);
        }
    }
}