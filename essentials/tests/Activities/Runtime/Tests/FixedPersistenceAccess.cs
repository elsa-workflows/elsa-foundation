using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Tests;

internal sealed class FixedPersistenceAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
{
    /// <summary>
    /// The access of an activator built outside any partition: it names no persistence scope, so the activity scope the
    /// activator creates is left unbound and needs no persistence services in the test's container.
    /// </summary>
    public static FixedPersistenceAccess Unbound { get; } = new(PersistenceAccessContext.Global);

    public PersistenceAccessContext Current => current;
}
