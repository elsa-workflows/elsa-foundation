namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Declares the stable descriptor consumer/schema pairs installed by a Runtime feature.
/// This is capability evidence only; activity instances are still created through the transient
/// activation seam.
/// </summary>
/// <remarks>
/// Register static capability evidence with <c>AddRuntimeActivityConsumerCapability</c>. Consumers
/// combine supported schemas by ordinal <see cref="ConsumerKey"/>; implementation type is not identity.
/// </remarks>
public interface IRuntimeActivityConsumerCapability
{
    string ConsumerKey { get; }

    IReadOnlyCollection<string> SupportedSchemaVersions { get; }
}
