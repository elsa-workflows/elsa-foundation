using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork.V2.Testing;

/// <summary>
/// Fixed <see cref="IPersistenceAccessContextAccessor"/> for store tests that pin one access context.
/// </summary>
public sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
{
    public PersistenceAccessContext Current { get; } = current;
}
