using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default <see cref="IRuntimeCheckpointCadenceResolver"/> (ADR 0032 R5). Resolves the effective checkpoint cadence
/// with the precedence: mandatory boundaries (owned by the commit layer, never expressed here) &gt; per-workflow
/// authored cadence &gt; host default. The host default is signalled by the presence of the registered coalescing
/// options — absent means the shell runs the Immediate policy, so authored-Coalesced is clamped to Immediate because
/// no coalescing machinery is registered to honour it (the honest reachability boundary).
/// </summary>
public sealed class RuntimeCheckpointCadenceResolver(
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IEnumerable<CoalescingRuntimeCheckpointPersistenceOptions> hostCoalescingOptions) : IRuntimeCheckpointCadenceResolver
{
    // Envelope metadata breadcrumb stamped by WorkflowStartDispatcher.CreateDispatchMetadata for the start command.
    private const string ArtifactIdMetadataKey = "runtime.artifactId";

    private readonly CoalescingRuntimeCheckpointPersistenceOptions? _hostOptions = hostCoalescingOptions.SingleOrDefault();

    public async ValueTask<ResolvedCheckpointCadence> ResolveAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // Resume/continuation drains: the run stamped its effective cadence at start. Prefer it — it is the authoritative
        // record of what this run executes under, and reading it avoids an executable load and stays correct even after
        // the host default is later reconfigured.
        var state = await workflowExecutionStateStore.FindAsync(envelope.WorkflowExecutionId, cancellationToken);
        if (state is not null && TryReadStamp(state, out var stamped))
            return stamped;

        // Start drain (no state row yet) or an unstamped legacy instance: resolve from the pinned executable's authored
        // cadence. The artifact id is available on the start envelope before any state row exists.
        var artifactId = TryGetArtifactId(envelope) ?? state?.PinnedExecutable.ArtifactId;
        if (artifactId is null)
            return HostDefault();

        var executable = await executableStore.FindAsync(artifactId, cancellationToken);
        return Resolve(executable);
    }

    public ResolvedCheckpointCadence Resolve(WorkflowExecutable? executable) => Combine(executable?.CheckpointCadence);

    private ResolvedCheckpointCadence Combine(WorkflowExecutableCheckpointCadence? authored)
    {
        if (authored is null)
            return HostDefault();

        if (!authored.IsCoalesced)
            return ResolvedCheckpointCadence.Immediate;

        // Authored Coalesced is only reachable when the host registered the coalescing machinery. On an Immediate host
        // it degrades to Immediate rather than silently claiming a cadence the shell cannot run.
        return _hostOptions is null
            ? ResolvedCheckpointCadence.Immediate
            : ResolvedCheckpointCadence.CoalescedWith(authored.MaxSegmentCheckpoints ?? _hostOptions.MaxSegmentCheckpoints);
    }

    private ResolvedCheckpointCadence HostDefault() =>
        _hostOptions is null
            ? ResolvedCheckpointCadence.Immediate
            : ResolvedCheckpointCadence.CoalescedWith(_hostOptions.MaxSegmentCheckpoints);

    private static string? TryGetArtifactId(WorkflowExecutionCommandEnvelope envelope)
    {
        if (envelope.Command.Metadata.TryGetValue(ArtifactIdMetadataKey, out var fromCommand) && !string.IsNullOrWhiteSpace(fromCommand))
            return fromCommand;
        if (envelope.Metadata.TryGetValue(ArtifactIdMetadataKey, out var fromEnvelope) && !string.IsNullOrWhiteSpace(fromEnvelope))
            return fromEnvelope;
        return null;
    }

    private static bool TryReadStamp(WorkflowExecutionState state, out ResolvedCheckpointCadence resolved)
    {
        if (!state.SystemMetadata.TryGetValue(RuntimeMetadataKeys.CheckpointCadence, out var mode))
        {
            resolved = ResolvedCheckpointCadence.Immediate;
            return false;
        }

        if (StringComparer.Ordinal.Equals(mode, WorkflowExecutableCheckpointCadence.CoalescedMode) &&
            state.SystemMetadata.TryGetValue(RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints, out var maxRaw) &&
            int.TryParse(maxRaw, out var max) && max > 0)
        {
            resolved = ResolvedCheckpointCadence.CoalescedWith(max);
            return true;
        }

        resolved = ResolvedCheckpointCadence.Immediate;
        return true;
    }
}
