using Elsa.Persistence.Core;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests;

internal static class GroundworkDistributedTestAccess
{
    public static IPersistenceAccessContextAccessor Scoped(string scope = PersistenceScope.DefaultValue) =>
        new FixedAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

    private sealed class FixedAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
