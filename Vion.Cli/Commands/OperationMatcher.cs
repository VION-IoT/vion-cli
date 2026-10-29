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
        /// <summary>
        ///     Among the templates that match, the most specific wins: compared segment by segment, a literal before a
        ///     parameter, as the host routes — `arrangements/projects` is the projects list, not the `{collection}` read.
        /// </summary>
        public static Operation? Find(string method, string path)
        {
            var at = path.IndexOf('?', StringComparison.Ordinal);
            var segments = Split(at >= 0 ? path[..at] : path);
            return Operations.All
                             .Where(operation => string.Equals(operation.Method, method, StringComparison.OrdinalIgnoreCase))
                             .Select(operation => (Operation: operation, Template: Split(operation.Template)))
                             .Where(candidate => Matches(candidate.Template, segments))
                             .OrderByDescending(candidate => Specificity(candidate.Template), StringComparer.Ordinal)
                             .Select(candidate => candidate.Operation)
                             .FirstOrDefault();
        }

        private static string[] Split(string path)
        {
            return path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool IsParameter(string segment)
        {
            return segment.StartsWith('{') && segment.EndsWith('}');
        }

        /// <summary>One character per segment, `1` for a literal and `0` for a parameter; ordinal order is specificity order.</summary>
        private static string Specificity(string[] template)
        {
            return string.Concat(template.Select(segment => IsParameter(segment) ? '0' : '1'));
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
                if (!IsParameter(template[i]) && !string.Equals(template[i], actual[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}