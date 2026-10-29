using System.Linq;
using Vion.Cli.Commands;
using Vion.Cli.Derive;
using Vion.Cli.Derived;

namespace Vion.Cli.Test.Derive
{
    /// <summary>
    ///     Premise tests, citing no id by design: the emitted global-option list is the tool's own, and the committed table
    ///     is the current snapshot's. Neither is a behaviour a user observes; both are what the derived criteria rest on.
    /// </summary>
    [TestClass]
    public class EmitterShould
    {
        [TestMethod]
        public void EmitGlobalOptionNamesToolDefines()
        {
            // Arrange
            var globals = new GlobalOptions();
            var body = new BodyOptions();
            var defined = globals.All
                                 .Select(o => o.Name.TrimStart('-'))
                                 .Concat(body.ForJson.Concat(body.ForMultipart).Select(o => o.Name.TrimStart('-')))
                                 .Concat(["help", "version"])
                                 .Distinct()
                                 .OrderBy(n => n, System.StringComparer.Ordinal)
                                 .ToArray();

            // Act
            var emitted = Operations.GlobalOptionNames.OrderBy(n => n, System.StringComparer.Ordinal).ToArray();

            // Assert
            CollectionAssert.AreEqual(defined, emitted);
        }

        [TestMethod]
        public void CompileTableOfCurrentSnapshot()
        {
            // Arrange
            var snapshot = System.IO.File.ReadAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "v1.json"));

            // Act
            var document = Deriver.Derive(snapshot, "{}");

            // Assert
            Assert.AreEqual(Operations.SnapshotSha256, document.SnapshotSha256);
            Assert.HasCount(Operations.All.Count, document.Operations);
        }
    }
}