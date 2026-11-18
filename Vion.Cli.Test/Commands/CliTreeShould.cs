using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vion.Cli.Derived;
using Vion.Cli.Test.TestHelpers;

namespace Vion.Cli.Test.Commands
{
    /// <summary>The tree as a user meets it: which commands exist, which are hidden, what help says.</summary>
    [TestClass]
    public class CliTreeShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.1")]
        public async Task OfferCommandPerTreeOperationAndNoneForSystem()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(200);

            // Act
            var run = await Tool.RunAsync(handler, ["ops", "--json"]);

            // Assert
            var items = System.Text.Json.Nodes.JsonNode.Parse(run.Out)!["items"]!.AsArray();
            var commands = items.Where(i => i!["protocol"]!.GetValue<string>() == "http").Select(i => i!["command"]!.GetValue<string>()).ToList();
            Assert.HasCount(Snapshot.TreeOperationCount, commands);
            CollectionAssert.AreEquivalent(Operations.All.Where(o => o.InTree).Select(o => "vion " + o.CommandPath).ToList(), commands);
            Assert.IsFalse(commands.Any(c => c.StartsWith("vion system", System.StringComparison.Ordinal)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.1")]
        public async Task ParseEveryTreeOperationToItsCommand()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(200);
            var missing = new List<string>();

            // Act
            foreach (var operation in Operations.All.Where(o => o.InTree))
            {
                var run = await Tool.RunAsync(handler, [operation.CommandDomain, operation.CommandAggregate, operation.CommandVerb, "--help"]);
                if (run.ExitCode != 0 || !run.Out.Contains($"vion {operation.CommandPath}", System.StringComparison.Ordinal))
                {
                    missing.Add(operation.Id);
                }
            }

            // Assert
            Assert.IsEmpty(missing);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.2")]
        public async Task HideDeprecatedCommandFromHelpAndKeepItInvocable()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(200);
            var operations = new[] { SyntheticOperation.Create("tenant.thing.list", false), SyntheticOperation.Create("tenant.thing.get", true) };

            // Act
            var help = await Tool.RunAsync(handler, ["tenant", "thing", "--help"], operations: operations);
            var invoked = await Tool.RunAsync(handler, Tool.OnTest("tenant", "thing", "get", "--tenant", Tool.Tenant), Tool.Token, operations: operations);

            // Assert
            StringAssert.Contains(help.Out, "  list  ");
            Assert.IsFalse(help.Out.Contains("  get  ", System.StringComparison.Ordinal), help.Out);
            Assert.AreEqual(0, invoked.ExitCode, invoked.Error);
            Assert.HasCount(1, handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.2")]
        [DataRow(false, "2027-02-01", "Deprecated: may be gone from 2027-02-01.")]
        [DataRow(true, "2031-07-15", "Stability: stable — changes only additively or by a deprecation.\n  Deprecated: may be gone from 2031-07-15.")]
        [DataRow(false, null, "Deprecated.")]
        public async Task StateDeprecationOnDeprecatedCommandsOwnHelpOnly(bool stable, string? sunset, string line)
        {
            // Arrange
            using var handler = RecordingHandler.Empty(200);
            var operations = new[]
                             {
                                 SyntheticOperation.Create("tenant.thing.list", false),
                                 SyntheticOperation.Create("tenant.thing.get", true, stable ? Stability.Stable : Stability.Beta, sunset),
                             };

            // Act
            var own = await Tool.RunAsync(handler, ["tenant", "thing", "get", "--help"], operations: operations);
            var parent = await Tool.RunAsync(handler, ["tenant", "thing", "--help"], operations: operations);

            // Assert
            Assert.AreEqual(0, own.ExitCode, own.Error);
            StringAssert.Contains(own.Out.ReplaceLineEndings("\n"), $"  Does get.\n  {line}\n");
            Assert.IsFalse(parent.Out.Contains("Deprecated", System.StringComparison.Ordinal), parent.Out);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.7")]
        [DataRow(false, true)]
        [DataRow(true, false)]
        public async Task PrintHiddenCommandsOwnHelp(bool deprecated, bool hidden)
        {
            // Arrange
            using var handler = RecordingHandler.Empty(200);
            var operations = new[]
                             {
                                 SyntheticOperation.Create("tenant.thing.list", false),
                                 SyntheticOperation.Create("tenant.thing.get", deprecated, hidden: hidden),
                             };

            // Act
            var own = await Tool.RunAsync(handler, ["tenant", "thing", "get", "--help"], operations: operations);
            var parent = await Tool.RunAsync(handler, ["tenant", "thing", "--help"], operations: operations);

            // Assert
            Assert.AreEqual(0, own.ExitCode, own.Error);
            StringAssert.Contains(own.Out.ReplaceLineEndings("\n"), "Description:\n  Does get.");
            StringAssert.Contains(own.Out, "vion tenant thing get [options]");
            Assert.IsFalse(parent.Out.Contains("  get  ", System.StringComparison.Ordinal), parent.Out);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.6")]
        public async Task StateStabilityOnStableCommandsOwnHelpOnly()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(200);
            var operations = new[] { SyntheticOperation.Create("tenant.thing.list", false), SyntheticOperation.Create("tenant.thing.get", false, Stability.Stable) };

            // Act
            var stable = await Tool.RunAsync(handler, ["tenant", "thing", "get", "--help"], operations: operations);
            var beta = await Tool.RunAsync(handler, ["tenant", "thing", "list", "--help"], operations: operations);
            var parent = await Tool.RunAsync(handler, ["tenant", "thing", "--help"], operations: operations);

            // Assert
            StringAssert.Contains(stable.Out.ReplaceLineEndings("\n"), "  Does get.\n  Stability: stable — changes only additively or by a deprecation.\n");
            StringAssert.Contains(beta.Out.ReplaceLineEndings("\n"), "  Does list.\n\nUsage:");
            Assert.IsFalse(parent.Out.Contains("Stability", System.StringComparison.Ordinal), parent.Out);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-001.6")]
        public async Task DescribeCommandAndOptionsFromDocument()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(200);

            // Act
            var run = await Tool.RunAsync(handler, ["tenant", "edge-gateway", "list", "--help"]);

            // Assert
            StringAssert.Contains(run.Out, "Returns all edge gateways for a tenant");
            StringAssert.Contains(run.Out, "--edge-gateway-id <uuid>  Lists only the rows of this edge gateway of the tenant.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-001.1")]
        public async Task ParseEveryChannelToItsWatchCommand()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(200);
            var missing = new List<string>();

            // Act
            foreach (var channel in Channels.All)
            {
                var run = await Tool.RunAsync(handler, [channel.CommandDomain, channel.CommandAggregate, "watch", "--help"]);
                if (run.ExitCode != 0 || !run.Out.Contains($"vion {channel.CommandDomain} {channel.CommandAggregate} watch [options]", System.StringComparison.Ordinal))
                {
                    missing.Add(channel.Id);
                }
            }

            // Assert
            Assert.IsNotEmpty(Channels.All);
            Assert.IsEmpty(missing);
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-001.4")]
        public async Task DescribeWatchCommandAndOptionsFromEventDocument()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(200);

            // Act
            var run = await Tool.RunAsync(handler, ["tenant", "sync-status", "watch", "--help"]);

            // Assert
            StringAssert.Contains(run.Out, "How far the gateway has applied what was sent to it, published each time a sync status changes.");
            StringAssert.Contains(run.Out, "--edge-gateway-id <value> (REQUIRED)  The edge gateway's id.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-WATCH-001.5")]
        public async Task DescribeTimeoutOnWatchAsBoundingTheWatch()
        {
            // Arrange
            using var handler = RecordingHandler.Empty(200);

            // Act
            var run = await Tool.RunAsync(handler, ["tenant", "sync-status", "watch", "--help"]);

            // Assert
            var help = System.Text.RegularExpressions.Regex.Replace(run.Out, @"\s+", " ");
            StringAssert.Contains(help,
                                  "--timeout <duration> How long to watch once subscribed: 90s, 15m, 1h or seconds; until interrupted when absent. Exits 0 then, 1 if never subscribed.");
        }
    }
}