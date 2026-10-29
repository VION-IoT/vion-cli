using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Commands
{
    /// <summary>`vion whoami`: the caller's identity and memberships from `me.identity.get`, with the ids a context takes.</summary>
    [TestClass]
    public class WhoamiCommandShould
    {
        private const string Me =
            """
            { "user": { "identityProviderUserId": "idp-1", "email": "operator@example.test" }, "locale": "de", "notificationTopic": null,
              "tenantMemberships": [ { "tenantUserId": "tu-1", "tenantId": "11111111-1111-1111-1111-111111111111", "tenantSlug": "acme", "tenantName": "Acme Energy" } ],
              "integratorMemberships": [ { "integratorUserId": "iu-1", "integratorId": "44444444-4444-4444-4444-444444444444", "integratorSlug": "integ", "integratorName": "Integ AG", "tags": [] } ],
              "platformMemberships": [] }
            """;

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.4")]
        public async Task PrintIdentityAndMembershipsWithIds()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, Me);

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("whoami"), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("https://api.test.vion.swiss/me", handler.Requests[0].Uri.ToString());
            Assert.AreEqual($"Bearer {Tool.Token}", handler.Requests[0].Headers["Authorization"]);
            StringAssert.Contains(run.Out, "operator@example.test");
            StringAssert.Contains(run.Out, "Acme Energy");
            StringAssert.Contains(run.Out, Tool.Tenant);
            StringAssert.Contains(run.Out, "Integ AG");
            StringAssert.Contains(run.Out, "44444444-4444-4444-4444-444444444444");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.4")]
        public async Task WriteIdentityDocumentVerbatimInJsonMode()
        {
            // Arrange
            using var handler = RecordingHandler.Json(200, Me);

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("whoami", "--json"), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.IsTrue(JsonNode.DeepEquals(JsonNode.Parse(Me), JsonNode.Parse(run.Out)), run.Out);
        }
    }
}
