using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Stages the one root-state projection change after every migration proof has succeeded.</summary>
public sealed class WorkflowMigrationPlanner
{
    public void Stage(
        WorkflowExecutionState source,
        WorkflowExecutableIdentity targetIdentity,
        WorkflowExecutableSourceProvenance targetSource,
        IWorkflowAlterationStagingWorkspace staging)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetIdentity);
        ArgumentNullException.ThrowIfNull(targetSource);
        ArgumentNullException.ThrowIfNull(staging);

        var migrated = source with
        {
            PinnedExecutable = targetIdentity,
            PinnedSource = targetSource
        };
        staging.Stage(new MigrateWorkflowStagedChange(migrated));
    }

    private sealed record MigrateWorkflowStagedChange(WorkflowExecutionState Workflow) : IWorkflowAlterationRuntimeCheckpointStagedChange
    {
        public string ChangeKey => "runtime.alteration.migrate-workflow";

        public void Apply(WorkflowAlterationRuntimeCheckpointStateBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.SetWorkflowExecution(Workflow);
        }
    }
}
