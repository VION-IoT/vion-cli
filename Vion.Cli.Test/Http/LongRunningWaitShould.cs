using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Http
{
    /// <summary>
    ///     `--wait`, through the whole tool: a start published `LongRunningOperation` followed to its end, the long-running
    ///     operation's own read re-read, the outcome rendered and the exit code taken from it, the timeout, the interruption
    ///     and the refusals. The commands are the real snapshot's — `integrator.edgeGatewaySimulation.update` the start,
    ///     `integrator.longRunningOperation.get` the read.
    /// </summary>
    [TestClass]
    public class LongRunningWaitShould
    {
        private const string Integrator = "33333333-3333-3333-3333-333333333333";

        private const string Gateway = "44444444-4444-4444-4444-444444444444";

        private const string Handle = "55555555-5555-5555-5555-555555555555";

        private static readonly string StartPath = $"/integrator/{Integrator}/edgeGateways/{Gateway}/updateSimulation";

        private static readonly string Location = $"/integrator/{Integrator}/longRunningOperations/{Handle}";

        private static readonly string[] Start = ["integrator", "edge-gateway-simulation", "update", "--integrator", Integrator, "--edge-gateway-id", Gateway, "--yes"];

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.1")]
        public async Task PollOperationLocationAfterEachRetryAfterUntilEnded()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var started = time.GetUtcNow();
            var instants = new List<DateTimeOffset>();
            var answers = new Queue<HttpResponseMessage>([
                Answer(202, State("Queued"), Location, 3),
                Answer(200, State("Running"), null, null),
                Answer(200, State("Succeeded", """{ "id": "g" }"""), null, null),
            ]);
            using var handler = new RecordingHandler(_ =>
                                                     {
                                                         instants.Add(time.GetUtcNow());
                                                         return answers.Dequeue();
                                                     });

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. Start, "--wait"]), Tool.Token, time: time));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            CollectionAssert.AreEqual(new[]
                                      {
                                          ("POST", $"https://api.test.vion.swiss{StartPath}"),
                                          ("GET", $"https://api.test.vion.swiss{Location}"),
                                          ("GET", $"https://api.test.vion.swiss{Location}"),
                                      },
                                      handler.Requests.Select(r => (r.Method, r.Uri.ToString())).ToList());
            Assert.AreEqual($"Bearer {Tool.Token}", handler.Requests[2].Headers["Authorization"]);
            CollectionAssert.AreEqual(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5) }, instants.Select(i => i - started).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.1")]
        public async Task RereadLongRunningOperationReadUntilEnded()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var answers = new Queue<HttpResponseMessage>([
                Answer(200, State("Running"), null, 2),
                Answer(200, State("Succeeded", """{ "id": "g" }"""), null, null),
            ]);
            using var handler = new RecordingHandler(_ => answers.Dequeue());

            // Act
            var run = await Tool.RunAdvancingAsync(time,
                                                   () => Tool.RunAsync(handler,
                                                                       Tool.OnTest("integrator", "long-running-operation", "get", "--integrator", Integrator, "--id", Handle, "--wait"),
                                                                       Tool.Token,
                                                                       time: time));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(2, handler.Requests);
            Assert.AreEqual($"https://api.test.vion.swiss{Location}", handler.Requests[1].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.1")]
        public async Task ResolveTokenAgainWhenPollAnswersUnauthorized()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var minted = 0;
            var answers = new Queue<HttpResponseMessage>([
                Answer(202, State("Queued"), Location, null),
                new HttpResponseMessage(HttpStatusCode.Unauthorized),
                Answer(200, State("Succeeded", "{}"), null, null),
            ]);
            using var handler = new RecordingHandler(request => request.RequestUri!.Host.StartsWith("auth.", StringComparison.Ordinal) ?
                                                                    RecordingHandler.Response(200, $$"""{ "access_token": "minted-{{++minted}}", "expires_in": 300 }""") :
                                                                    answers.Dequeue());
            var environment = new Dictionary<string, string?> { ["VION_CLIENT_ID"] = "client", ["VION_CLIENT_SECRET"] = Guid.NewGuid().ToString() };

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, [.. Start, "--wait", "--env", "test"], environment: environment, time: time));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var calls = handler.To("api.test.vion.swiss");
            CollectionAssert.AreEqual(new[] { "Bearer minted-1", "Bearer minted-1", "Bearer minted-2" }, calls.Select(c => c.Headers["Authorization"]).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.2")]
        [DataRow(false)]
        [DataRow(true)]
        public async Task RenderResultAndExitZeroWhenSucceeded(bool json)
        {
            // Arrange
            var time = new FakeTimeProvider();
            var answers = new Queue<HttpResponseMessage>([
                Answer(202, State("Queued"), Location, null),
                Answer(200, State("Succeeded", """{ "id": "gateway-row", "name": "bench" }"""), null, null),
            ]);
            using var handler = new RecordingHandler(_ => answers.Dequeue());
            string[] args = json ? [.. Start, "--wait", "--json"] : [.. Start, "--wait"];

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest(args), Tool.Token, time: time));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(run.Error, $"long-running operation {Handle} succeeded.");
            if (json)
            {
                Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{ "id": "gateway-row", "name": "bench" }"""), JsonNode.Parse(run.Out)), run.Out);
            }
            else
            {
                StringAssert.Contains(run.Out, "gateway-row");
                Assert.DoesNotContain("Succeeded", run.Out);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.2")]
        public async Task RenderPreviewOfDryRunWithoutPolling()
        {
            // Arrange
            var preview = """{ "operationId": "integrator.edgeGateway.create", "state": "Succeeded", "id": null, "result": { "name": "preview" } }""";
            using var handler = RecordingHandler.Json(202, preview);

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("integrator", "edge-gateway", "create", "--integrator", Integrator, "-f", "name=preview", "--dry-run", "--wait", "--json"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(1, handler.Requests);
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{ "name": "preview" }"""), JsonNode.Parse(run.Out)), run.Out);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.3")]
        public async Task ReportErrorAndExitOneWhenFailed()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var failed = """{ "id": "h", "state": "Failed", "error": { "statusCode": "Conflict", "message": "the namespace is busy", "exceptionId": "x-1" } }""";
            var answers = new Queue<HttpResponseMessage>([
                Answer(202, State("Running"), Location, null),
                RecordingHandler.Response(200, failed),
            ]);
            using var handler = new RecordingHandler(_ => answers.Dequeue());

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. Start, "--wait", "--json"]), Tool.Token, time: time));

            // Assert
            Assert.AreEqual(1, run.ExitCode, run.Out);
            var error = JsonNode.Parse(run.Out)!["error"]!;
            Assert.AreEqual(409, error["status"]!.GetValue<int>());
            Assert.AreEqual("the namespace is busy", error["message"]!.GetValue<string>());
            Assert.AreEqual("x-1", error["exceptionId"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.4")]
        [TestProperty("spec", "AC-CLI-003.5")]
        public async Task ExitThreeWithLastAnswerWhenTimeoutReached()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var started = time.GetUtcNow();
            var instants = new List<DateTimeOffset>();
            using var handler = new RecordingHandler(request =>
                                                     {
                                                         instants.Add(time.GetUtcNow());
                                                         return request.Method == HttpMethod.Post ? Answer(202, State("Queued"), Location, 4) : Answer(200, State("Running"), null, 4);
                                                     });

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. Start, "--wait", "--timeout", "10s", "--json"]), Tool.Token, time: time));

            // Assert
            Assert.AreEqual(3, run.ExitCode, run.Out);
            var error = JsonNode.Parse(run.Out)!["error"]!;
            Assert.AreEqual("Running", error["longRunningOperation"]!["state"]!.GetValue<string>());
            StringAssert.Contains(error["message"]!.GetValue<string>(), $"`vion api GET {Location} --wait`");
            CollectionAssert.AreEqual(new[] { 0, 4, 8, 10 }, instants.Select(i => (int)(i - started).TotalSeconds).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.4")]
        public async Task StopWaitingAfterFifteenMinutesWithoutTimeout()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var started = time.GetUtcNow();
            DateTimeOffset? last = null;
            using var handler = new RecordingHandler(request =>
                                                     {
                                                         last = time.GetUtcNow();
                                                         return request.Method == HttpMethod.Post ? Answer(202, State("Queued"), Location, 60) : Answer(200, State("Running"), null, 60);
                                                     });

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. Start, "--wait"]), Tool.Token, time: time), 7200);

            // Assert
            Assert.AreEqual(3, run.ExitCode, run.Error);
            Assert.AreEqual(TimeSpan.FromMinutes(15), last - started);
            StringAssert.Contains(run.Error, "still Running after 15m");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.5")]
        public async Task NamePickUpAndExitOneWhenPollAnswersError()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var answers = new Queue<HttpResponseMessage>([
                Answer(202, State("Queued"), Location, null),
                RecordingHandler.Response(503, """{ "statusCode": "ServiceUnavailable", "message": "down", "exceptionId": "x-2" }"""),
            ]);
            using var handler = new RecordingHandler(_ => answers.Dequeue());

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. Start, "--wait"]), Tool.Token, time: time));

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(run.Error, $"the work may go on. Pick it up with `vion api GET {Location} --wait`.");
            StringAssert.Contains(run.Error, "error 503 ServiceUnavailable: down");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.5")]
        public async Task NamePickUpAndExitOneWhenInterrupted()
        {
            // Arrange
            using var interrupt = new CancellationTokenSource();
            using var handler = new RecordingHandler(_ => Answer(202, State("Queued"), Location, null));

            // Act
            var running = Tool.RunAsync(handler, Tool.OnTest([.. Start, "--wait"]), Tool.Token, time: new FakeTimeProvider(), cancellationToken: interrupt.Token);
            while (handler.Requests.Count == 0)
            {
                await Task.Delay(1);
            }

            await Task.Delay(50);
            await interrupt.CancelAsync();
            var run = await running;

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(run.Error, $"stopped waiting; the work goes on. Pick it up with `vion api GET {Location} --wait`.");
            Assert.HasCount(1, handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.1")]
        public async Task FollowOperationLocationOfStartSentThroughApi()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var answers = new Queue<HttpResponseMessage>([
                Answer(202, State("Queued"), Location, null),
                Answer(200, State("Succeeded", """{ "id": "g" }"""), null, null),
            ]);
            using var handler = new RecordingHandler(_ => answers.Dequeue());

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest("api", "POST", StartPath, "--yes", "--wait", "--json"), Tool.Token, time: time));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual($"https://api.test.vion.swiss{Location}", handler.Requests[1].Uri.ToString());
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse("""{ "id": "g" }"""), JsonNode.Parse(run.Out)), run.Out);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.6")]
        public async Task FollowOperationLocationOnPathTableDoesNotKnow()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var answers = new Queue<HttpResponseMessage>([
                Answer(202, State("Queued"), "/elsewhere/operations/1", null),
                Answer(200, State("Succeeded", """{ "done": true }"""), null, null),
            ]);
            using var handler = new RecordingHandler(_ => answers.Dequeue());

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest("api", "POST", "/elsewhere/things", "--wait", "--json"), Tool.Token, time: time));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("https://api.test.vion.swiss/elsewhere/operations/1", handler.Requests[1].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.6")]
        public async Task RenderAnswerOfUnknownPathWithoutOperationLocationAsIs()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, State("Queued"));

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("api", "POST", "/elsewhere/things", "--wait", "--json"), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(1, handler.Requests);
            Assert.AreEqual("Queued", JsonNode.Parse(run.Out)!["state"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.9")]
        public async Task IgnoreWaitOnOperationAnsweringOutcome()
        {
            // Arrange
            // A body and a header a long-running operation would carry, so that a wait which did not ignore the flag would poll.
            var time = new FakeTimeProvider();
            using var handler = new RecordingHandler(_ => Answer(200, State("Queued"), Location, null));

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest("me", "identity", "get", "--wait", "--json"), Tool.Token, time: time));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(1, handler.Requests);
            Assert.AreEqual("Queued", JsonNode.Parse(run.Out)!["state"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.7")]
        public async Task RefuseOperationLocationOnOtherOrigin()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var handler = new RecordingHandler(_ => Answer(202, State("Queued"), "https://elsewhere.example.test/operations/1", null));

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. Start, "--wait"]), Tool.Token, time: time));

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.HasCount(1, handler.Requests);
            StringAssert.Contains(run.Error, "is not on https://api.test.vion.swiss");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.8")]
        [DataRow(false)]
        [DataRow(true)]
        public async Task NameOperationLocationWithoutWait(bool json)
        {
            // Arrange
            using var handler = new RecordingHandler(_ => Answer(202, State("Queued"), Location, 2));
            string[] args = json ? [.. Start, "--json"] : Start;

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest(args), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(1, handler.Requests);
            StringAssert.Contains(run.Error, $"long-running operation at {Location}: pass --wait to wait for its outcome, or run `vion api GET {Location} --wait`.");
            StringAssert.Contains(run.Out, "Queued");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.5")]
        public async Task ExitTwoOnTimeoutWithoutWait()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest([.. Start, "--timeout", "10s"]), Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode, run.Error);
            StringAssert.Contains(run.Error, "--timeout bounds --wait, and --wait is absent");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.5")]
        [DataRow("ten")]
        [DataRow("0")]
        [DataRow("5d")]
        public async Task ExitTwoOnMalformedTimeout(string timeout)
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest([.. Start, "--wait", "--timeout", timeout]), Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode, run.Error);
            Assert.IsEmpty(handler.Requests);
        }

        private static string State(string state, string? result = null)
        {
            return $$"""{ "id": "{{Handle}}", "operationId": "integrator.edgeGatewaySimulation.update", "state": "{{state}}", "result": {{result ?? "null"}} }""";
        }

        private static HttpResponseMessage Answer(int status, string json, string? location, int? retryAfter)
        {
            var response = RecordingHandler.Response(status, json);
            if (location != null)
            {
                response.Headers.Add("Operation-Location", location);
            }

            if (retryAfter is { } seconds)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            return response;
        }
    }
}
