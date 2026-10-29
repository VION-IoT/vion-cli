using System;
using System.Collections.Generic;

namespace Vion.Cli.Context
{
    /// <summary>An environment: where the API is, where its realm is, and the client the tool signs in as.</summary>
    internal sealed record ApiEnvironment(string Name, string ApiUrl, string AuthUrl, string ClientId)
    {
        public const string DefaultClientId = "vion-cli";

        /// <summary>
        ///     The two environments the tool knows without configuration; any other is defined by `context use --api-url
        ///     --auth-url`.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, ApiEnvironment> BuiltIn = new Dictionary<string, ApiEnvironment>(StringComparer.OrdinalIgnoreCase)
                                                                                     {
                                                                                         ["production"] = new("production",
                                                                                                              "https://api.vion.swiss",
                                                                                                              "https://auth.vion.swiss/realms/vion",
                                                                                                              DefaultClientId),
                                                                                         ["test"] = new("test",
                                                                                                        "https://api.test.vion.swiss",
                                                                                                        "https://auth.test.vion.swiss/realms/vion",
                                                                                                        DefaultClientId),
                                                                                     };
    }
}