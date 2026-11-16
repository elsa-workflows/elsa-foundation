using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Explicit ordinary access for native history participant/owner fixtures; it grants no inspection authority.</summary>
internal sealed class RuntimeHistoryTestAccess(string scope = PersistenceScope.DefaultValue) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new(scope));
}
