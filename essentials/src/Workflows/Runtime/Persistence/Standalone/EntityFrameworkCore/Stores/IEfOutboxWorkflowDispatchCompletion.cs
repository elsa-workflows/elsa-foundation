using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>The optional classic dispatch row projection staged inside the physical outbox completion transaction.</summary>
internal interface IEfOutboxWorkflowDispatchCompletion
{
    ValueTask<EfOutboxWorkflowDispatchFacts> ReadAsync(string scope, WorkflowDispatchRecord projectedDispatch, CancellationToken token);
    void Stage(string scope, EfOutboxWorkflowDispatchFacts existing, WorkflowDispatchRecord projected);
    ValueTask<bool> MatchesAsync(string scope, WorkflowDispatchRecord dispatch, CancellationToken token);
}

internal sealed record EfOutboxWorkflowDispatchFacts(WorkflowDispatchEntity Row, WorkflowDispatchRecord Existing, WorkflowExecutionState? Child);
