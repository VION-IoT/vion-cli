using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Commands
{
    /// <summary>`vion auth login`, `logout` and `status`: the device-code sign-in, the store it writes, and what it reports.</summary>
    [TestClass]
    public class AuthCommandsShould
    {
        private const string DeviceEndpoint = "https://auth.test.vion.swiss/realms/vion/protocol/openid-connect/auth/device";

        private const string TokenEndpoint = "https://auth.test.vion.swiss/realms/vion/protocol/openid-connect/token";

        private const string DeviceAnswer =
            """{ "device_code": "dev-1", "user_code": "ABCD-EFGH", "verification_uri": "https://auth.test.vion.swiss/realms/vion/device", "verification_uri_complete": "https://auth.test.vion.swiss/realms/vion/device?user_code=ABCD-EFGH", "expires_in": 600, "interval": 5 }""";

        private static readonly DateTimeOffset Start = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.1")]
        public async Task RequestDeviceCodeAndPrintVerificationUrlAndCode()
        {
            // Arrange
            using var handler = DeviceFlow(["authorization_pending", null]);
            using var home = new TemporaryHome();
            var time = new FakeTimeProvider(Start);
            var opened = new List<Uri>();

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, ["auth", "login", "--env", "test"], home: home.Root, time: time, openBrowser: uri => { opened.Add(uri); return true; }));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var device = handler.To(DeviceEndpoint)[0];
            Assert.AreEqual("POST", device.Method);
            Assert.AreEqual("client_id=vion-cli&scope=openid+user_impersonation+web_mqtt_access", device.Body);
            StringAssert.Contains(run.Error, "https://auth.test.vion.swiss/realms/vion/device?user_code=ABCD-EFGH");
            StringAssert.Contains(run.Error, "ABCD-EFGH");
            CollectionAssert.AreEqual(new[] { new Uri("https://auth.test.vion.swiss/realms/vion/device?user_code=ABCD-EFGH") }, opened);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.1")]
        public async Task LeaveBrowserClosedWithNoBrowser()
        {
            // Arrange
            using var handler = DeviceFlow([null]);
            using var home = new TemporaryHome();
            var time = new FakeTimeProvider(Start);
            var opened = 0;

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, ["auth", "login", "--env", "test", "--no-browser"], home: home.Root, time: time, openBrowser: _ => { opened++; return true; }));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual(0, opened);
            StringAssert.Contains(run.Error, "ABCD-EFGH");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.2")]
        public async Task PollAtGrantedIntervalAndFiveSecondsLongerAfterSlowDown()
        {
            // Arrange
            var time = new FakeTimeProvider(Start);
            var polls = new List<DateTimeOffset>();
            using var handler = DeviceFlow(["authorization_pending", "slow_down", null], _ => polls.Add(time.GetUtcNow()));
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, ["auth", "login", "--env", "test", "--no-browser"], home: home.Root, time: time));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.HasCount(3, polls);
            Assert.AreEqual(TimeSpan.FromSeconds(5), polls[1] - polls[0]);
            Assert.AreEqual(TimeSpan.FromSeconds(10), polls[2] - polls[1]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.2")]
        [DataRow("access_denied")]
        [DataRow("expired_token")]
        public async Task ExitOneWhenTokenEndpointRefuses(string error)
        {
            // Arrange
            using var handler = DeviceFlow([error]);
            using var home = new TemporaryHome();
            var time = new FakeTimeProvider(Start);

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, ["auth", "login", "--env", "test", "--no-browser"], home: home.Root, time: time));

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(run.Error, error);
            Assert.IsFalse(File.Exists(Path.Combine(home.Root, "credentials.json")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.2")]
        public async Task ExitOneWhenCodeExpiresWhilePending()
        {
            // Arrange
            using var handler = new RecordingHandler(request => request.RequestUri!.ToString() == DeviceEndpoint ?
                                                                    RecordingHandler.Response(200, DeviceAnswer.Replace("\"expires_in\": 600", "\"expires_in\": 12", StringComparison.Ordinal)) :
                                                                    RecordingHandler.Response(400, """{ "error": "authorization_pending" }"""));
            using var home = new TemporaryHome();
            var time = new FakeTimeProvider(Start);

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, ["auth", "login", "--env", "test", "--no-browser"], home: home.Root, time: time));

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(run.Error, "expired");
            Assert.HasCount(2, handler.To(TokenEndpoint));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.3")]
        public async Task StoreCredentialsPerEnvironmentReadableByOwnerOnly()
        {
            // Arrange
            using var handler = DeviceFlow([null]);
            using var home = new TemporaryHome();
            home.Write("credentials.json", """{ "production": { "accessToken": "prod-at", "refreshToken": null, "expiresAt": "2026-10-29T09:00:00+00:00", "obtainedBy": "device" } }""");
            var time = new FakeTimeProvider(Start);

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, ["auth", "login", "--env", "test", "--no-browser"], home: home.Root, time: time));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var path = Path.Combine(home.Root, "credentials.json");
            var stored = JsonNode.Parse(File.ReadAllText(path))!;
            Assert.AreEqual("at-1", stored["test"]!["accessToken"]!.GetValue<string>());
            Assert.AreEqual("rt-1", stored["test"]!["refreshToken"]!.GetValue<string>());
            Assert.AreEqual(time.GetUtcNow().AddSeconds(300), DateTimeOffset.Parse(stored["test"]!["expiresAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));
            Assert.AreEqual("prod-at", stored["production"]!["accessToken"]!.GetValue<string>());
            if (OperatingSystem.IsWindows())
            {
                var security = new FileInfo(path).GetAccessControl();
                var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
                Assert.IsTrue(security.AreAccessRulesProtected, "the file inherits rules");
                Assert.HasCount(1, rules);
                Assert.AreEqual(WindowsIdentity.GetCurrent().User, ((FileSystemAccessRule)rules[0]!).IdentityReference);
            }
            else
            {
                Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.6")]
        [DataRow(true, "removed")]
        [DataRow(false, "none were stored")]
        public async Task RemoveEnvironmentCredentialsAndSayWhetherAnyWereStored(bool stored, string said)
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);
            using var home = new TemporaryHome();
            home.Write("credentials.json",
                       stored ?
                           """{ "production": { "accessToken": "prod-at", "refreshToken": null, "expiresAt": "2026-10-29T09:00:00+00:00", "obtainedBy": "device" }, "test": { "accessToken": "test-at", "refreshToken": "rt", "expiresAt": "2026-10-29T09:00:00+00:00", "obtainedBy": "device" } }""" :
                           """{ "production": { "accessToken": "prod-at", "refreshToken": null, "expiresAt": "2026-10-29T09:00:00+00:00", "obtainedBy": "device" } }""");

            // Act
            var run = await Tool.RunAsync(handler, ["auth", "logout", "--env", "test"], home: home.Root);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(run.Out, said);
            var remaining = JsonNode.Parse(File.ReadAllText(Path.Combine(home.Root, "credentials.json")))!.AsObject();
            Assert.IsFalse(remaining.ContainsKey("test"));
            Assert.IsTrue(remaining.ContainsKey("production"));
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.7")]
        public async Task ReportStoredSessionsAndNextSource()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);
            using var home = new TemporaryHome();
            var time = new FakeTimeProvider(Start);
            var named = Tool.Jwt("""{ "sub": "u-1", "preferred_username": "operator@example.test" }""");
            var anonymous = Tool.Jwt("""{ "sub": "u-2" }""");
            home.Write("credentials.json",
                       $$"""{ "test": { "accessToken": "{{named}}", "refreshToken": "rt", "expiresAt": "2026-10-29T09:00:00+00:00", "obtainedBy": "device" }, "production": { "accessToken": "{{anonymous}}", "refreshToken": null, "expiresAt": "2026-10-29T10:00:00+00:00", "obtainedBy": "device" } }""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          ["auth", "status", "--env", "test", "--json"],
                                          home: home.Root,
                                          time: time,
                                          environment: new Dictionary<string, string?> { ["VION_CLIENT_ID"] = "ci", ["VION_CLIENT_SECRET"] = "s" });

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            var document = JsonNode.Parse(run.Out)!;
            var sessions = document["sessions"]!.AsArray();
            Assert.HasCount(2, sessions);
            var test = sessions.Single(s => s!["environment"]!.GetValue<string>() == "test")!;
            Assert.AreEqual("operator@example.test", test["subject"]!.GetValue<string>());
            Assert.AreEqual("2026-10-29T09:00:00+00:00", test["expiresAt"]!.GetValue<string>());
            Assert.IsTrue(test["refreshToken"]!.GetValue<bool>());
            var production = sessions.Single(s => s!["environment"]!.GetValue<string>() == "production")!;
            Assert.AreEqual("u-2", production["subject"]!.GetValue<string>());
            Assert.IsFalse(production["refreshToken"]!.GetValue<bool>());
            Assert.AreEqual("test", document["next"]!["environment"]!.GetValue<string>());
            StringAssert.Contains(document["next"]!["source"]!.GetValue<string>(), "VION_CLIENT_ID");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.7")]
        public async Task ReportStoredSessionAsTable()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(500);
            using var home = new TemporaryHome();
            var named = Tool.Jwt("""{ "sub": "u-1", "preferred_username": "operator@example.test" }""");
            home.Write("credentials.json", $$"""{ "test": { "accessToken": "{{named}}", "refreshToken": "rt", "expiresAt": "2026-10-29T09:00:00+00:00", "obtainedBy": "device" } }""");

            // Act
            var run = await Tool.RunAsync(handler, ["auth", "status", "--env", "test"], home: home.Root);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(run.Out, "operator@example.test");
            StringAssert.Contains(run.Out, "2026-10-29T09:00:00+00:00");
            StringAssert.Contains(run.Out, "next call on test: the stored credentials");
            Assert.IsFalse(run.Out.Contains(named, StringComparison.Ordinal), "the token itself was printed");
        }

        /// <summary>
        ///     The realm's two endpoints: the device code once, then the token endpoint answering each listed error in
        ///     turn and a token for `null`; any poll past the list answers a token too.
        /// </summary>
        private static RecordingHandler DeviceFlow(IReadOnlyList<string?> tokenAnswers, Action<HttpRequestMessage>? onPoll = null)
        {
            var polls = 0;
            return new RecordingHandler(request =>
                                        {
                                            var url = request.RequestUri!.ToString();
                                            if (url == DeviceEndpoint)
                                            {
                                                return RecordingHandler.Response(200, DeviceAnswer);
                                            }

                                            if (url == TokenEndpoint)
                                            {
                                                onPoll?.Invoke(request);
                                                var answer = polls < tokenAnswers.Count ? tokenAnswers[polls] : null;
                                                polls++;
                                                return answer == null ?
                                                           RecordingHandler.Response(200, """{ "access_token": "at-1", "refresh_token": "rt-1", "expires_in": 300, "token_type": "Bearer" }""") :
                                                           RecordingHandler.Response(400, $$"""{ "error": "{{answer}}", "error_description": "{{answer}} from the realm" }""");
                                            }

                                            return RecordingHandler.Response(500, "{}");
                                        });
        }
    }
}
