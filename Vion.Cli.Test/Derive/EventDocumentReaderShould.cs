using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vion.Cli.Derive;

namespace Vion.Cli.Test.Derive
{
    /// <summary>
    ///     The derive step over the committed event document and over small synthetic ones: which channels become watch
    ///     commands, which address parameters become options, and what the step refuses. The collisions are proven on
    ///     synthetic documents only, since the real one has none. One premise test cites no id by design: the refusal of a
    ///     hand-out naming an operation the OpenAPI document lacks guards the derivation itself, which no user observes.
    /// </summary>
    [TestClass]
    public class EventDocumentReaderShould
    {
        private const string Thing = """
                                     "tenant.thing.state": {
                                       "description": "A thing.",
                                       "parameters": { "tenantId": { "description": "The tenant's id." }, "thingId": { "description": "The thing's id." } },
                                       "messages": { "ThingUpdate": {} },
                                       "x-vion-handedOutBy": [ { "schema": "ThingOutput", "key": "tenant.thing.state", "operation": "tenant.thing.list", "row": "items[]",
                                         "join": [ { "channelParameter": "tenantId", "operationParameter": "tenantId" }, { "channelParameter": "thingId", "member": "items[].id" } ] } ]
                                     }
                                     """;

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-001.1")]
        public void DeriveWatchCommandForEveryHandedOutChannelOfSnapshotAndNoOther()
        {
            // Arrange
            var operations = Deriver.Derive(Fixture("v1.json"), "{}");
            var events = Fixture("asyncapi-v1.json");

            // Act
            var channels = Deriver.DeriveChannels(events, operations);

            // Assert
            using var document = JsonDocument.Parse(events);
            var handedOut = document.RootElement
                                    .GetProperty("channels")
                                    .EnumerateObject()
                                    .Where(channel => channel.Value.TryGetProperty("x-vion-handedOutBy", out _))
                                    .Select(channel => channel.Name)
                                    .ToList();
            CollectionAssert.AreEqual(handedOut, channels.Channels.Select(channel => channel.Id).ToList());
            Assert.DoesNotContain("me.lastWill.event", handedOut);
            var deployment = channels.Channels.Single(channel => channel.Id == "integrator.softwareDeployment.state");
            Assert.AreEqual("integrator software-deployment", $"{deployment.CommandDomain} {deployment.CommandAggregate}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-001.2")]
        [DataRow("tenant.syncStatus.state", "tenantId", null, "tenant", true)]
        [DataRow("tenant.syncStatus.state", "edgeGatewayId", "edge-gateway-id", null, true)]
        [DataRow("integrator.systemHealth.state", "integratorId", null, "integrator", true)]
        [DataRow("integrator.systemHealth.state", "edgeGatewayId", "edge-gateway-id", null, false)]
        [DataRow("tenant.serviceProperty.state", "serviceIdentifier", "service-identifier", null, true)]
        public void NameOptionFromAddressParameterThatSelectsRow(string channelId, string parameter, string? optionName, string? global, bool required)
        {
            // Arrange
            var operations = Deriver.Derive(Fixture("v1.json"), "{}");

            // Act
            var channels = Deriver.DeriveChannels(Fixture("asyncapi-v1.json"), operations);

            // Assert
            var derived = channels.Channels.Single(channel => channel.Id == channelId).Parameters.Single(p => p.Name == parameter);
            Assert.AreEqual(optionName, derived.OptionName);
            Assert.AreEqual(global, derived.Global);
            Assert.AreEqual(required, derived.Required);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-001.2")]
        [DataRow("tenant.alert.state", "alertRuleId")]
        [DataRow("me.notification.event", "identityProviderUserId")]
        public void TakeNoOptionForAddressParameterThatSelectsNoRow(string channelId, string parameter)
        {
            // Arrange
            var operations = Deriver.Derive(Fixture("v1.json"), "{}");

            // Act
            var channels = Deriver.DeriveChannels(Fixture("asyncapi-v1.json"), operations);

            // Assert
            Assert.IsFalse(channels.Channels.Single(channel => channel.Id == channelId).Parameters.Any(p => p.Name == parameter));
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-001.3")]
        public void RefuseTwoChannelsThatDeriveToOneCommand()
        {
            // Arrange
            var operations = Deriver.Derive(Fixture("synthetic.json"), "{}");
            var events = Events(Thing + "," + Thing.Replace("tenant.thing.state", "tenant.thing.event", StringComparison.Ordinal));

            // Act / Assert
            var exception = Assert.ThrowsExactly<DeriveException>(() => Deriver.DeriveChannels(events, operations));
            Assert.AreEqual("channels 'tenant.thing.state' and 'tenant.thing.event' both derive to `vion tenant thing watch`.", exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-001.3")]
        public void RefuseChannelThatDerivesToCommandOfOperation()
        {
            // Arrange
            var derived = Deriver.Derive(Fixture("synthetic.json"), "{}");
            var watching = derived.Operations.Single(operation => operation.Id == "tenant.thing.get") with { Id = "tenant.thing.watch", Verb = "watch", CommandVerb = "watch" };
            var operations = derived with { Operations = [.. derived.Operations, watching] };

            // Act / Assert
            var exception = Assert.ThrowsExactly<DeriveException>(() => Deriver.DeriveChannels(Events(Thing), operations));
            Assert.AreEqual("channel 'tenant.thing.state' and operation 'tenant.thing.watch' both derive to `vion tenant thing watch`.", exception.Message);
        }

        [TestMethod]
        public void RefuseHandOutNamingOperationOpenApiDocumentLacks()
        {
            // Arrange
            var operations = Deriver.Derive(Fixture("synthetic.json"), "{}");
            var events = Events(Thing.Replace("\"operation\": \"tenant.thing.list\"", "\"operation\": \"tenant.thing.missing\"", StringComparison.Ordinal));

            // Act / Assert
            var exception = Assert.ThrowsExactly<DeriveException>(() => Deriver.DeriveChannels(events, operations));
            StringAssert.Contains(exception.Message, "'tenant.thing.missing'");
        }

        private static string Events(string channels)
        {
            return "{ \"asyncapi\": \"3.0.0\", \"channels\": {" + channels + "} }";
        }

        private static string Fixture(string name)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        }
    }
}