using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Commands;
using Vion.Cli.Context;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Auth
{
    internal enum TokenOrigin
    {
        StandardInput,

        ClientCredentialFlags,

        ClientCredentialVariables,

        Store,

        None,
    }

    /// <summary>Which source the next call would take its token from, before taking it — what `auth status` reports.</summary>
    internal sealed record TokenPlan(TokenOrigin Origin, string Description);

    /// <summary>
    ///     The bearer token for a call, from the first source that offers one: `--token -` on standard input; `--client-id`
    ///     with `--client-secret`; `VION_CLIENT_ID` with `VION_CLIENT_SECRET`; the stored session of the environment,
    ///     refreshed when it is about to expire. One order for every call, so a script's token always wins over a person's
    ///     sign-in on the same machine.
    /// </summary>
    internal static class TokenSource
    {
        /// <summary>A stored token this close to its expiry is refreshed first, so the call it is sent on never meets a 401.</summary>
        public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);

        public static TokenPlan Plan(GlobalValues globals, ResolvedContext context, CliServices services)
        {
            if (globals.Token != null)
            {
                return new TokenPlan(TokenOrigin.StandardInput, "the token piped into --token -");
            }

            if (globals.ClientId != null || globals.ClientSecret != null)
            {
                if (globals.ClientId == null || globals.ClientSecret == null)
                {
                    throw new UsageException("--client-id and --client-secret go together; one of them is missing.");
                }

                return new TokenPlan(TokenOrigin.ClientCredentialFlags, $"the client credentials of --client-id {globals.ClientId}");
            }

            var clientId = Variable(services, "VION_CLIENT_ID");
            var clientSecret = Variable(services, "VION_CLIENT_SECRET");
            if (clientId != null && clientSecret != null)
            {
                return new TokenPlan(TokenOrigin.ClientCredentialVariables, $"the client credentials of VION_CLIENT_ID {clientId}");
            }

            var sessions = CredentialStore.Load(services.HomeDirectory);
            return sessions.ContainsKey(context.Environment.Name) ?
                       new TokenPlan(TokenOrigin.Store, "the stored credentials") :
                       new TokenPlan(TokenOrigin.None, "none: no sign-in, no automation credentials, no token");
        }

        public static async Task<string> ResolveAsync(GlobalValues globals, ResolvedContext context, CliServices services, CancellationToken cancellationToken)
        {
            var plan = Plan(globals, context, services);
            switch (plan.Origin)
            {
                case TokenOrigin.StandardInput:
                    return await ReadStandardInput(globals.Token!, services);
                case TokenOrigin.ClientCredentialFlags:
                {
                    using var realm = new OAuthClient(services);
                    return (await realm.ClientCredentialsAsync(context.Environment, globals.ClientId!, globals.ClientSecret!, cancellationToken)).AccessToken;
                }

                case TokenOrigin.ClientCredentialVariables:
                {
                    using var realm = new OAuthClient(services);
                    return (await realm.ClientCredentialsAsync(context.Environment, Variable(services, "VION_CLIENT_ID")!, Variable(services, "VION_CLIENT_SECRET")!, cancellationToken))
                        .AccessToken;
                }

                case TokenOrigin.Store:
                    return await FromStore(context.Environment, services, cancellationToken);
                default:
                    throw new
                        CliFailure($"no token for {context.Environment.Name}: sign in with `vion auth login`, set VION_CLIENT_ID and VION_CLIENT_SECRET to a service account's credentials, or pipe a token into `--token -`.");
            }
        }

        private static async Task<string> ReadStandardInput(string flag, CliServices services)
        {
            if (flag != "-")
            {
                throw new UsageException("--token takes `-` and reads the token from standard input, so that it never lands in a shell history.");
            }

            // One line, not the whole stream: `--input -` and `-F key=@-` read the body from the same standard input
            // after it, so a script pipes the token first and the document after.
            var token = (await services.In.ReadLineAsync())?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                throw new CliFailure("--token -: standard input carried no token.");
            }

            return token;
        }

        private static async Task<string> FromStore(ApiEnvironment environment, CliServices services, CancellationToken cancellationToken)
        {
            var sessions = CredentialStore.Load(services.HomeDirectory);
            var stored = sessions[environment.Name];
            if (stored.ExpiresAt - services.Time.GetUtcNow() > RefreshMargin)
            {
                return stored.AccessToken;
            }

            if (stored.RefreshToken == null)
            {
                throw new CliFailure($"the stored sign-in for {environment.Name} expired and holds no refresh token; run `vion auth login`.");
            }

            using var realm = new OAuthClient(services);
            var refreshed = await realm.RefreshAsync(environment, stored.RefreshToken, cancellationToken) ??
                            throw new CliFailure($"the stored sign-in for {environment.Name} expired and the realm refused to refresh it; run `vion auth login`.");
            CredentialStore.Put(services.HomeDirectory, environment.Name, refreshed);
            return refreshed.AccessToken;
        }

        private static string? Variable(CliServices services, string name)
        {
            return services.Environment.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        }
    }
}
