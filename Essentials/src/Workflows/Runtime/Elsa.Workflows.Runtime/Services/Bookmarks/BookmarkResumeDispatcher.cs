using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Bookmarks;

public sealed class BookmarkResumeDispatcher : IBookmarkResumeDispatcher, IRuntimeHistoryDeliverySource
{
    private readonly IBookmarkStimulusLookup _bookmarkStimulusLookup;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IBookmarkResumeResolver _bookmarkResumeResolver;
    private readonly IWorkflowExecutionActorProvider _agentProvider;
    private readonly IRuntimeExecutionIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;
    private StimulusDeliveryServices? _delivery;
    private IRuntimeHistoryObservationWriter? _history;
    private IRuntimeHistoryStore? _historyReader;
    private IPersistenceAccessContextAccessor? _historyAccess;
    private IServiceProvider? _historyServices;

    internal static BookmarkResumeDispatcher Create(IServiceProvider provider) => new(
        provider.GetRequiredService<IBookmarkStimulusLookup>(), provider.GetRequiredService<IWorkflowExecutionStateStore>(),
        provider.GetRequiredService<IWorkflowExecutableStore>(), provider.GetRequiredService<IBookmarkResumeResolver>(),
        provider.GetRequiredService<IWorkflowExecutionActorProvider>(), provider.GetRequiredService<IRuntimeExecutionIdGenerator>(),
        provider.GetRequiredService<TimeProvider>())
        { _delivery = provider.GetRequiredService<StimulusDeliveryServices>(), _historyServices = provider };

    bool IRuntimeHistoryDeliverySource.CanRecordHistory(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader)
    {
        ResolveHistory();
        return ReferenceEquals(writer, _history) && ReferenceEquals(reader, _historyReader) && writer.CanRecordHistory(reader);
    }

    private void ResolveHistory()
    {
        _history ??= _historyServices?.GetService<IRuntimeHistoryObservationWriter>();
        _historyReader ??= _historyServices?.GetService<IRuntimeHistoryStore>();
        _historyAccess ??= _historyServices?.GetService<IPersistenceAccessContextAccessor>();
    }

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
            return await DispatchObservedAsync(prepared.Request, null, prepared.Eligible, cancellationToken,
                prepared.Eligible is null ? prepared : null);
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
        CancellationToken cancellationToken = default) => DispatchObservedAsync(request, dispatchOptions, null, cancellationToken);

    private async ValueTask<BookmarkResumeDispatchResult> DispatchObservedAsync(BookmarkResumeDispatchRequest request,
        WorkflowExecutionCommandDispatchOptions? options, SavedBookmarkResume? saved, CancellationToken cancellationToken,
        SavedBookmarkResumePreparation? unmatched = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        BookmarkResumeDispatchResult Unmatched() => new(BookmarkResumeDispatchStatus.NotFound, request.WorkflowExecutionId,
            reason: "The exact saved wait is retired or no longer eligible.");
        if (_historyServices is null && _history is null && _historyReader is null)
            return unmatched is null ? await DispatchCoreAsync(request, options, saved, cancellationToken) : Unmatched();
        var source = saved?.Source ?? unmatched?.Source;
        var target = saved?.Expected ?? unmatched?.Expected;
        var receivedAt = _timeProvider.GetUtcNow();
        var owner = await _workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
        var policy = owner?.HistoryCapturePolicy;
        string? receiptId = null;
        RuntimeHistoryCausation? cause = null;
        string? receiptEventId = null;
        if (policy?.SupportedKinds.Contains("DeliveryReceived", StringComparer.Ordinal) == true)
        {
            ResolveHistory();
            if (_history is null || _historyReader is null || !_history.CanRecordHistory(_historyReader) || _historyAccess is null)
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Delivery history requires its scoped owner provider.");
            _historyAccess.Current.RequireScope(); _historyAccess.Current.EnsureTenantScope(owner!.TenantId);
            receiptId = Guid.NewGuid().ToString("N");
            var logicalDelivery = source?.SourceOutboxItemId ?? (request.IdempotencyKey is { } key
                ? RuntimeHistoryIdentity.CreateBinding([request.WorkflowExecutionId, key]) : receiptId);
            cause = source?.Intent.HistoryCausation ?? request.HistoryCausation ?? new RuntimeHistoryCausation(WorkflowExecutionId: source?.Intent.WorkflowExecutionId,
                DeliveryId: logicalDelivery);
            var receipt = RuntimeHistoryDeliveryFacts.Create(policy, request.WorkflowExecutionId, receiptId, "DeliveryReceived", receivedAt,
                request.StimulusType, target?.BookmarkId, target?.ActivityExecutionId, target?.ExecutableNodeId, cause);
            if (!(await RecordHistoryAsync(receipt, cancellationToken)).Recorded)
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Delivery history is waiting for its retained execution head.");
            receiptEventId = receipt.Event.HistoryEventId;
        }
        BookmarkResumeDispatchResult result;
        var queuedCause = receiptEventId is null ? null : new RuntimeHistoryCausation(HistoryEventId: receiptEventId,
            WorkflowExecutionId: request.WorkflowExecutionId, DeliveryId: cause?.DeliveryId);
        try { result = unmatched is null ? await DispatchCoreAsync(request, options, saved, cancellationToken, queuedCause) : Unmatched(); }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (receiptId is not null)
            {
                var primaryUnavailable = CompoundCauseUnavailable(exception);
                try
                {
                    await RecordHistoryAsync(RuntimeHistoryDeliveryFacts.Create(policy!, request.WorkflowExecutionId, receiptId, "DeliveryFaulted",
                        _timeProvider.GetUtcNow(), request.StimulusType, target?.BookmarkId, target?.ActivityExecutionId, target?.ExecutableNodeId,
                        cause: cause! with { HistoryEventId = receiptEventId, WorkflowExecutionId = request.WorkflowExecutionId },
                        reasonCode: exception is RuntimeSchedulerWorkUnavailableException ? "runtime-unavailable" : "dispatch-threw"), cancellationToken);
                }
                catch (Exception observationFailure)
                {
                    if (source is not null)
                        throw new StimulusDeliveryBoundaryException(source.SourceOutboxItemId,
                            new AggregateException("Delivery and its failure observation both failed.", exception, observationFailure),
                            primaryUnavailable && CompoundCauseUnavailable(observationFailure));
                    PreserveDirectObservationFailure(request, exception, observationFailure);
                }
            }
            throw;
        }
        if (receiptId is null) return result;
        var name = result.Status switch
        {
            BookmarkResumeDispatchStatus.Dispatched => "DeliveryDispatched",
            BookmarkResumeDispatchStatus.Duplicate => "DeliveryDuplicate",
            BookmarkResumeDispatchStatus.Deferred => "DeliveryDeferred",
            BookmarkResumeDispatchStatus.NotFound => "DeliveryUnmatched",
            _ => "DeliveryRejected"
        };
        var responseCause = cause! with { HistoryEventId = receiptEventId, WorkflowExecutionId = request.WorkflowExecutionId };
        var disposition = RuntimeHistoryDeliveryFacts.Create(policy!, request.WorkflowExecutionId, receiptId, name, _timeProvider.GetUtcNow(),
            request.StimulusType, result.Bookmark?.BookmarkId ?? target?.BookmarkId,
            result.Bookmark?.ActivityExecutionId ?? target?.ActivityExecutionId,
            result.Bookmark?.ExecutableNodeId ?? target?.ExecutableNodeId,
            responseCause, result.CommandDispatch?.Status.ToString() ?? result.Status.ToString());
        var recorded = await RecordHistoryAsync(disposition, cancellationToken);
        if (!recorded.Recorded) return result;
        return new BookmarkResumeDispatchResult(result.Status, result.WorkflowExecutionId, result.Bookmark, result.Resolution,
            result.CommandDispatch, result.Agent, result.Reason, result.Lookup)
        { HistoryCausation = responseCause with { HistoryEventId = disposition.Event.HistoryEventId } };
    }

    private static void PreserveDirectObservationFailure(BookmarkResumeDispatchRequest request, Exception primary, Exception secondary)
    {
        AppendSupplementaryDiagnostic(primary, new KeyValuePair<WeakReference<object>, Exception>(new(request), secondary));
        try
        {
            System.Diagnostics.Trace.TraceWarning("A delivery failure observation also failed; its unchanged primary exception retains the supplementary cause.");
        }
        catch (Exception diagnosticFailure) when (diagnosticFailure is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // A host diagnostic listener is supplementary too; it cannot replace either delivery cause.
            AppendSupplementaryDiagnostic(primary, diagnosticFailure);
        }
    }

    private static void AppendSupplementaryDiagnostic(Exception primary, object diagnostic)
    {
        var data = primary.Data;
        lock (data.SyncRoot)
        {
            var old = data[RuntimeHistoryDeliveryFacts.ObservationFailureDataKey];
            var entries = old is IReadOnlyList<object> retained ? retained.ToList() : old is null ? [] : new List<object> { old };
            entries.Add(diagnostic);
            data[RuntimeHistoryDeliveryFacts.ObservationFailureDataKey] = Array.AsReadOnly(entries.ToArray());
        }
    }

    private static bool CompoundCauseUnavailable(Exception failure) => failure switch
    {
        RuntimePostCommitSourceBoundaryException source => source.IsUnavailable,
        RuntimePostCommitDeliveryException delivery => delivery.Kind == PostCommitFailureKind.Transient,
        _ => StimulusDeliveryFailures.IsUnavailable(failure)
    };

    private async ValueTask<RuntimeHistoryObservationResult> RecordHistoryAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken)
    {
        try { return await _history!.RecordAsync(observation, cancellationToken); }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        { throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Delivery history could not acknowledge its observation.", exception); }
    }

    private async ValueTask<BookmarkResumeDispatchResult> DispatchCoreAsync(BookmarkResumeDispatchRequest request,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions, SavedBookmarkResume? saved, CancellationToken cancellationToken, RuntimeHistoryCausation? historyCause = null)
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
            testRunDelivery: request.TestRunDelivery) { ExpectedBookmarkResume = saved?.Expected ?? request.ExpectedRecipient, HistoryCausation = historyCause });
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
        if (saved is not null && (await _delivery!.PrepareResumeAsync(saved.Source, request.WorkflowExecutionId, request.BookmarkId!, cancellationToken)).Eligible is null)
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
