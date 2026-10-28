using System;
using System.Linq;
using Vion.Cli.Derived;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     Finds the operation a raw `api` call addresses, by method and path against the table's templates, so that the
    ///     rules an operation carries — its effect, its dry run — hold for the raw call too.
    /// </summary>
    internal static class OperationMatcher
    {
        public static Operation? Find(string method, string path)
        {
            var at = path.IndexOf('?', StringComparison.Ordinal);
            var segments = Split(at >= 0 ? path[..at] : path);
            return Operations.All.FirstOrDefault(operation => string.Equals(operation.Method, method, StringComparison.OrdinalIgnoreCase) &&
                                                              Matches(Split(operation.Template), segments));
        }

        private static string[] Split(string path)
        {
            return path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        ///     Segment by segment: a literal matches case-insensitively, as the host routes; a `{parameter}` matches any one
        ///     segment.
        /// </summary>
        private static bool Matches(string[] template, string[] actual)
        {
            if (template.Length != actual.Length)
            {
                return false;
            }

            for (var i = 0; i < template.Length; i++)
            {
                var isParameter = template[i].StartsWith('{') && template[i].EndsWith('}');
                if (!isParameter && !string.Equals(template[i], actual[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}