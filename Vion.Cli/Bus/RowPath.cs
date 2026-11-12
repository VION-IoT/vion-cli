using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Vion.Cli.Bus
{
    /// <summary>
    ///     A member path as the event document writes one: names joined by dots, <c>[]</c> after a name that is a list.
    ///     <c>items[].properties[].identifier</c> is every property's identifier of every item.
    /// </summary>
    internal static class RowPath
    {
        public const string ListMark = "[]";

        public static IReadOnlyList<JsonNode> Select(JsonNode? root, string path)
        {
            IEnumerable<JsonNode> current = root == null ? [] : [root];
            foreach (var segment in path.Split('.'))
            {
                var isList = segment.EndsWith(ListMark, StringComparison.Ordinal);
                var name = isList ? segment[..^ListMark.Length] : segment;
                current = current.SelectMany(node => Step(node, name, isList));
            }

            return current.ToList();
        }

        private static IEnumerable<JsonNode> Step(JsonNode node, string name, bool isList)
        {
            if (node is not JsonObject obj || obj[name] is not { } child)
            {
                return [];
            }

            if (!isList)
            {
                return [child];
            }

            return child is JsonArray array ? array.Where(element => element != null).Select(element => element!) : [];
        }
    }
}
