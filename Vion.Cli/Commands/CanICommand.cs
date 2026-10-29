using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Auth;
using Vion.Cli.Context;
using Vion.Cli.Http;
using Vion.Cli.Infrastructure;
using Vion.Cli.Rendering;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     `vion can-i &lt;operationId&gt;`: the environment's catalog row joined with the caller's own permissions at the
    ///     context root, decided as the dashboard's `can.ts` decides — advisory, so whatever it cannot tell is allowed and
    ///     the API's 403 stays the backstop. Exit 0 allowed, 1 denied, 2 unknown.
    /// </summary>
    internal static class CanICommand
    {
        private static readonly string[] Domains = ["tenant", "integrator", "platform"];

        public static Command Create(CliTree tree, CliServices services)
        {
            var operation = new Argument<string>("operationId") { Description = "The operation, as `ops` names it: tenant.edgeGateway.list" };
            var command = new Command("can-i",
                                      "Decide from the environment's catalog and your own permissions whether you may call an operation; --project decides a project-scoped one at that project.");
            command.Arguments.Add(operation);
            command.SetAction((result, cancellationToken) =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals, services, () => RunAsync(result.GetValue(operation)!, globals, services, cancellationToken));
                              });
            return command;
        }

        private static async Task<int> RunAsync(string operationId, GlobalValues globals, CliServices services, CancellationToken cancellationToken)
        {
            var context = ContextResolver.Resolve(globals, services);
            var token = await TokenSource.ResolveAsync(globals, context, services, cancellationToken);
            using var client = new ApiClient(services);

            var catalog = await ReadAsync(client,
                                          context,
                                          "/capabilities",
                                          token,
                                          globals,
                                          cancellationToken);
            var row = (catalog["items"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(item => item["id"]?.ToString() == operationId);
            if (row == null)
            {
                return Report(operationId,
                              "unknown",
                              null,
                              [],
                              null,
                              $"{context.Environment.Name}'s catalog does not list {operationId}; `vion ops --live` shows what it serves.",
                              globals,
                              services);
            }

            var authorization = row["authorization"] as JsonObject;
            var mode = authorization?["mode"]?.ToString() ?? "";
            var required = (authorization?["permissions"] as JsonArray)?.Select(p => p?.ToString() ?? "").ToList() ?? [];
            if (required.Count == 0 || mode is not ("Root" or "Scope" or "Filtered" or "Entity"))
            {
                return Report(operationId,
                              "allowed",
                              mode,
                              required,
                              null,
                              $"{operationId} is decided by {Describe(mode)}, not by a permission you hold.",
                              globals,
                              services);
            }

            var domain = DomainOf(authorization, row["path"]?.ToString() ?? "");
            var rootId = domain != null ? context.Root(domain) : null;
            if (domain == null || rootId == null)
            {
                return Report(operationId,
                              "allowed",
                              mode,
                              required,
                              null,
                              $"nothing to decide against: the context names no {domain ?? "root"} for {operationId}; pass --{domain ?? "tenant"} <id>.",
                              globals,
                              services);
            }

            var held = await ReadAsync(client,
                                       context,
                                       $"/{domain}/{Uri.EscapeDataString(rootId)}/access/me/permissions",
                                       token,
                                       globals,
                                       cancellationToken);
            var root = Permissions(held["root"]);
            IReadOnlyList<string> holding;
            string where;
            switch (mode)
            {
                case "Root":
                    holding = root;
                    where = $"{domain} {rootId}";
                    break;
                case "Scope":
                case "Filtered":
                    if (globals.Project == null)
                    {
                        return Report(operationId,
                                      "allowed",
                                      mode,
                                      required,
                                      null,
                                      $"{operationId} is decided at a project; pass --project <id> to decide it there.",
                                      globals,
                                      services);
                    }

                    var scope = (held["scopes"] as JsonArray)?.OfType<JsonObject>()
                                                             .FirstOrDefault(s => string.Equals(s["projectId"]?.ToString(), globals.Project, StringComparison.OrdinalIgnoreCase) ||
                                                                                  string.Equals(s["scopeId"]?.ToString(), globals.Project, StringComparison.OrdinalIgnoreCase));
                    holding = scope != null ? Permissions(scope["permissions"]) : root;
                    where = scope != null ? $"project {globals.Project}" : $"{domain} {rootId} (no own assignment at project {globals.Project})";
                    break;
                default:
                    return Report(operationId,
                                  "allowed",
                                  mode,
                                  required,
                                  null,
                                  $"{operationId} is decided on the row it acts on, which only the call names.",
                                  globals,
                                  services);
            }

            var matched = required.Where(holding.Contains).ToList();
            return matched.Count > 0 ? Report(operationId,
                                              "allowed",
                                              mode,
                                              required,
                                              where,
                                              $"you hold {string.Join(", ", matched)} at {where}.",
                                              globals,
                                              services) : Report(operationId,
                                                                 "denied",
                                                                 mode,
                                                                 required,
                                                                 where,
                                                                 $"{operationId} needs one of {string.Join(", ", required)} at {where}; you hold none of them.",
                                                                 globals,
                                                                 services);
        }

        private static async Task<JsonObject> ReadAsync(ApiClient client,
                                                        ResolvedContext context,
                                                        string path,
                                                        string token,
                                                        GlobalValues globals,
                                                        CancellationToken cancellationToken)
        {
            using var request = RequestBuilder.Build(context.Environment,
                                                     "GET",
                                                     path,
                                                     new Dictionary<string, string>(StringComparer.Ordinal),
                                                     [],
                                                     null,
                                                     []);
            var response = await client.SendAsync(request, token, globals.Verbose, cancellationToken);
            if (!response.IsSuccess)
            {
                var error = ApiError.From(response, null);
                throw new CliFailure($"GET {path}: {string.Join(" ", error.Lines())}");
            }

            return response.Json as JsonObject ?? throw new CliFailure($"GET {path} answered no JSON object.");
        }

        /// <summary>The domain an operation belongs to: by its scope type when that is a domain, else by its path's first segment.</summary>
        private static string? DomainOf(JsonObject? authorization, string path)
        {
            var scopeType = authorization?["scopeType"]?.ToString();
            if (scopeType != null && Domains.Contains(scopeType, StringComparer.OrdinalIgnoreCase))
            {
                return scopeType.ToLowerInvariant();
            }

            var segment = path.Trim('/').Split('/').FirstOrDefault() ?? "";
            return Domains.FirstOrDefault(domain => string.Equals(domain, segment, StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> Permissions(JsonNode? node)
        {
            return (node as JsonArray)?.Select(p => p?.ToString() ?? "").ToList() ?? [];
        }

        private static string Describe(string mode)
        {
            return mode switch
            {
                "Membership" => "membership alone",
                "Caller" => "the caller's own identity",
                "Anonymous" => "nothing: it is anonymous",
                "InCode" => "the handler itself",
                "" => "no declared mode",
                _ => $"mode {mode} with no permission named",
            };
        }

        private static int Report(string operationId,
                                  string decision,
                                  string? mode,
                                  List<string> required,
                                  string? decidedAt,
                                  string reason,
                                  GlobalValues globals,
                                  CliServices services)
        {
            var exitCode = decision switch
            {
                "allowed" => ExitCodes.Ok,
                "denied" => ExitCodes.Failure,
                _ => ExitCodes.Usage,
            };
            if (globals.Json)
            {
                Renderer.WriteJson(new JsonObject
                                   {
                                       ["operation"] = operationId,
                                       ["decision"] = decision,
                                       ["mode"] = mode,
                                       ["requiredPermissions"] = new JsonArray(required.Select(p => (JsonNode)JsonValue.Create(p)).ToArray()),
                                       ["decidedAt"] = decidedAt,
                                       ["reason"] = reason,
                                   },
                                   services);
            }
            else
            {
                services.Out.WriteLine($"{decision}: {reason}");
                if (required.Count > 0 && decision != "denied")
                {
                    services.Out.WriteLine($"  requires one of: {string.Join(", ", required)}");
                }
            }

            return exitCode;
        }
    }
}