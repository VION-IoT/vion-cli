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
    ///     `vion ops`: the tree's operations from the committed documents, offline — what the tool can call over HTTP, what
    ///     it can watch over MQTT, and what each does to the state. `--all` adds the `system` operations, which have no
    ///     command and are reached through `api`. `--live` reads the environment's own documents, tokenless, and lists what
    ///     differs from the committed ones.
    /// </summary>
    internal static class OpsCommand
    {
        /// <summary>The protocol of an OpenAPI operation; the event document's server names the other.</summary>
        private const string Http = "http";

        private const string Mqtt = "mqtt";

        private static readonly string[] Methods = ["get", "put", "post", "delete", "patch"];

        public static Command Create(CliTree tree, CliServices services)
        {
            var all = new Option<bool>("--all") { Description = "Include the system operations, which have no command." };
            var live = new Option<bool>("--live")
                       {
                           Description =
                               "Read the environment's OpenAPI and event documents and list the operations added, removed or changed against the committed ones; exit 1 on any.",
                       };
            var command = new Command("ops",
                                      "List the operations of the committed documents, the watches included: the command, the operationId, the protocol, the method and path, the effect, the completion and the write-safety flags.");
            command.Options.Add(all);
            command.Options.Add(live);
            command.SetAction((result, cancellationToken) =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals,
                                                        services,
                                                        () => result.GetValue(live) ? LiveAsync(globals, services, cancellationToken) :
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
                                       ["protocol"] = Http,
                                       ["method"] = operation.Method,
                                       ["path"] = operation.Template,
                                       ["effect"] = operation.Effect.ToString(),
                                       ["completion"] = operation.Completion.ToString(),
                                       ["idempotent"] = operation.Idempotent,
                                       ["versioned"] = operation.Versioned,
                                       ["dryRun"] = operation.DryRun,
                                       ["deprecated"] = operation.Deprecated,
                                       ["hidden"] = operation.Hidden,
                                   });
            }

            var http = rows.Count;

            // A watch is the event document's operation that sends on its channel: it reads, and it streams until it ends.
            foreach (var channel in Channels.All)
            {
                rows.Add((JsonNode)new JsonObject
                                   {
                                       ["command"] = "vion " + channel.CommandPath,
                                       ["operationId"] = channel.OperationId,
                                       ["protocol"] = Mqtt,
                                       ["method"] = null,
                                       ["path"] = null,
                                       ["effect"] = "Read",
                                       ["completion"] = "Stream",
                                       ["idempotent"] = false,
                                       ["versioned"] = false,
                                       ["dryRun"] = false,
                                       ["deprecated"] = false,
                                       ["hidden"] = false,
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
                                        ["protocol"] = row["protocol"]!.DeepClone(),
                                        ["method"] = row["method"]?.DeepClone() ?? "—",
                                        ["path"] = row["path"]?.DeepClone() ?? "—",
                                        ["effect"] = row["effect"]!.DeepClone(),
                                        ["completion"] = row["completion"]!.DeepClone(),
                                        ["flags"] = string.Join(" ", flags),
                                    });
            }

            Renderer.WriteTable(new JsonObject { ["items"] = table }, globals, services);
            services.Error.WriteLine($"{http} {Http} operations, {rows.Count - http} {Mqtt} operations");
            return ExitCodes.Ok;
        }

        /// <summary>
        ///     The environment's documents against the committed ones, by operation id. The OpenAPI document's operations:
        ///     added, removed, or changed in method, path, `x-vion-authorization` or `x-vion-capability`. The event document's:
        ///     added, removed, or changed in action, channel, or what the watch derive reads of the channel — never its
        ///     address, which names the environment. Exit 0 only when nothing differs, so a smoke check can use it.
        /// </summary>
        private static async Task<int> LiveAsync(GlobalValues globals, CliServices services, CancellationToken cancellationToken)
        {
            var context = ContextResolver.Resolve(globals, services);
            var live = ReadOpenApiOperations(await ReadDocumentAsync(context.Environment, "/openapi/v1.json", services, cancellationToken));
            var liveEvents = ReadEventOperations(await ReadDocumentAsync(context.Environment, "/asyncapi/v1.json", services, cancellationToken));

            var committed = Operations.All.ToDictionary(o => o.Id, StringComparer.Ordinal);
            var items = new JsonArray();
            foreach (var (id, operation) in live.Where(pair => !committed.ContainsKey(pair.Key)).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                items.Add((JsonNode)Row("added", id, Http, operation.Method, operation.Path, null));
            }

            foreach (var operation in committed.Values.Where(o => !live.ContainsKey(o.Id)).OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                items.Add((JsonNode)Row("removed", operation.Id, Http, operation.Method, operation.Template, null));
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
                    items.Add((JsonNode)Row("changed", operation.Id, Http, served.Method, served.Path, string.Join(", ", differences)));
                }
            }

            var committedEvents = EventOperations.All.ToDictionary(o => o.Id, StringComparer.Ordinal);
            foreach (var id in liveEvents.Keys.Where(id => !committedEvents.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal))
            {
                items.Add((JsonNode)Row("added", id, Mqtt, null, null, null));
            }

            foreach (var id in committedEvents.Keys.Where(id => !liveEvents.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal))
            {
                items.Add((JsonNode)Row("removed", id, Mqtt, null, null, null));
            }

            foreach (var operation in committedEvents.Values.Where(o => liveEvents.ContainsKey(o.Id)).OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                var differences = EventDifferences(operation, liveEvents[operation.Id]);
                if (differences.Count > 0)
                {
                    items.Add((JsonNode)Row("changed", operation.Id, Mqtt, null, null, string.Join(", ", differences)));
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
                                         $"no differences: {context.Environment.Name} serves the committed documents' {committed.Count} {Http} and {committedEvents.Count} {Mqtt} operations." :
                                         $"{items.Count} difference(s) between {context.Environment.Name}'s {live.Count} {Http} and {liveEvents.Count} {Mqtt} operations and the committed {committed.Count} and {committedEvents.Count} (snapshots {Operations.SnapshotSha256[..12]}, {Channels.SnapshotSha256[..12]}).");
            return items.Count == 0 ? ExitCodes.Ok : ExitCodes.Failure;
        }

        private static JsonObject Row(string change, string id, string protocol, string? method, string? path, string? detail)
        {
            return new JsonObject
                   {
                       ["change"] = change,
                       ["operationId"] = id,
                       ["protocol"] = protocol,
                       ["method"] = method,
                       ["path"] = path,
                       ["detail"] = detail,
                   };
        }

        private static async Task<JsonObject> ReadDocumentAsync(ApiEnvironment environment, string documentPath, CliServices services, CancellationToken cancellationToken)
        {
            var url = environment.ApiUrl.TrimEnd('/') + documentPath;
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

            return root;
        }

        private static Dictionary<string, LiveOperation> ReadOpenApiOperations(JsonObject root)
        {
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
                            operations[id] = new LiveOperation(id,
                                                               method.ToUpperInvariant(),
                                                               path,
                                                               operation["x-vion-authorization"] as JsonObject,
                                                               operation["x-vion-capability"] as JsonObject);
                        }
                    }
                }
            }

            return operations;
        }

        /// <summary>
        ///     The event document's operations by id, each with its action, the id of the channel its `$ref` names, and
        ///     that channel as served — or an empty object where the document lacks it.
        /// </summary>
        private static Dictionary<string, EventOperation> ReadEventOperations(JsonObject root)
        {
            const string prefix = "#/channels/";
            var channels = root["channels"] as JsonObject;
            var operations = new Dictionary<string, EventOperation>(StringComparer.Ordinal);
            if (root["operations"] is not JsonObject served)
            {
                return operations;
            }

            foreach (var (id, node) in served)
            {
                var reference = node?["channel"]?["$ref"]?.ToString() ?? "";
                var channelId = reference.StartsWith(prefix, StringComparison.Ordinal) ?
                                    reference[prefix.Length..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal) : reference;
                var channel = channels?[channelId] as JsonObject ?? new JsonObject();
                operations[id] = new EventOperation(id, node?["action"]?.ToString() ?? "", channelId, channel.ToJsonString());
            }

            return operations;
        }

        /// <summary>
        ///     What differs between the committed operation and the served one, in what the watch derive reads: the action,
        ///     the channel, the channel's parameter names in order, each hand-out's `operation`, `row` and `join`, and the
        ///     registration. Both sides go through the same projection, so the committed side's copy is never read on its own.
        /// </summary>
        private static List<string> EventDifferences(EventOperation committed, EventOperation served)
        {
            var differences = new List<string>();
            if (committed.Action != served.Action)
            {
                differences.Add("action");
            }

            if (committed.Channel != served.Channel)
            {
                differences.Add("channel");
            }

            var mine = JsonNode.Parse(committed.Contract) as JsonObject ?? new JsonObject();
            var theirs = JsonNode.Parse(served.Contract) as JsonObject ?? new JsonObject();
            if (!JsonNode.DeepEquals(ParameterNames(mine), ParameterNames(theirs)))
            {
                differences.Add("parameters");
            }

            if (!JsonNode.DeepEquals(HandOuts(mine), HandOuts(theirs)))
            {
                differences.Add("x-vion-handedOutBy");
            }

            if (!JsonNode.DeepEquals(mine["x-vion-registeredBy"], theirs["x-vion-registeredBy"]))
            {
                differences.Add("x-vion-registeredBy");
            }

            return differences;
        }

        private static JsonArray ParameterNames(JsonObject channel)
        {
            return new JsonArray((channel["parameters"] as JsonObject)?.Select(parameter => (JsonNode?)parameter.Key).ToArray() ?? []);
        }

        private static JsonArray HandOuts(JsonObject channel)
        {
            return new JsonArray((channel["x-vion-handedOutBy"] as JsonArray)?.Select(handOut => (JsonNode?)new JsonObject
                                                                                                                {
                                                                                                                    ["operation"] = handOut?["operation"]?.DeepClone(),
                                                                                                                    ["row"] = handOut?["row"]?.DeepClone(),
                                                                                                                    ["join"] = handOut?["join"]?.DeepClone(),
                                                                                                                })
                                                                                                .ToArray() ??
                                 []);
        }

        private static bool SameAuthorization(JsonObject? served, Authorization committed)
        {
            if (served == null)
            {
                return false;
            }

            var permissions = (served["permissions"] as JsonArray)?.Select(p => p?.ToString() ?? "").OrderBy(p => p, StringComparer.Ordinal).ToList() ?? [];
            return served["mode"]?.ToString() == committed.Mode && permissions.SequenceEqual(committed.Permissions.OrderBy(p => p, StringComparer.Ordinal)) &&
                   served["scopeType"]?.ToString() == committed.ScopeType && served["scopeFrom"]?.ToString() == committed.ScopeFrom;
        }

        private static bool SameCapability(JsonObject? served, Operation committed)
        {
            if (served == null)
            {
                return false;
            }

            return served["effect"]?.ToString() == committed.Effect.ToString() && Flag(served, "idempotent") == committed.Idempotent &&
                   Flag(served, "versioned") == committed.Versioned && Flag(served, "dryRun") == committed.DryRun &&
                   (served["completion"]?.ToString() ?? "Response") == committed.Completion.ToString();
        }

        private static bool Flag(JsonObject node, string member)
        {
            return node[member] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
        }

        private sealed record LiveOperation(string Id, string Method, string Path, JsonObject? Authorization, JsonObject? Capability);
    }
}