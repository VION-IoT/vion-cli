using System;
using System.IO;
using System.Linq;
using Vion.Cli.Derive;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Derive
{
    /// <summary>
    ///     The derive step over the real snapshot and over the synthetic document: which operations become commands, how a
    ///     parameter becomes an option, and what cli.json may name. Two premise tests cite no id by design: the refusals of
    ///     an operation without an effect and of a completion outside its two values guard the derivation itself, which
    ///     no user observes.
    /// </summary>
    [TestClass]
    public class DeriverShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.1")]
        public void DeriveEveryOperationOfSnapshotWithKebabCaseCommandSegments()
        {
            // Arrange
            var snapshot = Fixture("v1.json");

            // Act
            var document = Deriver.Derive(snapshot, "{}");

            // Assert
            Assert.HasCount(Snapshot.OperationCount, document.Operations);
            Assert.HasCount(Snapshot.TreeOperationCount, document.Operations.Where(o => o.Domain != "system").ToList());
            var listing = document.Operations.Single(o => o.Id == "tenant.edgeGateway.list");
            Assert.AreEqual("tenant edge-gateway list", $"{listing.CommandDomain} {listing.CommandAggregate} {listing.CommandVerb}");
            var marking = document.Operations.Single(o => o.Id == "me.notification.markRead");
            Assert.AreEqual("me notification mark-read", $"{marking.CommandDomain} {marking.CommandAggregate} {marking.CommandVerb}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.3")]
        [DataRow("tenant.edgeGateway.list", "tenantId", null, "tenant", "Uuid")]
        [DataRow("tenant.edgeGateway.list", "edgeGatewayId", "edge-gateway-id", null, "Uuid")]
        [DataRow("tenant.edgeGateway.list", "projectId", null, "project", "Uuid")]
        [DataRow("tenant.logicConfiguration.activate", "dryRun", null, "dry-run", "Boolean")]
        [DataRow("tenant.alert.list", "limit", "limit", null, "Integer")]
        [DataRow("tenant.alert.list", "from", "from", null, "DateTime")]
        [DataRow("tenant.myPermissions.get", "scopeId", "scope-id", null, "StringArray")]
        [DataRow("me.notification.list", "unread", "unread", null, "Boolean")]
        public void NameAndTypeOptionFromParameter(string operationId, string parameter, string? optionName, string? global, string kind)
        {
            // Arrange
            var snapshot = Fixture("v1.json");

            // Act
            var document = Deriver.Derive(snapshot, "{}");

            // Assert
            var derived = document.Operations.Single(o => o.Id == operationId).Parameters.Single(p => p.Name == parameter);
            Assert.AreEqual(optionName, derived.OptionName);
            Assert.AreEqual(global, derived.Global);
            Assert.AreEqual(kind, derived.Kind.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.4")]
        public void SuffixOptionCollidingWithGlobal()
        {
            // Arrange
            var synthetic = Fixture("synthetic.json");

            // Act
            var document = Deriver.Derive(synthetic, "{}");

            // Assert
            var parameters = document.Operations.Single(o => o.Id == "tenant.thing.list").Parameters;
            Assert.AreEqual("json-param", parameters.Single(p => p.Name == "json").OptionName);
            Assert.AreEqual("edge-gateway-id", parameters.Single(p => p.Name == "edgeGatewayId").OptionName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.4")]
        public void LeaveEveryOptionOfSnapshotOutsideGlobalNames()
        {
            // Arrange
            var snapshot = Fixture("v1.json");

            // Act
            var document = Deriver.Derive(snapshot, "{}");

            // Assert
            var suffixed = document.Operations.SelectMany(o => o.Parameters).Where(p => p.OptionName?.EndsWith("-param", StringComparison.Ordinal) == true).ToList();
            Assert.IsEmpty(suffixed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.5")]
        public void RefusePresentationEntryDocumentLacks()
        {
            // Arrange
            var synthetic = Fixture("synthetic.json");

            // Act / Assert
            var exception = Assert.ThrowsExactly<DeriveException>(() => Deriver.Derive(synthetic, """{ "tenant.doesNot.exist": { "hide": true } }"""));
            StringAssert.Contains(exception.Message, "tenant.doesNot.exist");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.5")]
        public void CarryPresentationEntryDocumentHas()
        {
            // Arrange
            var synthetic = Fixture("synthetic.json");

            // Act
            var document = Deriver.Derive(synthetic, """{ "tenant.thing.list": { "hide": true, "alias": "ls" } }""");

            // Assert
            var operation = document.Operations.Single(o => o.Id == "tenant.thing.list");
            Assert.IsTrue(operation.Hidden);
            Assert.AreEqual("ls", operation.Alias);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.2")]
        public void MarkDeprecatedOperation()
        {
            // Arrange
            var synthetic = Fixture("synthetic.json");

            // Act
            var document = Deriver.Derive(synthetic, "{}");

            // Assert
            Assert.IsTrue(document.Operations.Single(o => o.Id == "tenant.thing.get").Deprecated);
            Assert.IsFalse(document.Operations.Single(o => o.Id == "tenant.thing.list").Deprecated);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-008.1")]
        public void CarryCompletionAndLongRunningAnswerOfEveryOperation()
        {
            // Arrange
            var snapshot = Fixture("v1.json");
            using var json = System.Text.Json.JsonDocument.Parse(snapshot);
            var published = json.RootElement
                                .GetProperty("paths")
                                .EnumerateObject()
                                .SelectMany(path => path.Value.EnumerateObject())
                                .Where(method => method.Value.TryGetProperty("operationId", out _))
                                .ToDictionary(method => method.Value.GetProperty("operationId").GetString()!,
                                              method => method.Value.GetProperty("x-vion-capability").GetProperty("completion").GetString());

            // Act
            var document = Deriver.Derive(snapshot, "{}");

            // Assert
            foreach (var operation in document.Operations)
            {
                Assert.AreEqual(published[operation.Id], operation.Completion, operation.Id);
            }

            CollectionAssert.AreEquivalent(new[]
                                           {
                                               "me.onboarding.provision",
                                               "integrator.edgeGateway.create",
                                               "integrator.edgeGatewaySimulation.update",
                                               "me.longRunningOperation.get",
                                               "integrator.longRunningOperation.get",
                                           },
                                           document.Operations.Where(o => o.AnswersLongRunningOperation).Select(o => o.Id).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.7")]
        public void ReadMultipartBodyParts()
        {
            // Arrange
            var snapshot = Fixture("v1.json");

            // Act
            var document = Deriver.Derive(snapshot, "{}");

            // Assert
            var upload = document.Operations.Single(o => o.Id == "integrator.logicBlockLibraryVersion.upload").Body!;
            Assert.AreEqual("multipart/form-data", upload.ContentType);
            Assert.AreEqual("nugetPackageFile", upload.FilePart);
            CollectionAssert.AreEquivalent(new[] { "releaseNotes", "tags" }, upload.TextParts.ToArray());
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.3")]
        [DataRow("edgeGatewayId", "edge-gateway-id")]
        [DataRow("userIDs", "user-ids")]
        [DataRow("logLevel", "log-level")]
        [DataRow("id", "id")]
        public void KebabCaseNameWithConsecutiveCapitalsTogether(string camelCase, string kebab)
        {
            // Arrange

            // Act
            var named = Naming.Kebab(camelCase);

            // Assert
            Assert.AreEqual(kebab, named);
        }

        [TestMethod]
        public void RefuseOperationWithoutEffect()
        {
            // Arrange
            var synthetic = Fixture("synthetic.json").Replace("\"effect\": \"Read\",", "\"effectless\": \"Read\",", StringComparison.Ordinal);

            // Act / Assert
            var exception = Assert.ThrowsExactly<DeriveException>(() => Deriver.Derive(synthetic, "{}"));
            StringAssert.Contains(exception.Message, "x-vion-capability.effect");
        }

        [TestMethod]
        public void RefuseCompletionOutsideItsTwoValues()
        {
            // Arrange
            var synthetic = Fixture("synthetic.json").Replace("\"effect\": \"Read\",", "\"effect\": \"Read\", \"completion\": \"Eventually\",", StringComparison.Ordinal);

            // Act / Assert
            var exception = Assert.ThrowsExactly<DeriveException>(() => Deriver.Derive(synthetic, "{}"));
            StringAssert.Contains(exception.Message, "x-vion-capability.completion 'Eventually'");
        }

        private static string Fixture(string name)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        }
    }
}