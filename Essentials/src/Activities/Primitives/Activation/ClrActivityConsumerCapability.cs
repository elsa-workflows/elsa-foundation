using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activation;

/// <summary>
/// Advertises that this engine can activate CLR-backed nodes (<see cref="WellKnownRuntimeActivityConsumers.ClrActivity"/>).
/// </summary>
/// <remarks>
/// Retained for consumers constructing this public capability directly. Feature authors should use
/// <c>AddRuntimeActivityConsumerCapability</c> to register by consumer key and supported schemas.
/// </remarks>
public sealed class ClrActivityConsumerCapability : IRuntimeActivityConsumerCapability
{
    /// <inheritdoc />
    public string ConsumerKey => WellKnownRuntimeActivityConsumers.ClrActivity;

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [RuntimeActivityDescriptor.InitialSchemaVersion];
}
