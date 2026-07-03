using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class BookmarkResumeDispatcher : IBookmarkResumeDispatcher
{
    private readonly IBookmarkStimulusLookup _bookmarkStimulusLookup;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IBookmarkResumeResolver _bookmarkResumeResolver;
    private readonly IWorkflowExecutionAgentProvider _agentProvider;
    private readonly IRuntimeExecutionIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;

    public BookmarkResumeDispatcher(
        IBookmarkStimulusLookup bookmarkStimulusLookup,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IWorkflowExecutableStore workflowExecutableStore,
        IBookmarkResumeResolver bookmarkResumeResolver,
        IWorkflowExecutionAgentProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator)
        : this(bookmarkStimulusLookup, workflowExecutionStateStore, workflowExecutableStore, bookmarkResumeResolver, agentProvider, idGenerator, TimeProvider.System)
    {
    }

    public BookmarkResumeDispatcher(
        IBookmarkStimulusLookup bookmarkStimulusLookup,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IWorkflowExecutableStore workflowExecutableStore,
        IBookmarkResumeResolver bookmarkResumeResolver,
        IWorkflowExecutionAgentProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(bookmarkStimulusLookup);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(workflowExecutableStore);
        ArgumentNullException.ThrowIfNull(bookmarkResumeResolver);
        ArgumentNullException.ThrowIfNull(agentProvider);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _bookmarkStimulusLookup = bookmarkStimulusLookup;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _workflowExecutableStore = workflowExecutableStore;
        _bookmarkResumeResolver = bookmarkResumeResolver;
        _agentProvider = agentProvider;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
    }

    public async ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _timeProvider.GetUtcNow();
        var lookup = await _bookmarkStimulusLookup.FindAsync(new BookmarkStimulusLookupRequest(
            request.WorkflowExecutionId,
            request.StimulusType,
            request.StimulusHash,
            now), cancellationToken);

        if (lookup.Status != BookmarkStimulusLookupStatus.Found)
            return new BookmarkResumeDispatchResult(
                lookup.Status == BookmarkStimulusLookupStatus.Ambiguous ? BookmarkResumeDispatchStatus.Ambiguous : BookmarkResumeDispatchStatus.NotFound,
                request.WorkflowExecutionId,
                reason: lookup.Reason,
                lookup: lookup);

        var bookmark = lookup.Bookmark!;
        var workflowExecution = await _workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (workflowExecution is null)
            return new BookmarkResumeDispatchResult(
                BookmarkResumeDispatchStatus.WorkflowExecutionMissing,
                request.WorkflowExecutionId,
                bookmark: bookmark,
                reason: $"Workflow execution state '{request.WorkflowExecutionId}' was not found.",
                lookup: lookup);

        var executable = await _workflowExecutableStore.FindAsync(workflowExecution.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            return new BookmarkResumeDispatchResult(
                BookmarkResumeDispatchStatus.ExecutableMissing,
                request.WorkflowExecutionId,
                bookmark: bookmark,
                reason: $"Pinned executable artifact '{workflowExecution.PinnedExecutable.ArtifactId}' was not found.",
                lookup: lookup);

        BookmarkResumeResolution resolution;
        try
        {
            resolution = _bookmarkResumeResolver.Resolve(new BookmarkResumeRequest(workflowExecution, executable, bookmark, request.Input));
        }
        catch (BookmarkResumeResolutionException exception)
        {
            return new BookmarkResumeDispatchResult(
                BookmarkResumeDispatchStatus.ResumeResolutionFailed,
                request.WorkflowExecutionId,
                bookmark: bookmark,
                reason: exception.Message,
                lookup: lookup);
        }

        var metadata = CreateMetadata(request, bookmark, workflowExecution.PinnedExecutable);
        var payload = JsonSerializer.SerializeToElement(new RuntimeResumeBookmarkCommandPayload(
            pinnedExecutable: workflowExecution.PinnedExecutable,
            bookmarkId: bookmark.BookmarkId,
            activityExecutionId: bookmark.ActivityExecutionId,
            executableNodeId: bookmark.ExecutableNodeId,
            resumeTargetId: bookmark.ResumeTargetId,
            stimulusType: request.StimulusType,
            stimulusHash: request.StimulusHash,
            input: request.Input,
            reason: RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason));
        var command = new WorkflowExecutionCommand(
            CommandId: _idGenerator.NewWorkflowExecutionCommandId(),
            WorkflowExecutionId: request.WorkflowExecutionId,
            Kind: WorkflowExecutionCommandKind.ResumeBookmark,
            EnqueuedAt: now,
            Payload: payload.Clone(),
            Metadata: metadata);
        var envelope = new WorkflowExecutionCommandEnvelope(
            envelopeId: _idGenerator.NewWorkflowExecutionCommandEnvelopeId(),
            workflowExecutionId: request.WorkflowExecutionId,
            command: command,
            idempotencyKey: CreateIdempotencyKey(request, bookmark),
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now,
            metadata: metadata);
        var activationRequest = new WorkflowExecutionAgentActivationRequest(
            workflowExecutionId: request.WorkflowExecutionId,
            reason: WorkflowExecutionAgentActivationReason.ResumeBookmark,
            requestedAt: now,
            requestedBy: request.RequestedBy,
            requiredCapabilities: WorkflowExecutionAgentCapabilities.None,
            metadata: metadata);

        var agent = await _agentProvider.GetAgentAsync(activationRequest, cancellationToken);
        var commandDispatch = await agent.EnqueueAsync(envelope, cancellationToken);
        var status = MapDispatchStatus(commandDispatch.Status);

        return new BookmarkResumeDispatchResult(
            status,
            request.WorkflowExecutionId,
            bookmark: bookmark,
            resolution: resolution,
            commandDispatch: commandDispatch,
            agent: agent.Descriptor,
            reason: status == BookmarkResumeDispatchStatus.Dispatched ? null : commandDispatch.Reason ?? $"Workflow execution agent returned dispatch status '{commandDispatch.Status}'.",
            lookup: lookup);
    }

    private static IReadOnlyDictionary<string, string> CreateMetadata(
        BookmarkResumeDispatchRequest request,
        BookmarkState bookmark,
        WorkflowExecutableIdentity pinnedExecutable)
    {
        var metadata = request.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["runtime.dispatcher"] = nameof(BookmarkResumeDispatcher);
        metadata["runtime.bookmarkId"] = bookmark.BookmarkId;
        metadata["runtime.activityExecutionId"] = bookmark.ActivityExecutionId;
        metadata["runtime.executableNodeId"] = bookmark.ExecutableNodeId;
        metadata["runtime.resumeTargetId"] = bookmark.ResumeTargetId;
        metadata["runtime.stimulusType"] = request.StimulusType;
        metadata["runtime.stimulusHash"] = request.StimulusHash;
        metadata["runtime.artifactId"] = pinnedExecutable.ArtifactId;
        metadata["runtime.artifactVersion"] = pinnedExecutable.ArtifactVersion;
        metadata["runtime.artifactHash"] = pinnedExecutable.ArtifactHash;
        return RuntimeModelMetadata.Snapshot(metadata);
    }

    private static string CreateIdempotencyKey(BookmarkResumeDispatchRequest request, BookmarkState bookmark) =>
        request.IdempotencyKey is not null
            ? $"{request.WorkflowExecutionId}:bookmark-resume:{bookmark.BookmarkId}:{request.StimulusType}:{request.StimulusHash}:{request.IdempotencyKey}"
            : $"{request.WorkflowExecutionId}:bookmark-resume:{bookmark.BookmarkId}:{request.StimulusType}:{request.StimulusHash}";

    private static BookmarkResumeDispatchStatus MapDispatchStatus(WorkflowExecutionCommandDispatchStatus status) =>
        status switch
        {
            WorkflowExecutionCommandDispatchStatus.Accepted => BookmarkResumeDispatchStatus.Dispatched,
            WorkflowExecutionCommandDispatchStatus.Duplicate => BookmarkResumeDispatchStatus.Duplicate,
            WorkflowExecutionCommandDispatchStatus.Rejected => BookmarkResumeDispatchStatus.Rejected,
            WorkflowExecutionCommandDispatchStatus.Deferred => BookmarkResumeDispatchStatus.Deferred,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown workflow execution command dispatch status.")
        };
}
