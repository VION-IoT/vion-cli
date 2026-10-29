using System.Collections.Generic;
using System.Threading.Tasks;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Context
{
    /// <summary>
    ///     The order the environment and each root are resolved in — the option, the variables, the current context —
    ///     proven where two of them disagree.
    /// </summary>
    [TestClass]
    public class ContextResolverShould
    {
        private const string Other = "55555555-5555-5555-5555-555555555555";

        private static readonly string ConfigWithTestTenant = $$$"""{ "current": "work", "contexts": { "work": { "env": "test", "tenant": "{{{Tool.Tenant}}}" } } }""";

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.3")]
        public async Task TakeRootFromOptionBeforeContext()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "items": [] }""");
            using var home = new TemporaryHome();
            home.Write("config.json", ConfigWithTestTenant);

            // Act
            var run = await Tool.RunAsync(handler, ["tenant", "edge-gateway", "list", "--tenant", Other, "--token", "-"], Tool.Token, home: home.Root);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Other}/edgeGateways", handler.Requests[0].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.3")]
        public async Task TakeEnvironmentFromOptionBeforeVariable()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "items": [] }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["tenant", "edge-gateway", "list", "--tenant", Tool.Tenant, "--env", "test", "--token", "-"],
                                          Tool.Token,
                                          environment: new Dictionary<string, string?> { ["VION_ENV"] = "production" });

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Tool.Tenant}/edgeGateways", handler.Requests[0].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.3")]
        public async Task TakeEnvironmentFromVariableBeforeContext()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "items": [] }""");
            using var home = new TemporaryHome();
            home.Write("config.json", ConfigWithTestTenant);

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["tenant", "edge-gateway", "list", "--tenant", Tool.Tenant, "--token", "-"],
                                          Tool.Token,
                                          home: home.Root,
                                          environment: new Dictionary<string, string?> { ["VION_ENV"] = "production" });

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual($"https://api.vion.swiss/tenant/{Tool.Tenant}/edgeGateways", handler.Requests[0].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.3")]
        public async Task OverrideEnvironmentUrlsFromVariables()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "items": [] }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["tenant", "edge-gateway", "list", "--tenant", Tool.Tenant, "--env", "test", "--token", "-"],
                                          Tool.Token,
                                          environment: new Dictionary<string, string?> { ["VION_API_URL"] = "https://api.example.test" });

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual($"https://api.example.test/tenant/{Tool.Tenant}/edgeGateways", handler.Requests[0].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.3")]
        public async Task ApplyContextRootsOnlyToTheirEnvironment()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "items": [] }""");
            using var home = new TemporaryHome();
            home.Write("config.json", ConfigWithTestTenant);

            // Act
            var run = await Tool.RunAsync(handler, ["tenant", "edge-gateway", "list", "--env", "production", "--token", "-"], Tool.Token, home: home.Root);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(run.Error, "--tenant");
            Assert.IsEmpty(handler.Requests);
        }
    }
}