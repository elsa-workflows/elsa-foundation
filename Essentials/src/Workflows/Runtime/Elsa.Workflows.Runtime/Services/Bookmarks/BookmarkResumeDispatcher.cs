using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Bookmarks;

public sealed class BookmarkResumeDispatcher : IBookmarkResumeDispatcher
{
    private readonly IBookmarkStimulusLookup _bookmarkStimulusLookup;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IBookmarkResumeResolver _bookmarkResumeResolver;
    private readonly IWorkflowExecutionActorProvider _agentProvider;
    private readonly IRuntimeExecutionIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;
    private StimulusDeliveryServices? _delivery;

    internal static BookmarkResumeDispatcher Create(IServiceProvider provider) => new(
        provider.GetRequiredService<IBookmarkStimulusLookup>(), provider.GetRequiredService<IWorkflowExecutionStateStore>(),
        provider.GetRequiredService<IWorkflowExecutableStore>(), provider.GetRequiredService<IBookmarkResumeResolver>(),
        provider.GetRequiredService<IWorkflowExecutionActorProvider>(), provider.GetRequiredService<IRuntimeExecutionIdGenerator>(),
        provider.GetRequiredService<TimeProvider>()) { _delivery = provider.GetRequiredService<StimulusDeliveryServices>() };

    bool IBookmarkResumeDispatcher.CanDispatchSavedStimulus(IStimulusDeliveryStore sources, IWorkflowExecutableStore material,
        IWorkflowExecutionStateStore executions) => _delivery is { } delivery && ReferenceEquals(sources, delivery.Sources) &&
        ReferenceEquals(material, delivery.Material) && ReferenceEquals(material, _workflowExecutableStore) &&
        ReferenceEquals(executions, delivery.Executions) && ReferenceEquals(executions, _workflowExecutionStateStore);

    async ValueTask<BookmarkResumeDispatchResult> IBookmarkResumeDispatcher.DispatchAsync(PostCommitDeliveryContext source, string workflowExecutionId,
        string bookmarkId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId); cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var delivery = _delivery ?? throw new NotSupportedException("This dispatcher has no canonical source support.");
            var prepared = await delivery.PrepareResumeAsync(source, workflowExecutionId, bookmarkId, cancellationToken);
            return prepared is null ? new(BookmarkResumeDispatchStatus.NotFound, workflowExecutionId, reason: "The exact saved wait is retired or no longer eligible.") :
                await DispatchCoreAsync(prepared.Request, null, prepared, cancellationToken);
        }
        catch (StimulusDeliveryBoundaryException) { throw; }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        { throw new StimulusDeliveryBoundaryException(source.SourceOutboxItemId, exception, StimulusDeliveryFailures.IsUnavailable(exception)); }
    }

    public BookmarkResumeDispatcher(
        IBookmarkStimulusLookup bookmarkStimulusLookup,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IWorkflowExecutableStore workflowExecutableStore,
        IBookmarkResumeResolver bookmarkResumeResolver,
        IWorkflowExecutionActorProvider agentProvider,
        IRuntimeExecutionIdGenerator idGenerator)
        : this(bookmarkStimulusLookup, workflowExecutionStateStore, workflowExecutableStore, bookmarkResumeResolver, agentProvider, idGenerator, TimeProvider.System)
    {
    }

    public BookmarkResumeDispatcher(
        IBookmarkStimulusLookup bookmarkStimulusLookup,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IWorkflowExecutableStore workflowExecutableStore,
        IBookmarkResumeResolver bookmarkResumeResolver,
        IWorkflowExecutionActorProvider agentProvider,
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

    public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
        BookmarkResumeDispatchRequest request,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
        CancellationToken cancellationToken = default) => DispatchCoreAsync(request, dispatchOptions, null, cancellationToken);

    private async ValueTask<BookmarkResumeDispatchResult> DispatchCoreAsync(BookmarkResumeDispatchRequest request,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions, SavedBookmarkResume? saved, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _timeProvider.GetUtcNow();
        var lookup = saved is not null ? new BookmarkStimulusLookupResult(BookmarkStimulusLookupStatus.Found, saved.Bookmark) :
            await _bookmarkStimulusLookup.FindAsync(new BookmarkStimulusLookupRequest(
            request.WorkflowExecutionId,
            request.StimulusType,
            request.StimulusHash,
            now, request.BookmarkId), cancellationToken);

        if (lookup.Status != BookmarkStimulusLookupStatus.Found)
            return new BookmarkResumeDispatchResult(
                lookup.Status == BookmarkStimulusLookupStatus.Ambiguous ? BookmarkResumeDispatchStatus.Ambiguous : BookmarkResumeDispatchStatus.NotFound,
                request.WorkflowExecutionId,
                reason: lookup.Reason,
                lookup: lookup);

        var bookmark = lookup.Bookmark!;
        var workflowExecution = saved?.Workflow ?? await _workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
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

        if (request.TestRunDelivery is { } receipt &&
            (workflowExecution.TestScope?.ScopeId != receipt.TestRunId || workflowExecution.RunKind != WorkflowRunKind.TestRun ||
             receipt.WaitId != bookmark.BookmarkId || receipt.StimulusDigest != TestRunDeliveryReceipt.Digest(request.Input)))
            return new BookmarkResumeDispatchResult(BookmarkResumeDispatchStatus.Rejected, request.WorkflowExecutionId,
                reason: "Test delivery authority does not match the selected wait.");

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
        var partition = workflowExecution.Partition
            ?? new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue);
        var commandId = _idGenerator.NewWorkflowExecutionCommandId();
        var idempotencyKey = CreateIdempotencyKey(request, bookmark);
        var triggerDelivery = request.PayloadType is null
            ? null
            : new RuntimeTypedTriggerDeliveryMetadata(
                deliveryId: commandId,
                payloadType: request.PayloadType,
                providerId: request.ProviderId!,
                receivedAt: now,
                deduplicationKey: idempotencyKey);
        var payload = JsonSerializer.SerializeToElement(new RuntimeResumeBookmarkCommandPayload(
            pinnedExecutable: workflowExecution.PinnedExecutable,
            bookmarkId: bookmark.BookmarkId,
            activityExecutionId: bookmark.ActivityExecutionId,
            executableNodeId: bookmark.ExecutableNodeId,
            resumeTargetId: bookmark.ResumeTargetId,
            stimulusType: request.StimulusType,
            stimulusHash: request.StimulusHash,
            input: request.Input,
            reason: RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason,
            triggerDelivery: triggerDelivery,
            testRunDelivery: request.TestRunDelivery) { ExpectedBookmarkResume = saved?.Expected });
        var command = new WorkflowExecutionCommand(
            CommandId: commandId,
            WorkflowExecutionId: request.WorkflowExecutionId,
            Kind: WorkflowExecutionCommandKind.ResumeBookmark,
            EnqueuedAt: now,
            Payload: payload.Clone(),
            Metadata: metadata);
        var envelope = new WorkflowExecutionCommandEnvelope(
            envelopeId: _idGenerator.NewWorkflowExecutionCommandEnvelopeId(),
            workflowExecutionId: request.WorkflowExecutionId,
            command: command,
            idempotencyKey: idempotencyKey,
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now,
            metadata: metadata,
            partition: partition);
        var activationRequest = new WorkflowExecutionActorActivationRequest(
            workflowExecutionId: request.WorkflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.ResumeBookmark,
            requestedAt: now,
            requestedBy: request.RequestedBy,
            requiredCapabilities: WorkflowExecutionActorCapabilities.None,
            metadata: metadata,
            partition: partition);

        var agent = await _agentProvider.GetAgentAsync(activationRequest, cancellationToken);
        if (saved is not null && await _delivery!.PrepareResumeAsync(saved.Source, request.WorkflowExecutionId, request.BookmarkId!, cancellationToken) is null)
            return new(BookmarkResumeDispatchStatus.NotFound, request.WorkflowExecutionId, reason: "The exact saved wait retired before enqueue.");
        var commandDispatch = await agent.EnqueueAsync(envelope, dispatchOptions ?? WorkflowExecutionCommandDispatchOptions.Default, cancellationToken);
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
        request.TestRunDelivery is { } receipt
            ? $"{request.WorkflowExecutionId}:test-drive:{receipt.DeliveryId}"
            : request.IdempotencyKey is not null
            ? $"{request.WorkflowExecutionId}:bookmark-resume:{bookmark.BookmarkId}:{request.StimulusType}:{request.StimulusHash}:{request.IdempotencyKey}"
            : $"{request.WorkflowExecutionId}:bookmark-resume:{bookmark.BookmarkId}:{request.StimulusType}:{request.StimulusHash}";

    private static BookmarkResumeDispatchStatus MapDispatchStatus(WorkflowExecutionCommandDispatchStatus status) =>
        status switch
        {
            WorkflowExecutionCommandDispatchStatus.Accepted => BookmarkResumeDispatchStatus.Dispatched,
            // The resume was dispatched successfully; the workflow faulted during the ensuing drain. The fault is
            // authoritatively surfaced on the dispatch result (status + reason) and via workflow status queries. A
            // dedicated faulted bookmark-resume status is a follow-up; treat as dispatched here.
            WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted => BookmarkResumeDispatchStatus.Dispatched,
            WorkflowExecutionCommandDispatchStatus.Duplicate => BookmarkResumeDispatchStatus.Duplicate,
            WorkflowExecutionCommandDispatchStatus.Rejected => BookmarkResumeDispatchStatus.Rejected,
            WorkflowExecutionCommandDispatchStatus.Deferred => BookmarkResumeDispatchStatus.Deferred,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown workflow execution command dispatch status.")
        };
}
