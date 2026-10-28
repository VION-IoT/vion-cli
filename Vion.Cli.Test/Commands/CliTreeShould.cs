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
            var commands = items.Select(i => i!["command"]!.GetValue<string>()).ToList();
            Assert.HasCount(264, commands);
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
            var operations = new[] { Synthetic("tenant.thing.list", false), Synthetic("tenant.thing.get", true) };

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

        private static Operation Synthetic(string id, bool deprecated)
        {
            var (domain, aggregate, verb) = (id.Split('.')[0], id.Split('.')[1], id.Split('.')[2]);
            return new Operation(id,
                                 domain,
                                 aggregate,
                                 verb,
                                 domain,
                                 aggregate,
                                 verb,
                                 "GET",
                                 $"/{domain}/{{tenantId}}/{aggregate}",
                                 $"Does {verb}.",
                                 [
                                     new Parameter("tenantId",
                                                   null,
                                                   "tenant",
                                                   ParameterLocation.Path,
                                                   true,
                                                   ParameterKind.Uuid,
                                                   "The tenant's id."),
                                 ],
                                 null,
                                 Effect.Read,
                                 false,
                                 false,
                                 false,
                                 deprecated,
                                 new Authorization("Root", ["X"], null, null),
                                 false,
                                 null);
        }
    }
}