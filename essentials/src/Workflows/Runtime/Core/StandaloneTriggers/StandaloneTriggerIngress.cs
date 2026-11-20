using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>Detached module-submitted signal. It contains no tenant, invocation grant, target capture or executable authority.</summary>
public sealed class StandaloneTriggerSignal
{
    public StandaloneTriggerSignal(string triggerId, string sourceKey, ValueTypeDescriptor payloadSchema, JsonElement payload, JsonElement? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerId); ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentNullException.ThrowIfNull(payloadSchema);
        if (payload.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("A signal payload must be defined.", nameof(payload));
        if (metadata is { ValueKind: not JsonValueKind.Object }) throw new ArgumentException("Source metadata must be an object.", nameof(metadata));
        TriggerId = triggerId; SourceKey = sourceKey; PayloadSchema = payloadSchema; Payload = payload.Clone();
        Metadata = metadata?.Clone() ?? JsonSerializer.SerializeToElement(new { });
    }
    public string TriggerId { get; }
    public string SourceKey { get; }
    public ValueTypeDescriptor PayloadSchema { get; }
    public JsonElement Payload { get; }
    public JsonElement Metadata { get; }
}

/// <summary>Producer-bound public module entry point. Runtime alone selects and accepts workflow recipients.</summary>
public interface IStandaloneTriggerIngress<TSource> where TSource : class
{
    ValueTask<StandaloneTriggerIngressResult> AdmitAsync(StandaloneTriggerSignal signal, CancellationToken cancellationToken = default);
}

/// <summary>Scoped runtime factory behind Core-only producer registration. It never accepts externally submitted authority.</summary>
public interface IStandaloneTriggerIngressFactory
{
    IStandaloneTriggerIngress<TSource> Create<TSource>() where TSource : class;
}

/// <summary>Installed source identity, owned by the composing module rather than a submitted signal.</summary>
public sealed record StandaloneTriggerSourceDescriptor(string TypeKey, int SchemaVersion, string SourceFamily, ValueTypeDescriptor PayloadSchema);

/// <summary>Installed source-policy support, resolved without invoking a request or occurrence callback.</summary>
public sealed record StandaloneTriggerSourceCapability(StandaloneTriggerSourceDescriptor Descriptor, bool SupportsResourceAuthorization)
{
    public string? WorkflowIntentKind { get; init; }
}

public enum StandaloneTriggerIngressStatus { Accepted, PartiallyAccepted }
public sealed record StandaloneTriggerIngressRefusal(string SubscriptionId, string Code);

/// <summary>Sanitized producer acknowledgement. Physical captures, source fingerprints and dispatch authority stay in Runtime.</summary>
public sealed class StandaloneTriggerIngressResult
{
    public StandaloneTriggerIngressResult(string admissionId, bool replayed, IReadOnlyList<string> acceptedSubscriptionIds,
        IReadOnlyList<StandaloneTriggerIngressRefusal> refusals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(admissionId); ArgumentNullException.ThrowIfNull(acceptedSubscriptionIds); ArgumentNullException.ThrowIfNull(refusals);
        if (acceptedSubscriptionIds.Count == 0 || acceptedSubscriptionIds.Any(string.IsNullOrWhiteSpace) ||
            acceptedSubscriptionIds.Distinct(StringComparer.Ordinal).Count() != acceptedSubscriptionIds.Count || refusals.Any(item => item is null))
            throw new ArgumentException("A successful source acknowledgement requires distinct accepted association identities.");
        AdmissionId = admissionId; Replayed = replayed;
        AcceptedSubscriptionIds = Array.AsReadOnly(acceptedSubscriptionIds.ToArray()); Refusals = Array.AsReadOnly(refusals.ToArray());
    }
    public string AdmissionId { get; }
    public bool Replayed { get; }
    public IReadOnlyList<string> AcceptedSubscriptionIds { get; }
    public IReadOnlyList<StandaloneTriggerIngressRefusal> Refusals { get; }
    public int AcceptedCount => AcceptedSubscriptionIds.Count;
    public int RefusedCount => Refusals.Count;
    public StandaloneTriggerIngressStatus Status => Refusals.Count == 0 ? StandaloneTriggerIngressStatus.Accepted : StandaloneTriggerIngressStatus.PartiallyAccepted;
}

/// <summary>Trusted server-side source context, resolved by the module's normal authenticated or background source boundary.</summary>
public sealed record StandaloneTriggerInvocationContext(TriggerScope Scope, WorkflowExecutionPartition Partition,
    WorkflowExecutionAuthoritySnapshot Actor)
{
    public RuntimeIngressReservation? Reservation { get; init; }
    public RecurringTriggerOccurrenceClaim? ScheduleClaim { get; init; }
    /// <summary>Trusted raw source intent, excluding route resolution, configuration and recipient selection.</summary>
    public JsonElement? SubmittedIntent { get; init; }
    /// <summary>Detached ordinary wait input; the physical source participant alone selects actual recipients.</summary>
    public RuntimeIngressWaitCandidate? WaitCandidate { get; init; }
}

/// <summary>Scoped replacement supplied by one source module. Implementations must never bind context from the signal body.</summary>
public interface IStandaloneTriggerSourceContext<TSource> where TSource : class
{
    /// <summary>Default unsupported. A source module must implement normal resource/signal authorization explicitly.</summary>
    bool SupportsResourceAuthorization => false;
    ValueTask<StandaloneTriggerInvocationContext> GetAsync(CancellationToken cancellationToken = default);
    /// <summary>Runs against the actual scoped resource before replay or capture. Paused/deleted state alone need not revoke access to an accepted receipt.</summary>
    ValueTask<bool> AuthorizeAsync(StandaloneTrigger trigger, StandaloneTriggerSignal signal, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}

/// <summary>A complete known pre-admission refusal. It carries safe findings and creates no accepted source.</summary>
public sealed class StandaloneTriggerAdmissionRefusedException(string code, IReadOnlyList<RuntimeSubscriptionRefusal> refusals,
    IReadOnlyList<string>? notAcceptedSubscriptionIds = null)
    : Exception("The trigger could not accept this signal.")
{
    public string Code { get; } = code;
    public IReadOnlyList<RuntimeSubscriptionRefusal> Refusals { get; } = Array.AsReadOnly(refusals.ToArray());
    public IReadOnlyList<string> NotAcceptedSubscriptionIds { get; } = Array.AsReadOnly((notAcceptedSubscriptionIds ?? []).ToArray());
}
