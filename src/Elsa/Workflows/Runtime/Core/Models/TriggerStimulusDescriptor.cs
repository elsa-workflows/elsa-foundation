namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The stimulus identity a start-trigger activity node reacts to, described at publish time (W7, E3-1).
/// It is the opaque <c>(StimulusType, StimulusHash)</c> routing pair the engine already uses on bookmarks,
/// optionally scoped to a passive correlation value; the trigger extractor turns it into a durable
/// <see cref="WorkflowTriggerBinding"/>.
/// </summary>
public sealed class TriggerStimulusDescriptor
{
    public TriggerStimulusDescriptor(
        string stimulusType,
        string stimulusHash,
        string? correlationScope = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);

        if (correlationScope is not null && string.IsNullOrWhiteSpace(correlationScope))
            throw new ArgumentException("Correlation scope cannot be blank when provided.", nameof(correlationScope));

        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        CorrelationScope = correlationScope;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string StimulusType { get; }
    public string StimulusHash { get; }
    public string? CorrelationScope { get; }

    /// <summary>
    /// Free-form provider metadata carried verbatim onto the resulting <see cref="WorkflowTriggerBinding.Metadata"/>
    /// (ordinal snapshot; empty by default). Providers that emit several descriptors for one node (e.g. one per HTTP
    /// method) use it to record the routing facets a consumer needs — the extractor copies it through unchanged.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }
}
