using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Vion.Cli.Infrastructure
{
    /// <summary>
    ///     vion's releases on GitHub, as the daily check and `update` read them: the API's view of one release, its
    ///     assets by name, and the version arithmetic both apply to a tag.
    /// </summary>
    internal static class Releases
    {
        public const string Api = "https://api.github.com/repos/VION-IoT/vion-cli/releases";

        public const string InstallScriptSh = "https://github.com/VION-IoT/vion-cli/releases/latest/download/install.sh";

        public const string InstallScriptPs1 = "https://github.com/VION-IoT/vion-cli/releases/latest/download/install.ps1";

        public static Uri Latest
        {
            get => new($"{Api}/latest");
        }

        public static Uri Tagged(string version)
        {
            return new Uri($"{Api}/tags/v{Uri.EscapeDataString(Numeric(version, false))}");
        }

        /// <summary>A client on the process's one handler; GitHub's API refuses a request without a user agent.</summary>
        public static HttpClient CreateClient(CliServices services, TimeSpan timeout)
        {
            var client = new HttpClient(services.Handler, false) { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("vion", Numeric(services.Version)));
            return client;
        }

        /// <summary>Numeric parts only: a prerelease or build suffix is set aside, so `0.2.0-preview.1` compares as `0.2.0`.</summary>
        public static bool IsNewer(string latest, string running)
        {
            return Version.TryParse(Numeric(latest), out var latestVersion) && Version.TryParse(Numeric(running), out var runningVersion) && latestVersion > runningVersion;
        }

        /// <summary>A tag without its `v`; with <paramref name="numericOnly" />, without a prerelease or build suffix either.</summary>
        public static string Numeric(string version, bool numericOnly = true)
        {
            var text = version.Trim().TrimStart('v', 'V');
            if (!numericOnly)
            {
                return text;
            }

            var end = text.IndexOfAny(['-', '+']);
            return end >= 0 ? text[..end] : text;
        }
    }
}
