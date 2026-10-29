using System;
using System.Collections.Generic;
using Vion.Cli.Commands;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Context
{
    /// <summary>
    ///     The environment and the root values a call runs with, after the flags, the variables and the current context
    ///     were read in that order.
    /// </summary>
    internal sealed record ResolvedContext(ApiEnvironment Environment, IReadOnlyDictionary<string, string?> Roots)
    {
        public string? Root(string key)
        {
            return Roots.TryGetValue(key, out var value) ? value : null;
        }
    }

    internal static class ContextResolver
    {
        public static readonly IReadOnlyList<string> RootKeys = ["integrator", "tenant", "project", "platform"];

        public static ResolvedContext Resolve(GlobalValues globals, CliServices services)
        {
            var config = ConfigStore.Load(services.HomeDirectory);
            var current = config.CurrentContext;

            var envName = globals.Env ?? Variable(services, "VION_ENV") ?? current?.Env;
            var apiUrl = Variable(services, "VION_API_URL");
            var authUrl = Variable(services, "VION_AUTH_URL");

            ApiEnvironment environment;
            if (envName != null)
            {
                if (!ApiEnvironment.BuiltIn.TryGetValue(envName, out var known) && !config.Environments.TryGetValue(envName, out known))
                {
                    throw new
                        UsageException($"unknown environment '{envName}': the built-in ones are production and test; another is defined with `vion context use --env {envName} --api-url <url> --auth-url <realm url>`.");
                }

                environment = known with { ApiUrl = apiUrl ?? known.ApiUrl, AuthUrl = authUrl ?? known.AuthUrl };
            }
            else if (apiUrl != null)
            {
                environment = new ApiEnvironment("custom", apiUrl, authUrl ?? "", ApiEnvironment.DefaultClientId);
            }
            else
            {
                throw new UsageException("no environment selected: pass --env production or --env test, set VION_ENV, or run `vion context use --env <name>`.");
            }

            // The current context's roots apply only to its own environment: a tenant of production is no tenant of test.
            var contextApplies = current != null && string.Equals(current.Env, environment.Name, StringComparison.OrdinalIgnoreCase);
            var roots = new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            ["integrator"] = globals.Integrator ?? (contextApplies ? current!.Integrator : null),
                            ["tenant"] = globals.Tenant ?? (contextApplies ? current!.Tenant : null),
                            ["project"] = globals.Project ?? (contextApplies ? current!.Project : null),
                            ["platform"] = globals.Platform ?? (contextApplies ? current!.Platform : null),
                        };

            return new ResolvedContext(environment, roots);
        }

        private static string? Variable(CliServices services, string name)
        {
            return services.Environment.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        }
    }
}