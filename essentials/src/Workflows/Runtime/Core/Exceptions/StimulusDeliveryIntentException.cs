namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>The owning adapter could not decode one canonical immutable stimulus intent.</summary>
public sealed class StimulusDeliveryIntentException : Exception
{
    public StimulusDeliveryIntentException(string intentId, string intentKind, Exception innerException)
        : base("The committed stimulus intent could not be decoded by its owning adapter.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId); ArgumentException.ThrowIfNullOrWhiteSpace(intentKind);
        ArgumentNullException.ThrowIfNull(innerException); IntentId = intentId; IntentKind = intentKind;
    }
    public string IntentId { get; }
    public string IntentKind { get; }
}
