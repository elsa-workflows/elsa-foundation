using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CShells.Features;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Http.IntegrationTests;

internal sealed record ResponseReplayCommitGateOptions(
    string PipeName,
    string TargetArtifactId,
    string TargetArtifactHash,
    string EndpointNodeId,
    string ResponseNodeId,
    bool GateAtResponseActivityStarted);

internal sealed record ResponseReplayClaimCorrelation(
    string WorkflowExecutionId,
    string ArtifactId,
    string ArtifactHash,
    string EndpointNodeId,
    string ActivityExecutionId,
    string SchedulerWorkItemId,
    string CommandId,
    string AttemptId);

internal sealed class ResponseReplayCommitGateState
{
    private ResponseReplayClaimCorrelation? _claim;
    private int _responseObserved;

    public ResponseReplayClaimCorrelation? Claim => Volatile.Read(ref _claim);

    public bool TryPublishClaim(ResponseReplayClaimCorrelation claim) =>
        Interlocked.CompareExchange(ref _claim, claim, null) is null;

    public bool TryObserveResponse() => Interlocked.CompareExchange(ref _responseObserved, 1, 0) == 0;
}

internal sealed class ResponseReplayCommitGateChannel(ResponseReplayCommitGateOptions options) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_pipe is not null)
            throw new InvalidOperationException("The response-replay gate channel is already connected.");

        var pipe = new NamedPipeClientStream(".", options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken);
        _pipe = pipe;
        _reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        _writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
        await SendAsync("ready", new { processId = Environment.ProcessId }, cancellationToken);
    }

    public Task SendAsync(string type, object payload, CancellationToken cancellationToken = default) =>
        WriteLineAsync(JsonSerializer.Serialize(new GateMessage(type, JsonSerializer.SerializeToElement(payload, JsonOptions)), JsonOptions), cancellationToken);

    public async Task WaitForCommandAsync(string expectedCommand, CancellationToken cancellationToken = default)
    {
        var line = await ReadLineAsync(cancellationToken);
        if (!StringComparer.Ordinal.Equals(line, expectedCommand))
            throw new InvalidDataException($"Expected parent command '{expectedCommand}', received '{line ?? "<closed>"}'.");
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        var writer = _writer ?? throw new InvalidOperationException("The response-replay gate channel is not connected.");
        await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var reader = _reader ?? throw new InvalidOperationException("The response-replay gate channel is not connected.");
        return await reader.ReadLineAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
            await _writer.DisposeAsync();
        _reader?.Dispose();
        if (_pipe is not null)
            await _pipe.DisposeAsync();
    }

    internal sealed record GateMessage(string Type, JsonElement Payload);
}

internal sealed class ResponseReplayCommitGateStore(
    IRuntimeCheckpointCommitStore inner,
    IRuntimeCoalescingSessionAccessor sessionAccessor,
    ResponseReplayCommitGateChannel channel,
    ResponseReplayCommitGateOptions options,
    ResponseReplayCommitGateState gateState) : IRuntimeCheckpointCommitStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.CommitAsync(commit, decision, cancellationToken);

        if (IsTargetEndpointClaim(commit, out var claim))
        {
            var claimCorrelation = new ResponseReplayClaimCorrelation(
                commit.WorkflowExecutionId,
                options.TargetArtifactId,
                options.TargetArtifactHash,
                options.EndpointNodeId,
                claim.ActivityExecutionId,
                claim.WorkItemId,
                claim.CommandId,
                claim.AttemptId);
            if (gateState.TryPublishClaim(claimCorrelation))
            {
                await channel.SendAsync("endpoint-claim-durable", new
                {
                    commitId = commit.CommitId,
                    checkpointId = commit.Checkpoint.CheckpointId,
                    executionId = claimCorrelation.WorkflowExecutionId,
                    artifactId = claimCorrelation.ArtifactId,
                    artifactHash = claimCorrelation.ArtifactHash,
                    nodeId = claimCorrelation.EndpointNodeId,
                    activityExecutionId = claimCorrelation.ActivityExecutionId,
                    schedulerWorkItemId = claimCorrelation.SchedulerWorkItemId,
                    commandId = claimCorrelation.CommandId,
                    attemptId = claimCorrelation.AttemptId,
                    decision = decision.Mode.ToString()
                }, cancellationToken);
                await channel.WaitForCommandAsync("continue", cancellationToken);
            }
            else if (gateState.Claim != claimCorrelation)
            {
                throw new InvalidOperationException("The response-replay gate observed more than one endpoint claim identity for its target workflow.");
            }
            else
            {
                throw new InvalidOperationException("The response-replay gate observed the target endpoint claim more than once.");
            }
        }

        if (options.GateAtResponseActivityStarted &&
            IsTargetResponseActivityStarted(commit, out var startedState, out var startedCorrelation))
        {
            var session = sessionAccessor.Current
                ?? throw new InvalidOperationException("The external-input ActivityStarted commit returned without an ambient coalescing session.");
            if (!session.IsActive || !session.AppliesTo(commit.WorkflowExecutionId) ||
                session.MaxSegmentCheckpoints != ResponseReplayPublicationHost.ExternalInputMaxSegmentCheckpoints)
                throw new InvalidOperationException("The external-input ActivityStarted commit did not return through the expected active cap-two coalescing session.");

            var bodyInput = startedState.InputSnapshot?.Values.GetValueOrDefault("Body")
                ?? throw new InvalidOperationException("The external-input ActivityStarted state has no Body input snapshot.");
            var bodyReference = bodyInput.ExternalReference;
            if (bodyInput.Presence != ValuePresence.Present || bodyInput.InlineValue is not null ||
                bodyInput.Policy.Storage != DurableValueStorage.External ||
                bodyReference is null ||
                !StringComparer.Ordinal.Equals(bodyReference.StorageProfile, ResponseReplayFileExternalPayloadStoreProfile.Name))
                throw new InvalidOperationException("The external-input ActivityStarted state does not carry the expected file-backed External Body reference.");

            var invokeIntent = commit.PostCommitIntents.SingleOrDefault(intent =>
                StringComparer.Ordinal.Equals(intent.Kind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork) &&
                StringComparer.Ordinal.Equals(intent.WorkflowExecutionId, commit.WorkflowExecutionId) &&
                StringComparer.Ordinal.Equals(intent.ActivityExecutionId, startedState.Execution.ActivityExecutionId))
                ?? throw new InvalidOperationException("The external-input ActivityStarted commit has no matching InvokeActivity post-commit intent.");
            var invokeWorkItem = invokeIntent.MaterializedSchedulerWorkItem
                ?? throw new InvalidOperationException("The external-input ActivityStarted intent has no materialized InvokeActivity work item.");
            var invokePayload = invokeWorkItem.Payload?.Deserialize<RuntimeInvokeActivityCommandPayload>()
                ?? throw new InvalidOperationException("The external-input ActivityStarted intent has no InvokeActivity payload.");
            if (commit.Checkpoint.Name != RuntimeCheckpointNames.ActivityStarted ||
                invokeWorkItem.CommandKind != WorkflowExecutionCommandKind.InvokeActivity ||
                !StringComparer.Ordinal.Equals(invokeWorkItem.WorkflowExecutionId, commit.WorkflowExecutionId) ||
                !StringComparer.Ordinal.Equals(invokePayload.ActivityExecutionId, startedState.Execution.ActivityExecutionId) ||
                !StringComparer.Ordinal.Equals(invokePayload.ExecutableNodeId, options.ResponseNodeId) ||
                !StringComparer.Ordinal.Equals(invokePayload.PinnedExecutable.ArtifactId, startedCorrelation.ArtifactId) ||
                !StringComparer.Ordinal.Equals(invokePayload.PinnedExecutable.ArtifactHash, startedCorrelation.ArtifactHash))
                throw new InvalidOperationException("The external-input ActivityStarted continuation does not target the pinned response activity and execution.");

            if (!gateState.TryObserveResponse())
                throw new InvalidOperationException("The response-replay gate observed more than one external-input ActivityStarted boundary.");

            await channel.SendAsync("response-activity-started", new
            {
                commitId = commit.CommitId,
                checkpointId = commit.Checkpoint.CheckpointId,
                checkpointName = commit.Checkpoint.Name,
                executionId = startedCorrelation.WorkflowExecutionId,
                artifactId = startedCorrelation.ArtifactId,
                artifactHash = startedCorrelation.ArtifactHash,
                endpointNodeId = startedCorrelation.EndpointNodeId,
                endpointActivityExecutionId = startedCorrelation.ActivityExecutionId,
                responseNodeId = startedState.Execution.AuthoredActivityId,
                responseActivityExecutionId = startedState.Execution.ActivityExecutionId,
                responseStatus = startedState.Status.ToString(),
                responseInputBody = new
                {
                    presence = bodyInput.Presence.ToString(),
                    inlineValuePresent = bodyInput.InlineValue.HasValue,
                    storage = bodyInput.Policy.Storage.ToString(),
                    storageProfile = bodyReference.StorageProfile,
                    locator = bodyReference.Locator,
                    typeAlias = bodyInput.Type.Alias,
                    collectionKind = bodyInput.Type.CollectionKind.ToString(),
                    schemaVersion = bodyInput.Type.SchemaVersion,
                    schema = bodyInput.Type.Schema?.GetRawText(),
                    metadata = bodyReference.Metadata
                },
                invokeIntentId = invokeIntent.IntentId,
                invokeOutboxItemId = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, invokeIntent.IntentId),
                invokeWorkItemId = invokeWorkItem.WorkItemId,
                invokeCommandKind = invokeWorkItem.CommandKind.ToString(),
                sessionActive = session.IsActive,
                sessionAppliesToExecution = session.AppliesTo(commit.WorkflowExecutionId),
                maxSegmentCheckpoints = session.MaxSegmentCheckpoints,
                hopCount = session.HopCount,
                decision = decision.Mode.ToString()
            }, cancellationToken);

            // The parent independently verifies the persisted ActivityStarted state, external file reference, and
            // Pending InvokeActivity outbox before acknowledging the kill window. The barrier never releases the child.
            await channel.WaitForCommandAsync("external-input-window-confirmed", cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
        }

        if (IsTargetResponseCompletion(commit, out var responseState, out var correlation))
        {
            var session = sessionAccessor.Current
                ?? throw new InvalidOperationException("The response completion returned without an ambient coalescing session.");
            if (!session.IsActive || !session.AppliesTo(commit.WorkflowExecutionId) || !session.HasBufferedChanges)
                throw new InvalidOperationException("The response completion did not return into an active applicable session with buffered changes.");
            if (!session.TryGetActivity(responseState.Execution.ActivityExecutionId, out var buffered, out var tombstoned) ||
                tombstoned || buffered?.Completion is null)
                throw new InvalidOperationException("The response completion was not visible in the coalescing activity overlay after the store returned.");
            if (!StringComparer.Ordinal.Equals(buffered.Execution.AuthoredActivityId, options.ResponseNodeId) ||
                !StringComparer.Ordinal.Equals(buffered.Completion.InvocationId, responseState.Completion!.InvocationId))
                throw new InvalidOperationException("The coalescing overlay returned a different response activity completion.");

            var completionJson = JsonSerializer.SerializeToUtf8Bytes(buffered.Completion, JsonOptions);
            var instruction = buffered.Completion.Result.InlineValue
                ?? throw new InvalidOperationException("The buffered response completion has no inline instruction.");
            var bodyInput = responseState.InputSnapshot?.Values.GetValueOrDefault("Body");
            var bodyReference = bodyInput?.ExternalReference;
            if (!gateState.TryObserveResponse())
                throw new InvalidOperationException("The response-replay gate observed more than one response completion for its target workflow.");

            await channel.SendAsync("response-buffered", new
            {
                commitId = commit.CommitId,
                checkpointId = commit.Checkpoint.CheckpointId,
                executionId = correlation.WorkflowExecutionId,
                artifactId = correlation.ArtifactId,
                artifactHash = correlation.ArtifactHash,
                endpointNodeId = correlation.EndpointNodeId,
                endpointActivityExecutionId = correlation.ActivityExecutionId,
                schedulerWorkItemId = correlation.SchedulerWorkItemId,
                commandId = correlation.CommandId,
                claimAttemptId = correlation.AttemptId,
                responseNodeId = buffered.Execution.AuthoredActivityId,
                responseActivityExecutionId = buffered.Execution.ActivityExecutionId,
                responseInputBody = bodyInput is null ? null : new
                {
                    presence = bodyInput.Presence.ToString(),
                    inlineValuePresent = bodyInput.InlineValue.HasValue,
                    storage = bodyInput.Policy.Storage.ToString(),
                    storageProfile = bodyReference?.StorageProfile,
                    locator = bodyReference?.Locator,
                    metadata = bodyReference?.Metadata
                },
                responseAttemptId = buffered.Completion.AttemptId,
                sessionExecutionId = session.WorkflowExecutionId,
                sessionActive = session.IsActive,
                sessionAppliesToExecution = session.AppliesTo(commit.WorkflowExecutionId),
                hasBufferedChanges = session.HasBufferedChanges,
                hopCount = session.HopCount,
                decision = decision.Mode.ToString(),
                completionSha256 = Convert.ToHexString(SHA256.HashData(completionJson)).ToLowerInvariant(),
                instruction = new
                {
                    statusCode = instruction.GetProperty("statusCode").GetInt32(),
                    body = instruction.GetProperty("body").GetString(),
                    contentType = instruction.GetProperty("contentType").GetString(),
                    headerCount = instruction.GetProperty("headers").EnumerateObject().Count()
                }
            }, cancellationToken);

            // The parent reads independent durable state while this request remains suspended in the store.
            // There is deliberately no release command: only the parent process kill can end this test barrier.
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
        }

        return result;
    }

    private bool IsTargetEndpointClaim(RuntimeCheckpointCommit commit, out ClaimIdentity claim)
    {
        claim = null!;
        if (!StringComparer.Ordinal.Equals(commit.Checkpoint.Name, RuntimeCheckpointNames.ActivityAttemptClaimed) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableArtifactId, out var artifactId) ||
            !StringComparer.Ordinal.Equals(artifactId, options.TargetArtifactId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableArtifactHash, out var artifactHash) ||
            !StringComparer.Ordinal.Equals(artifactHash, options.TargetArtifactHash) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableNodeId, out var nodeId) ||
            !StringComparer.Ordinal.Equals(nodeId, options.EndpointNodeId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityExecutionId, out var activityExecutionId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.SchedulerWorkItemId, out var workItemId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.CommandId, out var commandId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var attemptId))
            return false;

        var claimedState = commit.StateChanges.ActivityExecutions.SingleOrDefault(change =>
            change.Operation == RuntimeStateChangeOperation.Upsert &&
            StringComparer.Ordinal.Equals(change.State.Execution.ActivityExecutionId, activityExecutionId));
        if (claimedState?.State is not { } state ||
            !StringComparer.Ordinal.Equals(state.Execution.AuthoredActivityId, options.EndpointNodeId) ||
            !state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var stateAttemptId) ||
            !StringComparer.Ordinal.Equals(stateAttemptId, attemptId) ||
            !state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId, out var stateWorkItemId) ||
            !StringComparer.Ordinal.Equals(stateWorkItemId, workItemId) ||
            state.Attempts?.SingleOrDefault(attempt => StringComparer.Ordinal.Equals(attempt.AttemptId, attemptId)) is not { EndedAt: null })
            return false;

        claim = new ClaimIdentity(activityExecutionId, workItemId, commandId, attemptId);
        return true;
    }

    private bool IsTargetResponseCompletion(
        RuntimeCheckpointCommit commit,
        out ActivityExecutionState responseState,
        out ResponseReplayClaimCorrelation correlation)
    {
        responseState = null!;
        correlation = null!;
        var observedClaim = gateState.Claim;
        if (observedClaim is null ||
            !StringComparer.Ordinal.Equals(observedClaim.ArtifactId, options.TargetArtifactId) ||
            !StringComparer.Ordinal.Equals(observedClaim.ArtifactHash, options.TargetArtifactHash) ||
            !StringComparer.Ordinal.Equals(observedClaim.EndpointNodeId, options.EndpointNodeId) ||
            !StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, observedClaim.WorkflowExecutionId) ||
            !commit.StateChanges.ActivityExecutions.Any(change => change.State.Completion is not null))
            return false;

        var match = commit.StateChanges.ActivityExecutions.SingleOrDefault(change =>
            change.Operation == RuntimeStateChangeOperation.Upsert &&
            StringComparer.Ordinal.Equals(change.State.Execution.AuthoredActivityId, options.ResponseNodeId) &&
            change.State.Completion is not null);
        if (match?.State is not { } state)
            return false;

        responseState = state;
        correlation = observedClaim;
        return true;
    }

    private bool IsTargetResponseActivityStarted(
        RuntimeCheckpointCommit commit,
        out ActivityExecutionState responseState,
        out ResponseReplayClaimCorrelation correlation)
    {
        responseState = null!;
        correlation = null!;
        var observedClaim = gateState.Claim;
        if (observedClaim is null ||
            !StringComparer.Ordinal.Equals(observedClaim.ArtifactId, options.TargetArtifactId) ||
            !StringComparer.Ordinal.Equals(observedClaim.ArtifactHash, options.TargetArtifactHash) ||
            !StringComparer.Ordinal.Equals(observedClaim.EndpointNodeId, options.EndpointNodeId) ||
            !StringComparer.Ordinal.Equals(commit.WorkflowExecutionId, observedClaim.WorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(commit.Checkpoint.Name, RuntimeCheckpointNames.ActivityStarted) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableArtifactId, out var artifactId) ||
            !StringComparer.Ordinal.Equals(artifactId, options.TargetArtifactId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableArtifactHash, out var artifactHash) ||
            !StringComparer.Ordinal.Equals(artifactHash, options.TargetArtifactHash) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ExecutableNodeId, out var nodeId) ||
            !StringComparer.Ordinal.Equals(nodeId, options.ResponseNodeId) ||
            !commit.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityExecutionId, out var activityExecutionId))
            return false;

        var match = commit.StateChanges.ActivityExecutions.SingleOrDefault(change =>
            change.Operation == RuntimeStateChangeOperation.Upsert &&
            StringComparer.Ordinal.Equals(change.State.Execution.ActivityExecutionId, activityExecutionId) &&
            StringComparer.Ordinal.Equals(change.State.Execution.AuthoredActivityId, options.ResponseNodeId) &&
            change.State.Status == ActivityExecutionStatus.Running &&
            change.State.InputSnapshot is not null);
        if (match?.State is not { } state)
            return false;

        responseState = state;
        correlation = observedClaim;
        return true;
    }

    private sealed record ClaimIdentity(string ActivityExecutionId, string WorkItemId, string CommandId, string AttemptId);
}

[ShellFeature(
    name: "ResponseReplayCommitGate",
    DisplayName = "Response Replay Test Commit Gate",
    Description = "Test-only process barrier around the registered Coalesced commit store.",
    DependsOn = new object[] { "WorkflowsRuntimeCheckpointPersistence" })]
public sealed class ResponseReplayCommitGateFeature : IShellFeature, IPostConfigureShellServices
{
    public string PipeName { get; set; } = string.Empty;
    public string TargetArtifactId { get; set; } = string.Empty;
    public string TargetArtifactHash { get; set; } = string.Empty;
    public string EndpointNodeId { get; set; } = "http-in";
    public string ResponseNodeId { get; set; } = "write-response";
    public bool GateAtResponseActivityStarted { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        var options = new ResponseReplayCommitGateOptions(
            PipeName,
            TargetArtifactId,
            TargetArtifactHash,
            EndpointNodeId,
            ResponseNodeId,
            GateAtResponseActivityStarted);
        if (string.IsNullOrWhiteSpace(options.PipeName) ||
            string.IsNullOrWhiteSpace(options.TargetArtifactId) ||
            string.IsNullOrWhiteSpace(options.TargetArtifactHash))
            throw new InvalidOperationException("The test-only response-replay commit gate requires its named pipe and exact target artifact identity.");

        services.AddSingleton(options);
        services.AddSingleton<ResponseReplayCommitGateChannel>();
        services.AddSingleton<ResponseReplayCommitGateState>();
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        var registered = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore))
            ?? throw new InvalidOperationException("The response-replay commit gate could not find the registered checkpoint store.");
        services.Add(new ServiceDescriptor(
            typeof(IRuntimeCheckpointCommitStore),
            serviceProvider =>
            {
                var resolvedInner = ResponseReplayServiceDescriptorActivator.Instantiate(registered, serviceProvider);
                if (resolvedInner is not CoalescingRuntimeCheckpointCommitStore inner)
                    throw new InvalidOperationException($"The test gate expected the registered Coalesced store, found '{resolvedInner.GetType().FullName}'.");

                return new ResponseReplayCommitGateStore(
                    inner,
                    serviceProvider.GetRequiredService<IRuntimeCoalescingSessionAccessor>(),
                    serviceProvider.GetRequiredService<ResponseReplayCommitGateChannel>(),
                    serviceProvider.GetRequiredService<ResponseReplayCommitGateOptions>(),
                    serviceProvider.GetRequiredService<ResponseReplayCommitGateState>());
            },
            registered.Lifetime));
    }

}
