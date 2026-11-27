using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Commands
{
    /// <summary>
    ///     `vion update`: the install record decides whether this copy is replaced at all, the release's `.sha256` decides
    ///     whether anything on disk changes, and the binary at the recorded path is what changes. The installed binary is
    ///     a file under the temporary home, named to the tool through the executable-path seam.
    /// </summary>
    [TestClass]
    public class UpdateCommandShould
    {
        private static readonly DateTimeOffset Now = new(2026,
                                                         11,
                                                         27,
                                                         10,
                                                         0,
                                                         0,
                                                         TimeSpan.Zero);

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.2")]
        [DataRow(new[] { "update" }, "0.2.0", "https://api.github.com/repos/VION-IoT/vion-cli/releases/latest")]
        [DataRow(new[] { "update", "--version", "0.0.9" }, "0.0.9", "https://api.github.com/repos/VION-IoT/vion-cli/releases/tags/v0.0.9")]
        [DataRow(new[] { "update", "--version", "v0.0.9" }, "0.0.9", "https://api.github.com/repos/VION-IoT/vion-cli/releases/tags/v0.0.9")]
        public async Task ReplaceRecordedBinaryWithRelease(string[] args, string version, string releaseUrl)
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            var recordBefore = File.ReadAllText(InstallRecordPath(home));
            var release = new FakeRelease(version, "linux-musl-x64");
            using var handler = release.Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          args,
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual(releaseUrl, handler.Requests[0].Uri.ToString());
            CollectionAssert.AreEqual(release.Binary, File.ReadAllBytes(binary));
            Assert.AreEqual($"0.1.0 → {version}{Environment.NewLine}", run.Out);
            Assert.AreEqual(recordBefore, File.ReadAllText(InstallRecordPath(home)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.2")]
        [DataRow("0.2.0")]
        [DataRow("0.3.0")]
        public async Task ChangeNothingWhenLatestIsNotNewer(string running)
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            var before = File.ReadAllBytes(binary);
            using var handler = new FakeRelease("0.2.0", "linux-musl-x64").Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: running,
                                          executablePath: binary);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(1, handler.Requests);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(binary));
            StringAssert.StartsWith(run.Out, $"vion {running} is ");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.8")]
        public async Task RefuseDryRun()
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            var before = File.ReadAllBytes(binary);
            using var handler = new FakeRelease("0.2.0", "linux-musl-x64").Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update", "--dry-run"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary);

            // Assert
            Assert.AreEqual(2, run.ExitCode, run.Error);
            Assert.IsEmpty(handler.Requests);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(binary));
            StringAssert.Contains(run.Error, "`vion update --check`");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.5")]
        public async Task FailWithoutChangeWhenGitHubGivesNoAnswerInTime()
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            var before = File.ReadAllBytes(binary);
            using var handler = new RecordingHandler(_ => throw new TaskCanceledException("timed out", new TimeoutException()));

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary);

            // Assert
            Assert.AreEqual(1, run.ExitCode, run.Error);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(binary));
            Assert.AreEqual($"error: GitHub gave no answer for https://api.github.com/repos/VION-IoT/vion-cli/releases/latest within 10 minutes. Nothing was changed.{Environment.NewLine}",
                            run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.2")]
        [TestProperty("spec", "AC-CLI-003.1")]
        public async Task WriteOneDocumentUnderJson()
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            using var handler = new FakeRelease("0.2.0", "linux-musl-x64").Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update", "--json"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var document = JsonNode.Parse(run.Out)!;
            Assert.AreEqual("0.1.0", document["from"]!.GetValue<string>());
            Assert.AreEqual("0.2.0", document["to"]!.GetValue<string>());
            Assert.AreEqual(binary, document["path"]!.GetValue<string>());
            Assert.IsTrue(document["updated"]!.GetValue<bool>());
            Assert.AreEqual($"downloading vion-0.2.0-linux-musl-x64.tar.gz{Environment.NewLine}", run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.3")]
        public async Task RefuseWhenNoRecord()
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            var before = File.ReadAllBytes(binary);
            File.Delete(InstallRecordPath(home));
            using var handler = new FakeRelease("0.2.0", "linux-musl-x64").Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary);

            // Assert
            Assert.AreEqual(2, run.ExitCode, run.Error);
            Assert.IsEmpty(handler.Requests);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(binary));
            StringAssert.Contains(run.Error, binary);
            StringAssert.Contains(run.Error, "`curl -fsSL https://github.com/VION-IoT/vion-cli/releases/latest/download/install.sh | sh`");
            StringAssert.Contains(run.Error, "/usr/local/bin");
            Assert.IsFalse(run.Error.Contains("winget", StringComparison.Ordinal), run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.3")]
        public async Task RefuseWhenRecordNamesOtherBinary()
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            var before = File.ReadAllBytes(binary);
            var other = Path.Combine(home.Root, "elsewhere", "vion");
            WriteRecord(home, other);
            using var handler = new FakeRelease("0.2.0", "linux-musl-x64").Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary);

            // Assert
            Assert.AreEqual(2, run.ExitCode, run.Error);
            Assert.IsEmpty(handler.Requests);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(binary));
            StringAssert.Contains(run.Error, binary);
            StringAssert.Contains(run.Error, $"`{other} update`");
            StringAssert.Contains(run.Error, "`curl -fsSL https://github.com/VION-IoT/vion-cli/releases/latest/download/install.sh | sh`");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.3")]
        public async Task NameWindowsInstallScriptAndNoPackageManagerInRefusalOnWindows()
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            File.Delete(InstallRecordPath(home));
            using var handler = new FakeRelease("0.2.0", "win-x64").Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary,
                                          platform: "win-x64");

            // Assert
            Assert.AreEqual(2, run.ExitCode, run.Error);
            Assert.IsEmpty(handler.Requests);
            StringAssert.Contains(run.Error, binary);
            StringAssert.Contains(run.Error, "Update it the way it was installed");
            StringAssert.Contains(run.Error, "`irm https://github.com/VION-IoT/vion-cli/releases/latest/download/install.ps1 | iex`");
            Assert.IsFalse(run.Error.Contains("winget", StringComparison.Ordinal), run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.4")]
        public async Task ChangeNothingWhenArchiveFailsChecksum()
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            var before = File.ReadAllBytes(binary);
            var recordBefore = File.ReadAllText(InstallRecordPath(home));
            var listingBefore = Listing(home.Root);
            var release = new FakeRelease("0.2.0", "linux-musl-x64") { Checksum = new string('0', 64) + "  vion-0.2.0-linux-musl-x64.tar.gz\n" };
            using var handler = release.Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary);

            // Assert
            Assert.AreEqual(1, run.ExitCode, run.Error);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(binary));
            Assert.AreEqual(recordBefore, File.ReadAllText(InstallRecordPath(home)));
            CollectionAssert.AreEqual(listingBefore, Listing(home.Root));
            StringAssert.Contains(run.Error, new string('0', 64));
            StringAssert.Contains(run.Error, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(release.Archive)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.4")]
        [DataRow(false, true, "vion-0.2.0-linux-musl-x64.tar.gz")]
        [DataRow(true, false, "vion-0.2.0-linux-musl-x64.tar.gz.sha256")]
        public async Task ChangeNothingWhenReleaseLacksAsset(bool archiveAttached, bool checksumAttached, string missing)
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            var before = File.ReadAllBytes(binary);
            var recordBefore = File.ReadAllText(InstallRecordPath(home));
            var release = new FakeRelease("0.2.0", "linux-musl-x64") { ArchiveAttached = archiveAttached, ChecksumAttached = checksumAttached };
            using var handler = release.Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary);

            // Assert
            Assert.AreEqual(1, run.ExitCode, run.Error);
            Assert.HasCount(1, handler.Requests);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(binary));
            Assert.AreEqual(recordBefore, File.ReadAllText(InstallRecordPath(home)));
            StringAssert.Contains(run.Error, $"release v0.2.0 carries no {missing}.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.4")]
        public async Task ChangeNothingWhereNoArchiveIsPublishedForPlatform()
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            var before = File.ReadAllBytes(binary);
            using var handler = new FakeRelease("0.2.0", "linux-musl-x64").Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary,
                                          platform: null);

            // Assert
            Assert.AreEqual(1, run.ExitCode, run.Error);
            Assert.IsEmpty(handler.Requests);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(binary));
            StringAssert.Contains(run.Error, "no release archive is published for this operating system and processor");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.5")]
        public async Task ReportRecordedCopyAndChangeNothingOnCheck()
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            var before = File.ReadAllBytes(binary);
            var listingBefore = Listing(home.Root);
            using var handler = new FakeRelease("0.2.0", "linux-musl-x64").Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update", "--check", "--json"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(1, handler.Requests);
            var report = JsonNode.Parse(run.Out)!;
            Assert.AreEqual("0.1.0", report["running"]!.GetValue<string>());
            Assert.AreEqual("0.2.0", report["release"]!.GetValue<string>());
            Assert.IsTrue(report["newer"]!.GetValue<bool>());
            Assert.IsTrue(report["managed"]!.GetValue<bool>());
            CollectionAssert.AreEqual(before, File.ReadAllBytes(binary));
            CollectionAssert.AreEqual(listingBefore, Listing(home.Root));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.5")]
        public async Task ReportUnrecordedCopyAndChangeNothingOnCheck()
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root);
            var before = File.ReadAllBytes(binary);
            File.Delete(InstallRecordPath(home));
            var listingBefore = Listing(home.Root);
            using var handler = new FakeRelease("0.2.0", "linux-musl-x64").Handler();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update", "--check", "--json"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(1, handler.Requests);
            var report = JsonNode.Parse(run.Out)!;
            Assert.AreEqual("0.1.0", report["running"]!.GetValue<string>());
            Assert.AreEqual("0.2.0", report["release"]!.GetValue<string>());
            Assert.IsTrue(report["newer"]!.GetValue<bool>());
            Assert.IsFalse(report["managed"]!.GetValue<bool>());
            StringAssert.StartsWith(run.Error, $"no install record ({InstallRecordPath(home)}) names {binary}, so `vion update` leaves it alone.");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(binary));
            CollectionAssert.AreEqual(listingBefore, Listing(home.Root));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.6")]
        [OSCondition(OperatingSystems.Linux)]
        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        public async Task StageVerifiedBinaryAndNameSudoLineWhenFolderRefusesIt()
        {
            // Arrange
            using var home = new TemporaryHome();
            var folder = Path.Combine(home.Root, "bin");
            Directory.CreateDirectory(folder);
            var binary = Install(home, folder);
            var before = File.ReadAllBytes(binary);
            var recordBefore = File.ReadAllText(InstallRecordPath(home));
            var release = new FakeRelease("0.2.0", "linux-musl-x64");
            using var handler = release.Handler();
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["update"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          version: "0.1.0",
                                          executablePath: binary);

            // Assert
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Assert.AreEqual(1, run.ExitCode, run.Error);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(binary));
            Assert.AreEqual(recordBefore, File.ReadAllText(InstallRecordPath(home)));
            var line = Regex.Match(run.Error, @"sudo install -m 0755 '([^']+)' '([^']+)'");
            Assert.IsTrue(line.Success, run.Error);
            Assert.AreEqual(binary, line.Groups[2].Value);
            CollectionAssert.AreEqual(release.Binary, File.ReadAllBytes(line.Groups[1].Value));
            File.Delete(line.Groups[1].Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.7")]
        [OSCondition(OperatingSystems.Windows)]
        public async Task MoveReplacedBinaryAsideAndRemoveEarlierAsideOnWindows()
        {
            // Arrange
            using var home = new TemporaryHome();
            var binary = Install(home, home.Root, "vion.exe");
            var original = File.ReadAllBytes(binary);
            var release1 = new FakeRelease("0.2.0", "win-x64");
            var release2 = new FakeRelease("0.3.0", "win-x64");
            using var handler1 = release1.Handler();
            using var handler2 = release2.Handler();
            var run1 = await Tool.RunAsync(handler1,
                                           ["update"],
                                           home: home.Root,
                                           time: new FakeTimeProvider(Now),
                                           version: "0.1.0",
                                           executablePath: binary,
                                           platform: "win-x64");
            var aside1 = File.ReadAllBytes(binary + ".old");

            // Act
            var run2 = await Tool.RunAsync(handler2,
                                           ["update"],
                                           home: home.Root,
                                           time: new FakeTimeProvider(Now),
                                           version: "0.2.0",
                                           executablePath: binary,
                                           platform: "win-x64");

            // Assert
            Assert.AreEqual(0, run1.ExitCode, run1.Error);
            Assert.AreEqual(0, run2.ExitCode, run2.Error);
            CollectionAssert.AreEqual(original, aside1);
            CollectionAssert.AreEqual(release2.Binary, File.ReadAllBytes(binary));
            CollectionAssert.AreEqual(release1.Binary, File.ReadAllBytes(binary + ".old"));
            CollectionAssert.AreEqual(new[] { "install.json", "vion.exe", "vion.exe.old" }, Listing(home.Root));
        }

        /// <summary>
        ///     A binary of the test's own in <paramref name="folder" />, and a record naming it as an install script writes
        ///     one.
        /// </summary>
        private static string Install(TemporaryHome home, string folder, string name = "vion")
        {
            var binary = Path.Combine(folder, name);
            File.WriteAllText(binary, $"installed {Guid.NewGuid():N}");
            WriteRecord(home, binary);
            return binary;
        }

        private static void WriteRecord(TemporaryHome home, string path)
        {
            home.Write("install.json", new JsonObject { ["path"] = path }.ToJsonString());
        }

        private static string InstallRecordPath(TemporaryHome home)
        {
            return Path.Combine(home.Root, "install.json");
        }

        private static string[] Listing(string folder)
        {
            return Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
                            .Select(entry => Path.GetRelativePath(folder, entry))
                            .Order(StringComparer.Ordinal)
                            .ToArray();
        }
    }
}