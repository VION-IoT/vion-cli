using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Http
{
    /// <summary>
    ///     The two write-safety headers: the idempotency key a write carries, and where a versioned update's `If-Match`
    ///     comes from — the flag, the document, the aggregate's own `get`, or a refusal. Proven through the derived
    ///     commands and through `api`, which share the one resolver.
    /// </summary>
    [TestClass]
    public class WriteSafetyShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.3")]
        public async Task SendMintedIdempotencyKeyOnIdempotentWrite()
        {
            // Arrange
            using var handler = RecordingHandler.Json(201, """{ "id": "new" }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "dashboard",
                                                      "create",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "-f",
                                                      "name=smoke"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.IsTrue(handler.Requests[0].Headers.TryGetValue("Idempotency-Key", out var key), "no Idempotency-Key sent");
            Assert.IsTrue(Guid.TryParseExact(key, "D", out _), $"not a fresh key: {key}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.3")]
        public async Task SendGivenIdempotencyKey()
        {
            // Arrange
            using var handler = RecordingHandler.Json(201, """{ "id": "new" }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "dashboard",
                                                      "create",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "-f",
                                                      "name=smoke",
                                                      "--idempotency-key",
                                                      "smoke-2026-10-29-1"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("smoke-2026-10-29-1", handler.Requests[0].Headers["Idempotency-Key"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.3")]
        public async Task SendNoIdempotencyKeyOnOperationNotHonouringIt()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "items": [] }""");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("tenant", "edge-gateway", "list", "--tenant", Tool.Tenant), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.IsFalse(handler.Requests[0].Headers.ContainsKey("Idempotency-Key"), "a read carried an Idempotency-Key");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.3")]
        public async Task MintIdempotencyKeyForRawCallOnIdempotentPath()
        {
            // Arrange
            using var handler = RecordingHandler.Json(201, """{ "id": "new" }""");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "POST", $"/tenant/{Tool.Tenant}/dashboards", "-f", "name=smoke"), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.IsTrue(handler.Requests[0].Headers.TryGetValue("Idempotency-Key", out var key), "no Idempotency-Key sent");
            Assert.IsTrue(Guid.TryParseExact(key, "D", out _), $"not a fresh key: {key}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.4")]
        public async Task SendIfMatchFromFlagBeforeInputDocument()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");
            using var home = new TemporaryHome();
            var input = home.Write("dashboard.json", """{ "name": "x", "meta": { "version": 3, "permissions": [] } }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "dashboard",
                                                      "update",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--id",
                                                      Tool.Id,
                                                      "--input",
                                                      input,
                                                      "--if-match",
                                                      "7"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("\"7\"", handler.Requests[0].Headers["If-Match"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.4")]
        public async Task SendIfMatchFromInputDocumentAndStripMeta()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");
            using var home = new TemporaryHome();
            var input = home.Write("dashboard.json", """{ "name": "x", "meta": { "version": 3, "permissions": [] } }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "dashboard",
                                                      "update",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--id",
                                                      Tool.Id,
                                                      "--input",
                                                      input),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var request = handler.Requests[0];
            Assert.AreEqual("\"3\"", request.Headers["If-Match"]);
            Assert.AreEqual("""{"name":"x"}""", request.Body);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.4")]
        public async Task FetchIfMatchFromGetOnSamePath()
        {
            // Arrange
            using var handler = new RecordingHandler(request => request.Method == HttpMethod.Get ?
                                                                    Json(200, """{ "sections": [], "meta": { "version": 5, "permissions": [] } }""") : Json(200, "{}"));

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "overview-composition",
                                                      "upsert",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "-F",
                                                      "sections=[]"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(2, handler.Requests);
            Assert.AreEqual("GET", handler.Requests[0].Method);
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Tool.Tenant}/overviewComposition", handler.Requests[0].Uri.ToString());
            Assert.AreEqual("PUT", handler.Requests[1].Method);
            Assert.AreEqual("\"5\"", handler.Requests[1].Headers["If-Match"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.4")]
        public async Task RefuseVersionedUpdateWithoutVersion()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

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
                                                      "name=x"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(run.Error, "--if-match");
            StringAssert.Contains(run.Error, "meta.version");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.4")]
        public async Task RefuseRawCallOnVersionedPathWithoutVersion()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "PUT", $"/tenant/{Tool.Tenant}/dashboards/{Tool.Id}", "-f", "name=x"), Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(run.Error, "--if-match");
            Assert.IsEmpty(handler.Requests);
        }

        private static HttpResponseMessage Json(int status, string json)
        {
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json, new UTF8Encoding(false), "application/json") };
        }
    }
}