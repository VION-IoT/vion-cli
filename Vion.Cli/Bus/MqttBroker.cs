using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Adapter;
using MQTTnet.Exceptions;
using MQTTnet.Formatter;
using MQTTnet.Packets;
using MQTTnet.Protocol;

namespace Vion.Cli.Bus
{
    /// <summary>The broker over MQTT on WebSockets.</summary>
    internal sealed class MqttBroker : IBroker
    {
        public async Task<IBrokerConnection> ConnectAsync(BrokerSession session, string token, CancellationToken cancellationToken)
        {
            // The token is the connection's password, so it goes out only under TLS: an address of any other
            // scheme is refused here, before a client exists to send it. An address carrying credentials of its
            // own is refused too, and is then not echoed in the refusal.
            if (session.BrokerUrl.Scheme != "wss" || !string.IsNullOrEmpty(session.BrokerUrl.UserInfo))
            {
                throw new
                    BrokerRefusedException($"The broker's address must be a wss:// address without credentials; the one answered names the scheme {session.BrokerUrl.Scheme} and the host {session.BrokerUrl.Host}.");
            }

            var client = new MqttClientFactory().CreateMqttClient();
            var connection = new Connection(client, session);
            try
            {
                var options = new MqttClientOptionsBuilder().WithWebSocketServer(webSocket => webSocket.WithUri(session.BrokerUrl.ToString()))
                                                            .WithTlsOptions(tls => tls.UseTls())
                                                            .WithProtocolVersion(ProtocolVersionOf(session.ProtocolVersion))
                                                            .WithClientId(session.SubscriberId)
                                                            .WithCredentials(string.Empty, token)
                                                            .WithWillTopic(session.WillTopic)
                                                            .WithWillQualityOfServiceLevel((MqttQualityOfServiceLevel)session.WillQos)
                                                            .Build();
                await client.ConnectAsync(options, cancellationToken);
                return connection;
            }
            catch (MqttConnectingFailedException refused)
            {
                await connection.DisposeAsync();
                throw new BrokerRefusedException($"The broker refused the connection: {refused.Message}", refused);
            }
            catch (MqttCommunicationException failed)
            {
                await connection.DisposeAsync();
                throw new BrokerLostException($"The broker at {session.BrokerUrl.Host} could not be reached: {failed.Message}");
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }

        private static MqttProtocolVersion ProtocolVersionOf(int version)
        {
            return version switch
            {
                5 => MqttProtocolVersion.V500,
                4 => MqttProtocolVersion.V311,
                _ => throw new BrokerRefusedException($"The broker asks for MQTT protocol version {version}, which this tool does not speak."),
            };
        }

        private sealed class Connection : IBrokerConnection
        {
            private readonly IMqttClient _client;

            private readonly Channel<BusMessage> _messages = Channel.CreateUnbounded<BusMessage>(new UnboundedChannelOptions { SingleReader = true });

            private readonly BrokerSession _session;

            private int _ended;

            public Connection(IMqttClient client, BrokerSession session)
            {
                _client = client;
                _session = session;
                _client.ApplicationMessageReceivedAsync += OnMessage;
                _client.DisconnectedAsync += OnDisconnected;
            }

            public ChannelReader<BusMessage> Messages
            {
                get => _messages.Reader;
            }

            public async Task SubscribeAsync(string topic, CancellationToken cancellationToken)
            {
                var options = new MqttClientSubscribeOptionsBuilder()
                              .WithTopicFilter(filter => filter.WithTopic(topic).WithQualityOfServiceLevel((MqttQualityOfServiceLevel)_session.SubscribeQos))
                              .Build();
                MqttClientSubscribeResult result;
                try
                {
                    result = await _client.SubscribeAsync(options, cancellationToken);
                }
                catch (MqttCommunicationException closed)
                {
                    // The broker answers a subscription it does not grant by closing the connection, so a close
                    // while the acknowledgement is awaited is the refusal itself and not a lost connection.
                    throw new BrokerRefusedException($"The broker closed the connection instead of acknowledging the subscription to {topic}.", closed);
                }

                var refused = result.Items.FirstOrDefault(item => item.ResultCode > MqttClientSubscribeResultCode.GrantedQoS2);
                if (refused != null)
                {
                    throw new BrokerRefusedException($"The broker refused the subscription to {topic}: {refused.ResultCode}.");
                }
            }

            public async Task EndAsync(CancellationToken cancellationToken)
            {
                if (Interlocked.Exchange(ref _ended, 1) == 1 || !_client.IsConnected)
                {
                    _messages.Writer.TryComplete();
                    return;
                }

                // A clean disconnect makes the broker discard the will, so the registrations the will would have
                // ended are ended by publishing it here first.
                var will = new MqttApplicationMessageBuilder().WithTopic(_session.WillTopic).WithQualityOfServiceLevel((MqttQualityOfServiceLevel)_session.WillQos).Build();
                await _client.PublishAsync(will, cancellationToken);
                await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), cancellationToken);
                _messages.Writer.TryComplete();
            }

            public ValueTask DisposeAsync()
            {
                _client.ApplicationMessageReceivedAsync -= OnMessage;
                _client.DisconnectedAsync -= OnDisconnected;
                _messages.Writer.TryComplete();
                _client.Dispose();
                return ValueTask.CompletedTask;
            }

            private Task OnMessage(MqttApplicationMessageReceivedEventArgs received)
            {
                var message = received.ApplicationMessage;
                var schema = message.UserProperties?.FirstOrDefault(property => property.Name == "schema")?.ReadValueAsString();
                _messages.Writer.TryWrite(new BusMessage(message.Topic, schema, message.Payload.ToArray()));
                return Task.CompletedTask;
            }

            private Task OnDisconnected(MqttClientDisconnectedEventArgs disconnected)
            {
                if (Volatile.Read(ref _ended) == 0)
                {
                    _messages.Writer.TryComplete(new BrokerLostException($"The connection to the broker was lost: {disconnected.Reason}."));
                }

                return Task.CompletedTask;
            }
        }
    }
}