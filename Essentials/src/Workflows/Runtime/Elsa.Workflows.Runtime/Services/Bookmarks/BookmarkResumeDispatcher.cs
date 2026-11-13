using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.History;
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
    private IServiceProvider? _historyServices;
    private IRuntimeHistoryObservationWriter? _history;
    private IRuntimeHistoryStore? _historyReader;
    private IPersistenceAccessContextAccessor? _historyAccess;

    internal static BookmarkResumeDispatcher Create(IServiceProvider provider) => new(
        provider.GetRequiredService<IBookmarkStimulusLookup>(), provider.GetRequiredService<IWorkflowExecutionStateStore>(),
        provider.GetRequiredService<IWorkflowExecutableStore>(), provider.GetRequiredService<IBookmarkResumeResolver>(),
        provider.GetRequiredService<IWorkflowExecutionActorProvider>(), provider.GetRequiredService<IRuntimeExecutionIdGenerator>(),
        provider.GetRequiredService<TimeProvider>())
        { _delivery = provider.GetRequiredService<StimulusDeliveryServices>(),
          _historyServices = provider.GetService<RuntimeHistoryOptions>() is null ? null : provider };

    bool IBookmarkResumeDispatcher.CanRecordHistory(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader)
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
        { throw new StimulusDeliveryBoundaryException(source.SourceOutboxItemId, exception, CauseUnavailable(exception)); }
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
        cancellationToken.ThrowIfCancellationRequested();
        BookmarkResumeDispatchResult Unmatched() => new(BookmarkResumeDispatchStatus.NotFound, request.WorkflowExecutionId,
            reason: "The exact saved wait is retired or no longer eligible.");
        var source = saved?.Source ?? unmatched?.Source;
        var target = saved?.Expected ?? unmatched?.Expected ?? request.ExpectedRecipient;
        var receivedAt = _timeProvider.GetUtcNow();
        var owner = saved?.Workflow ?? await _workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
        var policy = owner?.HistoryCapturePolicy;
        if (_historyServices is null && _history is null && _historyReader is null)
        {
            // Module absence or an original constructor cannot waive an already pinned delivery obligation.
            if (policy?.SupportedKinds.Contains("DeliveryReceived", StringComparer.Ordinal) == true)
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The retained delivery profile requires its observation owner.");
            return unmatched is null ? await DispatchCoreAsync(request, options, saved, owner, cancellationToken, request.HistoryCausation) : Unmatched();
        }
        string? receiptId = null, receiptEventId = null;
        RuntimeHistoryCausation? cause = null;
        if (policy?.SupportedKinds.Contains("DeliveryReceived", StringComparer.Ordinal) == true)
        {
            ResolveHistory();
            if (_history is null || _historyReader is null || _historyAccess is null || !_history.CanRecordHistory(_historyReader))
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Delivery history requires its scoped owner provider.");
            _historyAccess.Current.RequireScope(); _historyAccess.Current.EnsureTenantScope(owner!.TenantId);
            receiptId = Guid.NewGuid().ToString("N");
            var logicalDelivery = source?.SourceOutboxItemId ?? (request.IdempotencyKey is { } key
                ? RuntimeHistoryIdentity.CreateBinding([request.WorkflowExecutionId, key]) : receiptId);
            cause = source is null ? request.HistoryCausation ?? new RuntimeHistoryCausation() : source.Intent.HistoryCausation ??
                new RuntimeHistoryCausation(WorkflowExecutionId: source.Intent.WorkflowExecutionId, DeliveryId: source.SourceOutboxItemId);
            cause = cause with { DeliveryId = cause.DeliveryId ?? logicalDelivery };
            var receipt = RuntimeHistoryDeliveryObservations.Create(policy, request.WorkflowExecutionId, receiptId, "DeliveryReceived", receivedAt,
                request.StimulusType, target?.BookmarkId ?? request.BookmarkId, target?.ActivityExecutionId, target?.ExecutableNodeId, cause);
            if (!(await RecordHistoryAsync(receipt, cancellationToken)).Recorded)
                throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Delivery history is waiting for its retained execution head.");
            receiptEventId = receipt.Event.HistoryEventId;
        }
        var queuedCause = receiptEventId is null ? request.HistoryCausation :
            new RuntimeHistoryCausation(HistoryEventId: receiptEventId, WorkflowExecutionId: request.WorkflowExecutionId, DeliveryId: cause!.DeliveryId);
        BookmarkResumeDispatchResult result;
        try { result = unmatched is null ? await DispatchCoreAsync(request, options, saved, owner, cancellationToken, queuedCause) : Unmatched(); }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception primary)
        {
            if (receiptId is not null && policy!.SupportedKinds.Contains("DeliveryFaulted", StringComparer.Ordinal))
            {
                var primaryUnavailable = CauseUnavailable(primary);
                try
                {
                    var failure = RuntimeHistoryDeliveryObservations.Create(policy, request.WorkflowExecutionId, receiptId, "DeliveryFaulted",
                        _timeProvider.GetUtcNow(), request.StimulusType, target?.BookmarkId ?? request.BookmarkId, target?.ActivityExecutionId,
                        target?.ExecutableNodeId, cause! with { HistoryEventId = receiptEventId, WorkflowExecutionId = request.WorkflowExecutionId },
                        primary is RuntimeSchedulerWorkUnavailableException ? "runtime-unavailable" : "dispatch-threw");
                    if (!(await RecordHistoryAsync(failure, cancellationToken)).Recorded)
                        throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Delivery failure history lost its retained owner.");
                }
                catch (Exception observationFailure)
                {
                    if (source is not null)
                        throw new StimulusDeliveryBoundaryException(source.SourceOutboxItemId,
                            new AggregateException("Delivery and its failure observation both failed.", primary, observationFailure),
                            primaryUnavailable && CauseUnavailable(observationFailure));
                    PreserveDirectObservationFailure(request, primary, observationFailure);
                }
            }
            throw;
        }
        if (receiptId is null) return result;
        var name = result.Status switch
        {
            BookmarkResumeDispatchStatus.Dispatched => "DeliveryDispatched",
            BookmarkResumeDispatchStatus.Duplicate => "DeliveryDuplicate",
            BookmarkResumeDispatchStatus.NotFound => "DeliveryUnmatched",
            BookmarkResumeDispatchStatus.Deferred => "DeliveryDeferred",
            _ => "DeliveryRejected"
        };
        if (!policy!.SupportedKinds.Contains(name, StringComparer.Ordinal)) return result;
        var reasonCode = result.CommandDispatch?.Status == WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted
            ? "accepted-but-faulted" : result.Status.ToString();
        var disposition = RuntimeHistoryDeliveryObservations.Create(policy, request.WorkflowExecutionId, receiptId, name, _timeProvider.GetUtcNow(),
            request.StimulusType, result.Bookmark?.BookmarkId ?? target?.BookmarkId ?? request.BookmarkId,
            result.Bookmark?.ActivityExecutionId ?? target?.ActivityExecutionId, result.Bookmark?.ExecutableNodeId ?? target?.ExecutableNodeId,
            cause! with { HistoryEventId = receiptEventId, WorkflowExecutionId = request.WorkflowExecutionId }, reasonCode);
        if (!(await RecordHistoryAsync(disposition, cancellationToken)).Recorded)
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "Delivery disposition history lost its retained owner.");
        return result;
    }

    private static bool CauseUnavailable(Exception failure) => failure switch
    {
        RuntimeHistoryIntegrityException => false,
        RuntimeHistoryUnavailableException => true,
        RuntimePostCommitSourceBoundaryException boundary => boundary.IsUnavailable,
        RuntimePostCommitDeliveryException delivery => delivery.Kind == PostCommitFailureKind.Transient,
        _ => StimulusDeliveryFailures.IsUnavailable(failure)
    };

    private async ValueTask<RuntimeHistoryObservationResult> RecordHistoryAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken)
    {
        try { return await _history!.RecordAsync(observation, cancellationToken); }
        catch (Exception failure) when ((failure is IOException or TimeoutException) && !RuntimeHistoryReadFailures.IsMalformed(failure))
        { throw new RuntimeHistoryUnavailableException(observation.Event.WorkflowExecutionId, "record-delivery-observation", failure); }
    }

    private static void PreserveDirectObservationFailure(BookmarkResumeDispatchRequest request, Exception primary, Exception observationFailure)
    {
        try
        {
            var data = primary.Data;
            lock (data.SyncRoot)
            {
                var previous = data[RuntimeHistoryDeliveryObservations.FailureDataKey];
                var entries = previous is IReadOnlyList<object> list ? list.ToList() : previous is null ? [] : new List<object> { previous };
                entries.Add(new KeyValuePair<WeakReference<object>, Exception>(new(request), observationFailure));
                data[RuntimeHistoryDeliveryObservations.FailureDataKey] = Array.AsReadOnly(entries.ToArray());
            }
        }
        catch (Exception diagnosticFailure) when (diagnosticFailure is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            // Supplementary diagnostics cannot replace the original dispatch failure or grant source authority.
        }
    }

    private async ValueTask<BookmarkResumeDispatchResult> DispatchCoreAsync(BookmarkResumeDispatchRequest request,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions, SavedBookmarkResume? saved, WorkflowExecutionState? observedOwner, CancellationToken cancellationToken,
        RuntimeHistoryCausation? historyCause = null)
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
        var workflowExecution = saved?.Workflow ?? observedOwner;
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
            testRunDelivery: request.TestRunDelivery)
            { ExpectedBookmarkResume = saved?.Expected ?? request.ExpectedRecipient, HistoryCausation = historyCause });
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
