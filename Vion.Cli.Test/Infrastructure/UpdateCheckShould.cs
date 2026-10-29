using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Infrastructure
{
    /// <summary>The daily look at the latest release: when it runs, what it caches, and the one line it prints when a newer version exists.</summary>
    [TestClass]
    public class UpdateCheckShould
    {
        private const string Releases = "https://api.github.com/repos/VION-IoT/vion-cli/releases/latest";

        private const string Latest = """{ "tag_name": "v0.2.0", "html_url": "https://github.com/VION-IoT/vion-cli/releases/tag/v0.2.0" }""";

        private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.1")]
        public async Task NameNewerReleaseOnStandardErrorAndCacheIt()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, Latest);
            using var home = new TemporaryHome();
            var time = new FakeTimeProvider(Now);

            // Act
            var run = await Tool.RunAsync(handler, ["ops"], home: home.Root, time: time, errorTerminal: true, version: "0.1.0");

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual(Releases, handler.Requests[0].Uri.ToString());
            Assert.IsFalse(handler.Requests[0].Headers.ContainsKey("Authorization"));
            StringAssert.Contains(run.Error, "0.2.0");
            StringAssert.Contains(run.Error, "https://github.com/VION-IoT/vion-cli/releases/tag/v0.2.0");
            Assert.IsFalse(run.Out.Contains("0.2.0", StringComparison.Ordinal), run.Out);
            var cache = JsonNode.Parse(File.ReadAllText(Path.Combine(home.Root, "update-check.json")))!;
            Assert.AreEqual("0.2.0", cache["latest"]!.GetValue<string>());
            Assert.AreEqual(Now, DateTimeOffset.Parse(cache["checkedAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.1")]
        [DataRow(23, 0)]
        [DataRow(25, 1)]
        public async Task ReadReleaseAgainOnlyAfterADay(int hoursSinceCheck, int requests)
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, Latest);
            using var home = new TemporaryHome();
            var time = new FakeTimeProvider(Now);
            home.Write("update-check.json", $$"""{ "checkedAt": "{{Now.AddHours(-hoursSinceCheck).ToString("o", System.Globalization.CultureInfo.InvariantCulture)}}", "latest": "0.1.0", "url": null }""");

            // Act
            var run = await Tool.RunAsync(handler, ["ops"], home: home.Root, time: time, errorTerminal: true, version: "0.1.0");

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(requests, handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.1")]
        public async Task SkipWhenStandardErrorIsNotTerminal()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, Latest);
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler, ["ops"], home: home.Root, time: new FakeTimeProvider(Now), errorTerminal: false, version: "0.1.0");

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.IsEmpty(handler.Requests);
            Assert.IsFalse(File.Exists(Path.Combine(home.Root, "update-check.json")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.1")]
        public async Task SkipInJsonMode()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, Latest);
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler, ["ops", "--json"], home: home.Root, time: new FakeTimeProvider(Now), errorTerminal: true, version: "0.1.0");

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.IsEmpty(handler.Requests);
            Assert.AreEqual("", run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.1")]
        public async Task SkipWhenDisabledByVariable()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, Latest);
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["ops"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          errorTerminal: true,
                                          version: "0.1.0",
                                          environment: new Dictionary<string, string?> { ["VION_NO_UPDATE_CHECK"] = "1" });

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.1")]
        [DataRow("0.2.0")]
        [DataRow("0.3.1")]
        public async Task StaySilentWhenRunningVersionIsNotOlder(string running)
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, Latest);
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler, ["ops"], home: home.Root, time: new FakeTimeProvider(Now), errorTerminal: true, version: running);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(1, handler.Requests);
            Assert.IsFalse(run.Error.Contains("0.2.0", StringComparison.Ordinal), run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-007.1")]
        public async Task StaySilentWhenReleaseReadFailsAndRetryNextDay()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(503);
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler, ["ops"], home: home.Root, time: new FakeTimeProvider(Now), errorTerminal: true, version: "0.1.0");

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("264 operations\r\n".Replace("\r\n", Environment.NewLine, StringComparison.Ordinal), run.Error);
            var cache = JsonNode.Parse(File.ReadAllText(Path.Combine(home.Root, "update-check.json")))!;
            Assert.IsNull(cache["latest"]);
            Assert.AreEqual(Now, DateTimeOffset.Parse(cache["checkedAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
