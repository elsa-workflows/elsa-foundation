namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Identifies one of the Runtime EF outbox adapters and its shared physical storage.</summary>
internal interface IEfRuntimePostCommitOutboxOwner
{
    EfRuntimePostCommitOutboxStorage Storage { get; }
}
