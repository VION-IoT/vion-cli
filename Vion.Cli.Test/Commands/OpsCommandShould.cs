using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Commands
{
    /// <summary>`vion ops`, offline: the tree's rows, and the system rows behind `--all`.</summary>
    [TestClass]
    public class OpsCommandShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.1")]
        public async Task ListTreeOperationsWithoutRequest()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);

            // Act
            var run = await Tool.RunAsync(handler, ["ops", "--json"]);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var items = JsonNode.Parse(run.Out)!["items"]!.AsArray();
            Assert.HasCount(264, items);
            var listing = items.Single(i => i!["operationId"]!.GetValue<string>() == "tenant.edgeGateway.list")!;
            Assert.AreEqual("vion tenant edge-gateway list", listing["command"]!.GetValue<string>());
            Assert.AreEqual("GET", listing["method"]!.GetValue<string>());
            Assert.AreEqual("/tenant/{tenantId}/edgeGateways", listing["path"]!.GetValue<string>());
            Assert.AreEqual("Read", listing["effect"]!.GetValue<string>());
            var activation = items.Single(i => i!["operationId"]!.GetValue<string>() == "tenant.logicConfiguration.activate")!;
            Assert.IsTrue(activation["dryRun"]!.GetValue<bool>());
            Assert.IsTrue(activation["idempotent"]!.GetValue<bool>());
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.1")]
        public async Task AddSystemOperationsWithAll()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);

            // Act
            var run = await Tool.RunAsync(handler, ["ops", "--all", "--json"]);

            // Assert
            var items = JsonNode.Parse(run.Out)!["items"]!.AsArray();
            Assert.HasCount(268, items);
            var catalog = items.Single(i => i!["operationId"]!.GetValue<string>() == "system.capability.list")!;
            Assert.IsNull(catalog["command"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.1")]
        public async Task ListTreeOperationsAsTable()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);

            // Act
            var run = await Tool.RunAsync(handler, ["ops"]);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(run.Out, "tenant.edgeGateway.list");
            StringAssert.Contains(run.Error, "264 operations");
        }
    }
}