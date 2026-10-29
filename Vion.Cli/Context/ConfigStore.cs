using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace Vion.Cli.Context
{
    /// <summary>A named context: an environment and whichever of the four root values were given.</summary>
    internal sealed record ContextEntry(string Env, string? Integrator, string? Tenant, string? Project, string? Platform);

    /// <summary>
    ///     What <c>config.json</c> under the home directory holds: the current context, every context, the custom
    ///     environments.
    /// </summary>
    internal sealed record CliConfig(string? Current, IReadOnlyDictionary<string, ContextEntry> Contexts, IReadOnlyDictionary<string, ApiEnvironment> Environments)
    {
        public static readonly CliConfig Empty = new(null,
                                                     new Dictionary<string, ContextEntry>(StringComparer.Ordinal),
                                                     new Dictionary<string, ApiEnvironment>(StringComparer.OrdinalIgnoreCase));

        public ContextEntry? CurrentContext
        {
            get => Current != null && Contexts.TryGetValue(Current, out var entry) ? entry : null;
        }
    }

    internal static class ConfigStore
    {
        public static string PathOf(string homeDirectory)
        {
            return Path.Combine(homeDirectory, "config.json");
        }

        /// <summary>
        ///     The configuration, or the empty one when the file is absent; an unreadable file is a failure the user can see,
        ///     not a silent reset.
        /// </summary>
        public static CliConfig Load(string homeDirectory)
        {
            var path = PathOf(homeDirectory);
            if (!File.Exists(path))
            {
                return CliConfig.Empty;
            }

            JsonObject root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new Infrastructure.CliFailure($"{path} is not a JSON object.");
            }
            catch (System.Text.Json.JsonException exception)
            {
                throw new Infrastructure.CliFailure($"{path} is not valid JSON: {exception.Message}");
            }

            var contexts = new Dictionary<string, ContextEntry>(StringComparer.Ordinal);
            if (root["contexts"] is JsonObject contextObject)
            {
                foreach (var (name, value) in contextObject)
                {
                    if (value is JsonObject entry && entry["env"] is JsonValue env)
                    {
                        contexts[name] = new ContextEntry(env.ToString(), Text(entry, "integrator"), Text(entry, "tenant"), Text(entry, "project"), Text(entry, "platform"));
                    }
                }
            }

            var environments = new Dictionary<string, ApiEnvironment>(StringComparer.OrdinalIgnoreCase);
            if (root["environments"] is JsonObject environmentObject)
            {
                foreach (var (name, value) in environmentObject)
                {
                    if (value is JsonObject entry && Text(entry, "apiUrl") is { } apiUrl && Text(entry, "authUrl") is { } authUrl)
                    {
                        environments[name] = new ApiEnvironment(name, apiUrl, authUrl, Text(entry, "clientId") ?? ApiEnvironment.DefaultClientId);
                    }
                }
            }

            return new CliConfig(Text(root, "current"), contexts, environments);
        }

        private static string? Text(JsonObject node, string member)
        {
            return node[member] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }
    }
}