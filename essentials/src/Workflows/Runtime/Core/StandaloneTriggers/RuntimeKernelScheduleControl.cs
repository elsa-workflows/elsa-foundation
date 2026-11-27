using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>The exact published source currently claiming one logical schedule.</summary>
public sealed record RuntimeKernelScheduleControlOwner(string SlotId, string SlotName, string ActivationId,
    long PublicationRevision, string SourceReferenceId, WorkflowExecutableIdentity Executable);

/// <summary>A schedule's desired control at one monotone revision, including a retained tombstone after release.</summary>
public sealed record RuntimeKernelScheduleControl(TriggerScope Scope, string ScheduleKey, string DefinitionId, string TriggerNode,
    long Revision, RuntimeKernelScheduleControlOwner? Owner)
{
    /// <summary>Binding metadata entry for the logical key behind a schedule reservation hash.</summary>
    public const string ScheduleKeyMetadata = "schedule-key";
}

public sealed record RuntimeKernelScheduleControlPage(IReadOnlyList<RuntimeKernelScheduleControl> Items, string? Next);

/// <summary>Reads desired schedule controls in the selected Runtime persistence scope and tenant.</summary>
public interface IRuntimeKernelScheduleControlReader
{
    ValueTask<RuntimeKernelScheduleControl?> FindAsync(TriggerScope scope, string scheduleKey, CancellationToken cancellationToken = default);

    /// <summary>Reads a bounded, forward-only live page of claims and released controls. Treat the cursor as opaque.</summary>
    ValueTask<RuntimeKernelScheduleControlPage> ListAsync(TriggerScope scope, string? after, int limit,
        CancellationToken cancellationToken = default);
}
