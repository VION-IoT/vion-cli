using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Commands
{
    /// <summary>`vion ops`, offline: the tree's rows, and the system rows behind `--all`; `ops --live`: the environment's document against the committed one.</summary>
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

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.2")]
        public async Task DiffLiveDocumentAgainstCommittedOne()
        {
            // Arrange
            var live = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1.json")))!;
            var paths = live["paths"]!.AsObject();
            paths.Remove("/tenant/{tenantId}/edgeGateways");
            paths["/tenant/{tenantId}/dashboards/{id}"]!["put"]!["x-vion-capability"]!["effect"] = "Irreversible";
            paths["/tenant/{tenantId}/widgetDefinitions/{id}"]!["put"]!["x-vion-authorization"]!["permissions"] = new JsonArray("Tenant.ManageEverything");
            paths["/system/ping"] = new JsonObject
                                    {
                                        ["get"] = new JsonObject
                                                  {
                                                      ["operationId"] = "system.ping.get",
                                                      ["x-vion-authorization"] = new JsonObject { ["mode"] = "Anonymous", ["permissions"] = new JsonArray(), ["scopeType"] = null, ["scopeFrom"] = null },
                                                      ["x-vion-capability"] = new JsonObject { ["effect"] = "Read", ["idempotent"] = false, ["versioned"] = false, ["dryRun"] = false },
                                                  },
                                    };
            using var handler = RecordingHandler.Json(200, live.ToJsonString());

            // Act
            var run = await Tool.RunAsync(handler, ["ops", "--live", "--env", "test", "--json"]);

            // Assert
            Assert.AreEqual(1, run.ExitCode, run.Error);
            Assert.AreEqual("https://api.test.vion.swiss/openapi/v1.json", handler.Requests[0].Uri.ToString());
            Assert.IsFalse(handler.Requests[0].Headers.ContainsKey("Authorization"), "the document was read with a token");
            var items = JsonNode.Parse(run.Out)!["items"]!.AsArray().Select(i => (i!["change"]!.GetValue<string>(), i["operationId"]!.GetValue<string>(), i["detail"]?.GetValue<string>())).ToList();
            CollectionAssert.Contains(items, ("added", "system.ping.get", (string?)null));
            CollectionAssert.Contains(items, ("removed", "tenant.edgeGateway.list", (string?)null));
            CollectionAssert.Contains(items, ("changed", "tenant.dashboard.update", (string?)"x-vion-capability"));
            CollectionAssert.Contains(items, ("changed", "tenant.widgetDefinition.update", (string?)"x-vion-authorization"));
            Assert.HasCount(4, items);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.2")]
        public async Task ExitZeroWhenLiveDocumentMatchesCommittedOne()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1.json")));

            // Act
            var run = await Tool.RunAsync(handler, ["ops", "--live", "--env", "test"]);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(run.Error, "no differences");
            StringAssert.Contains(run.Error, "268");
        }
    }
}
