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
    }
}