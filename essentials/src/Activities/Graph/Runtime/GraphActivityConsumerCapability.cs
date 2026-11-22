using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Graph.Runtime;

/// <summary>
/// Advertises that this engine can activate graph-backed nodes (<see cref="WellKnownRuntimeActivityConsumers.GraphActivity"/>).
/// </summary>
/// <remarks>
/// Its own type for the same reason as <c>ClrActivityConsumerCapability</c>: <c>TryAddEnumerable</c>
/// de-duplicates by implementation type, so two capabilities sharing one generic class silently collapse into
/// whichever composed first. See that type's remarks for the defect this shape prevents.
/// </remarks>
public sealed class GraphActivityConsumerCapability : IRuntimeActivityConsumerCapability
{
    /// <inheritdoc />
    public string ConsumerKey => WellKnownRuntimeActivityConsumers.GraphActivity;

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [RuntimeActivityDescriptor.InitialSchemaVersion];
}
