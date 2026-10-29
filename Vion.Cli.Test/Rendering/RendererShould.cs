using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Rendering
{
    /// <summary>What reaches each stream, in both modes, for an answer and for an error; and the exit code each ends with.</summary>
    [TestClass]
    public class RendererShould
    {
        private const string List =
            """{ "items": [ { "id": "a", "name": "one", "meta": { "permissions": [] } }, { "id": "b", "name": "two", "meta": { "permissions": [] } } ], "nextCursor": "abc" }""";

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.1")]
        public async Task WriteResponseBodyAloneToStandardOutputInJsonMode()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, List, response => response.Headers.Add("Idempotency-Replayed", "true"));

            // Act
            var run = await Tool.RunAsync(handler, ListArgs("--json"), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(List), JsonNode.Parse(run.Out)), run.Out);
            StringAssert.Contains(run.Error, "replayed");
            Assert.IsFalse(run.Out.Contains("replayed", System.StringComparison.Ordinal), run.Out);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.1")]
        public async Task WriteErrorObjectToStandardOutputInJsonMode()
        {
            // Arrange
            using var handler = RecordingHandler.Json(403, """{ "statusCode": "Forbidden", "message": "No.", "requiredPermissions": ["Project.Read"] }""");

            // Act
            var run = await Tool.RunAsync(handler, ListArgs("--json"), Tool.Token);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            var error = JsonNode.Parse(run.Out)!["error"]!;
            Assert.AreEqual(403, error["status"]!.GetValue<int>());
            Assert.AreEqual("No.", error["message"]!.GetValue<string>());
            Assert.AreEqual("", run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.1")]
        public async Task WriteUsageRefusalAsErrorObjectInJsonMode()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, List);

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("tenant", "edge-gateway", "list", "--json"), Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(JsonNode.Parse(run.Out)!["error"]!["message"]!.GetValue<string>(), "--tenant");
            Assert.AreEqual("", run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.2")]
        public async Task ProjectRowsToFieldsInJsonMode()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, List);

            // Act
            var run = await Tool.RunAsync(handler, ListArgs("--json", "--fields", "name,meta.permissions"), Tool.Token);

            // Assert
            var document = JsonNode.Parse(run.Out)!;
            Assert.AreEqual("""{"name":"one","meta.permissions":[]}""", document["items"]![0]!.ToJsonString());
            Assert.AreEqual("abc", document["nextCursor"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.2")]
        public async Task ProjectObjectToFieldsInTableMode()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "id": "a", "name": "one", "state": "ok" }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "edge-gateway",
                                                      "update",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--id",
                                                      Tool.Id,
                                                      "-f",
                                                      "name=one",
                                                      "--fields",
                                                      "name"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(run.Out, "name");
            StringAssert.Contains(run.Out, "one");
            Assert.IsFalse(run.Out.Contains("state", System.StringComparison.Ordinal), run.Out);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.3")]
        public async Task RenderListAsRowsWithoutMetaAndReportNextCursor()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, List);

            // Act
            var run = await Tool.RunAsync(handler, ListArgs(), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(run.Out, "id");
            StringAssert.Contains(run.Out, "name");
            StringAssert.Contains(run.Out, "one");
            StringAssert.Contains(run.Out, "two");
            Assert.IsFalse(run.Out.Contains("meta", System.StringComparison.Ordinal), run.Out);
            StringAssert.Contains(run.Error, "next cursor: abc");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.3")]
        public async Task RenderObjectAsKeyValueRows()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "id": "a", "nested": { "x": 1 } }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "edge-gateway",
                                                      "update",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--id",
                                                      Tool.Id,
                                                      "-f",
                                                      "name=one"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(run.Out, "id");
            StringAssert.Contains(run.Out, "nested");
            StringAssert.Contains(run.Out, """{"x":1}""");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.4")]
        public async Task ReportErrorDetailsOnStandardError()
        {
            // Arrange
            using var handler = RecordingHandler.Json(412,
                                                      """{ "statusCode": "PreconditionFailed", "message": "The row moved.", "problems": [ { "code": "Stale", "message": "Version 3 is behind." } ], "requiredPermissions": ["Project.ManageDashboards"], "currentVersion": "4", "exceptionId": "9d3a" }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "dashboard",
                                                      "update",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--id",
                                                      Tool.Id,
                                                      "-f",
                                                      "name=x",
                                                      "--if-match",
                                                      "3"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(run.Error, "error 412 PreconditionFailed: The row moved.");
            StringAssert.Contains(run.Error, "  - Stale: Version 3 is behind.");
            StringAssert.Contains(run.Error, "required permissions: Project.ManageDashboards");
            StringAssert.Contains(run.Error, "current version: 4");
            StringAssert.Contains(run.Error, "exception id: 9d3a");
            Assert.AreEqual("", run.Out);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.4")]
        public async Task ReportBodilessUnauthenticatedWithChallenge()
        {
            // Arrange
            using var handler = new RecordingHandler(_ =>
                                                     {
                                                         var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                                                         response.Headers.WwwAuthenticate.Add(new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                                                                                                  "error=\"invalid_token\""));
                                                         return response;
                                                     });

            // Act
            var run = await Tool.RunAsync(handler, ListArgs(), Tool.Token);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(run.Error, "error 401: unauthenticated: Bearer error=\"invalid_token\"");
            StringAssert.Contains(run.Error, "auth login");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.5")]
        [DataRow(200, 0)]
        [DataRow(500, 1)]
        public async Task ExitByOutcomeOfCall(int status, int exitCode)
        {
            // Arrange
            using var handler = RecordingHandler.Json(status, """{ "items": [] }""");

            // Act
            var run = await Tool.RunAsync(handler, ListArgs(), Tool.Token);

            // Assert
            Assert.AreEqual(exitCode, run.ExitCode, run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.5")]
        [DataRow("bogus")]
        [DataRow("tenant", "edge-gateway", "list", "--no-such-option")]
        [DataRow("tenant",
                 "dashboard",
                 "delete",
                 "--tenant",
                 "not-a-uuid",
                 "--id",
                 "x",
                 "--yes",
                 "--env",
                 "test")]
        public async Task ExitTwoOnUsageError(params string[] args)
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, args);

            // Assert
            Assert.AreEqual(2, run.ExitCode, run.Error);
            StringAssert.Contains(run.Error, "error: ");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.1")]
        public async Task WriteErrorObjectForUnexpectedFailureInJsonMode()
        {
            // Arrange
            using var handler = new RecordingHandler(_ => throw new System.InvalidOperationException("the handler fell over"));

            // Act
            var run = await Tool.RunAsync(handler, ListArgs("--json"), Tool.Token);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(JsonNode.Parse(run.Out)!["error"]!["message"]!.GetValue<string>(), "the handler fell over");
            Assert.AreEqual("", run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.1")]
        public async Task WriteErrorObjectForMissingInputFileInJsonMode()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "dashboard",
                                                      "create",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--input",
                                                      "no-such-body.json",
                                                      "--json"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(JsonNode.Parse(run.Out)!["error"]!["message"]!.GetValue<string>(), "no-such-body.json");
            Assert.AreEqual("", run.Error);
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.1")]
        public async Task WriteTextBodyAsJsonStringInJsonMode()
        {
            // Arrange
            using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
                                                          { Content = new StringContent("plain words", new UTF8Encoding(false), "text/plain") });

            // Act
            var run = await Tool.RunAsync(handler, ListArgs("--json"), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("\"plain words\"", run.Out.Trim());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.3")]
        public async Task ReportNextCursorInJsonMode()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, List);

            // Act
            var run = await Tool.RunAsync(handler, ListArgs("--json"), Tool.Token);

            // Assert
            StringAssert.Contains(run.Error, "next cursor: abc");
            Assert.IsFalse(run.Out.Contains("next cursor", System.StringComparison.Ordinal), run.Out);
        }

        private static string[] ListArgs(params string[] extra)
        {
            return Tool.OnTest(["tenant", "edge-gateway", "list", "--tenant", Tool.Tenant, .. extra]);
        }
    }
}