using System.Threading.Tasks;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Commands
{
    /// <summary>`vion api`: the raw call, the pairs as query on GET and as the body otherwise.</summary>
    [TestClass]
    public class ApiCommandShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.5")]
        public async Task SendPairsAsQueryOnGet()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "items": [] }""");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "GET", $"/tenant/{Tool.Tenant}/edgeGateways", "-f", $"projectId={Tool.Id}"), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var request = handler.Requests[0];
            Assert.AreEqual("GET", request.Method);
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Tool.Tenant}/edgeGateways?projectId={Tool.Id}", request.Uri.ToString());
            Assert.AreEqual($"Bearer {Tool.Token}", request.Headers["Authorization"]);
            Assert.IsNull(request.Body);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.5")]
        public async Task SendPairsAsJsonBodyOnPost()
        {
            // Arrange
            using var handler = RecordingHandler.Json(201, """{ "id": "new" }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("api",
                                                      "POST",
                                                      $"/tenant/{Tool.Tenant}/dashboards",
                                                      "-f",
                                                      "name=smoke",
                                                      "-F",
                                                      "columns=2",
                                                      "--json"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var request = handler.Requests[0];
            Assert.AreEqual("POST", request.Method);
            Assert.AreEqual("""{"name":"smoke","columns":2}""", request.Body);
            StringAssert.Contains(run.Out, "\"id\": \"new\"");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.5")]
        public async Task RefuseUnknownMethod()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "FETCH", "/me"), Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.6")]
        public async Task SendDryRunOnPathOfOperationOfferingIt()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "PUT", $"/tenant/{Tool.Tenant}/logicConfigurations/{Tool.Id}/activate", "--dry-run", "--yes"), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Tool.Tenant}/logicConfigurations/{Tool.Id}/activate?dryRun=true", handler.Requests[0].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.6")]
        public async Task RefuseDryRunOnPathWithoutIt()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "GET", $"/tenant/{Tool.Tenant}/edgeGateways", "--dry-run"), Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(run.Error, "offers no dry run");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.5")]
        [DataRow("/tenant/11111111-1111-1111-1111-111111111111/dashboards/22222222-2222-2222-2222-222222222222", DisplayName = "an irreversible operation of the table")]
        [DataRow("/somewhere/the/table/does/not/know", DisplayName = "a path outside the table")]
        public async Task RefuseDeleteWithoutYesOffTerminal(string path)
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "DELETE", path), Tool.Token, false);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(run.Error, "--yes");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.5")]
        public async Task SendDeleteWithYes()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "DELETE", $"/tenant/{Tool.Tenant}/dashboards/{Tool.Id}", "--yes"), Tool.Token, false);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("DELETE", handler.Requests[0].Method);
        }
    }
}