using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>The exact published source currently claiming one logical schedule.</summary>
public sealed record RuntimeKernelScheduleControlOwner(string SlotId, string SlotName, string ActivationId,
    long PublicationRevision, string SourceReferenceId, WorkflowExecutableIdentity Executable)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeKernelScheduleContinuity? Continuity { get; init; }
}

/// <summary>A trigger's unchanged publication span and the activation time of its current owner.</summary>
public sealed record RuntimeKernelScheduleContinuity(string Stamp, string Stimulus, DateTimeOffset FoundationActivatedAt);

/// <summary>A schedule's desired control at one monotone revision, including a retained tombstone after release.</summary>
public sealed record RuntimeKernelScheduleControl(TriggerScope Scope, string ScheduleKey, string DefinitionId, string TriggerNode,
    long Revision, RuntimeKernelScheduleControlOwner? Owner)
{
    /// <summary>Binding metadata entry for the logical key behind a schedule reservation hash.</summary>
    public const string ScheduleKeyMetadata = "schedule-key";

    /// <summary>Binding metadata for equality of the authored Timer interval or Cron expression.</summary>
    public const string ScheduleStimulusMetadata = "schedule-stimulus";

    /// <summary>
    /// The scope of the actor's control revision. The two default publication aliases share one actor in the default
    /// partition. A returned control still carries the exact publication scope and tenant in <see cref="Scope"/>.
    /// </summary>
    public static TriggerScope CanonicalScope(TriggerScope scope) =>
        scope.Persistence.Value == PersistenceScope.DefaultValue && scope.TenantId == PersistenceScope.DefaultValue
            ? scope with { TenantId = null }
            : scope;
}

public sealed record RuntimeKernelScheduleControlPage(IReadOnlyList<RuntimeKernelScheduleControl> Items, string? Next);

/// <summary>Reads desired schedule controls in the selected Runtime persistence scope and tenant.</summary>
public interface IRuntimeKernelScheduleControlReader
{
    ValueTask<RuntimeKernelScheduleControl?> FindAsync(TriggerScope scope, string scheduleKey, CancellationToken cancellationToken = default);

    /// <summary>Reads a bounded, forward-only live page of claims and released controls. Treat the cursor as opaque.</summary>
    /// <remarks>Use the cursor only to continue the current scan. Begin every reconciliation pass with a null cursor
    /// because rows and revisions can change behind a page already read.</remarks>
    ValueTask<RuntimeKernelScheduleControlPage> ListAsync(TriggerScope scope, string? after, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the inclusive end of a new pass, or null when this scope has no controls.</summary>
    ValueTask<string?> ReadTailAsync(TriggerScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>Reads a bounded page through the end captured at the start of this pass.</summary>
    ValueTask<RuntimeKernelScheduleControlPage> ListAsync(TriggerScope scope, string? after, string through, int limit,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
