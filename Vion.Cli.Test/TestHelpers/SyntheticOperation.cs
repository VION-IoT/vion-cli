using Vion.Cli.Derived;

namespace Vion.Cli.Test.TestHelpers
{
    /// <summary>
    ///     An operation the committed table does not hold, for a rule the real document does not exercise today: a
    ///     deprecated operation, or a stable one beside a deprecated one. Shaped as `tenant.edgeGateway.list` is: a
    ///     tenant-rooted read with its tenant from the context.
    /// </summary>
    internal static class SyntheticOperation
    {
        public static Operation Create(string id, bool deprecated, Stability stability = Stability.Beta, string? sunset = null)
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
                                 stability,
                                 sunset,
                                 Completion.Response,
                                 false,
                                 new Authorization("Root", ["X"], null, null),
                                 false,
                                 null);
        }
    }
}
