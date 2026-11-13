using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Commands
{
    /// <summary>
    ///     A watch command from its options to its exit: which row is read and which topic taken, how the tool connects,
    ///     subscribes and registers, what it prints, how it reconnects and how it ends. The API is a recording handler and
    ///     the broker a recording one, over the real tables; the broker's side of the way out — the will published and the
    ///     disconnect — is recorded at that seam, and what the MQTT client then sends is proven live, not here.
    /// </summary>
    [TestClass]
    public class WatchInvokerShould
    {
        private const string Integrator = "44444444-4444-4444-4444-444444444444";

        private const string OtherId = "33333333-3333-3333-3333-333333333333";

        private const string Subscriber =
            """{"subscriberId":"sub_abc123_","brokerUrl":"wss://ws.test.vion.swiss/ws","protocolVersion":5,"subscribeQos":0,"lastWill":{"topic":"will/topic","qos":1}}""";

        private static readonly string[] SyncStatus = ["tenant", "sync-status", "watch", "--tenant", Tool.Tenant, "--edge-gateway-id", Tool.Id];

        private static readonly string[] ServiceProperty =
        [
            "tenant", "service-property", "watch", "--tenant", Tool.Tenant, "--edge-gateway-id", Tool.Id, "--service-provider-identifier", "provider", "--service-identifier",
            "service",
        ];

        private static string Gateways
        {
            get =>
                J("""{"items":[{"id":"<other>","meta":{"topics":{"tenant.syncStatus.state":"topic/other"}}},{"id":"<id>","meta":{"topics":{"tenant.syncStatus.state":"topic/wanted"}}}]}""");
        }

        private static string Services
        {
            get =>
                J("""{"items":[{"identifier":"service","edgeGatewayId":"<id>","serviceProviderIdentifier":"other-provider","properties":[{"identifier":"x"}],"meta":{"topics":{"tenant.serviceProperty.state":"topic/other"}}},{"identifier":"service","edgeGatewayId":"<id>","serviceProviderIdentifier":"provider","properties":[{"identifier":"a"},{"identifier":"b"}],"meta":{"topics":{"tenant.serviceProperty.state":"topic/service"}}}]}""");
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.1")]
        public async Task SubscribeToTopicOfRowOptionsSelect()
        {
            // Arrange
            var broker = new RecordingBroker();
            using var handler = Api(broker, Gateways);
            var time = new FakeTimeProvider();

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. SyncStatus, "--timeout", "5s"]), Tool.Token, time: time, broker: broker));

            // Assert
            Assert.AreEqual(0, run.ExitCode);
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Tool.Tenant}/edgeGateways", handler.Requests[0].Uri.ToString());
            CollectionAssert.AreEqual(new[] { "topic/wanted" }, broker.Connections.Single().Subscriptions);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.1")]
        public async Task RefuseBeforeConnectingWhenNoRowMatches()
        {
            // Arrange
            var broker = new RecordingBroker { EndsOnConnect = true };
            using var handler = Api(broker, J("""{"items":[{"id":"<other>","meta":{"topics":{"tenant.syncStatus.state":"topic/other"}}}]}"""));

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest(SyncStatus), Tool.Token, broker: broker);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.AreEqual($"error: tenant.syncStatus.state: tenant.edgeGateway.list answered no row with id {Tool.Id}.{Environment.NewLine}", run.Error);
            Assert.IsEmpty(broker.Connections);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.1")]
        public async Task RefuseBeforeConnectingWhenRowHandsOutNoTopic()
        {
            // Arrange
            var broker = new RecordingBroker { EndsOnConnect = true };
            using var handler = Api(broker, J("""{"items":[{"id":"<id>","meta":{"topics":{"tenant.systemHealth.state":"topic/health"}}}]}"""));

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest(SyncStatus), Tool.Token, broker: broker);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            StringAssert.StartsWith(run.Error, "error: tenant.syncStatus.state: the row of tenant.edgeGateway.list hands out no topic for it.");
            Assert.IsEmpty(broker.Connections);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.1")]
        public async Task ReadGatewayRowWhenGatewayOptionGiven()
        {
            // Arrange
            var broker = new RecordingBroker();
            using var handler = Api(broker, J("""{"items":[{"id":"<id>","meta":{"topics":{"integrator.systemHealth.state":"topic/one-gateway"}}}]}"""));
            var time = new FakeTimeProvider();
            string[] args = ["integrator", "system-health", "watch", "--integrator", Integrator, "--edge-gateway-id", Tool.Id, "--timeout", "5s"];

            // Act
            await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest(args), Tool.Token, time: time, broker: broker));

            // Assert
            Assert.AreEqual($"https://api.test.vion.swiss/integrator/{Integrator}/edgeGateways", handler.Requests[0].Uri.ToString());
            CollectionAssert.AreEqual(new[] { "topic/one-gateway" }, broker.Connections.Single().Subscriptions);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.1")]
        public async Task ReadMembershipRowWhenGatewayOptionAbsent()
        {
            // Arrange
            var broker = new RecordingBroker();
            using var handler = Api(broker,
                                    J("""{"integratorMemberships":[{"integratorId":"<other>","meta":{"topics":{"integrator.systemHealth.state":"topic/other"}}},{"integratorId":"<integrator>","meta":{"topics":{"integrator.systemHealth.state":"topic/every-gateway"}}}]}"""));
            var time = new FakeTimeProvider();
            string[] args = ["integrator", "system-health", "watch", "--integrator", Integrator, "--timeout", "5s"];

            // Act
            await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest(args), Tool.Token, time: time, broker: broker));

            // Assert
            Assert.AreEqual("https://api.test.vion.swiss/me", handler.Requests[0].Uri.ToString());
            CollectionAssert.AreEqual(new[] { "topic/every-gateway" }, broker.Connections.Single().Subscriptions);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.2")]
        public async Task ConnectAsSubscriberAnsweredWithTokenAsPassword()
        {
            // Arrange
            var broker = new RecordingBroker();
            using var handler = Api(broker, Gateways);
            var time = new FakeTimeProvider();

            // Act
            await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. SyncStatus, "--timeout", "5s"]), Tool.Token, time: time, broker: broker));

            // Assert
            var connection = broker.Connections.Single();
            Assert.AreEqual(new Vion.Cli.Bus.BrokerSession(new Uri("wss://ws.test.vion.swiss/ws"),
                                                           "sub_abc123_",
                                                           5,
                                                           0,
                                                           "will/topic",
                                                           1),
                            connection.Session);
            Assert.AreEqual(Tool.Token, connection.Token);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.3")]
        public async Task RegisterEveryValueOfRowAfterAcknowledgement()
        {
            // Arrange
            var broker = new RecordingBroker();
            using var handler = Api(broker, Services);
            var time = new FakeTimeProvider();

            // Act
            await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. ServiceProperty, "--timeout", "5s"]), Tool.Token, time: time, broker: broker));

            // Assert
            var registration = handler.To("subscribeProperties").Single();
            Assert.AreEqual($"https://api.test.vion.swiss/tenant/{Tool.Tenant}/services/subscribeProperties", registration.Uri.ToString());
            Assert.AreEqual(J("""{"subscriberId":"sub_abc123_","properties":[{"edgeGatewayId":"<id>","serviceProviderIdentifier":"provider","serviceIdentifier":"service","propertyIdentifier":"a"},{"edgeGatewayId":"<id>","serviceProviderIdentifier":"provider","serviceIdentifier":"service","propertyIdentifier":"b"}]}"""),
                            registration.Body);
            string[] ordered = ["SUBSCRIBE topic/service", $"POST /tenant/{Tool.Tenant}/services/subscribeProperties"];
            CollectionAssert.AreEqual(ordered, broker.Log.Where(step => ordered.Contains(step)).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.3")]
        public async Task RegisterOnlyValuesGiven()
        {
            // Arrange
            var broker = new RecordingBroker();
            using var handler = Api(broker, Services);
            var time = new FakeTimeProvider();

            // Act
            await Tool.RunAdvancingAsync(time,
                                         () => Tool.RunAsync(handler,
                                                             Tool.OnTest([.. ServiceProperty, "--property-identifier", "b", "--timeout", "5s"]),
                                                             Tool.Token,
                                                             time: time,
                                                             broker: broker));

            // Assert
            var properties = JsonNode.Parse(handler.To("subscribeProperties").Single().Body!)!["properties"]!.AsArray();
            CollectionAssert.AreEqual(new[] { "b" }, properties.Select(property => property!["propertyIdentifier"]!.GetValue<string>()).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.4")]
        public async Task RefuseBeforeConnectingWhenRegistrationValueNotOnRow()
        {
            // Arrange
            var broker = new RecordingBroker { EndsOnConnect = true };
            using var handler = Api(broker, Services);

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest([.. ServiceProperty, "--property-identifier", "c"]), Tool.Token, broker: broker);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            Assert.AreEqual($"error: --property-identifier: the row carries no 'c'. It carries: a, b.{Environment.NewLine}", run.Error);
            Assert.IsEmpty(broker.Connections);
            Assert.IsEmpty(handler.To("subscribeProperties"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.5")]
        public async Task WriteWatchingNoticeOnceSubscriptionStands()
        {
            // Arrange
            var broker = new RecordingBroker();
            using var handler = Api(broker, Gateways);
            var time = new FakeTimeProvider();

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. SyncStatus, "--timeout", "5s"]), Tool.Token, time: time, broker: broker));

            // Assert
            Assert.AreEqual($"watching tenant.syncStatus.state{Environment.NewLine}", run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.5")]
        public async Task WriteNoWatchingNoticeWhenRegistrationRefused()
        {
            // Arrange
            var broker = new RecordingBroker { EndsOnConnect = true };
            using var handler = Api(broker, Services, 400);

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest(ServiceProperty), Tool.Token, broker: broker);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.DoesNotContain("watching", run.Error);
            StringAssert.Contains(run.Error, "not registered");
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.6")]
        public async Task ReconnectSubscribeAndRegisterInOrderWithFreshToken()
        {
            // Arrange
            var broker = new RecordingBroker
                         {
                             OnConnected = (connection, index) =>
                                           {
                                               if (index == 0)
                                               {
                                                   connection.Lose();
                                               }
                                           },
                         };
            using var handler = Api(broker, Services);
            var time = new FakeTimeProvider();
            var environment = new Dictionary<string, string?> { ["VION_CLIENT_ID"] = "client", ["VION_CLIENT_SECRET"] = "secret" };

            // Act
            var run = await Tool.RunAdvancingAsync(time,
                                                   () => Tool.RunAsync(handler,
                                                                       [.. ServiceProperty, "--env", "test", "--timeout", "30s"],
                                                                       environment: environment,
                                                                       time: time,
                                                                       broker: broker));

            // Assert
            Assert.AreEqual(0, run.ExitCode);
            string[] standing = ["CONNECT sub_abc123_", "SUBSCRIBE topic/service", $"POST /tenant/{Tool.Tenant}/services/subscribeProperties"];
            CollectionAssert.AreEqual(standing.Concat(standing).ToList(), broker.Log.Where(step => standing.Contains(step)).ToList());
            Assert.AreEqual($"watching tenant.serviceProperty.state{Environment.NewLine}reconnected to tenant.serviceProperty.state: messages may have been missed.{Environment.NewLine}",
                            run.Error);
            Assert.AreNotEqual(broker.Connections[0].Token, broker.Connections[1].Token);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.6")]
        public async Task ConnectAgainAfterTokenEndpointGaveNoAnswer()
        {
            // Arrange
            var broker = new RecordingBroker
                         {
                             OnConnected = (connection, index) =>
                                           {
                                               if (index == 0)
                                               {
                                                   connection.Lose();
                                               }
                                           },
                         };
            using var handler = Api(broker, Gateways, unreachableTokenRequest: 3);
            var time = new FakeTimeProvider();
            var environment = new Dictionary<string, string?> { ["VION_CLIENT_ID"] = "client", ["VION_CLIENT_SECRET"] = "secret" };

            // Act
            var run = await Tool.RunAdvancingAsync(time,
                                                   () => Tool.RunAsync(handler,
                                                                       [.. SyncStatus, "--env", "test", "--timeout", "30s"],
                                                                       environment: environment,
                                                                       time: time,
                                                                       broker: broker));

            // Assert
            Assert.AreEqual(0, run.ExitCode);
            Assert.HasCount(2, broker.Connections);
            Assert.AreEqual("minted-4", broker.Connections[1].Token);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.7")]
        public async Task ExitOneWhenBrokerRefusesConnection()
        {
            // Arrange
            var broker = new RecordingBroker { RefuseConnection = "The broker refused the connection: NotAuthorized" };
            using var handler = Api(broker, Gateways);

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest(SyncStatus), Tool.Token, broker: broker);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.AreEqual($"error: The broker refused the connection: NotAuthorized{Environment.NewLine}", run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.7")]
        public async Task ExitOneWhenBrokerRefusesSubscription()
        {
            // Arrange
            var broker = new RecordingBroker { RefuseSubscription = "The broker refused the subscription to topic/wanted: NotAuthorized." };
            using var handler = Api(broker, Gateways);

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest(SyncStatus), Tool.Token, broker: broker);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.AreEqual($"error: The broker refused the subscription to topic/wanted: NotAuthorized.{Environment.NewLine}", run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.8")]
        public async Task PublishWillAndDisconnectWhenTimeoutEndsWatch()
        {
            // Arrange
            var broker = new RecordingBroker();
            using var handler = Api(broker, Gateways);
            var time = new FakeTimeProvider();

            // Act
            await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. SyncStatus, "--timeout", "5s"]), Tool.Token, time: time, broker: broker));

            // Assert
            CollectionAssert.AreEqual(new[] { "PUBLISH will/topic empty", "DISCONNECT" }, broker.Log.TakeLast(2).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.8")]
        public async Task PublishWillAndDisconnectWhenInterruptEndsWatch()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var interrupt = new CancellationTokenSource(TimeSpan.FromSeconds(3), time);
            var broker = new RecordingBroker();
            using var handler = Api(broker, Gateways);

            // Act
            await Tool.RunAdvancingAsync(time,
                                         () => Tool.RunAsync(handler,
                                                             Tool.OnTest(SyncStatus),
                                                             Tool.Token,
                                                             time: time,
                                                             broker: broker,
                                                             cancellationToken: interrupt.Token));

            // Assert
            CollectionAssert.AreEqual(new[] { "PUBLISH will/topic empty", "DISCONNECT" }, broker.Log.TakeLast(2).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-003.1")]
        [TestProperty("spec", "AC-CLI-003.1")]
        public async Task WriteEachMessageOfChannelAsOneJsonLine()
        {
            // Arrange
            var broker = new RecordingBroker
                         {
                             OnConnected = (connection, _) =>
                                           {
                                               connection.Deliver("SyncStatusUpdate", """{ "status": "Pending", "objectId": "o1" }""");
                                               connection.Deliver("SomethingElse", """{ "status": "Ignored" }""");
                                               connection.Deliver("SyncStatusUpdate", """{ "status": "Completed", "objectId": "o1" }""");
                                           },
                         };
            using var handler = Api(broker, Gateways);
            var time = new FakeTimeProvider();

            // Act
            var run = await Tool.RunAdvancingAsync(time,
                                                   () => Tool.RunAsync(handler, Tool.OnTest([.. SyncStatus, "--timeout", "5s", "--json"]), Tool.Token, time: time, broker: broker));

            // Assert
            Assert.AreEqual(J("""{"status":"Pending","objectId":"o1"}<nl>{"status":"Completed","objectId":"o1"}<nl>"""), run.Out);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-003.1")]
        public async Task ProjectEachMessageToFieldsGiven()
        {
            // Arrange
            var broker = new RecordingBroker { OnConnected = (connection, _) => connection.Deliver("SyncStatusUpdate", """{"status":"Pending","objectId":"o1"}""") };
            using var handler = Api(broker, Gateways);
            var time = new FakeTimeProvider();

            // Act
            var run = await Tool.RunAdvancingAsync(time,
                                                   () => Tool.RunAsync(handler,
                                                                       Tool.OnTest([.. SyncStatus, "--timeout", "5s", "--json", "--fields", "status"]),
                                                                       Tool.Token,
                                                                       time: time,
                                                                       broker: broker));

            // Assert
            Assert.AreEqual(J("""{"status":"Pending"}<nl>"""), run.Out);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-003.1")]
        public async Task WriteEachMessageAsKeyValueTableWithoutJson()
        {
            // Arrange
            var broker = new RecordingBroker { OnConnected = (connection, _) => connection.Deliver("SyncStatusUpdate", """{"status":"Pending","objectId":"o1"}""") };
            using var handler = Api(broker, Gateways);
            var time = new FakeTimeProvider();

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. SyncStatus, "--timeout", "5s"]), Tool.Token, time: time, broker: broker));

            // Assert
            var rows = run.Out
                          .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .Select(row => string.Join(' ', row.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                          .ToList();
            CollectionAssert.AreEqual(new[] { "status Pending", "objectId o1" }, rows);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-003.2")]
        public async Task ExitZeroAtTimeoutGivenWithoutWait()
        {
            // Arrange
            var broker = new RecordingBroker();
            using var handler = Api(broker, Gateways);
            var time = new FakeTimeProvider();
            var start = time.GetUtcNow();

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. SyncStatus, "--timeout", "7s"]), Tool.Token, time: time, broker: broker));

            // Assert
            Assert.AreEqual(0, run.ExitCode);
            Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(7), time.GetUtcNow() - start);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-003.2")]
        public async Task ExitZeroWhenInterruptedWithoutTimeout()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var interrupt = new CancellationTokenSource(TimeSpan.FromSeconds(3), time);
            var broker = new RecordingBroker();
            using var handler = Api(broker, Gateways);

            // Act
            var run = await Tool.RunAdvancingAsync(time,
                                                   () => Tool.RunAsync(handler,
                                                                       Tool.OnTest(SyncStatus),
                                                                       Tool.Token,
                                                                       time: time,
                                                                       broker: broker,
                                                                       cancellationToken: interrupt.Token));

            // Assert
            Assert.AreEqual(0, run.ExitCode);
            Assert.AreEqual(J("watching tenant.syncStatus.state<nl>"), run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-003.3")]
        public async Task ExitOneWhenInterruptedBeforeWatchStands()
        {
            // Arrange
            var time = new FakeTimeProvider();
            using var interrupt = new CancellationTokenSource(TimeSpan.FromSeconds(3), time);
            var broker = new RecordingBroker { HoldsSubscription = true };
            using var handler = Api(broker, Gateways);

            // Act
            var run = await Tool.RunAdvancingAsync(time,
                                                   () => Tool.RunAsync(handler,
                                                                       Tool.OnTest(SyncStatus),
                                                                       Tool.Token,
                                                                       time: time,
                                                                       broker: broker,
                                                                       cancellationToken: interrupt.Token));

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.AreEqual(J("error: interrupted before the watch of tenant.syncStatus.state stood; nothing was watched.<nl>"), run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-003.3")]
        public async Task ExitOneWhenTimeoutEndsBeforeWatchStands()
        {
            // Arrange
            var time = new FakeTimeProvider();
            var broker = new RecordingBroker { HoldsSubscription = true };
            using var handler = Api(broker, Gateways);

            // Act
            var run = await Tool.RunAdvancingAsync(time, () => Tool.RunAsync(handler, Tool.OnTest([.. SyncStatus, "--timeout", "5s"]), Tool.Token, time: time, broker: broker));

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.AreEqual(J("error: --timeout ended before the watch of tenant.syncStatus.state stood; nothing was watched.<nl>"), run.Error);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.9")]
        public async Task RefuseBrokerAtAnotherHostThanEnvironmentsBeforeConnecting()
        {
            // Arrange
            var broker = new RecordingBroker { EndsOnConnect = true };
            using var handler = Api(broker, Gateways, subscriber: Subscriber.Replace("ws.test.vion.swiss", "elsewhere.example.test", StringComparison.Ordinal));

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest(SyncStatus), Tool.Token, broker: broker);

            // Assert
            Assert.AreEqual(1, run.ExitCode);
            Assert.AreEqual(J("error: me.subscriber.create answered a broker at elsewhere.example.test:443, and the environment test's broker is ws.test.vion.swiss:443; the token is not sent there.<nl>"),
                            run.Error);
            Assert.IsEmpty(broker.Connections);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.10")]
        public async Task RefuseBeforeSendingWhenEnvironmentNamesNoBroker()
        {
            // Arrange
            var broker = new RecordingBroker { EndsOnConnect = true };
            using var handler = Api(broker, Gateways);
            var environment = new Dictionary<string, string?> { ["VION_API_URL"] = "https://api.example.test" };

            // Act
            var run = await Tool.RunAsync(handler, [.. SyncStatus, "--token", "-"], Tool.Token, environment: environment, broker: broker);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            StringAssert.StartsWith(run.Error, "error: the environment custom names no broker");
            StringAssert.Contains(run.Error, "--broker-url");
            StringAssert.Contains(run.Error, "VION_BROKER_URL");
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-005.3")]
        public async Task ConnectToBrokerNamedByVariable()
        {
            // Arrange
            var broker = new RecordingBroker { EndsOnConnect = true };
            using var handler = Api(broker, Gateways, subscriber: Subscriber.Replace("ws.test.vion.swiss", "broker.example.test", StringComparison.Ordinal));
            var environment = new Dictionary<string, string?> { ["VION_API_URL"] = "https://api.example.test", ["VION_BROKER_URL"] = "wss://broker.example.test/ws" };

            // Act
            var run = await Tool.RunAsync(handler, [.. SyncStatus, "--token", "-"], Tool.Token, environment: environment, broker: broker);

            // Assert
            Assert.AreEqual(0, run.ExitCode, run.Error);
            Assert.AreEqual("broker.example.test", broker.Connections.Single().Session.BrokerUrl.Host);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-002.6")]
        public async Task RefuseDryRunBeforeSending()
        {
            // Arrange
            var broker = new RecordingBroker { EndsOnConnect = true };
            using var handler = Api(broker, Gateways);

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest([.. SyncStatus, "--dry-run"]), Tool.Token, broker: broker);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            Assert.AreEqual($"error: --dry-run: a watch offers no dry run.{Environment.NewLine}", run.Error);
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-003.4")]
        public async Task RefuseWaitBeforeSending()
        {
            // Arrange
            var broker = new RecordingBroker { EndsOnConnect = true };
            using var handler = Api(broker, Gateways);

            // Act
            var run = await Tool.RunAsync(handler, Tool.OnTest([.. SyncStatus, "--wait", "--timeout", "3s"]), Tool.Token, broker: broker);

            // Assert
            Assert.AreEqual(2, run.ExitCode);
            Assert.AreEqual($"error: --wait: a watch has no long-running operation to wait for; --timeout alone bounds it.{Environment.NewLine}", run.Error);
            Assert.IsEmpty(handler.Requests);
        }

        /// <summary>
        ///     A JSON literal with the test's ids put in, so that the literal itself needs no interpolation beside its
        ///     braces.
        /// </summary>
        private static string J(string json)
        {
            return json.Replace("<id>", Tool.Id, StringComparison.Ordinal)
                       .Replace("<other>", OtherId, StringComparison.Ordinal)
                       .Replace("<integrator>", Integrator, StringComparison.Ordinal)
                       .Replace("<nl>", Environment.NewLine, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The API a watch calls: the row's read answered with the body given, the subscriber, the registration, and a
        ///     token endpoint that mints a different token each time. Every request joins the broker's log, so one list
        ///     holds the order across both.
        /// </summary>
        private static RecordingHandler Api(RecordingBroker broker, string rows, int registrationStatus = 200, int unreachableTokenRequest = 0, string? subscriber = null)
        {
            var minted = 0;
            return new RecordingHandler(request =>
                                        {
                                            var path = request.RequestUri!.AbsolutePath;
                                            broker.Log.Add($"{request.Method.Method} {path}");
                                            if (path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
                                            {
                                                minted++;
                                                if (minted == unreachableTokenRequest)
                                                {
                                                    throw new HttpRequestException("no route to host");
                                                }

                                                return RecordingHandler.Response(200, $$"""{"access_token":"minted-{{minted}}","expires_in":300}""");
                                            }

                                            if (path == "/me/subscribers")
                                            {
                                                return RecordingHandler.Response(200, subscriber ?? Subscriber);
                                            }

                                            if (path.EndsWith("/subscribeProperties", StringComparison.Ordinal))
                                            {
                                                return registrationStatus == 200 ? RecordingHandler.Response(200, "{}") :
                                                           RecordingHandler.Response(registrationStatus, """{"statusCode":"BadRequest","message":"not registered"}""");
                                            }

                                            return request.Method == HttpMethod.Get ? RecordingHandler.Response(200, rows) : RecordingHandler.Response(404, "{}");
                                        });
        }
    }
}