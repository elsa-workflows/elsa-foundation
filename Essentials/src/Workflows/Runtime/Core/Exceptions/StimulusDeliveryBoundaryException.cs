namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>A committed stimulus source could not be read or validated before recipient effects.</summary>
public sealed class StimulusDeliveryBoundaryException : InvalidOperationException
{
    public StimulusDeliveryBoundaryException(string sourceOutboxItemId, Exception innerException, bool isUnavailable)
        : base("The committed stimulus delivery source could not be validated; its responsibility remains owed.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceOutboxItemId);
        ArgumentNullException.ThrowIfNull(innerException);
        SourceOutboxItemId = sourceOutboxItemId;
        IsUnavailable = isUnavailable;
    }

    public string SourceOutboxItemId { get; }
    /// <summary>True for a temporary dependency failure; false for contradictory, corrupt or unsupported source state.</summary>
    public bool IsUnavailable { get; }
}
