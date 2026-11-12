using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Bus;

namespace Vion.Cli.Test.Bus
{
    /// <summary>
    ///     The broker client's own refusals, which happen before it opens a connection and so need no broker. What it
    ///     sends once connected is behind the network and is proven live.
    /// </summary>
    [TestClass]
    public class MqttBrokerShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-WATCH-002.9")]
        [DataRow("ws://broker.example.test/ws", "the scheme ws")]
        [DataRow("https://broker.example.test/ws", "the scheme https")]
        [DataRow("wss://user:secret@broker.example.test/ws", "the scheme wss")]
        public async Task RefuseAddressThatIsNotWssOrCarriesCredentials(string address, string named)
        {
            // Arrange
            var broker = new MqttBroker();
            var session = new BrokerSession(new Uri(address),
                                            "sub_abc123_",
                                            5,
                                            0,
                                            "will/topic",
                                            1);

            // Act / Assert
            var exception = await Assert.ThrowsExactlyAsync<BrokerRefusedException>(() => broker.ConnectAsync(session, "test-token", CancellationToken.None));
            StringAssert.Contains(exception.Message, named);
            Assert.DoesNotContain("secret", exception.Message);
        }
    }
}
