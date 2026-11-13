using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vion.Cli.Derive;

namespace Vion.Cli.Test.Derive
{
    /// <summary>
    ///     The derive step over the committed event document and over small synthetic ones: which channels become watch
    ///     commands, which address parameters become options, and what the step refuses. The collisions are proven on
    ///     synthetic documents only, since the real one has none. Four premise tests cite no id by design: the refusals of a
    ///     hand-out naming an operation the OpenAPI document lacks, of a registration filling from a parameter no caller
    ///     supplies, of a handed-out channel no operation sends on, and of servers without one protocol, guard the
    ///     derivation itself, which no user observes.
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

        [TestMethod]
        public void RefuseRegistrationThatFillsFromParameterCallerDoesNotSupply()
        {
            // Arrange
            var operations = Deriver.Derive(Fixture("synthetic.json"), "{}");
            var registered = Thing.TrimEnd().TrimEnd('}') +
                             ", \"x-vion-registeredBy\": { \"operation\": \"tenant.thing.list\", \"subscriberId\": \"subscriberId\", \"fill\": [ { \"channelParameter\": \"elsewhereId\", \"bodyMember\": \"elsewhereId\" } ] } }";

            // Act / Assert
            var exception = Assert.ThrowsExactly<DeriveException>(() => Deriver.DeriveChannels(Events(registered), operations));
            StringAssert.Contains(exception.Message, "'elsewhereId'");
        }

        [TestMethod]
        public void RefuseHandedOutChannelNoOperationSendsOn()
        {
            // Arrange
            var operations = Deriver.Derive(Fixture("synthetic.json"), "{}");
            var events = Events(Thing).Replace("\"action\": \"send\"", "\"action\": \"receive\"", StringComparison.Ordinal);

            // Act / Assert
            var exception = Assert.ThrowsExactly<DeriveException>(() => Deriver.DeriveChannels(events, operations));
            Assert.AreEqual("channel 'tenant.thing.state' is handed out and no operation of the event document sends on it.", exception.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-006.1")]
        public void ReadProtocolFromServers()
        {
            // Arrange
            var operations = Deriver.Derive(Fixture("synthetic.json"), "{}");

            // Act
            var channels = Deriver.DeriveChannels(Events(Thing, "\"a\": { \"protocol\": \"amqp\" }, \"b\": { \"protocol\": \"amqp\" }"), operations);

            // Assert
            Assert.AreEqual("amqp", channels.Protocol);
            StringAssert.Contains(ChannelEmitter.Emit(channels), "new Channel(\n            \"tenant.thing.state\", \"tenant.thing.state\", \"amqp\",");
        }

        [TestMethod]
        [DataRow("", "the event document's servers declare no protocol.")]
        [DataRow("\"a\": { \"protocol\": \"mqtt\" }, \"b\": { \"protocol\": \"amqp\" }", "the event document's servers declare more than one protocol: mqtt, amqp.")]
        public void RefuseServersWithoutOneProtocol(string servers, string message)
        {
            // Arrange
            var operations = Deriver.Derive(Fixture("synthetic.json"), "{}");

            // Act / Assert
            var exception = Assert.ThrowsExactly<DeriveException>(() => Deriver.DeriveChannels(Events(Thing, servers), operations));
            Assert.AreEqual(message, exception.Message);
        }

        /// <summary>
        ///     A synthetic event document: the servers given, one MQTT server unless given, the channels given, and for each
        ///     channel the operation that sends on it.
        /// </summary>
        private static string Events(string channels, string servers = "\"test\": { \"protocol\": \"mqtt\" }")
        {
            var ids = JsonNode.Parse("{" + channels + "}")!.AsObject().Select(channel => channel.Key);
            var sending = string.Join(", ", ids.Select(id => $"\"{id}\": {{ \"action\": \"send\", \"channel\": {{ \"$ref\": \"#/channels/{id}\" }} }}"));
            return "{ \"asyncapi\": \"3.0.0\", \"servers\": {" + servers + "}, \"channels\": {" + channels + "}, \"operations\": {" + sending + "} }";
        }

        private static string Fixture(string name)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        }
    }
}