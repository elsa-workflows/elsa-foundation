using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The rule-violation faulter a drain orchestrator requires by construction, over empty in-memory stores, for tests whose
/// drains never have a commit refused.
/// </summary>
internal static class TestCheckpointRuleViolationFaulter
{
    public static CheckpointRuleViolationWorkflowFaulter Create(TimeProvider? timeProvider = null) =>
        new(
            new InMemoryWorkflowExecutionStateStore(),
            new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                new InMemoryRuntimeCheckpointCommitStore(),
                new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
                [],
                []),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            timeProvider ?? TimeProvider.System);
}
