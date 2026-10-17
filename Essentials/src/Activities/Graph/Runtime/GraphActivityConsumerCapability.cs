using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Graph.Runtime;

/// <summary>
/// Advertises that this engine can activate graph-backed nodes (<see cref="WellKnownRuntimeActivityConsumers.GraphActivity"/>).
/// </summary>
/// <remarks>
/// Retained for consumers constructing this public capability directly. Feature authors should use
/// <c>AddRuntimeActivityConsumerCapability</c> to register by consumer key and supported schemas.
/// </remarks>
public sealed class GraphActivityConsumerCapability : IRuntimeActivityConsumerCapability
{
    /// <inheritdoc />
    public string ConsumerKey => WellKnownRuntimeActivityConsumers.GraphActivity;

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [RuntimeActivityDescriptor.InitialSchemaVersion];
}
