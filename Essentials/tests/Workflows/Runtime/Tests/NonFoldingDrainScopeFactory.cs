using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// A drain scope whose session folds nothing. Every drain runs in a session, but this one is never made ambient, so no
/// store sees it and every checkpoint the code under test makes commits as it is made. For drain orchestrator tests
/// that drive the drain loop with test doubles and have no stores to fold.
/// </summary>
internal sealed class NonFoldingDrainScopeFactory : IRuntimeCoalescingDrainScopeFactory
{
    public static NonFoldingDrainScopeFactory Instance { get; } = new();

    public IRuntimeCoalescingDrainScope Begin(string workflowExecutionId, ResolvedPersistenceMode mode) =>
        new Scope(new RuntimeCoalescingSession(workflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions(), mode: mode));

    private sealed class Scope(RuntimeCoalescingSession session) : IRuntimeCoalescingDrainScope
    {
        public RuntimeCoalescingSession Session => session;

        public ValueTask FlushAtQuiescenceAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
