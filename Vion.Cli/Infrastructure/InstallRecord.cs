using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vion.Cli.Infrastructure
{
    /// <summary>
    ///     `install.json` under the home directory: which binary an install script, or `update`, placed, and so which one
    ///     `update` may replace. It vouches for the running binary when its path names the process's own executable; a
    ///     copy a package manager or a person placed has no record naming it, and is left to whatever placed it.
    /// </summary>
    internal sealed record InstallRecord(string Path, string? Version, string? InstalledBy, string? InstalledAt)
    {
        public const string FileName = "install.json";

        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

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
                           new InstallRecord(record["path"]!.GetValue<string>(), Text(record["version"]), Text(record["installedBy"]), Text(record["installedAt"])) :
                           null;
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

        /// <summary>The record's text, written by <see cref="Files.WriteAtomically" /> to <see cref="Location" />.</summary>
        public static string Render(CliServices services, string path, string version, string installedBy)
        {
            var record = new JsonObject
                         {
                             ["path"] = path,
                             ["version"] = version,
                             ["installedBy"] = installedBy,
                             ["installedAt"] = services.Time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                         };
            return record.ToJsonString(Indented);
        }

        private static string? Text(JsonNode? node)
        {
            return node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : null;
        }
    }
}
