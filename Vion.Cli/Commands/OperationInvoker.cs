using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Net.Http;
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
    ///     Runs one derived command: resolves the context, checks the flags against the operation's capabilities, fills
    ///     the parameters, confirms an irreversible call, takes the token, sends, renders. The one path every operation
    ///     takes; a rule that holds for one holds for all.
    /// </summary>
    internal static class OperationInvoker
    {
        public static Task<int> InvokeAsync(Operation operation,
                                            IReadOnlyList<BoundParameter> bound,
                                            ParseResult result,
                                            CliTree tree,
                                            CliServices services,
                                            CancellationToken cancellationToken)
        {
            var globals = tree.Globals.Read(result);
            return Guard.RunAsync(globals,
                                  services,
                                  () => RunAsync(operation,
                                                 bound,
                                                 result,
                                                 globals,
                                                 tree,
                                                 services,
                                                 cancellationToken));
        }

        /// <summary>
        ///     An irreversible operation is confirmed on a terminal and refused off one: a script that meant it passes
        ///     --yes, and a script that did not is stopped before the request leaves.
        /// </summary>
        public static async Task Confirm(string what, CliServices services)
        {
            if (!services.Console.IsInputTerminal)
            {
                throw new UsageException($"{what} is irreversible; pass --yes to run it without a terminal.");
            }

            await services.Error.WriteAsync($"{what} is irreversible. Continue? [y/N] ");
            var answer = (await services.In.ReadLineAsync())?.Trim();
            if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliFailure("cancelled.");
            }
        }

        private static async Task<int> RunAsync(Operation operation,
                                                IReadOnlyList<BoundParameter> bound,
                                                ParseResult result,
                                                GlobalValues globals,
                                                CliTree tree,
                                                CliServices services,
                                                CancellationToken cancellationToken)
        {
            var context = ContextResolver.Resolve(globals, services);

            if (globals.DryRun && !operation.DryRun)
            {
                var offering = string.Join(", ", Operations.All.Where(o => o.DryRun).Select(o => o.Id));
                throw new UsageException($"--dry-run: {operation.Id} offers no dry run. The operations that do: {offering}.");
            }

            var pathValues = new Dictionary<string, string>(StringComparer.Ordinal);
            var query = new List<KeyValuePair<string, string>>();
            foreach (var parameter in operation.Parameters)
            {
                var values = ValuesOf(parameter, bound, result, globals, context);
                if (values == null)
                {
                    if (parameter.Required && parameter.In == ParameterLocation.Path)
                    {
                        throw new UsageException(MissingMessage(operation, parameter));
                    }

                    continue;
                }

                if (parameter.In == ParameterLocation.Path)
                {
                    pathValues[parameter.Name] = values[0];
                }
                else
                {
                    query.AddRange(values.Select(value => new KeyValuePair<string, string>(parameter.Name, value)));
                }
            }

            // The token first, one line of standard input; the confirmation next; the body last, the rest of the stream.
            var token = await TokenSource.ResolveAsync(globals, context, services);
            if (operation.Effect == Effect.Irreversible && !globals.Yes)
            {
                await Confirm(operation.Id, services);
            }

            HttpContent? content = null;
            if (operation.Body != null)
            {
                var body = tree.Body.Read(result);
                content = operation.Body.IsMultipart ? BodyInput.BuildMultipart(operation.Body, body) :
                          BodyInput.BuildJson(body, services) is { } node ? BodyInput.JsonContent(node) : null;
            }

            var headers = new List<KeyValuePair<string, string>>();
            if (operation.Idempotent && globals.IdempotencyKey != null)
            {
                headers.Add(new KeyValuePair<string, string>("Idempotency-Key", globals.IdempotencyKey));
            }

            if (operation.Versioned && globals.IfMatch != null)
            {
                headers.Add(new KeyValuePair<string, string>("If-Match", IfMatchValue(globals.IfMatch)));
            }

            using var request = RequestBuilder.Build(context.Environment,
                                                     operation.Method,
                                                     operation.Template,
                                                     pathValues,
                                                     query,
                                                     content,
                                                     headers);
            using var client = new ApiClient(services);
            var response = await client.SendAsync(request, token, globals.Verbose, cancellationToken);
            return Renderer.Render(response, globals, services, operation);
        }

        private static IReadOnlyList<string>? ValuesOf(Parameter parameter, IReadOnlyList<BoundParameter> bound, ParseResult result, GlobalValues globals, ResolvedContext context)
        {
            if (parameter.Global == "dry-run")
            {
                return globals.DryRun ? ["true"] : null;
            }

            if (parameter.Global != null)
            {
                return context.Root(parameter.Global) is { } root ? [root] : null;
            }

            var binding = bound.First(b => b.Parameter == parameter);
            return binding.Read(result);
        }

        private static string MissingMessage(Operation operation, Parameter parameter)
        {
            return parameter.Global != null ?
                       $"{operation.Id} needs a {parameter.Global}: pass --{parameter.Global} <id>, or set one with `vion context use --{parameter.Global} <id>`." :
                       $"{operation.Id} needs --{parameter.OptionName}.";
        }

        /// <summary>
        ///     An ETag is quoted on the wire; a bare version from `meta.version` or the command line is quoted here, a quoted
        ///     one passed as given.
        /// </summary>
        private static string IfMatchValue(string version)
        {
            return version.StartsWith('"') ? version : $"\"{version}\"";
        }
    }
}