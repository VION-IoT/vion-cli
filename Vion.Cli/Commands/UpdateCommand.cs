using System;
using System.CommandLine;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Infrastructure;
using Vion.Cli.Rendering;

namespace Vion.Cli.Commands
{
    /// <summary>
    ///     `vion update [--version x] [--check]`: replaces the binary the install record names with a release's, after
    ///     its archive matched its `.sha256`. A copy the record does not name is refused before any request, because
    ///     whatever placed it — winget, a person — would no longer know what is installed.
    /// </summary>
    /// <remarks>
    ///     "Windows" is decided twice, on purpose. <see cref="CliServices.Platform" /> names what is published for this
    ///     binary: the archive's name and format, and the install line and package manager the refusal names. The
    ///     operating system the process runs on decides how its file system behaves: the rename aside, the executable
    ///     mode, the command that places a staged binary.
    /// </remarks>
    internal static class UpdateCommand
    {
        /// <summary>The archive is about ten megabytes; the bound only keeps a stalled connection from hanging the command.</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

        public static Command Create(CliTree tree, CliServices services)
        {
            var version = new Option<string>("--version")
                          { Description = "Install this release instead of the latest: a newer one, or an older one to roll back to.", HelpName = "version" };
            var check = new Option<bool>("--check") { Description = "Only report the running version, the release's and whether this copy can be updated." };
            var command = new Command("update",
                                      "Replace this vion with the latest release, or the one --version names, where an install script placed it; verified against the release's checksum first.");
            command.Options.Add(version);
            command.Options.Add(check);
            command.SetAction((result, cancellationToken) =>
                              {
                                  var globals = tree.Globals.Read(result);
                                  return Guard.RunAsync(globals, services, () => RunAsync(globals, result.GetValue(version), result.GetValue(check), services, cancellationToken));
                              });
            tree.Update = command;
            return command;
        }

        private static async Task<int> RunAsync(GlobalValues globals, string? requested, bool check, CliServices services, CancellationToken cancellationToken)
        {
            if (globals.DryRun)
            {
                throw new UsageException("--dry-run: `update` offers no dry run; `vion update --check` reports what it would install.");
            }

            var record = InstallRecord.Read(services);
            var managed = record != null && record.Vouches(services.ExecutablePath);
            if (!managed && !check)
            {
                throw new UsageException(Refusal(record, services));
            }

            var platform = services.Platform;
            if (platform == null && !check)
            {
                throw new CliFailure("no release archive is published for this operating system and processor, so `vion update` has nothing to install. Nothing was changed.");
            }

            using var client = Releases.CreateClient(services, Timeout);
            var release = await ReadReleaseAsync(client, requested, cancellationToken);
            var target = Releases.WithoutV(release["tag_name"]!.GetValue<string>());
            var running = Releases.WithoutV(services.Version);
            if (check)
            {
                return Report(globals, services, running, target, managed ? null : Refusal(record, services));
            }

            // A version asked for by name is installed whenever it is not the running one, which is how a rollback
            // works; the latest is installed only when newer, so a build ahead of the release is never replaced unasked.
            var differs = requested != null ? !string.Equals(target, running, StringComparison.Ordinal) : Releases.IsNewer(target, running);
            var path = record!.Path;
            if (!differs)
            {
                if (globals.Json)
                {
                    Renderer.WriteJson(new JsonObject { ["from"] = running, ["to"] = running, ["release"] = target, ["path"] = path, ["updated"] = false }, services);
                }
                else
                {
                    services.Out.WriteLine(requested == null && Releases.IsNewer(running, target) ?
                                               $"vion {running} is newer than the latest release, {target}; `vion update --version {target}` installs it." :
                                               $"vion {running} is current: the {(requested != null ? "requested" : "latest")} release is {target}.");
                }

                return ExitCodes.Ok;
            }

            var binary = await DownloadVerifiedAsync(client,
                                                     release,
                                                     target,
                                                     platform!,
                                                     services,
                                                     cancellationToken);

            // Rendered before the swap: once the file at the running binary's path is the new release, a build that
            // loads code from that file on first use — a single-file build that is not NativeAOT — would load the
            // other release's. The record names the path, which the swap keeps, so it is left as it is.
            var output = globals.Json ? Renderer.Json(new JsonObject { ["from"] = running, ["to"] = target, ["release"] = target, ["path"] = path, ["updated"] = true }) :
                             $"{running} → {target}";
            Replace(path, binary, target);
            services.Out.WriteLine(output);
            return ExitCodes.Ok;
        }

        private static async Task<JsonObject> ReadReleaseAsync(HttpClient client, string? requested, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requested == null ? Releases.Latest : Releases.Tagged(requested));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await SendAsync(client, request, cancellationToken);
            if (requested != null && response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new CliFailure($"there is no release v{Releases.WithoutV(requested)} of vion; the releases are listed at https://github.com/VION-IoT/vion-cli/releases.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CliFailure($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase} to the read of the release; nothing was changed.");
            }

            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken)) is JsonObject release && release["tag_name"] is JsonValue ? release :
                       throw new CliFailure("GitHub's answer to the read of the release names no tag; nothing was changed.");
        }

        private static async Task<byte[]> DownloadVerifiedAsync(HttpClient client,
                                                                JsonObject release,
                                                                string target,
                                                                string platform,
                                                                CliServices services,
                                                                CancellationToken cancellationToken)
        {
            var windows = IsWindows(platform);
            var archiveName = $"vion-{target}-{platform}{(windows ? ".zip" : ".tar.gz")}";
            var archiveUrl = Asset(release, archiveName);
            var checksumUrl = Asset(release, archiveName + ".sha256");
            if (archiveUrl == null || checksumUrl == null)
            {
                throw new
                    CliFailure($"release v{target} carries no {(archiveUrl == null ? archiveName : archiveName + ".sha256")}. Its build attaches the archives in the minutes after the release is published: try again shortly, or pass --version for an earlier release. Nothing was changed.");
            }

            services.Error.WriteLine($"downloading {archiveName}");
            var archive = await DownloadAsync(client, archiveUrl, cancellationToken);
            var checksum = System.Text.Encoding.UTF8.GetString(await DownloadAsync(client, checksumUrl, cancellationToken));
            var expected = checksum.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
            var actual = Convert.ToHexStringLower(SHA256.HashData(archive));
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new
                    CliFailure($"{archiveName} does not match its .sha256: the checksum file says {expected ?? "nothing"}, the download hashes to {actual}. Nothing was changed.");
            }

            return windows ? FromZip(archive, "vion.exe", archiveName) : FromTarGz(archive, "vion", archiveName);
        }

        /// <summary>An asset still uploading is listed with another state, and its URL answers nothing yet.</summary>
        private static Uri? Asset(JsonObject release, string name)
        {
            var asset = (release["assets"] as JsonArray ?? []).OfType<JsonObject>()
                                                              .FirstOrDefault(a => a["name"]?.ToString() == name && (a["state"] == null || a["state"]!.ToString() == "uploaded"));
            return asset?["browser_download_url"]?.ToString() is { } url ? new Uri(url) : null;
        }

        private static async Task<byte[]> DownloadAsync(HttpClient client, Uri url, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendAsync(client, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CliFailure($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase} to the download of {url}; nothing was changed.");
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await client.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new NoAnswer($"could not reach GitHub for {request.RequestUri}: {exception.Message} Nothing was changed.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new NoAnswer($"GitHub gave no answer for {request.RequestUri} within {client.Timeout.TotalMinutes:0} minutes. Nothing was changed.");
            }
        }

        private static byte[] FromTarGz(byte[] archive, string entryName, string archiveName)
        {
            using var gzip = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress);
            using var reader = new TarReader(gzip);
            while (reader.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && entry.Name.TrimStart('.', '/') == entryName && entry.DataStream != null)
                {
                    using var binary = new MemoryStream();
                    entry.DataStream.CopyTo(binary);
                    return binary.ToArray();
                }
            }

            throw new CliFailure($"{archiveName} holds no {entryName}; nothing was changed.");
        }

        private static byte[] FromZip(byte[] archive, string entryName, string archiveName)
        {
            using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
            var entry = zip.GetEntry(entryName) ?? throw new CliFailure($"{archiveName} holds no {entryName}; nothing was changed.");
            using var stream = entry.Open();
            using var binary = new MemoryStream();
            stream.CopyTo(binary);
            return binary.ToArray();
        }

        /// <summary>
        ///     The new binary is written beside the old one first, so the last step is a rename inside one folder. On Linux
        ///     that rename replaces the old name atomically and a running process keeps the file it started from. Windows
        ///     refuses to overwrite or delete a running executable but lets it be renamed, so the old one is moved aside to
        ///     `.old` and the next update removes it.
        /// </summary>
        private static void Replace(string path, byte[] binary, string target)
        {
            var full = Path.GetFullPath(path);
            var folder = Path.GetDirectoryName(full)!;
            var name = Path.GetFileName(full);
            var temporary = Path.Combine(folder, $".{name}.{Guid.NewGuid():N}.tmp");
            try
            {
                WriteExecutable(temporary, binary);
                if (OperatingSystem.IsWindows())
                {
                    RemoveLeftAside(folder, name);
                    var aside = AsideName(folder, name);
                    File.Move(full, aside);
                    try
                    {
                        File.Move(temporary, full);
                    }
                    catch (Exception)
                    {
                        File.Move(aside, full);
                        throw;
                    }
                }
                else
                {
                    File.Move(temporary, full, true);
                }
            }
            catch (UnauthorizedAccessException)
            {
                TryDelete(temporary);
                throw Staged(full, binary, target);
            }
            catch (Exception)
            {
                TryDelete(temporary);
                throw;
            }
        }

        /// <summary>
        ///     A folder this user may not write, such as `/usr/local/bin`, is never escalated into: the verified binary is
        ///     left in the temporary folder with the one command that places it, for the person to run with the rights
        ///     they choose.
        /// </summary>
        private static CliFailure Staged(string path, byte[] binary, string target)
        {
            var windows = OperatingSystem.IsWindows();
            var staged = Path.Combine(Path.GetTempPath(), $"vion-{target}-{Guid.NewGuid():N}{(windows ? ".exe" : "")}");
            WriteExecutable(staged, binary);
            var place = windows ? $"Move-Item -Force {PowerShellQuote(staged)} {PowerShellQuote(path)}   # from an administrator PowerShell" :
                            $"sudo install -m 0755 {ShellQuote(staged)} {ShellQuote(path)}";
            return new
                CliFailure($"this user may not write {Path.GetDirectoryName(path)}, so {path} is unchanged. The verified v{target} is at {staged}; place it with:{Environment.NewLine}  {place}");
        }

        private static void WriteExecutable(string path, byte[] binary)
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                         UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            }

            using var stream = new FileStream(path, options);
            stream.Write(binary);
        }

        /// <summary>
        ///     An aside still held by a process started before the last update cannot be removed yet; the next update tries
        ///     again.
        /// </summary>
        private static void RemoveLeftAside(string folder, string name)
        {
            foreach (var aside in Directory.EnumerateFiles(folder, name + "*.old"))
            {
                TryDelete(aside);
            }
        }

        private static string AsideName(string folder, string name)
        {
            var aside = Path.Combine(folder, name + ".old");
            for (var n = 1; File.Exists(aside); n++)
            {
                aside = Path.Combine(folder, $"{name}.{n}.old");
            }

            return aside;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Left for the next update.
            }
        }

        private static string Refusal(InstallRecord? record, CliServices services)
        {
            var executable = services.ExecutablePath ?? "this vion";
            var why = record == null ? $"no install record ({InstallRecord.Location(services)}) names {executable}" :
                          $"the install record names {record.Path}, not {executable}; to update that one, run `{record.Path} update`";
            var windows = services.Platform is { } platform ? IsWindows(platform) : OperatingSystem.IsWindows();
            return windows ?
                       $"{why}, so `vion update` leaves it alone. Update it the way it was installed, `winget upgrade vion` for winget's copy, or switch to a managed install with `irm {Releases.InstallScriptPs1} | iex`." :
                       $"{why}, so `vion update` leaves it alone. Update it the way it was installed, or switch to a managed install with `curl -fsSL {Releases.InstallScriptSh} | sh`; an older copy placed by hand, for example in /usr/local/bin, can then be removed.";
        }

        private static bool IsWindows(string platform)
        {
            return platform.StartsWith("win", StringComparison.Ordinal);
        }

        /// <param name="refusal">What `update` would refuse with, for a copy the record does not vouch for; none for one it does.</param>
        private static int Report(GlobalValues globals, CliServices services, string running, string target, string? refusal)
        {
            var newer = Releases.IsNewer(target, running);
            var report = new JsonObject { ["running"] = running, ["release"] = target, ["newer"] = newer, ["managed"] = refusal == null, ["path"] = services.ExecutablePath };
            if (globals.Json)
            {
                Renderer.WriteJson(report, services);
            }
            else
            {
                Renderer.WriteTable(report, globals, services);
            }

            if (refusal != null)
            {
                services.Error.WriteLine(refusal);
            }
            else if (newer)
            {
                services.Error.WriteLine("`vion update` installs it.");
            }

            return ExitCodes.Ok;
        }

        private static string ShellQuote(string text)
        {
            return "'" + text.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        }

        private static string PowerShellQuote(string text)
        {
            return "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
        }
    }
}