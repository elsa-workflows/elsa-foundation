using Kernel.Foundation.Contracts;

namespace Kernel.WorkflowActor;

public sealed record MigrationPlanOutcomeRow(ActorRef Actor, MigrationPlanOutcome? Outcome);

/// <summary>An empty outcome means Pending while open, and No Answer after close.</summary>
public sealed record MigrationPlanResultPage(bool Closed, Page<MigrationPlanOutcomeRow> Results);

/// <summary>Reads a plan's projected outcomes by exact reference in the trusted host's tenant scope.</summary>
public interface IMigrationPlanReads
{
    ValueTask<MigrationPlanResultPage> ListAsync(BoundScope scope, ActorRef plan, int pageSize,
        PageToken? from = null, CancellationToken cancellationToken = default);
}
