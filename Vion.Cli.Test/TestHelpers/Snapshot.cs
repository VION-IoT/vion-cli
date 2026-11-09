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

        /// <summary>Every operation the document declares, the `system` ones included.</summary>
        public static int OperationCount
        {
            get => OperationIds().Count();
        }

        /// <summary>The operations that become tree commands: every one outside the `system` domain.</summary>
        public static int TreeOperationCount
        {
            get => OperationIds().Count(id => !id.StartsWith("system.", StringComparison.Ordinal));
        }

        private static string[] OperationIds()
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1.json")));
            return document.RootElement.GetProperty("paths")
                           .EnumerateObject()
                           .SelectMany(path => path.Value.EnumerateObject().Where(method => Methods.Contains(method.Name)))
                           .Select(method => method.Value.GetProperty("operationId").GetString()!)
                           .ToArray();
        }
    }
}
