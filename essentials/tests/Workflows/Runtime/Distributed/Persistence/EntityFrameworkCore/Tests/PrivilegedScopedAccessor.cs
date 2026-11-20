using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Tests;

internal sealed class PrivilegedScopedAccessor(string scope) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; } =
        PersistenceAccessContext.PrivilegedScoped(new PersistenceScope(scope), new PersistenceAccessPurpose("maintenance"));
}
