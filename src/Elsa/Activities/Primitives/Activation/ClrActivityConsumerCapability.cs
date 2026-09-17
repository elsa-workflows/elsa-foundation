using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activation;

/// <summary>
/// Advertises that this engine can activate CLR-backed nodes (<see cref="WellKnownRuntimeActivityConsumers.ClrActivity"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is its own type on purpose, and that is load-bearing.</b> Capabilities are registered with
/// <c>TryAddEnumerable</c>, which de-duplicates by <em>implementation type</em>. This capability and the graph
/// one previously shared the single generic <c>RuntimeActivityConsumerCapability</c> class and differed only in
/// data, so whichever feature composed second was <b>silently discarded</b> — an engine composing both
/// <c>ActivitiesPrimitives</c> and <c>GraphActivitiesRuntime</c> advertised only one of them.
/// </para>
/// <para>
/// The consequence was not cosmetic: the import gate (FR-B-005a) and the publishing preflight both read this
/// registry, so a fully-featured engine reported <c>activity consumer 'elsa.clr-activity' schema '1' is not
/// installed</c> and refused to import artifacts it was perfectly able to run. It stayed hidden because nothing
/// enumerated the registry until spec 151 added that gate.
/// </para>
/// <para>
/// A distinct type per capability keeps <c>TryAddEnumerable</c>'s idempotency — composing a feature twice still
/// registers once — while making the de-duplication key match the thing being de-duplicated.
/// </para>
/// </remarks>
public sealed class ClrActivityConsumerCapability : IRuntimeActivityConsumerCapability
{
    /// <inheritdoc />
    public string ConsumerKey => WellKnownRuntimeActivityConsumers.ClrActivity;

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [RuntimeActivityDescriptor.InitialSchemaVersion];
}
