using Vion.Cli.Commands;

namespace Vion.Cli.Test.Commands
{
    /// <summary>
    ///     Which table row a raw `api` path addresses. A premise test, cited by no criterion: the row chosen decides which
    ///     rules — dry run, confirmation, the 404 wording — the raw call gets, and the host routes a literal segment before
    ///     a parameter, so the tool must too.
    /// </summary>
    [TestClass]
    public class OperationMatcherShould
    {
        [TestMethod]
        [DataRow("/tenant/11111111-1111-1111-1111-111111111111/arrangements/projects", "tenant.projectArrangement.list")]
        [DataRow("/tenant/11111111-1111-1111-1111-111111111111/arrangements/overview", "tenant.overviewSectionArrangement.get")]
        public void PreferLiteralSegmentOverParameter(string path, string operationId)
        {
            // Arrange

            // Act
            var operation = OperationMatcher.Find("GET", path);

            // Assert
            Assert.AreEqual(operationId, operation?.Id);
        }

        [TestMethod]
        public void MatchLiteralSegmentsCaseInsensitivelyAndIgnoreQuery()
        {
            // Arrange

            // Act
            var operation = OperationMatcher.Find("get", "/Tenant/11111111-1111-1111-1111-111111111111/EdgeGateways?projectId=x");

            // Assert
            Assert.AreEqual("tenant.edgeGateway.list", operation?.Id);
        }

        [TestMethod]
        public void FindNothingForUnknownPath()
        {
            // Arrange

            // Act
            var operation = OperationMatcher.Find("GET", "/somewhere/the/table/does/not/know");

            // Assert
            Assert.IsNull(operation);
        }
    }
}