using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Auth
{
    /// <summary>
    ///     Where a call's bearer token comes from, in the one order every call tries; proven where two sources are both
    ///     present.
    /// </summary>
    [TestClass]
    public class TokenSourceShould
    {
        private const string TokenEndpoint = "https://auth.test.vion.swiss/realms/vion/protocol/openid-connect/token";

        private const string Api = "https://api.test.vion.swiss/";

        private static readonly DateTimeOffset Now = new(2026,
                                                         10,
                                                         29,
                                                         8,
                                                         0,
                                                         0,
                                                         TimeSpan.Zero);

        private static readonly string[] List = ["tenant", "edge-gateway", "list", "--tenant", Tool.Tenant, "--env", "test"];

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.4")]
        public async Task UseTokenFromStandardInputBeforeEveryOtherSource()
        {
            // Arrange
            using var handler = Realm("issued-1");
            using var home = new TemporaryHome();
            Store(home, "stored-at", Now.AddHours(1));

            // Act
            var run = await Tool.RunAsync(handler,
                                          [.. List, "--token", "-", "--client-id", "flag", "--client-secret", "s"],
                                          "piped-token",
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          environment: new Dictionary<string, string?> { ["VION_CLIENT_ID"] = "ci", ["VION_CLIENT_SECRET"] = "s" });

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("Bearer piped-token", handler.To(Api)[0].Headers["Authorization"]);
            Assert.IsEmpty(handler.To(TokenEndpoint));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.4")]
        public async Task UseClientCredentialFlagsBeforeVariables()
        {
            // Arrange
            using var handler = Realm("issued-1");
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler,
                                          [.. List, "--client-id", "flag-client", "--client-secret", "flag-secret"],
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          environment: new Dictionary<string, string?> { ["VION_CLIENT_ID"] = "ci", ["VION_CLIENT_SECRET"] = "s" });

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("grant_type=client_credentials&client_id=flag-client&client_secret=flag-secret&scope=openid", handler.To(TokenEndpoint)[0].Body);
            Assert.AreEqual("Bearer issued-1", handler.To(Api)[0].Headers["Authorization"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.4")]
        public async Task UseClientCredentialVariablesBeforeStore()
        {
            // Arrange
            using var handler = Realm("issued-1");
            using var home = new TemporaryHome();
            Store(home, "stored-at", Now.AddHours(1));

            // Act
            var run = await Tool.RunAsync(handler,
                                          List,
                                          home: home.Root,
                                          time: new FakeTimeProvider(Now),
                                          environment: new Dictionary<string, string?> { ["VION_CLIENT_ID"] = "ci", ["VION_CLIENT_SECRET"] = "ci-secret" });

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("grant_type=client_credentials&client_id=ci&client_secret=ci-secret&scope=openid", handler.To(TokenEndpoint)[0].Body);
            Assert.AreEqual("Bearer issued-1", handler.To(Api)[0].Headers["Authorization"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.4")]
        public async Task UseStoredTokenWhileItIsValid()
        {
            // Arrange
            using var handler = Realm("issued-1");
            using var home = new TemporaryHome();
            Store(home, "stored-at", Now.AddSeconds(31));

            // Act
            var run = await Tool.RunAsync(handler, List, home: home.Root, time: new FakeTimeProvider(Now));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("Bearer stored-at", handler.To(Api)[0].Headers["Authorization"]);
            Assert.IsEmpty(handler.To(TokenEndpoint));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.4")]
        public async Task RefreshStoredTokenExpiringWithinThirtySeconds()
        {
            // Arrange
            using var handler = Realm("issued-2");
            using var home = new TemporaryHome();
            Store(home, "stored-at", Now.AddSeconds(29), "rt-1");

            // Act
            var run = await Tool.RunAsync(handler, List, home: home.Root, time: new FakeTimeProvider(Now));

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("grant_type=refresh_token&client_id=vion-cli&refresh_token=rt-1", handler.To(TokenEndpoint)[0].Body);
            Assert.AreEqual("Bearer issued-2", handler.To(Api)[0].Headers["Authorization"]);
            var stored = JsonNode.Parse(File.ReadAllText(Path.Combine(home.Root, "credentials.json")))!["test"]!;
            Assert.AreEqual("issued-2", stored["accessToken"]!.GetValue<string>());
            Assert.AreEqual("rt-2", stored["refreshToken"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.4")]
        public async Task RefuseWhenRefreshFails()
        {
            // Arrange
            using var handler = new RecordingHandler(_ => RecordingHandler.Response(400, """{ "error": "invalid_grant", "error_description": "Session not active" }"""));
            using var home = new TemporaryHome();
            Store(home, "stored-at", Now.AddSeconds(-1), "rt-1");

            // Act
            var run = await Tool.RunAsync(handler, List, home: home.Root, time: new FakeTimeProvider(Now));

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(run.Error, "auth login");
            Assert.IsEmpty(handler.To(Api));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.4")]
        public async Task RefuseClientIdWithoutSecret()
        {
            // Arrange
            using var handler = Realm("issued-1");
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler, [.. List, "--client-id", "flag-client"], home: home.Root, time: new FakeTimeProvider(Now));

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.Contains(run.Error, "--client-secret");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.5")]
        public async Task RefuseNamingEverySourceWhenNoneYieldsToken()
        {
            // Arrange
            using var handler = Realm("issued-1");
            using var home = new TemporaryHome();

            // Act
            var run = await Tool.RunAsync(handler, List, home: home.Root, time: new FakeTimeProvider(Now));

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(run.Error, "auth login");
            StringAssert.Contains(run.Error, "VION_CLIENT_ID");
            StringAssert.Contains(run.Error, "--token");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-004.3")]
        public async Task IgnoreCredentialsStoredForAnotherEnvironment()
        {
            // Arrange
            using var handler = Realm("issued-1");
            using var home = new TemporaryHome();
            Store(home, "prod-at", Now.AddHours(1), environment: "production");

            // Act
            var run = await Tool.RunAsync(handler, List, home: home.Root, time: new FakeTimeProvider(Now));

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.Contains(run.Error, "auth login");
            Assert.IsEmpty(handler.Requests);
        }

        /// <summary>The realm issuing one token for any grant, and the API answering an empty list.</summary>
        private static RecordingHandler Realm(string accessToken)
        {
            return new RecordingHandler(request => request.RequestUri!.ToString() == TokenEndpoint ?
                                                       RecordingHandler.Response(200, $$"""{ "access_token": "{{accessToken}}", "refresh_token": "rt-2", "expires_in": 300 }""") :
                                                       RecordingHandler.Response(200, """{ "items": [] }"""));
        }

        private static void Store(TemporaryHome home, string accessToken, DateTimeOffset expiresAt, string? refreshToken = null, string environment = "test")
        {
            var refresh = refreshToken == null ? "null" : $"\"{refreshToken}\"";
            home.Write("credentials.json",
                       $$"""{ "{{environment}}": { "accessToken": "{{accessToken}}", "refreshToken": {{refresh}}, "expiresAt": "{{expiresAt.ToString("o", System.Globalization.CultureInfo.InvariantCulture)}}", "obtainedBy": "device" } }""");
        }
    }
}