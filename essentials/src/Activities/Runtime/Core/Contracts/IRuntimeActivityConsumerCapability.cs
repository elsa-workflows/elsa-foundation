namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Declares the stable descriptor consumer/schema pairs installed by a Runtime feature.
/// This is capability evidence only; activity instances are still created through the transient
/// activation seam.
/// </summary>
public interface IRuntimeActivityConsumerCapability
{
    string ConsumerKey { get; }

    IReadOnlyCollection<string> SupportedSchemaVersions { get; }
}
