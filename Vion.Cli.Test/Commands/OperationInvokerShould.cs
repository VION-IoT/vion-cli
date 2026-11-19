using System.Collections.Generic;
using System.Threading.Tasks;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Commands
{
    /// <summary>
    ///     A derived command on the wire: the request it sends, the refusals that keep one from leaving, and the warning
    ///     an answer of a deprecated operation adds. No operation is deprecated in the committed document, so the warning
    ///     is proven on answers that carry the headers cloud-api sends for one.
    /// </summary>
    [TestClass]
    public class OperationInvokerShould
    {
        /// <summary>The line end a multipart body puts between a part's headers and its content, and after it.</summary>
        private const string CrLf = "\r\n";

        private const string Gateway = "33333333-3333-3333-3333-333333333333";

        private const string Warning = "warning: tenant.edgeGateway.list is deprecated, sunset 2027-02-01 — https://api.test.vion.swiss/changelog";

        private static readonly string[] Listing = Tool.OnTest("tenant", "edge-gateway", "list", "--tenant", Tool.Tenant);

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.6")]
        [DataRow(false)]
        [DataRow(true)]
        public async Task WarnOnceOnStandardErrorAndLeaveAnswerAsIs(bool json)
        {
            // Arrange
            const string body = """{ "items": [ { "id": "g-1", "name": "north" } ] }""";
            using var deprecated = new RecordingHandler(_ => RecordingHandler.Deprecated(RecordingHandler.Response(200, body)));
            using var current = RecordingHandler.Json(200, body);
            var args = json ? [.. Listing, "--json"] : Listing;

            // Act
            var warned = await Tool.RunAsync(deprecated, args, Tool.Token);
            var plain = await Tool.RunAsync(current, args, Tool.Token);

            // Assert
            Assert.AreEqual(Warning + System.Environment.NewLine, warned.Error);
            Assert.AreEqual("", plain.Error);
            Assert.AreEqual(plain.Out, warned.Out);
            Assert.AreEqual(plain.ExitCode, warned.ExitCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.6")]
        public async Task WarnBesideRefusalOfDeprecatedOperation()
        {
            // Arrange
            using var handler = new RecordingHandler(_ => RecordingHandler.Deprecated(RecordingHandler.Response(403, """{ "message": "denied" }""")));

            // Act
            var run = await Tool.RunAsync(handler, Listing, Tool.Token);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.AreEqual(Warning, run.Error.Split(System.Environment.NewLine)[0]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.6")]
        [DataRow("Mon, 01 Feb 2027 00:00:00 GMT", null, "warning: tenant.edgeGateway.list is deprecated, sunset 2027-02-01")]
        [DataRow("next spring", "</changelog>; rel=\"deprecation\"", "warning: tenant.edgeGateway.list is deprecated, sunset next spring — https://api.test.vion.swiss/changelog")]
        [DataRow("Sun, 15 Jul 2029 00:00:00 GMT",
                 "</next>; rel=\"next\", </log>; rel=\"alternate deprecation\"",
                 "warning: tenant.edgeGateway.list is deprecated, sunset 2029-07-15 — https://api.test.vion.swiss/log")]
        [DataRow("Mon, 01 Feb 2027 00:00:00 GMT",
                 "</changelog?from=a,b>; rel=\"deprecation\"",
                 "warning: tenant.edgeGateway.list is deprecated, sunset 2027-02-01 — https://api.test.vion.swiss/changelog?from=a,b")]
        public async Task WarnWithWhatAnswerCarries(string sunset, string? link, string warning)
        {
            // Arrange
            using var handler = new RecordingHandler(_ => RecordingHandler.Deprecated(RecordingHandler.Response(200, """{ "items": [] }"""), sunset, link));

            // Act
            var run = await Tool.RunAsync(handler, Listing, Tool.Token);

            // Assert
            Assert.AreEqual(warning + System.Environment.NewLine, run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-003.6")]
        public async Task ResolveChangelogAgainstApiUrlOfEnvironment()
        {
            // Arrange
            using var handler = new RecordingHandler(_ => RecordingHandler.Deprecated(RecordingHandler.Response(200, """{ "items": [] }""")));
            var environment = new Dictionary<string, string?> { ["VION_API_URL"] = "https://api.example.test/base" };

            // Act
            var run = await Tool.RunAsync(handler, ["tenant", "edge-gateway", "list", "--tenant", Tool.Tenant, "--token", "-"], Tool.Token, environment: environment);

            // Assert
            Assert.AreEqual("https://api.example.test/base/tenant/" + Tool.Tenant + "/edgeGateways", handler.Requests[0].Uri.ToString());
            Assert.AreEqual("warning: tenant.edgeGateway.list is deprecated, sunset 2027-02-01 — https://api.example.test/changelog" + System.Environment.NewLine, run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.1")]
        public async Task SendMethodPathAndQueryOfOperation()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "items": [] }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "edge-gateway",
                                                      "list",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--edge-gateway-id",
                                                      Gateway),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var request = handler.Requests[0];
            Assert.AreEqual("GET", request.Method);
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Tool.Tenant}/edgeGateways?edgeGatewayId={Gateway}", request.Uri.ToString());
            Assert.AreEqual($"Bearer {Tool.Token}", request.Headers["Authorization"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.1")]
        public async Task SendJsonBodyFromPairsAndDocument()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");
            using var home = new TemporaryHome();
            var input = home.Write("body.json", """{ "name": "from file", "layout": { "rows": 1 } }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "dashboard",
                                                      "create",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--input",
                                                      input,
                                                      "-f",
                                                      "name=vion-cli-smoke",
                                                      "-F",
                                                      "layout.columns=3",
                                                      "-F",
                                                      "shared=true"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var request = handler.Requests[0];
            Assert.AreEqual("POST", request.Method);
            StringAssert.StartsWith(request.ContentType, "application/json");
            Assert.AreEqual("""{"name":"vion-cli-smoke","layout":{"rows":1,"columns":3},"shared":true}""", request.Body);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.2")]
        public async Task RefuseWhenContextParameterMissing()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("tenant", "edge-gateway", "list"), Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(run.Error, "--tenant");
            StringAssert.Contains(run.Error, "context use");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.2")]
        public async Task RefuseWhenPathOptionMissing()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "dashboard",
                                                      "delete",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--yes"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(run.Error, "--id");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.2")]
        public async Task FillContextParameterFromCurrentContext()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, """{ "items": [] }""");
            using var home = new TemporaryHome();
            home.Write("config.json", $$$"""{ "current": "t", "contexts": { "t": { "env": "test", "tenant": "{{{Tool.Tenant}}}" } } }""");

            // Act
            var run = await Tool.RunAsync(handler, ["tenant", "edge-gateway", "list", "--token", "-"], Tool.Token, home: home.Root);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Tool.Tenant}/edgeGateways", handler.Requests[0].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.5")]
        public async Task RefuseIrreversibleWithoutYesOffTerminal()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "dashboard",
                                                      "delete",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--id",
                                                      Tool.Id),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(run.Error, "--yes");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.5")]
        public async Task SendIrreversibleWithYes()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "dashboard",
                                                      "delete",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--id",
                                                      Tool.Id,
                                                      "--yes"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("DELETE", handler.Requests[0].Method);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.5")]
        [DataRow("y", 1, 0)]
        [DataRow("n", 0, 1)]
        public async Task AskBeforeIrreversibleOnTerminal(string answer, int requestsSent, int exitCode)
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "dashboard",
                                                      "delete",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--id",
                                                      Tool.Id),
                                          $"{Tool.Token}\n{answer}\n",
                                          true);

            // Assert
            StringAssert.Contains(run.Error, "tenant.dashboard.delete is irreversible. Continue? [y/N]");
            Assert.HasCount(requestsSent, handler.Requests);
            Assert.AreEqual(exitCode, run.ExitCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.6")]
        public async Task SendDryRunOnOperationOfferingIt()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "logic-configuration",
                                                      "activate",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--id",
                                                      Tool.Id,
                                                      "--dry-run",
                                                      "--yes"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Tool.Tenant}/logicConfigurations/{Tool.Id}/activate?dryRun=true", handler.Requests[0].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.6")]
        public async Task RefuseDryRunOnOperationWithoutIt()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("tenant",
                                                      "edge-gateway",
                                                      "list",
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--dry-run"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(run.Error, "offers no dry run");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.7")]
        public async Task SendFileAndTextPartsAsMultipart()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");
            using var home = new TemporaryHome();
            var package = home.Write("Library.1.0.0.nupkg", "not really a package");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("integrator",
                                                      "logic-block-library-version",
                                                      "upload",
                                                      "--integrator",
                                                      Tool.Tenant,
                                                      "--file",
                                                      package,
                                                      "-f",
                                                      "releaseNotes=first",
                                                      "--yes"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var request = handler.Requests[0];
            StringAssert.StartsWith(request.ContentType, "multipart/form-data");
            StringAssert.Contains(request.Body, "name=nugetPackageFile; filename=Library.1.0.0.nupkg");
            StringAssert.Contains(request.Body, "not really a package");
            StringAssert.Contains(request.Body, "name=releaseNotes");
            StringAssert.Contains(request.Body, "first");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.7")]
        public async Task SendFieldFileAsMultipartTextPart()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");
            using var home = new TemporaryHome();
            var package = home.Write("Library.1.1.0.nupkg", "not really a package");
            var notes = home.Write("notes.md", "## 1.1.0 - the notes from a file");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("integrator",
                                                      "logic-block-library-version",
                                                      "upload",
                                                      "--integrator",
                                                      Tool.Tenant,
                                                      "--file",
                                                      package,
                                                      "-F",
                                                      $"releaseNotes=@{notes}",
                                                      "--yes"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(handler.Requests[0].Body, "name=releaseNotes" + CrLf + CrLf + "## 1.1.0 - the notes from a file" + CrLf);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.7")]
        public async Task SendFieldStandardInputAsMultipartTextPart()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");
            using var home = new TemporaryHome();
            var package = home.Write("Library.1.1.0.nupkg", "not really a package");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("integrator",
                                                      "logic-block-library-version",
                                                      "upload",
                                                      "--integrator",
                                                      Tool.Tenant,
                                                      "--file",
                                                      package,
                                                      "-F",
                                                      "releaseNotes=@-",
                                                      "--yes"),
                                          Tool.Token + "\n" + "the notes from a pipe");

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(handler.Requests[0].Body, "name=releaseNotes" + CrLf + CrLf + "the notes from a pipe" + CrLf);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.7")]
        public async Task SendFieldUntypedAsMultipartTextPart()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, "{}");
            using var home = new TemporaryHome();
            var package = home.Write("Library.1.1.0.nupkg", "not really a package");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("integrator",
                                                      "logic-block-library-version",
                                                      "upload",
                                                      "--integrator",
                                                      Tool.Tenant,
                                                      "--file",
                                                      package,
                                                      "-F",
                                                      "releaseNotes=1.50",
                                                      "--yes"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(handler.Requests[0].Body, "name=releaseNotes" + CrLf + CrLf + "1.50" + CrLf);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.8")]
        public async Task ExplainBodilessNotFoundAsOperationAbsentFromEnvironment()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(404);

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("tenant", "edge-gateway", "list", "--tenant", Tool.Tenant), Tool.Token);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(run.Error, "tenant.edgeGateway.list is not in this environment's document");
            StringAssert.Contains(run.Error, "ops --live");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.8")]
        public async Task RenderNotFoundWithBodyAsOrdinaryError()
        {
            // Arrange
            using var handler = RecordingHandler.Json(404, """{ "statusCode": "NotFound", "message": "No such dashboard." }""");

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
                                                      "1"),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(run.Error, "error 404 NotFound: No such dashboard.");
            Assert.IsFalse(run.Error.Contains("environment's document", System.StringComparison.Ordinal), run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.1")]
        public async Task ReadTokenLineBeforeBodyFromStandardInput()
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
                                                      "-"),
                                          $"{Tool.Token}\n{{ \"name\": \"from stdin\" }}\n");

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var request = handler.Requests[0];
            Assert.AreEqual($"Bearer {Tool.Token}", request.Headers["Authorization"]);
            Assert.AreEqual("""{"name":"from stdin"}""", request.Body);
        }
    }
}