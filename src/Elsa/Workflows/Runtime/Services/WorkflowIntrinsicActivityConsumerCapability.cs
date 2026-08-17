using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services;

/// <summary>
/// Advertises the engine's own intrinsic consumer (<see cref="WellKnownRuntimeActivityConsumers.Intrinsic"/>), the
/// descriptor key <c>ExecutableNodeCompiler</c> stamps onto every intrinsic node (<c>Set</c>, <c>Merge</c>,
/// <c>Return</c>, <c>Control</c>, <c>SetCorrelationId</c>, …).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> Intrinsics are executed by <see cref="WorkflowIntrinsicExecutor"/> — the engine
/// itself — rather than by an <c>IActivityActivationStrategy</c>, so nothing in the activation-strategy registry
/// speaks for them. But <c>IRuntimeActivityConsumerCapability</c> is capability <em>advertisement</em>, a distinct
/// concept from strategy registration: it answers "can this runtime execute material declaring this consumer?",
/// and for intrinsics the answer is yes, with the provider being the runtime spine. Hence a capability with no
/// matching strategy — which is correct, not an inconsistency.
/// </para>
/// <para>
/// <b>Why it is needed.</b> <c>WorkflowExecutable</c>'s constructor derives <c>RuntimeRequirements</c> from every
/// node's consumer key, so any compiled workflow containing an intrinsic declares
/// <c>RuntimeRequirement("intrinsic", "1")</c>. Without this advertisement the requirement reads as
/// <c>Missing</c> to <c>RuntimeRequirementChecker</c>, and every intrinsic-bearing artifact is rejected by the
/// artifact import gate and reported unready by publishing's deployment preflight. Advertising the capability is
/// preferred over excluding intrinsics from the derivation, because the derivation feeds a content-addressed
/// model and the requirement set is meant to be a <em>complete</em> statement of what a portable artifact needs:
/// a future trimmed runtime that dropped an intrinsic would then be caught at the import gate rather than passing
/// silently and faulting on first execution.
/// </para>
/// <para>
/// Registered with <c>TryAddEnumerable</c> in <c>AddWorkflowRuntime()</c>: this is a fan-in contribution beside
/// the CLR and graph advertisements, never a replacement contract.
/// </para>
/// </remarks>
public sealed class WorkflowIntrinsicActivityConsumerCapability : IRuntimeActivityConsumerCapability
{
    /// <inheritdoc />
    public string ConsumerKey => WellKnownRuntimeActivityConsumers.Intrinsic;

    /// <summary>
    /// The descriptor schema versions the engine reads for an intrinsic node.
    /// </summary>
    /// <remarks>
    /// Exactly <see cref="RuntimeActivityDescriptor.InitialSchemaVersion"/>. The compiler constructs intrinsic
    /// nodes without an explicit <c>descriptorSchemaVersion</c>, so <c>ExecutableNode</c> defaults it to that
    /// value — the intrinsic descriptor payload's own inner <c>schemaVersion</c> field is payload content, not the
    /// descriptor schema this axis matches on.
    /// </remarks>
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [RuntimeActivityDescriptor.InitialSchemaVersion];
}
