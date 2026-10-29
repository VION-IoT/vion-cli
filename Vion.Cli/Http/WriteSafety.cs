using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Commands;
using Vion.Cli.Context;
using Vion.Cli.Derived;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Http
{
    /// <summary>
    ///     The two headers that make a write safe to repeat and safe against a lost update, resolved the same way for a
    ///     derived command and for a raw call on a path the table knows. The idempotency key is the given one, else a fresh
    ///     one where the operation honours it. `If-Match` is the flag, else the document's `meta.version`, else the
    ///     aggregate's own `get` on the same path, else a refusal before anything is sent.
    /// </summary>
    internal static class WriteSafety
    {
        public static void AddIdempotencyKey(Operation? operation, GlobalValues globals, List<KeyValuePair<string, string>> headers)
        {
            if (globals.IdempotencyKey != null)
            {
                headers.Add(new KeyValuePair<string, string>("Idempotency-Key", globals.IdempotencyKey));
            }
            else if (operation is { Idempotent: true })
            {
                headers.Add(new KeyValuePair<string, string>("Idempotency-Key", Guid.NewGuid().ToString("D")));
            }
        }

        /// <summary>
        ///     Adds `If-Match` for a versioned operation, taking `meta` out of the body it came from: the input DTO has no
        ///     such member, and the loop `get --json`, edit, `update --input` carries the version in it.
        /// </summary>
        public static async Task AddIfMatchAsync(Operation? operation,
                                                 GlobalValues globals,
                                                 JsonNode? body,
                                                 string expandedPath,
                                                 ResolvedContext context,
                                                 string token,
                                                 ApiClient client,
                                                 List<KeyValuePair<string, string>> headers,
                                                 CancellationToken cancellationToken)
        {
            if (globals.IfMatch != null)
            {
                headers.Add(new KeyValuePair<string, string>("If-Match", Tag(globals.IfMatch)));
                StripMeta(operation, body);
                return;
            }

            if (operation is not { Versioned: true })
            {
                return;
            }

            if (body is JsonObject document && document["meta"] is JsonObject meta && meta["version"] is JsonValue version)
            {
                headers.Add(new KeyValuePair<string, string>("If-Match", Tag(version.ToString())));
                document.Remove("meta");
                return;
            }

            var read = Operations.All.FirstOrDefault(o => o.Method == "GET" && o.Template == operation.Template);
            if (read != null)
            {
                using var request = RequestBuilder.Build(context.Environment, "GET", expandedPath, new Dictionary<string, string>(StringComparer.Ordinal), [], null, []);
                var current = await client.SendAsync(request, token, globals.Verbose, cancellationToken);
                var fetched = current.Json is JsonObject row && row["meta"] is JsonObject rowMeta && rowMeta["version"] is JsonValue rowVersion ? rowVersion.ToString() : current.ETag;
                if (current.IsSuccess && fetched != null)
                {
                    headers.Add(new KeyValuePair<string, string>("If-Match", Tag(fetched)));
                    StripMeta(operation, body);
                    return;
                }

                throw new CliFailure($"{operation.Id} replaces a versioned row, and {read.Id} answered {current.Status} without a version to send as If-Match.");
            }

            throw new UsageException($"{operation.Id} replaces a versioned row: pass --if-match <version>, or --input the document from `get --json`, whose meta.version is the version; the list row's meta.version carries it too.");
        }

        private static void StripMeta(Operation? operation, JsonNode? body)
        {
            if (operation is { Versioned: true } && body is JsonObject document)
            {
                document.Remove("meta");
            }
        }

        /// <summary>
        ///     An ETag is a quoted strong tag on the wire, and the host matches a version only by one; a bare version from
        ///     `meta.version` or the command line is quoted here, a quoted one passed as given.
        /// </summary>
        private static string Tag(string version)
        {
            return version.StartsWith('"') ? version : $"\"{version}\"";
        }
    }
}
