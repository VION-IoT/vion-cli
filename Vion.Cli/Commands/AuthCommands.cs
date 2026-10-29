using System.CommandLine;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Auth;
using Vion.Cli.Context;
using Vion.Cli.Infrastructure;
using Vion.Cli.Rendering;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     `vion auth login|logout|status`: the device-code sign-in into the environment's realm, its removal, and what the
    ///     store holds. The environment is the resolved one, so `--env` and the current context select it as for any call.
    /// </summary>
    internal static class AuthCommands
    {
        public static Command Create(CliTree tree, CliServices services)
        {
            var auth = new Command("auth", "Sign in to an environment, sign out of it, or see what is stored.");
            auth.Subcommands.Add(Login(tree, services));
            auth.Subcommands.Add(Logout(tree, services));
            auth.Subcommands.Add(Status(tree, services));
            return auth;
        }

        private static Command Login(CliTree tree, CliServices services)
        {
            var noBrowser = new Option<bool>("--no-browser") { Description = "Print the verification URL and code instead of opening the browser; use a phone." };
            var command = new Command("login", "Sign in to the environment with a device code: open the URL, enter the code, and the tokens are stored.");
            command.Options.Add(noBrowser);
            command.SetAction((result, cancellationToken) =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals, services, () => LoginAsync(globals, result.GetValue(noBrowser), services, cancellationToken));
                              });
            return command;
        }

        private static async Task<int> LoginAsync(GlobalValues globals, bool noBrowser, CliServices services, CancellationToken cancellationToken)
        {
            var context = ContextResolver.Resolve(globals, services);
            var environment = context.Environment;
            using var realm = new OAuthClient(services);
            var device = await realm.RequestDeviceCodeAsync(environment, cancellationToken);

            services.Error.WriteLine($"Sign in to {environment.Name}: open {device.VerificationUri}");
            services.Error.WriteLine($"and enter the code {device.UserCode} if asked. Waiting for the sign-in…");
            if (!noBrowser && !services.OpenBrowser(device.VerificationUri))
            {
                services.Error.WriteLine("(the browser did not open; use the URL above)");
            }

            var credentials = await realm.PollDeviceTokenAsync(environment, device, cancellationToken);
            CredentialStore.Put(services.HomeDirectory, environment.Name, credentials);

            var subject = Jwt.Subject(credentials.AccessToken) ?? "(unknown subject)";
            var expires = credentials.ExpiresAt.ToString(CredentialStore.TimestampFormat, CultureInfo.InvariantCulture);
            if (globals.Json)
            {
                Renderer.WriteJson(new JsonObject { ["environment"] = environment.Name, ["subject"] = subject, ["expiresAt"] = expires }, services);
            }
            else
            {
                services.Out.WriteLine($"signed in to {environment.Name} as {subject}; the token expires {expires} and is refreshed as needed.");
            }

            return ExitCodes.Ok;
        }

        private static Command Logout(CliTree tree, CliServices services)
        {
            var command = new Command("logout", "Remove the environment's stored credentials.");
            command.SetAction(result =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals,
                                                        services,
                                                        () =>
                                                        {
                                                            var environment = ContextResolver.Resolve(globals, services).Environment;
                                                            var removed = CredentialStore.Remove(services.HomeDirectory, environment.Name);
                                                            if (globals.Json)
                                                            {
                                                                Renderer.WriteJson(new JsonObject { ["environment"] = environment.Name, ["removed"] = removed }, services);
                                                            }
                                                            else
                                                            {
                                                                services.Out.WriteLine(removed ?
                                                                                           $"removed the stored credentials for {environment.Name}." :
                                                                                           $"none were stored for {environment.Name}; nothing removed.");
                                                            }

                                                            return Task.FromResult(ExitCodes.Ok);
                                                        });
                              });
            return command;
        }

        private static Command Status(CliTree tree, CliServices services)
        {
            var command = new Command("status", "Show each stored sign-in — the subject, the expiry, whether it can refresh — and where the next call takes its token.");
            command.SetAction(result =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals, services, () => Task.FromResult(StatusOf(globals, services)));
                              });
            return command;
        }

        private static int StatusOf(GlobalValues globals, CliServices services)
        {
            var sessions = new JsonArray();
            foreach (var (environment, credentials) in CredentialStore.Load(services.HomeDirectory))
            {
                sessions.Add((JsonNode)new JsonObject
                                       {
                                           ["environment"] = environment,
                                           ["subject"] = Jwt.Subject(credentials.AccessToken),
                                           ["expiresAt"] = credentials.ExpiresAt.ToString(CredentialStore.TimestampFormat, CultureInfo.InvariantCulture),
                                           ["refreshToken"] = credentials.RefreshToken != null,
                                           ["obtainedBy"] = credentials.ObtainedBy,
                                       });
            }

            // The next call's source needs an environment; with none selected the plan says so instead of refusing.
            JsonObject next;
            try
            {
                var context = ContextResolver.Resolve(globals, services);
                var plan = TokenSource.Plan(globals, context, services);
                next = new JsonObject { ["environment"] = context.Environment.Name, ["source"] = plan.Description };
            }
            catch (UsageException exception)
            {
                next = new JsonObject { ["environment"] = null, ["source"] = exception.Message };
            }

            if (globals.Json)
            {
                Renderer.WriteJson(new JsonObject { ["sessions"] = sessions, ["next"] = next }, services);
                return ExitCodes.Ok;
            }

            if (sessions.Count == 0)
            {
                services.Out.WriteLine("no stored sign-in.");
            }
            else
            {
                Renderer.WriteTable(new JsonObject { ["items"] = sessions }, globals, services);
            }

            services.Out.WriteLine(next["environment"] != null ? $"next call on {next["environment"]}: {next["source"]}" : $"next call: {next["source"]}");
            return ExitCodes.Ok;
        }
    }
}
