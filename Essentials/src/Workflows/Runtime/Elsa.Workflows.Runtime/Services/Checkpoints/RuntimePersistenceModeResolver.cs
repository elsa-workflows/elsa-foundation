using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

/// <summary>
/// Default <see cref="IRuntimePersistenceModeResolver"/>. The workflow's chosen mode wins over the host default. The host
/// default is signalled by the registered <see cref="PersistenceModeOptions"/>: absent means the shell runs Durable, so a
/// workflow that chose Balanced is clamped to Durable because no folding machinery is registered to honour it.
/// </summary>
public sealed class RuntimePersistenceModeResolver(
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IEnumerable<PersistenceModeOptions> hostOptions,
    IWorkflowExecutableReader? executableReader = null) : IRuntimePersistenceModeResolver
{
    // Envelope metadata breadcrumb stamped by WorkflowStartDispatcher.CreateDispatchMetadata for the start command.
    private const string ArtifactIdMetadataKey = "runtime.artifactId";

    private readonly PersistenceModeOptions? _hostOptions = hostOptions.SingleOrDefault();

    public async ValueTask<ResolvedPersistenceMode> ResolveAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // Resume and continuation drains: the run recorded its mode at start. That record is authoritative, avoids an
        // executable load, and stays correct after the host default is reconfigured.
        var state = await workflowExecutionStateStore.FindAsync(envelope.WorkflowExecutionId, cancellationToken);
        if (state is not null && TryReadRecordedMode(state, out var recorded))
            return new(recorded);

        // First drain (no state row yet): resolve from the pinned executable. The artifact id is on the start envelope
        // before any state row exists.
        var artifactId = TryGetArtifactId(envelope) ?? state?.PinnedExecutable.ArtifactId;
        if (artifactId is null)
            return new(HostDefault());

        var executable = await PinnedExecutableRead.FindAsync(executableReader, executableStore, artifactId, cancellationToken);
        return new(Resolve(executable));
    }

    public PersistenceMode Resolve(WorkflowExecutable? executable)
    {
        if (executable?.PersistenceMode is not { } chosen)
            return HostDefault();

        // A chosen Balanced is only reachable when the host registered the folding machinery.
        return _hostOptions is null ? PersistenceMode.Durable : chosen;
    }

    private PersistenceMode HostDefault() => _hostOptions?.Mode ?? PersistenceMode.Durable;

    private static string? TryGetArtifactId(WorkflowExecutionCommandEnvelope envelope)
    {
        if (envelope.Command.Metadata.TryGetValue(ArtifactIdMetadataKey, out var fromCommand) && !string.IsNullOrWhiteSpace(fromCommand))
            return fromCommand;
        if (envelope.Metadata.TryGetValue(ArtifactIdMetadataKey, out var fromEnvelope) && !string.IsNullOrWhiteSpace(fromEnvelope))
            return fromEnvelope;
        return null;
    }

    private static bool TryReadRecordedMode(WorkflowExecutionState state, out PersistenceMode mode)
    {
        mode = default;
        return state.SystemMetadata.TryGetValue(RuntimeMetadataKeys.PersistenceMode, out var recorded) &&
               Enum.TryParse(recorded, ignoreCase: false, out mode) &&
               Enum.IsDefined(mode);
    }
}
