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
    ///     version than the running one is named in one line on standard error, with `vion update` where the install record
    ///     vouches for the running binary and the release page otherwise — an unrecorded copy belongs to whatever placed
    ///     it, and `update` would refuse it. Never in JSON mode, never when `VION_NO_UPDATE_CHECK` is set, silent on any
    ///     failure — a tool's update notice is never the reason a command fails.
    /// </summary>
    internal static class UpdateCheck
    {
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

                // A read that fails — a status, a timeout, no network — keeps what was known and still stamps today, so
                // the next command does not wait on the same network again; tomorrow retries.
                var (latest, url) = (cache?.Latest, cache?.Url);
                try
                {
                    (latest, url) = await ReadLatestAsync(services, cancellationToken) is var read && read.Latest != null ? read : (latest, url);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    // Kept: the known version, the date below.
                }

                Files.WriteAtomically(path,
                                      new JsonObject { ["checkedAt"] = now.ToString("o", CultureInfo.InvariantCulture), ["latest"] = latest, ["url"] = url }.ToJsonString(Indented),
                                      false);
                Notify(latest, url, services);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // A failed check is nobody's problem today: an unwritable home directory is the one way to land here.
            }
        }

        private static void Notify(string? latest, string? url, CliServices services)
        {
            if (latest == null || !Releases.IsNewer(latest, services.Version))
            {
                return;
            }

            var how = InstallRecord.Read(services)?.Vouches(services.ExecutablePath) == true ? " — run `vion update`" : url != null ? " — " + url : "";
            services.Error.WriteLine($"a newer vion is available: {latest} (running {services.Version}){how}");
        }

        private static async Task<(string? Latest, string? Url)> ReadLatestAsync(CliServices services, CancellationToken cancellationToken)
        {
            using var client = Releases.CreateClient(services, Timeout);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await client.GetAsync(Releases.Latest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (null, null);
            }

            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) as JsonObject;
            var tag = body?["tag_name"]?.ToString();
            return (tag != null ? Releases.NumericPart(tag) : null, body?["html_url"]?.ToString());
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