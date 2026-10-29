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
    ///     `vion whoami`: `me.identity.get` rendered as the identity and the three membership tables, with the ids a
    ///     `context use` takes. In JSON mode the answer is the body, as for any call.
    /// </summary>
    internal static class WhoamiCommand
    {
        public static Command Create(CliTree tree, CliServices services)
        {
            var command = new Command("whoami", "Show who is signed in: the identity and the tenant, integrator and platform memberships with their ids.");
            command.SetAction((result, cancellationToken) =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals, services, () => RunAsync(globals, services, cancellationToken));
                              });
            return command;
        }

        private static async Task<int> RunAsync(GlobalValues globals, CliServices services, CancellationToken cancellationToken)
        {
            var context = ContextResolver.Resolve(globals, services);
            var operation = Operations.All.First(o => o.Id == "me.identity.get");
            var token = await TokenSource.ResolveAsync(globals, context, services, cancellationToken);
            using var client = new ApiClient(services);
            using var request = RequestBuilder.Build(context.Environment,
                                                     operation.Method,
                                                     operation.Template,
                                                     new System.Collections.Generic.Dictionary<string, string>(),
                                                     [],
                                                     null,
                                                     []);
            var response = await client.SendAsync(request, token, globals.Verbose, cancellationToken);
            if (globals.Json || !response.IsSuccess || response.Json is not JsonObject me)
            {
                return Renderer.Render(response, globals, services, operation);
            }

            var identity = new JsonObject();
            if (me["user"] is JsonObject user)
            {
                foreach (var (name, value) in user)
                {
                    identity[name] = value?.DeepClone();
                }
            }

            identity["locale"] = me["locale"]?.DeepClone();
            Renderer.WriteTable(identity, globals, services);
            foreach (var (member, title) in new[] { ("tenantMemberships", "tenants"), ("integratorMemberships", "integrators"), ("platformMemberships", "platforms") })
            {
                var items = me[member] as JsonArray ?? [];
                services.Out.WriteLine($"{title}: {items.Count}");
                if (items.Count > 0)
                {
                    Renderer.WriteTable(new JsonObject { ["items"] = items.DeepClone() }, globals, services);
                }
            }

            return ExitCodes.Ok;
        }
    }
}