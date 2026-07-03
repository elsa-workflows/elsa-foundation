namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The stimulus identity a start-trigger activity node reacts to, described at publish time (W7, E3-1).
/// It is the opaque <c>(StimulusType, StimulusHash)</c> routing pair the engine already uses on bookmarks,
/// optionally scoped to a passive correlation value; the trigger extractor turns it into a durable
/// <see cref="WorkflowTriggerBinding"/>.
/// </summary>
public sealed class TriggerStimulusDescriptor
{
    public TriggerStimulusDescriptor(string stimulusType, string stimulusHash, string? correlationScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);

        if (correlationScope is not null && string.IsNullOrWhiteSpace(correlationScope))
            throw new ArgumentException("Correlation scope cannot be blank when provided.", nameof(correlationScope));

        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        CorrelationScope = correlationScope;
    }

    public string StimulusType { get; }
    public string StimulusHash { get; }
    public string? CorrelationScope { get; }
}
