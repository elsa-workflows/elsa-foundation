using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>The authored start node and its literal Event name.</summary>
public sealed record RuntimeKernelEventEntry(string Node, string Name);

/// <summary>The immutable source and material of an Event registration.</summary>
public sealed record RuntimeKernelEventSource(string ActivationId, string ReferenceId, string Kind, string SourceId,
    string? SourceVersion, WorkflowExecutableIdentity Executable)
{
    /// <summary>The resolved durable persistence mode. Absent values use Balanced.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PersistenceMode? Mode { get; init; }
}

/// <summary>One scoped slot revision awaiting or retaining its provider registration.</summary>
public sealed record RuntimeKernelEventPublication(TriggerScope Scope, string SlotId, string SlotName, string DefinitionId,
    long Revision, RuntimeKernelEventSource? Source, IReadOnlyList<RuntimeKernelEventEntry> Entries)
{
    public const string ReservationType = "kernel-event-publication-v1";
    public const string EventNameMetadata = "event-name";
    public const string ModeMetadata = "event-mode";

    /// <summary>Gives each slot and node its own claim; a stimulus may fan out across these claims.</summary>
    public static string ReservationKey(string slotId, string node) => WorkflowTriggerBinding.BuildId(ReservationType, slotId, node);
}

/// <summary>A publication generation and its immutable position in the scoped Event scan.</summary>
public sealed record RuntimeKernelEventPublicationScanItem(long Ordinal, RuntimeKernelEventPublication Publication);

/// <summary>Reads Event registrations staged with publication authority and records exact provider acknowledgments.</summary>
public interface IRuntimeKernelEventPublicationStore
{
    ValueTask<RuntimeKernelEventPublication?> FindAsync(TriggerScope scope, string slotId, long revision,
        CancellationToken cancellationToken = default);

    /// <summary>Read a bounded page of current unapplied registrations or retained superseded generations.</summary>
    /// <remarks>Restart a sweep with a null cursor so changes behind an earlier cursor are visited.</remarks>
    ValueTask<IReadOnlyList<RuntimeKernelEventPublication>> ListAsync(TriggerScope scope, bool superseded, string? afterSlot,
        long afterRevision, int limit, CancellationToken cancellationToken = default);

    /// <summary>Reads the committed positive upper ordinal shared by both scan lanes, or zero when none has been assigned.</summary>
    ValueTask<long> ReadScanTailAsync(TriggerScope scope, CancellationToken cancellationToken = default);

    /// <summary>Reads ordered generations after <paramref name="after"/> through the fixed pass bound.</summary>
    /// <remarks>Start a new pass at zero to revisit failures and older generations that have since become superseded.</remarks>
    ValueTask<IReadOnlyList<RuntimeKernelEventPublicationScanItem>> ListScanAsync(TriggerScope scope, bool superseded,
        long after, long through, int limit, CancellationToken cancellationToken = default);

    /// <summary>Reads immutable predecessor membership in slot-hash and revision order, excluding all new generations.</summary>
    /// <remarks>The private predecessor writer must stop before upgrade. A new pass begins at a null slot.</remarks>
    ValueTask<IReadOnlyList<RuntimeKernelEventPublication>> ListLegacyScanAsync(TriggerScope scope, bool superseded,
        string? afterSlot, long afterRevision, int limit, CancellationToken cancellationToken = default);

    ValueTask<bool> AcknowledgeAsync(RuntimeKernelEventPublication publication, CancellationToken cancellationToken = default);

    /// <summary>Remove a superseded material root only after its exact provider revision is proven drained.</summary>
    ValueTask<bool> ReleaseDrainedAsync(RuntimeKernelEventPublication publication, CancellationToken cancellationToken = default);
}
