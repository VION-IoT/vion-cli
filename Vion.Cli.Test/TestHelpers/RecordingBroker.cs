using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Vion.Cli.Bus;

namespace Vion.Cli.Test.TestHelpers
{
    /// <summary>One connection as the tool opened it: who connected with what, and what it did on it, in order.</summary>
    internal sealed class RecordedConnection : IBrokerConnection
    {
        private readonly RecordingBroker _broker;
        private readonly System.Threading.Channels.Channel<BusMessage> _messages = Channel.CreateUnbounded<BusMessage>();

        public BrokerSession Session { get; }

        public string Token { get; }

        public List<string> Subscriptions { get; } = [];

        public bool Ended { get; private set; }

        public ChannelReader<BusMessage> Messages
        {
            get => _messages.Reader;
        }

        public RecordedConnection(RecordingBroker broker, BrokerSession session, string token)
        {
            _broker = broker;
            Session = session;
            Token = token;
        }

        public Task SubscribeAsync(string topic, CancellationToken cancellationToken)
        {
            if (_broker.RefuseSubscription != null)
            {
                throw new BrokerRefusedException(_broker.RefuseSubscription);
            }

            Subscriptions.Add(topic);
            _broker.Log.Add($"SUBSCRIBE {topic}");
            return Task.CompletedTask;
        }

        public Task EndAsync(CancellationToken cancellationToken)
        {
            Ended = true;
            _broker.Log.Add($"PUBLISH {Session.WillTopic} empty");
            _broker.Log.Add("DISCONNECT");
            _messages.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _messages.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        /// <summary>Delivers one message as the broker would: on a topic, with its schema user property, a JSON payload.</summary>
        public void Deliver(string schema, string json, string topic = "topic/delivered")
        {
            _messages.Writer.TryWrite(new BusMessage(topic, schema, Encoding.UTF8.GetBytes(json)));
        }

        /// <summary>Ends the stream without a loss, which ends the watch.</summary>
        public void EndStream()
        {
            _messages.Writer.TryComplete();
        }

        /// <summary>Ends the connection as a network loss does: the reader fails, nothing was ended.</summary>
        public void Lose()
        {
            _messages.Writer.TryComplete(new BrokerLostException("The connection to the broker was lost: test."));
        }
    }

    /// <summary>
    ///     Stands in for the broker: records every connection and what was done on it, and lets a test act as the broker
    ///     once a subscription stands — deliver, lose the connection, or end the run. Never touches the network.
    /// </summary>
    internal sealed class RecordingBroker : IBroker
    {
        public List<RecordedConnection> Connections { get; } = [];

        /// <summary>Every step across the API and the broker, in the order it happened, as the test's handler adds its own.</summary>
        public List<string> Log { get; } = [];

        /// <summary>The reason to refuse every connection with, or null to accept.</summary>
        public string? RefuseConnection { get; set; }

        /// <summary>The reason to refuse every subscription with, or null to acknowledge.</summary>
        public string? RefuseSubscription { get; set; }

        /// <summary>What the broker does each time a connection is accepted; the index is the connection's, from zero.</summary>
        public Action<RecordedConnection, int>? OnConnected { get; set; }

        /// <summary>
        ///     Ends the stream of a connection as soon as it is accepted. A test of a refusal that comes before any
        ///     connection sets it, so that a tool which wrongly connects ends its run and fails the assertions, where it
        ///     would otherwise watch for ever.
        /// </summary>
        public bool EndsOnConnect { get; set; }

        public Task<IBrokerConnection> ConnectAsync(BrokerSession session, string token, CancellationToken cancellationToken)
        {
            if (RefuseConnection != null)
            {
                throw new BrokerRefusedException(RefuseConnection);
            }

            var connection = new RecordedConnection(this, session, token);
            Connections.Add(connection);
            Log.Add($"CONNECT {session.SubscriberId}");
            OnConnected?.Invoke(connection, Connections.Count - 1);
            if (EndsOnConnect)
            {
                connection.EndStream();
            }

            return Task.FromResult<IBrokerConnection>(connection);
        }
    }
}
