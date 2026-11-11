using System.Linq;
using System.Text.Json.Nodes;
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
        [DataRow("/tenant/11111111-1111-1111-1111-111111111111/dashboards/22222222-2222-2222-2222-222222222222")]
        [DataRow("/somewhere/the/table/does/not/know")]
        public async Task RefuseDeleteWithoutYesOffTerminal(string path)
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "DELETE", path), Tool.Token);

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
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "DELETE", $"/tenant/{Tool.Tenant}/dashboards/{Tool.Id}", "--yes"), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("DELETE", handler.Requests[0].Method);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.5")]
        public async Task ReadVersionBeforeVersionedWriteWithoutNoWriteSafety()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "meta": { "version": 7 } }""");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "PUT", $"/tenant/{Tool.Tenant}/overviewComposition", "-f", "a=b"), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            CollectionAssert.AreEqual(new[] { "GET", "PUT" }, handler.Requests.Select(r => r.Method).ToList());
            Assert.AreEqual("\"7\"", handler.Requests[1].Headers["If-Match"]);
            Assert.IsTrue(handler.Requests[1].Headers.ContainsKey("Idempotency-Key"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.6")]
        public async Task SendVersionedWriteAsGivenWithNoWriteSafety()
        {
            // Arrange
            using var handler = RecordingHandler.Json(428, """{ "statusCode": "PreconditionRequired", "message": "If-Match is required.", "exceptionId": "x" }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("api",
                                                      "PUT",
                                                      $"/tenant/{Tool.Tenant}/overviewComposition",
                                                      "--input",
                                                      "-",
                                                      "--no-write-safety",
                                                      "--json"),
                                          Tool.Token + "\n" + """{ "a": "b", "meta": { "version": 7 } }""");

            // Assert
            Assert.AreEqual(1, run.ExitCode, run.Out);
            var request = Assert.ContainsSingle(handler.Requests);
            Assert.AreEqual("PUT", request.Method);
            Assert.IsFalse(request.Headers.ContainsKey("If-Match"), "If-Match was added");
            Assert.IsFalse(request.Headers.ContainsKey("Idempotency-Key"), "an Idempotency-Key was minted");
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{ "a": "b", "meta": { "version": 7 } }"""), JsonNode.Parse(request.Body!)), request.Body);
            Assert.AreEqual(428, JsonNode.Parse(run.Out)!["error"]!["status"]!.GetValue<int>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.6")]
        [DataRow("k-1", "3", "\"3\"")]
        [DataRow("k-2", "\"9\"", "\"9\"")]
        public async Task SendGivenHeadersOnlyWithNoWriteSafety(string key, string version, string sent)
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("api",
                                                      "PUT",
                                                      $"/tenant/{Tool.Tenant}/overviewComposition",
                                                      "-f",
                                                      "a=b",
                                                      "--no-write-safety",
                                                      "--idempotency-key",
                                                      key,
                                                      "--if-match",
                                                      version),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var request = Assert.ContainsSingle(handler.Requests);
            Assert.AreEqual(key, request.Headers["Idempotency-Key"]);
            Assert.AreEqual(sent, request.Headers["If-Match"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.6")]
        public async Task SendDryRunOnPathWithoutItWithNoWriteSafety()
        {
            // Arrange
            using var handler = RecordingHandler.Json(400, """{ "statusCode": "BadRequest", "message": "no dry run", "exceptionId": "x" }""");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "GET", $"/tenant/{Tool.Tenant}/edgeGateways", "--dry-run", "--no-write-safety"), Tool.Token);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Tool.Tenant}/edgeGateways?dryRun=true", Assert.ContainsSingle(handler.Requests).Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.6")]
        public async Task ConfirmIrreversibleCallWithNoWriteSafety()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "DELETE", $"/tenant/{Tool.Tenant}/dashboards/{Tool.Id}", "--no-write-safety"), Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.7")]
        public async Task WriteStatusAndHeadersBeforeBodyWithInclude()
        {
            // Arrange
            using var handler = RecordingHandler.Json(202, """{ "state": "Queued" }""", response => response.Headers.Add("Operation-Location", "/me/longRunningOperations/1"));

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "POST", "/onboarding/provision", "--yes", "-i"), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var lines = run.Out.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            Assert.AreEqual("HTTP 202 Accepted", lines[0]);
            CollectionAssert.Contains(lines, "Operation-Location: /me/longRunningOperations/1");
            CollectionAssert.Contains(lines, "Content-Type: application/json; charset=utf-8");
            var blank = lines.IndexOf("");
            Assert.IsTrue(blank > 0 && run.Out.IndexOf("Queued", System.StringComparison.Ordinal) > run.Out.IndexOf("Operation-Location", System.StringComparison.Ordinal),
                          run.Out);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.7")]
        [TestProperty("spec", "AC-CLI-003.1")]
        [DataRow(202, 0)]
        [DataRow(428, 1)]
        public async Task WriteStatusHeadersAndBodyAsOneDocumentWithIncludeJson(int status, int exitCode)
        {
            // Arrange
            using var handler = RecordingHandler.Json(status, """{ "state": "Queued" }""", response => response.Headers.Add("Retry-After", "2"));

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("api",
                                                      "POST",
                                                      "/onboarding/provision",
                                                      "--yes",
                                                      "--include",
                                                      "--json"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(exitCode, run.ExitCode, run.Error);
            var document = JsonNode.Parse(run.Out)!.AsObject();
            CollectionAssert.AreEqual(new[] { "status", "headers", "body" }, document.Select(member => member.Key).ToList());
            Assert.AreEqual(status, document["status"]!.GetValue<int>());
            Assert.AreEqual("2", document["headers"]!["Retry-After"]!.GetValue<string>());
            Assert.AreEqual("Queued", document["body"]!["state"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.7")]
        public async Task RefuseIncludeWithWait()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "GET", "/me", "--include", "--wait"), Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            Assert.IsEmpty(handler.Requests);
        }
    }
}