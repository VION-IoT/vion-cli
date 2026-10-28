using System.Threading.Tasks;
using Vion.Cli.Commands;
using Vion.Cli.Context;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Auth
{
    /// <summary>
    ///     The bearer token for a call. The one source so far is a token piped into <c>--token -</c>; the device-code
    ///     sign-in, the service-account credentials and the stored session join it in the order the spec states.
    /// </summary>
    internal static class TokenSource
    {
        public static async Task<string> ResolveAsync(GlobalValues globals, ResolvedContext context, CliServices services)
        {
            if (globals.Token != null)
            {
                if (globals.Token != "-")
                {
                    throw new UsageException("--token takes `-` and reads the token from standard input, so that it never lands in a shell history.");
                }

                // One line, not the whole stream: `--input -` and `-F key=@-` read the body from the same standard
                // input after it, so a script pipes the token first and the document after.
                var token = (await services.In.ReadLineAsync())?.Trim();
                if (string.IsNullOrEmpty(token))
                {
                    throw new CliFailure("--token -: standard input carried no token.");
                }

                return token;
            }

            throw new
                CliFailure($"no token for {context.Environment.Name}: pipe one into `--token -`. Signing in with `vion auth login`, or with VION_CLIENT_ID and VION_CLIENT_SECRET, lands with the next release.");
        }
    }
}