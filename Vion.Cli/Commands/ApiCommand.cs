using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Auth;
using Vion.Cli.Context;
using Vion.Cli.Derived;
using Vion.Cli.Http;
using Vion.Cli.Infrastructure;
using Vion.Cli.Rendering;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     `vion api &lt;METHOD&gt; &lt;path&gt;`: the raw call, for an operation outside the tree or a path the snapshot does
    ///     not
    ///     know. The pairs are the query on GET and the JSON body otherwise, `gh api`'s shape.
    /// </summary>
    internal static class ApiCommand
    {
        private static readonly HashSet<string> Methods = new(["GET", "POST", "PUT", "DELETE", "PATCH"], StringComparer.OrdinalIgnoreCase);

        public static Command Create(CliTree tree, CliServices services)
        {
            var method = new Argument<string>("method") { Description = "GET, POST, PUT, DELETE or PATCH." };
            method.Validators.Add(result =>
                                  {
                                      if (result.GetValueOrDefault<string>() is { } value && !Methods.Contains(value))
                                      {
                                          result.AddError($"method '{value}' is not one of GET, POST, PUT, DELETE, PATCH.");
                                      }
                                  });
            var path = new Argument<string>("path") { Description = "The path under the environment's API, with its query if any: /tenant/<id>/edgeGateways" };
            var command = new Command("api", "Send a raw request to the environment's API with the resolved token, and render the answer as any command's.");
            var include = new Option<bool>("--include", "-i") { Description = "Show the answer's status and headers: before the body, or with it in one --json document." };
            var noWriteSafety = new Option<bool>("--no-write-safety")
                                {
                                    Description =
                                        "Send the request as given: no minted Idempotency-Key, no If-Match read or taken from the body, no dry-run refusal; --idempotency-key and --if-match still send theirs.",
                                };
            command.Arguments.Add(method);
            command.Arguments.Add(path);
            command.Options.Add(include);
            command.Options.Add(noWriteSafety);
            foreach (var option in tree.Body.ForJson)
            {
                command.Options.Add(option);
            }

            command.SetAction((result, cancellationToken) =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals,
                                                        services,
                                                        () => RunAsync(result.GetValue(method)!,
                                                                       result.GetValue(path)!,
                                                                       tree.Body.Read(result),
                                                                       new RawMode(result.GetValue(include), result.GetValue(noWriteSafety)),
                                                                       globals,
                                                                       services,
                                                                       cancellationToken));
                              });
            return command;
        }

        private static async Task<int> RunAsync(string method,
                                                string path,
                                                BodyValues body,
                                                RawMode mode,
                                                GlobalValues globals,
                                                CliServices services,
                                                CancellationToken cancellationToken)
        {
            var context = ContextResolver.Resolve(globals, services);
            var upper = method.ToUpperInvariant();
            if (mode.Include && globals.Wait)
            {
                throw new UsageException("--include shows one answer's headers, and --wait renders the outcome of several reads; pass one of them.");
            }

            // The raw call is still a call on a known operation when its path matches a template: the dry-run and the
            // irreversible rules hold for it as for the derived command. A path the table does not know is treated by
            // its method — a DELETE is irreversible — and offers no dry run. --no-write-safety keeps the confirmation, which
            // changes nothing on the wire, and drops what the tool would add to the request: the key, If-Match, the refusal.
            var operation = OperationMatcher.Find(upper, path);
            var safety = mode.NoWriteSafety ? null : operation;
            var query = new List<KeyValuePair<string, string>>();
            if (globals.DryRun)
            {
                if (!mode.NoWriteSafety && operation is not { DryRun: true })
                {
                    var offering = string.Join(", ", Operations.All.Where(o => o.DryRun).Select(o => $"{o.Method} {o.Template}"));
                    throw new UsageException($"--dry-run: {upper} {path} offers no dry run. The operations that do: {offering}.");
                }

                query.Add(new KeyValuePair<string, string>("dryRun", "true"));
            }

            var token = await TokenSource.ResolveAsync(globals, context, services, cancellationToken);
            var irreversible = operation?.Effect == Effect.Irreversible || (operation == null && upper == "DELETE");
            if (irreversible && !globals.Yes)
            {
                await OperationInvoker.Confirm(operation?.Id ?? $"{upper} {path}", services);
            }

            JsonNode? document = null;
            if (upper == "GET")
            {
                query.AddRange(body.RawFields.Concat(body.Fields).Select(BodyInput.SplitPair));
            }
            else
            {
                document = BodyInput.BuildJson(body, services);
            }

            using var client = new ApiClient(services);
            var headers = new List<KeyValuePair<string, string>>();
            WriteSafety.AddIdempotencyKey(safety, globals, headers);
            var at = path.IndexOf('?', StringComparison.Ordinal);
            await WriteSafety.AddIfMatchAsync(safety,
                                              globals,
                                              document,
                                              at >= 0 ? path[..at] : path,
                                              context,
                                              token,
                                              client,
                                              headers,
                                              cancellationToken);

            using var request = RequestBuilder.Build(context.Environment,
                                                     upper,
                                                     path,
                                                     new Dictionary<string, string>(StringComparer.Ordinal),
                                                     query,
                                                     document != null ? BodyInput.JsonContent(document) : null,
                                                     headers);
            var response = await client.SendAsync(request, token, globals.Verbose, cancellationToken);
            if (mode.Include)
            {
                return Renderer.RenderIncluded(response, globals, services, operation);
            }

            if (globals.Wait && LongRunningWait.Follows(operation, upper, response))
            {
                response = await LongRunningWait.FollowAsync(response,
                                                             upper == "GET" ? request.RequestUri : null,
                                                             globals,
                                                             context,
                                                             token,
                                                             client,
                                                             services,
                                                             cancellationToken);
            }

            return Renderer.Render(response, globals, services, operation);
        }

        /// <summary>`--include` and `--no-write-safety`, the two options only the raw call takes.</summary>
        private sealed record RawMode(bool Include, bool NoWriteSafety);
    }
}