using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Delivers one committed successful-child signal through the ordinary bookmark resume path.</summary>
public sealed class ParentResumeExecutor(
    IBookmarkResumeDispatcher bookmarkResumeDispatcher,
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionStateStore activityExecutionStateStore,
    IBookmarkStateStore bookmarkStateStore,
    ILogger<ParentResumeExecutor>? logger = null) : IRuntimePostCommitDurableSourceIntentHandler, IRuntimeHistoryDeliverySource
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger<ParentResumeExecutor> _logger = logger ?? NullLogger<ParentResumeExecutor>.Instance;
    private IServiceProvider? _historyServices;
    private TimeProvider _historyClock = TimeProvider.System;

    internal static ParentResumeExecutor Create(IServiceProvider services) => new(
        services.GetRequiredService<IBookmarkResumeDispatcher>(), services.GetRequiredService<IWorkflowExecutionStateStore>(),
        services.GetRequiredService<IActivityExecutionStateStore>(), services.GetRequiredService<IBookmarkStateStore>(),
        services.GetService<ILogger<ParentResumeExecutor>>())
        { _historyServices = services, _historyClock = services.GetRequiredService<TimeProvider>() };

    bool IRuntimeHistoryDeliverySource.CanRecordHistory(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader) =>
        _historyServices is not null && ReferenceEquals(writer, _historyServices.GetService<IRuntimeHistoryObservationWriter>()) &&
        ReferenceEquals(reader, _historyServices.GetService<IRuntimeHistoryStore>()) &&
        ReferenceEquals(bookmarkResumeDispatcher, _historyServices.GetService<IBookmarkResumeDispatcher>()) &&
        bookmarkResumeDispatcher is IRuntimeHistoryDeliverySource source && source.CanRecordHistory(writer, reader);

    public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.HistoryCausation is not null) return ValueTask.FromException(new RuntimePostCommitSourceBoundaryException(null, intent.Kind,
            new InvalidOperationException("A history-stamped parent resume requires its verified durable source."), false));
        return HandleCoreAsync(intent, null, cancellationToken);
    }

    public async ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Intent.HistoryCausation is { } cause && cause.DeliveryId != source.SourceOutboxItemId)
            throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind,
                new InvalidOperationException("The parent resume differs from its exact child source."), false);
        source = await WorkflowDispatchHistorySource.ValidateAsync(source, _historyServices, _historyClock, cancellationToken);
        try { await HandleCoreAsync(source.Intent, source.Intent.HistoryCausation, cancellationToken); }
        catch (RuntimeSchedulerWorkUnavailableException exception) when (exception.Code == "runtime.history.capture-unavailable")
        { throw new RuntimePostCommitSourceBoundaryException(source.SourceOutboxItemId, source.Intent.Kind, exception, true); }
    }

    private async ValueTask HandleCoreAsync(RuntimePostCommitIntent intent, RuntimeHistoryCausation? sourceCause, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!StringComparer.Ordinal.Equals(intent.Kind, DispatchWorkflowConstants.ResumeParentIntentKind) ||
            intent.Payload is null)
        {
            throw new InvalidOperationException($"ParentResumeExecutor cannot handle post-commit intent '{intent.IntentId}'.");
        }

        var payload = intent.Payload.Value.Deserialize<WorkflowDispatchParentResumePayload>(SerializerOptions)
            ?? throw new InvalidOperationException($"DispatchWorkflow parent-resume intent '{intent.IntentId}' has an invalid payload.");
        var identity = new WorkflowDispatchIdentity(
            payload.ParentWorkflowExecutionId,
            payload.ParentActivityExecutionId);
        ValidateIntent(intent, payload, identity);
        if (sourceCause is not null && sourceCause.WorkflowExecutionId != payload.ChildWorkflowExecutionId)
            throw new InvalidOperationException("Parent resume history must retain its actual child origin.");

        var result = await bookmarkResumeDispatcher.DispatchAsync(
            new BookmarkResumeDispatchRequest(
                workflowExecutionId: payload.ParentWorkflowExecutionId,
                stimulusType: payload.StimulusType,
                stimulusHash: payload.StimulusHash,
                input: JsonSerializer.SerializeToElement(payload),
                idempotencyKey: identity.ParentResumeIdempotencyKey,
                requestedBy: DispatchWorkflowConstants.ParentResumeProviderId,
                metadata: new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.DispatchId] = payload.DispatchId,
                    [RuntimeMetadataKeys.ChildWorkflowExecutionId] = payload.ChildWorkflowExecutionId
                },
                payloadType: new ValueTypeDescriptor(
                    TypeAliasConvention.CanonicalAlias(typeof(WorkflowDispatchParentResumePayload)),
                    schemaVersion: 1),
                providerId: DispatchWorkflowConstants.ParentResumeProviderId) { HistoryCausation = sourceCause },
            cancellationToken: cancellationToken);

        if (result.Bookmark is { } resolvedBookmark)
            ValidateBookmark(resolvedBookmark, payload);

        if (await IsConsumedOrTerminalAsync(payload, cancellationToken))
        {
            if (payload.Result.Status == WorkflowDispatchStatus.DispatchFailed)
            {
                _logger.LogInformation(
                    new EventId(68108, "WorkflowDispatchFailureResumeConsumed"),
                    "Workflow dispatch failure resume consumed for {DispatchId}, parent {ParentWorkflowExecutionId}, activity {ParentActivityExecutionId}",
                    payload.DispatchId,
                    payload.ParentWorkflowExecutionId,
                    payload.ParentActivityExecutionId);
            }
            return;
        }

        throw new ParentResumeDeferredException(
            payload.DispatchId,
            payload.ParentWorkflowExecutionId);
    }

    private static void ValidateIntent(
        RuntimePostCommitIntent intent,
        WorkflowDispatchParentResumePayload payload,
        WorkflowDispatchIdentity identity)
    {
        if (!StringComparer.Ordinal.Equals(intent.IntentId, identity.ParentResumeIntentId) ||
            !StringComparer.Ordinal.Equals(intent.WorkflowExecutionId, payload.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.ActivityExecutionId, payload.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.IdempotencyKey, identity.ParentResumeIdempotencyKey))
        {
            throw new InvalidOperationException($"DispatchWorkflow parent-resume intent '{intent.IntentId}' does not match its deterministic dispatch identity.");
        }
    }

    private async ValueTask<bool> IsConsumedOrTerminalAsync(
        WorkflowDispatchParentResumePayload payload,
        CancellationToken cancellationToken)
    {
        var workflow = await workflowExecutionStateStore.FindAsync(
            payload.ParentWorkflowExecutionId,
            cancellationToken);
        if (workflow is null || workflow.Status.IsTerminal())
            return true;

        var activity = await activityExecutionStateStore.FindAsync(
            payload.ParentWorkflowExecutionId,
            payload.ParentActivityExecutionId,
            cancellationToken);
        var bookmark = await bookmarkStateStore.FindAsync(
            payload.ParentWorkflowExecutionId,
            payload.BookmarkId,
            cancellationToken);
        if (bookmark is not null)
        {
            ValidateBookmark(bookmark, payload);
            return false;
        }

        return activity?.Status is ActivityExecutionStatus.Completed
            or ActivityExecutionStatus.Faulted
            or ActivityExecutionStatus.Cancelled;
    }

    private static void ValidateBookmark(
        BookmarkState bookmark,
        WorkflowDispatchParentResumePayload payload)
    {
        if (!StringComparer.Ordinal.Equals(bookmark.BookmarkId, payload.BookmarkId) ||
            !StringComparer.Ordinal.Equals(bookmark.WorkflowExecutionId, payload.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(bookmark.ActivityExecutionId, payload.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(
                bookmark.ResumeTargetId,
                WorkflowExecutableResumeTarget.ComposeScopedId(bookmark.ExecutableNodeId, DispatchWorkflowConstants.CompletionResumeTargetId)) ||
            !StringComparer.Ordinal.Equals(bookmark.StimulusType, payload.StimulusType) ||
            !StringComparer.Ordinal.Equals(bookmark.StimulusHash, payload.StimulusHash))
        {
            throw new InvalidOperationException($"DispatchWorkflow bookmark '{bookmark.BookmarkId}' conflicts with dispatch '{payload.DispatchId}'.");
        }
    }
}
