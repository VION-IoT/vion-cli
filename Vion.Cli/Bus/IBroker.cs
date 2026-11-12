using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Vion.Cli.Bus
{
    /// <summary>
    ///     How a subscriber connects, as <c>me.subscriber.create</c> answers it: nothing here is the tool's own choice.
    /// </summary>
    internal sealed record BrokerSession(
        Uri BrokerUrl,
        string SubscriberId,
        int ProtocolVersion,
        int SubscribeQos,
        string WillTopic,
        int WillQos);

    /// <summary>
    ///     A message as the broker delivered it; <see cref="Schema" /> is its <c>schema</c> user property, null without
    ///     one.
    /// </summary>
    internal sealed record BusMessage(string Topic, string? Schema, ReadOnlyMemory<byte> Payload);

    /// <summary>The broker refused the connection or a subscription; the message carries its reason.</summary>
    internal sealed class BrokerRefusedException : Exception
    {
        public BrokerRefusedException(string message) : base(message)
        {
        }

        public BrokerRefusedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>The connection ended without the tool ending it, or the broker could not be reached at all.</summary>
    internal sealed class BrokerLostException : Exception
    {
        public BrokerLostException(string message) : base(message)
        {
        }
    }

    /// <summary>The broker, as the tool reaches it: the seam a test replaces with a recording one.</summary>
    internal interface IBroker
    {
        /// <summary>
        ///     Connects as the session's subscriber with its will registered, an empty username and the token as the password.
        /// </summary>
        /// <exception cref="BrokerRefusedException">The broker refused the connection.</exception>
        /// <exception cref="BrokerLostException">The broker could not be reached.</exception>
        Task<IBrokerConnection> ConnectAsync(BrokerSession session, string token, CancellationToken cancellationToken);
    }

    internal interface IBrokerConnection : IAsyncDisposable
    {
        /// <summary>
        ///     Every message delivered on the connection, in order. The reader completes with a
        ///     <see cref="BrokerLostException" /> when the connection is lost and without one after <see cref="EndAsync" />.
        /// </summary>
        ChannelReader<BusMessage> Messages { get; }

        /// <summary>Subscribes at the session's quality of service and returns once the broker has acknowledged it.</summary>
        /// <exception cref="BrokerRefusedException">
        ///     The broker refused the subscription, or closed the connection instead of
        ///     answering.
        /// </exception>
        Task SubscribeAsync(string topic, CancellationToken cancellationToken);

        /// <summary>Publishes the will itself, empty, and then disconnects cleanly.</summary>
        Task EndAsync(CancellationToken cancellationToken);
    }
}