using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Vion.Cli.Infrastructure
{
    /// <summary>
    ///     Once a day, on a terminal, the latest release is read from GitHub and cached under the home directory; a newer
    ///     version than the running one is named in one line on standard error. Never in JSON mode, never when
    ///     `VION_NO_UPDATE_CHECK` is set, silent on any failure — a tool's update notice is never the reason a command fails.
    /// </summary>
    internal static class UpdateCheck
    {
        public const string ReleasesUrl = "https://api.github.com/repos/VION-IoT/vion-cli/releases/latest";

        private static readonly TimeSpan Daily = TimeSpan.FromDays(1);

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

        public static async Task RunAsync(bool json, CliServices services, CancellationToken cancellationToken)
        {
            if (json || !services.Console.IsErrorTerminal || (services.Environment.TryGetValue("VION_NO_UPDATE_CHECK", out var disabled) && !string.IsNullOrWhiteSpace(disabled)))
            {
                return;
            }

            try
            {
                var path = Path.Combine(services.HomeDirectory, "update-check.json");
                var cache = Read(path);
                var now = services.Time.GetUtcNow();
                if (cache is { } cached && cached.CheckedAt is { } checkedAt && now - checkedAt < Daily)
                {
                    Notify(cached.Latest, cached.Url, services);
                    return;
                }

                var (latest, url) = await ReadLatestAsync(services, cancellationToken);
                Files.WriteAtomically(path,
                                      new JsonObject { ["checkedAt"] = now.ToString("o", CultureInfo.InvariantCulture), ["latest"] = latest, ["url"] = url }.ToJsonString(Indented),
                                      false);
                Notify(latest, url, services);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A failed check is nobody's problem today; the cache's date, when it was written, retries it tomorrow.
            }
        }

        private static void Notify(string? latest, string? url, CliServices services)
        {
            if (latest != null && IsNewer(latest, services.Version))
            {
                services.Error.WriteLine($"a newer vion is available: {latest} (running {services.Version}){(url != null ? " — " + url : "")}");
            }
        }

        /// <summary>Numeric parts only: a prerelease or build suffix is set aside, so `0.2.0-preview.1` compares as `0.2.0`.</summary>
        private static bool IsNewer(string latest, string running)
        {
            return Version.TryParse(Numeric(latest), out var latestVersion) && Version.TryParse(Numeric(running), out var runningVersion) && latestVersion > runningVersion;
        }

        private static string Numeric(string version)
        {
            var text = version.TrimStart('v', 'V');
            var end = text.IndexOfAny(['-', '+']);
            return end >= 0 ? text[..end] : text;
        }

        private static async Task<(string? Latest, string? Url)> ReadLatestAsync(CliServices services, CancellationToken cancellationToken)
        {
            using var client = new HttpClient(services.Handler, false) { Timeout = Timeout };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("vion", Numeric(services.Version)));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await client.GetAsync(new Uri(ReleasesUrl), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (null, null);
            }

            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) as JsonObject;
            var tag = body?["tag_name"]?.ToString();
            return (tag != null ? Numeric(tag) : null, body?["html_url"]?.ToString());
        }

        private static (DateTimeOffset? CheckedAt, string? Latest, string? Url)? Read(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var cache = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                var checkedAt = cache?["checkedAt"]?.ToString();
                return (checkedAt != null && DateTimeOffset.TryParse(checkedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null,
                           cache?["latest"]?.ToString(), cache?["url"]?.ToString());
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}