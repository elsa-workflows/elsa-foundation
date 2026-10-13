using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

/// <summary>
/// Default <see cref="IRuntimePersistenceModeResolver"/>. A run's mode is decided once, when it is started: the
/// workflow's choice wins over the host default, and the start dispatcher writes the result onto the start command,
/// which the scheduler queue keeps, as first delivered, until the run's first commit. From that commit on, the mode
/// recorded on the run decides. Every drain of a run therefore finds the same mode, whichever command starts it,
/// including a drain that runs the run again after its first pass ended before anything was committed.
/// </summary>
internal sealed class RuntimePersistenceModeResolver(
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IWorkflowSchedulerWorkQueue schedulerWorkQueue,
    IEnumerable<PersistenceModeOptions> hostOptions,
    IWorkflowExecutableReader? executableReader = null) : IRuntimePersistenceModeResolver
{
    private readonly PersistenceModeOptions? _hostOptions = hostOptions.SingleOrDefault();

    // The persistence modes register the host's options. A host without them cannot run any mode, so it has no default.
    private PersistenceMode HostDefault => _hostOptions?.Mode ?? throw new InvalidOperationException(
        "This host has no persistence-mode options, so no run has a mode to resolve. " +
        "Call AddPersistenceModes() after the workflows runtime and its persistence provider are registered.");

    public async ValueTask<ResolvedPersistenceMode> ResolveAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var workflowExecutionId = envelope.WorkflowExecutionId;
        var state = await workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
        if (state is not null)
            return new(await ModeOfRecordedRunAsync(state, cancellationToken), RunHasRecord: true);

        // Nothing committed yet: the run's start decides. That is the start the queue holds, which is the one the drain
        // runs: the queue keeps a start's first delivery, so a start delivered again (stamped under a host default that
        // has changed since) is not the one that runs. A start the queue does not hold decides for itself.
        var start = await FindQueuedStartAsync(workflowExecutionId, cancellationToken)
            ?? (envelope.Command.Kind == WorkflowExecutionCommandKind.Start
                ? new StartCommand(envelope.Command.Metadata, envelope.Command.Payload)
                : null);
        if (start is not null)
            return new(await ModeOfStartAsync(workflowExecutionId, start, cancellationToken), RunHasRecord: false);

        // Neither a record nor a queued start: nothing of this run is durable, so no choice exists to keep, and no work
        // the drain finds can start the run. This is a command for a run that never started here, or one for an
        // Ephemeral run that completed without a record.
        return new(HostDefault, RunHasRecord: false);
    }

    public PersistenceMode Resolve(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        return executable.PersistenceMode ?? HostDefault;
    }

    private async ValueTask<PersistenceMode> ModeOfRecordedRunAsync(WorkflowExecutionState state, CancellationToken cancellationToken)
    {
        if (state.SystemMetadata.TryGetValue(RuntimeMetadataKeys.PersistenceMode, out var recorded))
            return Parse(state.WorkflowExecutionId, recorded, "recorded on the run");

        // A run faulted into existence by a start that carried no mode has a record without one. Its pinned workflow
        // decides, as it did when the run was started.
        return await ModeOfPinnedWorkflowAsync(state.WorkflowExecutionId, state.PinnedExecutable.ArtifactId, cancellationToken);
    }

    private async ValueTask<PersistenceMode> ModeOfStartAsync(string workflowExecutionId, StartCommand start, CancellationToken cancellationToken)
    {
        if (start.Metadata.TryGetValue(RuntimeMetadataKeys.PersistenceMode, out var stamped))
            return Parse(workflowExecutionId, stamped, "written on its start command");

        // A start that was not made by the start dispatcher carries no mode. Its pinned workflow decides.
        WorkflowExecutionStartCommandPayload? payload;
        try
        {
            payload = start.Payload?.Deserialize<WorkflowExecutionStartCommandPayload>();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw Undetermined(workflowExecutionId, "its start command's payload cannot be read", exception);
        }

        return payload is null
            ? throw Undetermined(workflowExecutionId, "its start command carries neither a mode nor a pinned workflow")
            : await ModeOfPinnedWorkflowAsync(workflowExecutionId, payload.PinnedExecutable.ArtifactId, cancellationToken);
    }

    private async ValueTask<PersistenceMode> ModeOfPinnedWorkflowAsync(string workflowExecutionId, string artifactId, CancellationToken cancellationToken)
    {
        var executable = await PinnedExecutableRead.FindAsync(executableReader, executableStore, artifactId, cancellationToken)
            ?? throw Undetermined(workflowExecutionId, $"its pinned workflow '{artifactId}' cannot be found");
        return Resolve(executable);
    }

    // The queue keeps a run's start until the run's first commit, in arrival order, so a run that was never committed
    // finds its start near the head.
    private async ValueTask<StartCommand?> FindQueuedStartAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var query = new RuntimeSchedulerWorkQuery(workflowExecutionId);
        while (true)
        {
            var page = await schedulerWorkQueue.ListAsync(query, cancellationToken);
            var start = page.Items.FirstOrDefault(item => item.CommandKind == WorkflowExecutionCommandKind.Start);
            if (start is not null)
                return new StartCommand(start.CommandMetadata, start.Payload);
            if (page.NextContinuationToken is null)
                return null;
            query = new RuntimeSchedulerWorkQuery(workflowExecutionId, query.Limit, page.NextContinuationToken);
        }
    }

    private static PersistenceMode Parse(string workflowExecutionId, string value, string source) =>
        Enum.TryParse<PersistenceMode>(value, ignoreCase: false, out var mode) && Enum.IsDefined(mode)
            ? mode
            : throw Undetermined(workflowExecutionId, $"the mode '{value}' {source} is not one this runtime knows");

    private static InvalidOperationException Undetermined(string workflowExecutionId, string reason, Exception? innerException = null) =>
        new($"The persistence mode of workflow execution '{workflowExecutionId}' cannot be determined: {reason}. " +
            "The run is not drained rather than run in a mode it did not choose.", innerException);

    private sealed record StartCommand(IReadOnlyDictionary<string, string> Metadata, JsonElement? Payload);
}
