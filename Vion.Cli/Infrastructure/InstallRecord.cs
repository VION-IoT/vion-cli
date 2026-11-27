using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vion.Cli.Infrastructure
{
    /// <summary>
    ///     `install.json` under the home directory, written by the install scripts: the path of the binary they placed, and
    ///     so of the one `update` may replace. It vouches for the running binary when that path names the process's own
    ///     executable; a copy a package manager or a person placed has no record naming it, and is left to whatever placed
    ///     it.
    /// </summary>
    internal sealed record InstallRecord(string Path)
    {
        public const string FileName = "install.json";

        public static string Location(CliServices services)
        {
            return System.IO.Path.Combine(services.HomeDirectory, FileName);
        }

        /// <summary>The record, or none when there is no file or it is not a record: either way nothing is vouched for.</summary>
        public static InstallRecord? Read(CliServices services)
        {
            var location = Location(services);
            if (!File.Exists(location))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(File.ReadAllText(location)) is JsonObject record && record["path"]?.GetValueKind() == JsonValueKind.String ?
                           new InstallRecord(record["path"]!.GetValue<string>()) : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        ///     Both sides made full paths first, so a record written with a trailing separator or a relative folder still
        ///     matches; Windows' file names are case-insensitive, so its comparison is too.
        /// </summary>
        public bool Vouches(string? executablePath)
        {
            if (string.IsNullOrEmpty(executablePath) || string.IsNullOrWhiteSpace(Path))
            {
                return false;
            }

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(System.IO.Path.GetFullPath(Path), System.IO.Path.GetFullPath(executablePath), comparison);
        }
    }
}
