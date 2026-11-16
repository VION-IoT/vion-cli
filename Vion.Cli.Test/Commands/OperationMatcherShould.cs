using Vion.Cli.Commands;

namespace Vion.Cli.Test.Commands
{
    /// <summary>
    ///     Which table row a raw `api` path addresses. A premise test, cited by no criterion: the row chosen decides which
    ///     rules — dry run, confirmation, the 404 wording — the raw call gets, and the host routes a literal segment before
    ///     a parameter, so the tool must too. The values a path carries are how a versioned write reaches its aggregate's
    ///     `get` for `If-Match`, so they must come back by name and unescaped, as `RequestBuilder.Expand` escapes them again.
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
        public void ReadPathValuesByParameterNameUnescaped()
        {
            // Arrange

            // Act
            var values = OperationMatcher.Values("/tenant/{tenantId}/logicConfigurations/{id}/apply", "/Tenant/t%20one/LogicConfigurations/c-1/apply");

            // Assert
            Assert.IsNotNull(values);
            Assert.HasCount(2, values);
            Assert.AreEqual("t one", values["tenantId"]);
            Assert.AreEqual("c-1", values["id"]);
        }

        [TestMethod]
        public void ReadNoPathValuesForPathOffTemplate()
        {
            // Arrange

            // Act
            var values = OperationMatcher.Values("/tenant/{tenantId}/logicConfigurations/{id}/apply", "/tenant/t/logicConfigurations/c-1");

            // Assert
            Assert.IsNull(values);
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