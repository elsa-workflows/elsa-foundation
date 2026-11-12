using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfExecutionCommandTransportTestBoundary(ExecutionCommandTransportDbContext context,
    IPersistenceAccessContextAccessor access, IExecutionCommandTransport actualTransport) : IExecutionCommandTransportTestBoundary
{
    public bool CanTransportTestEntry(WorkflowTestEntry entry, IExecutionCommandTransport physicalTransport)
    {
        try
        {
            var scope = access.Current.RequireScope();
            access.Current.EnsureTenantScope(entry.Scope.TenantId);
            if (scope.Value != entry.Scope.Partition.Value || !ReferenceEquals(actualTransport, physicalTransport) ||
                physicalTransport is not EfExecutionCommandTransport actual || !actual.Uses(context, scope, entry.Scope.TenantId)) return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { return false; }
        var gate = ExecutionCommandTransportWriteVersion.Gate(context);
        var state = gate?.StateOf(ExecutionCommandTransportEfModule.SchemaFamily);
        return gate?.DatabaseIdentity is not null && state is { WritesRefused: false } &&
            Version.Parse(state.WriteVersion) >= Version.Parse(ExecutionCommandTransportEfModule.TestEntrySchemaVersion);
    }
}
