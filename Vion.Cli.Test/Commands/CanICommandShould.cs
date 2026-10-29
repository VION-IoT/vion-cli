using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Commands
{
    /// <summary>
    ///     `vion can-i`: the environment's catalog row joined with the caller's own permissions at the context root, decided
    ///     as the dashboard decides. The catalog rows mirror `tenant.overviewSectionArrangement.upsert` (Root),
    ///     `tenant.dashboardArrangement.upsert` (Scope), `tenant.projectArrangement.list` (Filtered) and `me.identity.get`
    ///     (Caller) as the snapshot carries them.
    /// </summary>
    [TestClass]
    public class CanICommandShould
    {
        private const string Project = "33333333-3333-3333-3333-333333333333";

        private const string Catalog = """
                                       { "items": [
                                         { "id": "tenant.overviewSectionArrangement.upsert", "method": "PUT", "path": "/tenant/{tenantId}/arrangements/{collection}", "effect": "Reversible", "idempotent": true, "versioned": true, "dryRun": false,
                                           "authorization": { "mode": "Root", "permissions": ["Tenant.ManageTenantOverview"], "scopeType": "Tenant", "scopeFrom": "tenantId" } },
                                         { "id": "tenant.dashboardArrangement.upsert", "method": "PUT", "path": "/tenant/{tenantId}/arrangements/dashboards/{projectId}", "effect": "Reversible", "idempotent": true, "versioned": true, "dryRun": false,
                                           "authorization": { "mode": "Scope", "permissions": ["Project.ManageDashboards"], "scopeType": "Project", "scopeFrom": "projectId" } },
                                         { "id": "tenant.projectArrangement.list", "method": "GET", "path": "/tenant/{tenantId}/arrangements/projects", "effect": "Read", "idempotent": false, "versioned": false, "dryRun": false,
                                           "authorization": { "mode": "Filtered", "permissions": ["Project.Read"], "scopeType": "Project", "scopeFrom": "projectId" } },
                                         { "id": "me.identity.get", "method": "GET", "path": "/me", "effect": "Read", "idempotent": false, "versioned": false, "dryRun": false,
                                           "authorization": { "mode": "Caller", "permissions": [], "scopeType": null, "scopeFrom": null } }
                                       ] }
                                       """;

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.3")]
        [DataRow("""["Tenant.ManageTenantOverview"]""", 0, "allowed")]
        [DataRow("""["Tenant.Read"]""", 1, "denied")]
        public async Task DecideRootOperationFromRootSet(string rootPermissions, int exitCode, string decision)
        {
            // Arrange
            using var handler = Environment(rootPermissions, "[]");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("can-i", "tenant.overviewSectionArrangement.upsert", "--tenant", Tool.Tenant), Tool.Token);

            // Assert
            Assert.AreEqual(exitCode, run.ExitCode, run.Error);
            StringAssert.Contains(run.Out, decision);
            StringAssert.Contains(run.Out, "Tenant.ManageTenantOverview");
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Tool.Tenant}/access/me/permissions", handler.To("/access/me/permissions")[0].Uri.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.3")]
        [DataRow("tenant.dashboardArrangement.upsert", Project, 0)]
        [DataRow("tenant.dashboardArrangement.upsert", "66666666-6666-6666-6666-666666666666", 1)]
        [DataRow("tenant.projectArrangement.list", Project, 1)]
        public async Task DecideScopeOrFilteredOperationAtProjectFromHeldScopeElseRoot(string operation, string project, int exitCode)
        {
            // Arrange
            using var handler = Environment("[]",
                                            $$"""[ { "projectId": "{{Project}}", "scopeId": "{{Project}}", "scopeType": "Project", "permissions": ["Project.ManageDashboards"] } ]""");

            // Act
            var run = await Tool.RunAsync(handler,
                                          Tool.OnTest("can-i",
                                                      operation,
                                                      "--tenant",
                                                      Tool.Tenant,
                                                      "--project",
                                                      project),
                                          Tool.Token);

            // Assert
            Assert.AreEqual(exitCode, run.ExitCode, run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.3")]
        [DataRow("tenant.dashboardArrangement.upsert")]
        [DataRow("me.identity.get")]
        public async Task AllowWhenNothingDecides(string operation)
        {
            // Arrange
            using var handler = Environment("[]", "[]");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("can-i", operation, "--tenant", Tool.Tenant), Tool.Token);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            StringAssert.Contains(run.Out, "allowed");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.3")]
        public async Task AnswerUnknownForOperationTheCatalogLacks()
        {
            // Arrange
            using var handler = Environment("[]", "[]");

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest("can-i", "tenant.nothing.here", "--tenant", Tool.Tenant, "--json"), Tool.Token);

            // Assert
            Assert.AreEqual(2, run.ExitCode, run.Error);
            Assert.AreEqual("unknown", JsonNode.Parse(run.Out)!["decision"]!.GetValue<string>());
        }

        /// <summary>The catalog, and the tenant's own permissions as given.</summary>
        private static RecordingHandler Environment(string rootPermissions, string scopes)
        {
            return new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/capabilities" ?
                                                       RecordingHandler.Response(200, Catalog) :
                                                       request.RequestUri.AbsolutePath.EndsWith("/access/me/permissions", System.StringComparison.Ordinal) ?
                                                           RecordingHandler.Response(200,
                                                                                     $$"""{ "tenantId": "{{Tool.Tenant}}", "root": {{rootPermissions}}, "scopes": {{scopes}}, "meta": { "permissions": [], "version": null } }""") :
                                                           RecordingHandler.Response(404, "{}"));
        }
    }
}