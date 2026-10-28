using System;
using System.Collections.Generic;
using System.Text;

namespace Vion.Cli.Derive
{
    /// <summary>
    ///     The one place a document name becomes a command-line name. The tool reads the names off the emitted table and
    ///     applies no rule of its own, so a test of this class is a test of every derived name.
    /// </summary>
    internal static class Naming
    {
        /// <summary>
        ///     Every option the tool defines on its own, in kebab case without the dashes. A derived option that would
        ///     take one of these names takes the <c>-param</c> suffix instead, and the tool asserts at test time that this
        ///     list is its own.
        /// </summary>
        public static readonly IReadOnlyList<string> GlobalOptionNames =
        [
            "env", "integrator", "tenant", "project", "platform",
            "json", "fields", "yes", "dry-run", "idempotency-key", "if-match",
            "input", "raw-field", "field", "file",
            "token", "client-id", "client-secret", "verbose", "help", "version",
        ];

        /// <summary>
        ///     Parameters the globals supply, by the parameter's name in the document: the four context keys fill the
        ///     root and project parameters wherever they appear, and <c>--dry-run</c> is the <c>dryRun</c> query flag.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> GlobalParameters = new Dictionary<string, string>(StringComparer.Ordinal)
                                                                                      {
                                                                                          ["tenantId"] = "tenant",
                                                                                          ["integratorId"] = "integrator",
                                                                                          ["platformId"] = "platform",
                                                                                          ["projectId"] = "project",
                                                                                          ["dryRun"] = "dry-run",
                                                                                      };

        private static readonly HashSet<string> GlobalOptionNameSet = new(GlobalOptionNames, StringComparer.Ordinal);

        /// <summary>camelCase to kebab-case: a dash before each upper-case letter that follows a letter or digit.</summary>
        public static string Kebab(string camelCase)
        {
            var builder = new StringBuilder(camelCase.Length + 4);
            for (var i = 0; i < camelCase.Length; i++)
            {
                var c = camelCase[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && char.IsLetterOrDigit(camelCase[i - 1]))
                    {
                        builder.Append('-');
                    }

                    builder.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        /// <summary>The derived option's name for a parameter a global does not supply, suffixed where it collides with a global.</summary>
        public static string OptionName(string parameterName)
        {
            var kebab = Kebab(parameterName);
            return GlobalOptionNameSet.Contains(kebab) ? kebab + "-param" : kebab;
        }

        /// <summary>A camelCase identifier as words, for a group's description: <c>edgeGateway</c> becomes <c>edge gateway</c>.</summary>
        public static string Words(string camelCase)
        {
            return Kebab(camelCase).Replace('-', ' ');
        }
    }
}