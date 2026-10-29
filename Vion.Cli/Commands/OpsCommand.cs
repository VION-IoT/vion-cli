using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Context;
using Vion.Cli.Derived;
using Vion.Cli.Infrastructure;
using Vion.Cli.Rendering;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     `vion ops`: the tree's operations from the committed document, offline — what the tool can call and what each
    ///     call does to the state. `--all` adds the `system` operations, which have no command and are reached through `api`.
    ///     `--live` reads the environment's own document, tokenless, and lists what differs from the committed one.
    /// </summary>
    internal static class OpsCommand
    {
        private static readonly string[] Methods = ["get", "put", "post", "delete", "patch"];

        public static Command Create(CliTree tree, CliServices services)
        {
            var all = new Option<bool>("--all") { Description = "Include the system operations, which have no command." };
            var live = new Option<bool>("--live") { Description = "Read the environment's document and list the operations added, removed or changed against the committed one; exit 1 on any." };
            var command = new Command("ops",
                                      "List the operations of the committed document: the command, the operationId, the method and path, the effect and the write-safety flags.");
            command.Options.Add(all);
            command.Options.Add(live);
            command.SetAction((result, cancellationToken) =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals,
                                                        services,
                                                        () => result.GetValue(live) ?
                                                                  LiveAsync(globals, services, cancellationToken) :
                                                                  Task.FromResult(Run(globals, result.GetValue(all), services)));
                              });
            return command;
        }

        private static int Run(GlobalValues globals, bool includeSystem, CliServices services)
        {
            var rows = new JsonArray();
            foreach (var operation in Operations.All.Where(o => o.InTree || includeSystem))
            {
                rows.Add((JsonNode)new JsonObject
                                   {
                                       ["command"] = operation.InTree ? "vion " + operation.CommandPath : null,
                                       ["operationId"] = operation.Id,
                                       ["method"] = operation.Method,
                                       ["path"] = operation.Template,
                                       ["effect"] = operation.Effect.ToString(),
                                       ["idempotent"] = operation.Idempotent,
                                       ["versioned"] = operation.Versioned,
                                       ["dryRun"] = operation.DryRun,
                                       ["deprecated"] = operation.Deprecated,
                                       ["hidden"] = operation.Hidden,
                                   });
            }

            var document = new JsonObject { ["items"] = rows };
            if (globals.Json)
            {
                Renderer.WriteJson(Fields.Project(document, globals.Fields), services);
                return ExitCodes.Ok;
            }

            var table = new JsonArray();
            foreach (var row in rows.OfType<JsonObject>())
            {
                var flags = new List<string>();
                if ((bool)row["idempotent"]!)
                {
                    flags.Add("idempotent");
                }

                if ((bool)row["versioned"]!)
                {
                    flags.Add("versioned");
                }

                if ((bool)row["dryRun"]!)
                {
                    flags.Add("dry-run");
                }

                if ((bool)row["deprecated"]!)
                {
                    flags.Add("deprecated");
                }

                if ((bool)row["hidden"]!)
                {
                    flags.Add("hidden");
                }

                table.Add((JsonNode)new JsonObject
                                    {
                                        ["command"] = row["command"]?.DeepClone() ?? "—",
                                        ["operation"] = row["operationId"]!.DeepClone(),
                                        ["method"] = row["method"]!.DeepClone(),
                                        ["path"] = row["path"]!.DeepClone(),
                                        ["effect"] = row["effect"]!.DeepClone(),
                                        ["flags"] = string.Join(" ", flags),
                                    });
            }

            Renderer.WriteTable(new JsonObject { ["items"] = table }, globals, services);
            services.Error.WriteLine($"{rows.Count} operations");
            return ExitCodes.Ok;
        }

        /// <summary>
        ///     The environment's document against the committed one, by operationId: added, removed, or changed in method,
        ///     path, `x-vion-authorization` or `x-vion-capability`. Exit 0 only when nothing differs, so a smoke check can
        ///     use it.
        /// </summary>
        private static async Task<int> LiveAsync(GlobalValues globals, CliServices services, CancellationToken cancellationToken)
        {
            var context = ContextResolver.Resolve(globals, services);
            var live = await ReadDocumentAsync(context.Environment, services, cancellationToken);

            var committed = Operations.All.ToDictionary(o => o.Id, StringComparer.Ordinal);
            var items = new JsonArray();
            foreach (var (id, operation) in live.Where(pair => !committed.ContainsKey(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                items.Add((JsonNode)Row("added", id, operation.Method, operation.Path, null));
            }

            foreach (var operation in committed.Values.Where(o => !live.ContainsKey(o.Id)).OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                items.Add((JsonNode)Row("removed", operation.Id, operation.Method, operation.Template, null));
            }

            foreach (var operation in committed.Values.Where(o => live.ContainsKey(o.Id)).OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                var served = live[operation.Id];
                var differences = new List<string>();
                if (!string.Equals(served.Method, operation.Method, StringComparison.OrdinalIgnoreCase) || served.Path != operation.Template)
                {
                    differences.Add("method/path");
                }

                if (!SameAuthorization(served.Authorization, operation.Authorization))
                {
                    differences.Add("x-vion-authorization");
                }

                if (!SameCapability(served.Capability, operation))
                {
                    differences.Add("x-vion-capability");
                }

                if (differences.Count > 0)
                {
                    items.Add((JsonNode)Row("changed", operation.Id, served.Method, served.Path, string.Join(", ", differences)));
                }
            }

            var document = new JsonObject { ["items"] = items };
            if (globals.Json)
            {
                Renderer.WriteJson(Fields.Project(document, globals.Fields), services);
            }
            else if (items.Count > 0)
            {
                Renderer.WriteTable(document, globals, services);
            }

            services.Error.WriteLine(items.Count == 0 ?
                                         $"no differences: {context.Environment.Name} serves the committed document's {committed.Count} operations." :
                                         $"{items.Count} difference(s) between {context.Environment.Name}'s {live.Count} operations and the committed {committed.Count} (snapshot {Operations.SnapshotSha256[..12]}).");
            return items.Count == 0 ? ExitCodes.Ok : ExitCodes.Failure;
        }

        private static JsonObject Row(string change, string id, string method, string path, string? detail)
        {
            return new JsonObject
                   {
                       ["change"] = change,
                       ["operationId"] = id,
                       ["method"] = method,
                       ["path"] = path,
                       ["detail"] = detail,
                   };
        }

        private static async Task<Dictionary<string, LiveOperation>> ReadDocumentAsync(ApiEnvironment environment, CliServices services, CancellationToken cancellationToken)
        {
            var url = environment.ApiUrl.TrimEnd('/') + "/openapi/v1.json";
            using var client = new HttpClient(services.Handler, false) { Timeout = TimeSpan.FromSeconds(60) };
            string text;
            try
            {
                using var response = await client.GetAsync(new Uri(url), cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new CliFailure($"GET {url} answered {(int)response.StatusCode}.");
                }

                text = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new CliFailure($"GET {url}: {exception.Message}");
            }

            JsonObject root;
            try
            {
                root = JsonNode.Parse(text) as JsonObject ?? throw new CliFailure($"{url} is not a JSON object.");
            }
            catch (JsonException exception)
            {
                throw new CliFailure($"{url} is not valid JSON: {exception.Message}");
            }

            var operations = new Dictionary<string, LiveOperation>(StringComparer.Ordinal);
            if (root["paths"] is JsonObject paths)
            {
                foreach (var (path, item) in paths)
                {
                    if (item is not JsonObject methods)
                    {
                        continue;
                    }

                    foreach (var method in Methods)
                    {
                        if (methods[method] is JsonObject operation && operation["operationId"]?.ToString() is { Length: > 0 } id)
                        {
                            operations[id] = new LiveOperation(id, method.ToUpperInvariant(), path, operation["x-vion-authorization"] as JsonObject, operation["x-vion-capability"] as JsonObject);
                        }
                    }
                }
            }

            return operations;
        }

        private static bool SameAuthorization(JsonObject? served, Authorization committed)
        {
            if (served == null)
            {
                return false;
            }

            var permissions = (served["permissions"] as JsonArray)?.Select(p => p?.ToString() ?? "").OrderBy(p => p, StringComparer.Ordinal).ToList() ?? [];
            return served["mode"]?.ToString() == committed.Mode &&
                   permissions.SequenceEqual(committed.Permissions.OrderBy(p => p, StringComparer.Ordinal)) &&
                   served["scopeType"]?.ToString() == committed.ScopeType &&
                   served["scopeFrom"]?.ToString() == committed.ScopeFrom;
        }

        private static bool SameCapability(JsonObject? served, Operation committed)
        {
            if (served == null)
            {
                return false;
            }

            return served["effect"]?.ToString() == committed.Effect.ToString() &&
                   Flag(served, "idempotent") == committed.Idempotent &&
                   Flag(served, "versioned") == committed.Versioned &&
                   Flag(served, "dryRun") == committed.DryRun;
        }

        private static bool Flag(JsonObject node, string member)
        {
            return node[member] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
        }

        private sealed record LiveOperation(string Id, string Method, string Path, JsonObject? Authorization, JsonObject? Capability);
    }
}
