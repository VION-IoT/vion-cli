using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Auth
{
    /// <summary>One environment's session: the tokens, when the access token expires, and how it was obtained.</summary>
    internal sealed record StoredCredentials(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, string ObtainedBy);

    /// <summary>
    ///     `credentials.json` under the home directory, keyed by environment name, readable by the owner only. Read whole
    ///     and written whole: the file is small, and a partial update would need a lock the tool does not want.
    /// </summary>
    internal static class CredentialStore
    {
        /// <summary>An expiry as stored and shown: ISO 8601 to the second with its offset, which is what a person reads back.</summary>
        public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ssK";

        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

        public static string PathOf(string homeDirectory)
        {
            return Path.Combine(homeDirectory, "credentials.json");
        }

        public static Dictionary<string, StoredCredentials> Load(string homeDirectory)
        {
            var sessions = new Dictionary<string, StoredCredentials>(StringComparer.OrdinalIgnoreCase);
            var path = PathOf(homeDirectory);
            if (!File.Exists(path))
            {
                return sessions;
            }

            JsonObject root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new CliFailure($"{path} is not a JSON object.");
            }
            catch (JsonException exception)
            {
                throw new CliFailure($"{path} is not valid JSON: {exception.Message}");
            }

            foreach (var (environment, value) in root)
            {
                if (value is JsonObject entry && Text(entry, "accessToken") is { } accessToken && Text(entry, "expiresAt") is { } expiresAt)
                {
                    sessions[environment] = new StoredCredentials(accessToken,
                                                                  Text(entry, "refreshToken"),
                                                                  DateTimeOffset.Parse(expiresAt, CultureInfo.InvariantCulture),
                                                                  Text(entry, "obtainedBy") ?? "device");
                }
            }

            return sessions;
        }

        public static void Save(string homeDirectory, IReadOnlyDictionary<string, StoredCredentials> sessions)
        {
            var root = new JsonObject();
            foreach (var (environment, credentials) in sessions)
            {
                root[environment] = new JsonObject
                                    {
                                        ["accessToken"] = credentials.AccessToken,
                                        ["refreshToken"] = credentials.RefreshToken,
                                        ["expiresAt"] = credentials.ExpiresAt.ToString(TimestampFormat, CultureInfo.InvariantCulture),
                                        ["obtainedBy"] = credentials.ObtainedBy,
                                    };
            }

            Files.WriteAtomically(PathOf(homeDirectory), root.ToJsonString(Indented), true);
        }

        /// <summary>Stores one environment's session beside the others.</summary>
        public static void Put(string homeDirectory, string environment, StoredCredentials credentials)
        {
            var sessions = Load(homeDirectory);
            sessions[environment] = credentials;
            Save(homeDirectory, sessions);
        }

        /// <summary>Removes one environment's session; true when there was one.</summary>
        public static bool Remove(string homeDirectory, string environment)
        {
            var sessions = Load(homeDirectory);
            if (!sessions.Remove(environment))
            {
                return false;
            }

            Save(homeDirectory, sessions);
            return true;
        }

        private static string? Text(JsonObject node, string member)
        {
            return node[member] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }
    }
}