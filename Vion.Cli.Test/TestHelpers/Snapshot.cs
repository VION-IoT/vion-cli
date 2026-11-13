using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Vion.Cli.Test.TestHelpers
{
    /// <summary>
    ///     The committed snapshot, counted from the document itself rather than through the derived table, so a test over
    ///     the whole tree carries no number that a snapshot move would make stale.
    /// </summary>
    internal static class Snapshot
    {
        private static readonly string[] Methods = ["get", "put", "post", "delete", "patch"];

        private static readonly Lazy<string[]> OperationIds = new(ReadOperationIds);

        /// <summary>Every operation the document declares, the `system` ones included.</summary>
        public static int OperationCount
        {
            get => OperationIds.Value.Length;
        }

        /// <summary>The operations that become tree commands: every one outside the `system` domain.</summary>
        public static int TreeOperationCount
        {
            get => OperationIds.Value.Count(id => !id.StartsWith("system.", StringComparison.Ordinal));
        }

        /// <summary>The channels of the committed event document a row hands out: each one is a watch command.</summary>
        public static int WatchCount
        {
            get
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "asyncapi-v1.json")));
                return document.RootElement.GetProperty("channels").EnumerateObject().Count(channel => channel.Value.TryGetProperty("x-vion-handedOutBy", out _));
            }
        }

        private static string[] ReadOperationIds()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1.json")));
            return document.RootElement
                           .GetProperty("paths")
                           .EnumerateObject()
                           .SelectMany(path => path.Value.EnumerateObject().Where(method => Methods.Contains(method.Name)))
                           .Select(method => method.Value.GetProperty("operationId").GetString()!)
                           .ToArray();
        }
    }
}