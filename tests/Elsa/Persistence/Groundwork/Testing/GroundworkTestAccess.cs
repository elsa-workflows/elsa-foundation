using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>Canonical persistence-scope bindings for the Groundwork-backed test suites.</summary>
public static class GroundworkTestAccess
{
    public const string DefaultScopeValue = "default";

    public static IPersistenceAccessContextAccessor DefaultAccessContextAccessor { get; } =
        AccessContext(DefaultScopeValue);

    public static IPersistenceAccessContextAccessor AccessContext(string scope) =>
        new FixedPersistenceAccessContextAccessor(
            PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

    private sealed class FixedPersistenceAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
