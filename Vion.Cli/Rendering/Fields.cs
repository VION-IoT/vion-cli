using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Vion.Cli.Rendering
{
    /// <summary>
    ///     `--fields a,b.c`: each row of a list, or the object itself, kept to those members, by dotted path; a member
    ///     that is absent stays absent.
    /// </summary>
    internal static class Fields
    {
        public static JsonNode? Project(JsonNode? node, IReadOnlyList<string> fields)
        {
            if (fields.Count == 0 || node == null)
            {
                return node;
            }

            if (node is JsonObject envelope && envelope["items"] is JsonArray items)
            {
                var projected = new JsonObject { ["items"] = new JsonArray(items.Select(item => ProjectOne(item, fields)).ToArray()) };
                foreach (var (name, value) in envelope.Where(member => member.Key != "items"))
                {
                    projected[name] = value?.DeepClone();
                }

                return projected;
            }

            return ProjectOne(node, fields);
        }

        public static bool TryGet(JsonNode? node, string dottedPath, out JsonNode? value)
        {
            value = node;
            foreach (var segment in dottedPath.Split('.'))
            {
                if (value is JsonObject obj && obj.ContainsKey(segment))
                {
                    value = obj[segment];
                }
                else
                {
                    value = null;
                    return false;
                }
            }

            return true;
        }

        private static JsonNode? ProjectOne(JsonNode? node, IReadOnlyList<string> fields)
        {
            if (node is not JsonObject source)
            {
                return node?.DeepClone();
            }

            var result = new JsonObject();
            foreach (var field in fields)
            {
                if (TryGet(source, field, out var value))
                {
                    result[field] = value?.DeepClone();
                }
            }

            return result;
        }
    }
}