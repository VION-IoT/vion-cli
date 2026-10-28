using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Vion.Cli.Derive
{
    /// <summary>
    ///     What <c>cli.json</c> says about an operation: whether its command is hidden from help and under which alias it
    ///     is also reachable. Facts live in the document; this file holds presentation only, keyed by operationId.
    /// </summary>
    internal sealed record PresentationEntry(bool Hide, string? Alias);

    internal sealed class Presentation
    {
        private readonly Dictionary<string, PresentationEntry> _entries;

        public static Presentation Empty { get; } = new([]);

        public IEnumerable<string> OperationIds
        {
            get => _entries.Keys;
        }

        private Presentation(Dictionary<string, PresentationEntry> entries)
        {
            _entries = entries;
        }

        public static Presentation Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new DeriveException("cli.json must be an object keyed by operationId.");
            }

            var entries = new Dictionary<string, PresentationEntry>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new DeriveException($"cli.json: the entry for '{property.Name}' must be an object.");
                }

                var hide = property.Value.TryGetProperty("hide", out var hideElement) && hideElement.ValueKind == JsonValueKind.True;
                var alias = property.Value.TryGetProperty("alias", out var aliasElement) && aliasElement.ValueKind == JsonValueKind.String ? aliasElement.GetString() : null;

                // `picker` is read by the spec that defines pickers; until then it is accepted and carried nowhere.
                entries[property.Name] = new PresentationEntry(hide, alias);
            }

            return new Presentation(entries);
        }

        public PresentationEntry? For(string operationId)
        {
            return _entries.TryGetValue(operationId, out var entry) ? entry : null;
        }
    }
}