using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vion.Cli.Auth
{
    /// <summary>
    ///     Reads the payload of a JWT without validating it — the tool only reports whose token it holds; the API is what
    ///     validates.
    /// </summary>
    internal static class Jwt
    {
        /// <summary>
        ///     The subject as a person reads it: `preferred_username`, else `email`, else `sub`; null for a token that is no
        ///     JWT.
        /// </summary>
        public static string? Subject(string token)
        {
            var payload = Payload(token);
            return Text(payload, "preferred_username") ?? Text(payload, "email") ?? Text(payload, "sub");
        }

        private static JsonObject? Payload(string token)
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            try
            {
                var base64 = parts[1].Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
                return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64))) as JsonObject;
            }
            catch (FormatException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? Text(JsonObject? node, string member)
        {
            return node?[member] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        }
    }
}