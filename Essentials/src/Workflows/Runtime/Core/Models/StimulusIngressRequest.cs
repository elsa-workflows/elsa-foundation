using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Versioned routing data retained by a canonical ingress source.</summary>
/// <remarks>
/// A proposed provider contract, with no enabled producer yet. Live dispatch options and a caller's
/// cached match collection are deliberately absent. Owner selection is a trusted router fact, never
/// inferred from business metadata. It does not replace source or recipient authorization.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class StimulusIngressRequest
{
    public const int CurrentVersion = 1;

    public StimulusIngressRequest(int version, string stimulusType, string stimulusHash,
        JsonElement? input, string? correlationId, StimulusRoutingMode mode, string requestedBy,
        IReadOnlyDictionary<string, string> metadata, ValueTypeDescriptor? payloadType,
        string? providerId, string? targetWorkflowExecutionId, StimulusStartKeyScope startKeyScope,
        bool resumeAllMatchingBookmarks, string? selectionOwnerTriggerBindingId)
        : this(version, stimulusType, stimulusHash, input, correlationId, mode, requestedBy, metadata,
            payloadType, providerId, targetWorkflowExecutionId, startKeyScope, resumeAllMatchingBookmarks,
            selectionOwnerTriggerBindingId, input.HasValue) { }

    /// <summary>Explicit input presence survives nullable JSON deserialization; null is ordinary data.</summary>
    [JsonConstructor]
    public StimulusIngressRequest(int version, string stimulusType, string stimulusHash,
        JsonElement? input, string? correlationId, StimulusRoutingMode mode, string requestedBy,
        IReadOnlyDictionary<string, string> metadata, ValueTypeDescriptor? payloadType,
        string? providerId, string? targetWorkflowExecutionId, StimulusStartKeyScope startKeyScope,
        bool resumeAllMatchingBookmarks, string? selectionOwnerTriggerBindingId, bool inputPresent)
    {
        if (version != CurrentVersion) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(startKeyScope)) throw new ArgumentOutOfRangeException(nameof(startKeyScope));
        foreach (var optional in new[] { correlationId, providerId, targetWorkflowExecutionId, selectionOwnerTriggerBindingId })
            if (optional is not null) ArgumentException.ThrowIfNullOrWhiteSpace(optional);
        if ((payloadType is null) != (providerId is null))
            throw new ArgumentException("Typed stimulus data requires both payload type and provider.");
        if (metadata.Any(pair => pair.Value is null))
            throw new ArgumentException("Stimulus metadata cannot contain null values.", nameof(metadata));
        if (targetWorkflowExecutionId is not null && mode != StimulusRoutingMode.ResumeOnly)
            throw new ArgumentException("A targeted stimulus must use resume-only routing.");
        if (selectionOwnerTriggerBindingId is not null && mode == StimulusRoutingMode.ResumeOnly)
            throw new ArgumentException("Resume-only routing cannot select an owning start binding.");
        if (startKeyScope == StimulusStartKeyScope.Occurrence &&
            (mode != StimulusRoutingMode.StartOnly || selectionOwnerTriggerBindingId is null))
            throw new ArgumentException("An occurrence source requires start-only routing through its owning binding.");
        Version = version;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        if (!inputPresent && input is not null)
            throw new ArgumentException("An absent ingress input cannot carry a value.", nameof(input));
        Input = inputPresent ? input?.Clone() ?? JsonSerializer.SerializeToElement<object?>(null) : null;
        CorrelationId = correlationId;
        Mode = mode;
        RequestedBy = requestedBy;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        PayloadType = payloadType;
        ProviderId = providerId;
        TargetWorkflowExecutionId = targetWorkflowExecutionId;
        StartKeyScope = startKeyScope;
        ResumeAllMatchingBookmarks = resumeAllMatchingBookmarks;
        SelectionOwnerTriggerBindingId = selectionOwnerTriggerBindingId;
    }

    public int Version { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public JsonElement? Input { get; }
    public bool InputPresent => Input.HasValue;
    public string? CorrelationId { get; }
    public StimulusRoutingMode Mode { get; }
    public string RequestedBy { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public ValueTypeDescriptor? PayloadType { get; }
    public string? ProviderId { get; }
    public string? TargetWorkflowExecutionId { get; }
    public StimulusStartKeyScope StartKeyScope { get; }
    public bool ResumeAllMatchingBookmarks { get; }
    /// <summary>Null for global selection; a non-null trusted owner narrows selection before effects.</summary>
    public string? SelectionOwnerTriggerBindingId { get; }
}
