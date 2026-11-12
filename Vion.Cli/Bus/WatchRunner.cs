using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Cli.Derived;
using Vion.Cli.Infrastructure;

namespace Vion.Cli.Bus
{
    /// <summary>
    ///     What one watch needs that is not the broker: the channel and its topic, how long to watch, and the three
    ///     calls the procedure makes — a token for each connect, the subscriber to connect as, and the registration where
    ///     the channel asks for one.
    /// </summary>
    internal sealed record WatchPlan(
        Channel Channel,
        string Topic,
        TimeSpan? Timeout,
        Func<CancellationToken, Task<string>> Token,
        Func<string, CancellationToken, Task<BrokerSession>> Subscriber,
        Func<BrokerSession, string, CancellationToken, Task>? Register);

    /// <summary>
    ///     The watch itself, behind one entry point: it hands each message of the channel to the sink it is given and
    ///     writes nothing to standard output, so that a command and any later caller share one procedure.
    /// </summary>
    internal static class WatchRunner
    {
        private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);

        private static readonly TimeSpan LongestBackoff = TimeSpan.FromSeconds(30);

        /// <summary>How long the way out may take before the process leaves the will to the broker.</summary>
        private static readonly TimeSpan EndBudget = TimeSpan.FromSeconds(2);

        /// <summary>
        ///     Watches until the plan's timeout or <paramref name="interrupt" />, and returns then. A refusal by the broker,
        ///     or a broker that cannot be reached before the watch stood once, is a <see cref="CliFailure" />.
        /// </summary>
        public static async Task RunAsync(WatchPlan plan, Action<BusMessage> sink, CliServices services, CancellationToken interrupt)
        {
            var token = await plan.Token(interrupt);
            var session = await plan.Subscriber(token, interrupt);

            using var timeout = plan.Timeout is { } bound ? new CancellationTokenSource(bound, services.Time) : new CancellationTokenSource();
            using var ending = CancellationTokenSource.CreateLinkedTokenSource(interrupt, timeout.Token);
            var stoodOnce = false;
            var backoff = FirstBackoff;
            while (!ending.IsCancellationRequested)
            {
                IBrokerConnection? connection = null;
                try
                {
                    if (stoodOnce)
                    {
                        token = await plan.Token(ending.Token);
                    }

                    connection = await services.Broker.ConnectAsync(session, token, ending.Token);
                    await connection.SubscribeAsync(plan.Topic, ending.Token);
                    if (plan.Register != null)
                    {
                        // After the acknowledgement and never before: the gateway answers a registration with the
                        // current values at once, and a subscription not yet standing would miss them.
                        await plan.Register(session, token, ending.Token);
                    }

                    services.Error.WriteLine(stoodOnce ? $"reconnected to {plan.Channel.Id}: messages may have been missed." : $"watching {plan.Channel.Id}");
                    stoodOnce = true;
                    backoff = FirstBackoff;

                    await foreach (var message in connection.Messages.ReadAllAsync(ending.Token))
                    {
                        if (message.Schema == plan.Channel.MessageName)
                        {
                            sink(message);
                        }
                    }

                    return;
                }
                catch (OperationCanceledException) when (ending.IsCancellationRequested)
                {
                    return;
                }
                catch (BrokerRefusedException refused)
                {
                    throw new CliFailure(refused.Message);
                }
                catch (BrokerLostException lost) when (!stoodOnce)
                {
                    throw new CliFailure(lost.Message);
                }
                catch (BrokerLostException)
                {
                    try
                    {
                        await Task.Delay(backoff, services.Time, ending.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    backoff = backoff + backoff < LongestBackoff ? backoff + backoff : LongestBackoff;
                }
                finally
                {
                    if (connection != null)
                    {
                        await EndAsync(connection);
                    }
                }
            }
        }

        /// <summary>
        ///     The one way out. It runs on its own short budget, since the token that ended the watch is already cancelled,
        ///     and it swallows its failures: a connection that is gone has nothing left to end, and the broker publishes
        ///     the will of one that ends without a clean disconnect.
        /// </summary>
        private static async Task EndAsync(IBrokerConnection connection)
        {
            try
            {
                using var budget = new CancellationTokenSource(EndBudget);
                await connection.EndAsync(budget.Token);
            }
            catch (Exception)
            {
                // Nothing is owed to a connection that could not be ended.
            }

            await connection.DisposeAsync();
        }
    }
}