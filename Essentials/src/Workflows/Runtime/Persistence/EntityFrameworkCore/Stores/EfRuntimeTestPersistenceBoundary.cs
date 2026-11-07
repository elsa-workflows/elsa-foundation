using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfRuntimeTestPersistenceBoundary(RuntimeDbContext context,
    IPersistenceAccessContextAccessor access) : IRuntimeTestPersistenceBoundary
{
    public bool CanPersistTestEntry(RuntimeTestPersistenceBoundaryContext selected)
    {
        try
        {
            var scope = access.Current.RequireScope();
            access.Current.EnsureTenantScope(selected.Entry.Scope.TenantId);
            if (scope.Value != selected.Entry.Scope.Partition.Value ||
                selected.Executions is not EfWorkflowExecutionStateStore executions || !ReferenceEquals(executions.Context, context) || executions.AccessContext.RequireScope() != scope ||
                selected.Scheduler is not EfSchedulerWorkQueueStore scheduler || !scheduler.Uses(context, scope) ||
                selected.Checkpoints is not EfRuntimeCheckpointCommitStore checkpoints || !checkpoints.Uses(context, scope) ||
                selected.Outbox is not EfRuntimePostCommitOutboxStore outbox || !outbox.Uses(context, scope) ||
                selected.Background is not EfBackgroundActivityWorkStore background || !background.Uses(context, scope) ||
                selected.Timers is not EfDurableTimerStore timers || !timers.Uses(context, scope)) return false;
            var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
                .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
            if (gate is null) return false;
            return Ready(RuntimeWorkflowExecutionEfModule.SchemaFamily, RuntimeWorkflowExecutionEfModule.TestEntrySchemaVersion) &&
                Ready(RuntimeSchedulerWorkEfModule.SchemaFamily, RuntimeSchedulerWorkEfModule.TestEntrySchemaVersion) &&
                Ready(RuntimeOperationalStateEfModule.SchemaFamily, RuntimeOperationalStateEfModule.TestEntrySchemaVersion) &&
                Ready(RuntimePostCommitOutboxEfModule.SchemaFamily, RuntimePostCommitOutboxEfModule.TestEntrySchemaVersion) &&
                Ready(RuntimeBackgroundActivityWorkEfModule.SchemaFamily, RuntimeBackgroundActivityWorkEfModule.TestEntrySchemaVersion);

            bool Ready(string family, string minimum) => gate.StateOf(family) is { WritesRefused: false } state &&
                Version.Parse(state.WriteVersion) >= Version.Parse(minimum);
        }
        catch (InvalidOperationException) { return false; }
    }
}
