using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Vion.Cli.Test.TestHelpers
{
    /// <summary>
    ///     One release of vion as GitHub serves it to `update`: the API's release object, under `releases/latest` and
    ///     under its tag, and its archive and `.sha256` at their download URLs. The archive is built in the shape the
    ///     release workflow attaches — a tar.gz holding `vion`, a zip holding `vion.exe` — around bytes the test chose.
    /// </summary>
    internal sealed class FakeRelease
    {
        public const string Download = "https://github.com/VION-IoT/vion-cli/releases/download";

        public string Version { get; }

        public string Platform { get; }

        public byte[] Binary { get; }

        public byte[] Archive { get; }

        public string ArchiveName { get; }

        /// <summary>The `.sha256` file's text, `sha256sum` format; a test that wants a mismatch replaces it.</summary>
        public string Checksum { get; set; }

        public bool ArchiveAttached { get; set; } = true;

        public bool ChecksumAttached { get; set; } = true;

        public FakeRelease(string version, string platform)
        {
            Version = version;
            Platform = platform;
            Binary = Encoding.UTF8.GetBytes($"vion {version} for {platform} {Guid.NewGuid():N}");
            var windows = platform.StartsWith("win", StringComparison.Ordinal);
            ArchiveName = $"vion-{version}-{platform}{(windows ? ".zip" : ".tar.gz")}";
            Archive = windows ? Zip(Binary) : TarGz(Binary);
            Checksum = $"{Convert.ToHexStringLower(SHA256.HashData(Archive))}  {ArchiveName}\n";
        }

        /// <summary>A handler answering this release under `latest` when <paramref name="latest" />, and always under its tag.</summary>
        public RecordingHandler Handler(bool latest = true)
        {
            return new RecordingHandler(request =>
                                        {
                                            var url = request.RequestUri!.ToString();
                                            if ((latest && url == "https://api.github.com/repos/VION-IoT/vion-cli/releases/latest") ||
                                                url == $"https://api.github.com/repos/VION-IoT/vion-cli/releases/tags/v{Version}")
                                            {
                                                return RecordingHandler.Response(200, ReleaseJson());
                                            }

                                            if (ArchiveAttached && url == $"{Download}/v{Version}/{ArchiveName}")
                                            {
                                                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive) };
                                            }

                                            if (ChecksumAttached && url == $"{Download}/v{Version}/{ArchiveName}.sha256")
                                            {
                                                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(Checksum)) };
                                            }

                                            return new HttpResponseMessage(HttpStatusCode.NotFound);
                                        });
        }

        private string ReleaseJson()
        {
            var assets = new JsonArray();
            if (ArchiveAttached)
            {
                assets.Add(Asset(ArchiveName));
            }

            if (ChecksumAttached)
            {
                assets.Add(Asset(ArchiveName + ".sha256"));
            }

            return new JsonObject
                   {
                       ["tag_name"] = $"v{Version}", ["html_url"] = $"https://github.com/VION-IoT/vion-cli/releases/tag/v{Version}", ["assets"] = assets,
                   }.ToJsonString();
        }

        private JsonObject Asset(string name)
        {
            return new JsonObject { ["name"] = name, ["state"] = "uploaded", ["browser_download_url"] = $"{Download}/v{Version}/{name}" };
        }

        private static byte[] TarGz(byte[] binary)
        {
            using var archive = new MemoryStream();
            using (var gzip = new GZipStream(archive, CompressionLevel.Fastest, true))
            using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, false))
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "vion") { DataStream = new MemoryStream(binary) });
            }

            return archive.ToArray();
        }

        private static byte[] Zip(byte[] binary)
        {
            using var archive = new MemoryStream();
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
            using (var entry = zip.CreateEntry("vion.exe").Open())
            {
                entry.Write(binary);
            }

            return archive.ToArray();
        }
    }
}
