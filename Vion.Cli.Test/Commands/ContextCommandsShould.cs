using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Commands
{
    /// <summary>`vion context use|list|show`: what `config.json` holds afterwards, which environments the tool knows, and what it prints.</summary>
    [TestClass]
    public class ContextCommandsShould
    {
        private const string Project = "33333333-3333-3333-3333-333333333333";

        private const string Integrator = "44444444-4444-4444-4444-444444444444";

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.2")]
        public async Task StoreContextNamedAfterEnvironmentWithGivenRootsAndMakeItCurrent()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler, ["context", "use", "--env", "test", "--tenant", Tool.Tenant, "--project", Project], home: home.Root);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var config = JsonNode.Parse(File.ReadAllText(Path.Combine(home.Root, "config.json")))!;
            Assert.AreEqual("test", config["current"]!.GetValue<string>());
            var context = config["contexts"]!["test"]!;
            Assert.AreEqual("test", context["env"]!.GetValue<string>());
            Assert.AreEqual(Tool.Tenant, context["tenant"]!.GetValue<string>());
            Assert.AreEqual(Project, context["project"]!.GetValue<string>());
            Assert.IsNull(context["integrator"]);
            Assert.IsNull(context["platform"]);
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.2")]
        public async Task UpdateNamedContextKeepingValuesNotGiven()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);
            using var home = new TemporaryHome();
            home.Write("config.json", $$$"""{ "current": "other", "contexts": { "other": { "env": "production" }, "work": { "env": "test", "tenant": "{{{Tool.Tenant}}}" } } }""");

            // Act
            var run = await Tool.RunAsync(handler, ["context", "use", "work", "--project", Project], home: home.Root);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var config = JsonNode.Parse(File.ReadAllText(Path.Combine(home.Root, "config.json")))!;
            Assert.AreEqual("work", config["current"]!.GetValue<string>());
            Assert.AreEqual(Tool.Tenant, config["contexts"]!["work"]!["tenant"]!.GetValue<string>());
            Assert.AreEqual(Project, config["contexts"]!["work"]!["project"]!.GetValue<string>());
            Assert.AreEqual("production", config["contexts"]!["other"]!["env"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.2")]
        public async Task SelectExistingContextByNameAlone()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);
            using var home = new TemporaryHome();
            home.Write("config.json", $$$"""{ "current": "other", "contexts": { "other": { "env": "production" }, "work": { "env": "test", "tenant": "{{{Tool.Tenant}}}" } } }""");

            // Act
            var run = await Tool.RunAsync(handler, ["context", "use", "work"], home: home.Root);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var config = JsonNode.Parse(File.ReadAllText(Path.Combine(home.Root, "config.json")))!;
            Assert.AreEqual("work", config["current"]!.GetValue<string>());
            Assert.AreEqual(Tool.Tenant, config["contexts"]!["work"]!["tenant"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.2")]
        [DataRow("context", "use", "nowhere")]
        [DataRow("context", "use", "--env", "test", "--tenant", "not-a-guid")]
        [DataRow("context", "use", "fresh", "--tenant", "11111111-1111-1111-1111-111111111111")]
        public async Task RefuseUnknownNameAloneNonGuidRootOrNewContextWithoutEnvironment(params string[] args)
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler, args, home: home.Root);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            Assert.IsFalse(File.Exists(Path.Combine(home.Root, "config.json")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.1")]
        [DataRow("production", "https://api.vion.swiss/")]
        [DataRow("test", "https://api.test.vion.swiss/")]
        public async Task KnowBuiltInEnvironmentUrls(string environment, string apiUrl)
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "items": [] }""");

            // Act
            var run = await Tool.RunAsync(handler, ["tenant", "edge-gateway", "list", "--tenant", Tool.Tenant, "--env", environment, "--token", "-"], Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual($"{apiUrl}tenant/{Tool.Tenant}/edgeGateways", handler.Requests[0].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.1")]
        public async Task DefineCustomEnvironmentFromUrlsAndCallIt()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "items": [] }""");
            using var home = new TemporaryHome();

            // Act
            var defined = await Tool.RunAsync(handler,
                                              ["context", "use", "--env", "acme", "--api-url", "https://api.example.test", "--auth-url", "https://auth.example.test/realms/vion", "--tenant", Tool.Tenant],
                                              home: home.Root);
            var called = await Tool.RunAsync(handler, ["tenant", "edge-gateway", "list", "--token", "-"], Tool.Token, home: home.Root);

            // Assert
            Assert.AreEqual(0, defined.ExitCode, defined.Error);
            var environment = JsonNode.Parse(File.ReadAllText(Path.Combine(home.Root, "config.json")))!["environments"]!["acme"]!;
            Assert.AreEqual("https://api.example.test", environment["apiUrl"]!.GetValue<string>());
            Assert.AreEqual("https://auth.example.test/realms/vion", environment["authUrl"]!.GetValue<string>());
            Assert.AreEqual("vion-cli", environment["clientId"]!.GetValue<string>());
            Assert.AreEqual(0, called.ExitCode, called.Error);
            Assert.AreEqual($"https://api.example.test/tenant/{Tool.Tenant}/edgeGateways", handler.Requests[0].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.1")]
        [DataRow("context", "use", "--env", "acme", "--api-url", "https://api.example.test")]
        [DataRow("context", "use", "--env", "test", "--api-url", "https://api.example.test", "--auth-url", "https://auth.example.test/realms/vion")]
        [DataRow("context", "use", "--env", "acme")]
        public async Task RefuseHalfDefinedOrRedefinedEnvironment(params string[] args)
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler, args, home: home.Root);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            Assert.IsFalse(File.Exists(Path.Combine(home.Root, "config.json")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.4")]
        public async Task ListContextsMarkingCurrent()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);
            using var home = new TemporaryHome();
            home.Write("config.json", $$$"""{ "current": "work", "contexts": { "other": { "env": "production", "integrator": "{{{Integrator}}}" }, "work": { "env": "test", "tenant": "{{{Tool.Tenant}}}" } } }""");

            // Act
            var table = await Tool.RunAsync(handler, ["context", "list"], home: home.Root);
            var json = await Tool.RunAsync(handler, ["context", "list", "--json"], home: home.Root);

            // Assert
            Assert.AreEqual(0, table.ExitCode, table.Error);
            var work = table.Out.Split('\n').Single(line => line.Contains(" work ", System.StringComparison.Ordinal));
            var other = table.Out.Split('\n').Single(line => line.Contains(" other ", System.StringComparison.Ordinal));
            StringAssert.Contains(work, "*");
            Assert.IsFalse(other.Contains('*'), other);
            var items = JsonNode.Parse(json.Out)!["items"]!.AsArray();
            var current = items.Single(i => i!["name"]!.GetValue<string>() == "work")!;
            Assert.IsTrue(current["current"]!.GetValue<bool>());
            Assert.AreEqual(Tool.Tenant, current["tenant"]!.GetValue<string>());
            Assert.IsFalse(items.Single(i => i!["name"]!.GetValue<string>() == "other")!["current"]!.GetValue<bool>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.4")]
        public async Task ShowCurrentContextWithResolvedUrlsAndValues()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);
            using var home = new TemporaryHome();
            home.Write("config.json", $$$"""{ "current": "work", "contexts": { "work": { "env": "test", "tenant": "{{{Tool.Tenant}}}" } } }""");

            // Act
            var run = await Tool.RunAsync(handler, ["context", "show", "--json", "--project", Project], home: home.Root);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var shown = JsonNode.Parse(run.Out)!;
            Assert.AreEqual("work", shown["name"]!.GetValue<string>());
            Assert.AreEqual("test", shown["env"]!.GetValue<string>());
            Assert.AreEqual("https://api.test.vion.swiss", shown["apiUrl"]!.GetValue<string>());
            Assert.AreEqual("https://auth.test.vion.swiss/realms/vion", shown["authUrl"]!.GetValue<string>());
            Assert.AreEqual(Tool.Tenant, shown["tenant"]!.GetValue<string>());
            Assert.AreEqual(Project, shown["project"]!.GetValue<string>());
            Assert.IsNull(shown["integrator"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.4")]
        public async Task ShowNoContextWhenNoneIsCurrent()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler, ["context", "show"], home: home.Root);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(run.Out, "no current context");
            StringAssert.Contains(run.Out, "context use");
        }
    }
}
