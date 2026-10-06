using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// Advertises that this engine runs intrinsic nodes (<see cref="WellKnownRuntimeActivityConsumers.Intrinsic"/>).
/// </summary>
/// <remarks>
/// <para>
/// An intrinsic node carries the reserved <c>intrinsic</c> descriptor type, so every executable that holds one
/// declares the runtime requirement <c>intrinsic</c> at its descriptor schema version. No activation strategy
/// consumes that descriptor: <see cref="WorkflowIntrinsicExecutor"/> runs the node. The requirement still has to be
/// answered, because <c>IRuntimeRequirementChecker</c> reads this registry and nothing else, and its verdict decides
/// whether a member claims the execution (spec 184, FR-011), whether an artifact is admitted on import, and what
/// Publishing's preflight reports.
/// </para>
/// <para>
/// It is registered beside the executor it speaks for, so a runtime that can run the nodes also advertises the
/// consumer. It is its own type for the reason <c>ClrActivityConsumerCapability</c> gives: <c>TryAddEnumerable</c>
/// de-duplicates by implementation type.
/// </para>
/// </remarks>
public sealed class IntrinsicActivityConsumerCapability : IRuntimeActivityConsumerCapability
{
    /// <inheritdoc />
    public string ConsumerKey => WellKnownRuntimeActivityConsumers.Intrinsic;

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [RuntimeActivityDescriptor.InitialSchemaVersion];
}
