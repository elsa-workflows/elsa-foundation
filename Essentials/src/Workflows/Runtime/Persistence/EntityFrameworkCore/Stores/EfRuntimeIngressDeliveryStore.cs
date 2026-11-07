using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeIngressDeliveryStore(RuntimeDbContext context, IStandaloneTriggerStore resources,
    IRuntimePostCommitOutboxStore outbox, IWorkflowActivationAuthority slots, IWorkflowExecutableStore executables,
    IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions,
    IWorkflowExecutableRootWriteLeaseManager leases, IPersistenceAccessContextAccessor access, TimeProvider time,
    NativeIngressTargetEvidence? evidence = null, IRuntimeModelExecutionReader? modelInputs = null,
    IRecurringTriggerScheduleStore? schedules = null, IEnumerable<IStimulusDeliveryIntentAdapter>? stimulusAdapters = null,
    NativeIngressWaitTargetEvidence? waitEvidence = null, IBookmarkResumeResolver? resumeResolver = null,
    IRuntimeCheckpointCommitStore? checkpoints = null) : IRuntimeIngressDeliveryStore
{
    public bool SupportsAdmission
    {
        get
        {
            if (resources is not EfStandaloneTriggerStore resourceStore || !ReferenceEquals(resourceStore.Context, context) ||
                outbox is not EfRuntimePostCommitOutboxStore sourceStore || slots is not EfWorkflowActivationAuthority authority ||
                !ReferenceEquals(authority.Context, context) || executables.GetMaterialStore() is not EfWorkflowExecutableStore material ||
                !ReferenceEquals(material.Context, context) || references is not EfWorkflowExecutableSourceReferenceStore sourceReferences ||
                !ReferenceEquals(sourceReferences.Context, context) || !context.Database.IsRelational() ||
                evidence is null || evidence.PhysicalQueue is not EfSchedulerWorkQueueStore proofQueue || !ReferenceEquals(proofQueue.Context, context) ||
                evidence.PhysicalExecutions is not EfWorkflowExecutionStateStore proofExecutions || !ReferenceEquals(proofExecutions.Context, context) ||
                !RuntimeSchemaWriteVersion.SupportsIngress(context)) return false;
            var scope = access.Current.RequireScope();
            return sourceStore.Uses(context, scope) && authority.AccessContext.RequireScope() == scope &&
                ((IStimulusDeliveryStore)sourceStore).CanRetainExecutables(outbox, executables, references, executions);
        }
    }

    public async ValueTask<RuntimeIngressAdmissionReceipt?> FindAcceptedAsync(TriggerScope scope, string sourceOutboxItemId,
        string requestFingerprint, CancellationToken cancellationToken = default)
    {
        Require(scope, cancellationToken); RequireOwnedBoundary();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceOutboxItemId); ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        var row = await LoadAsync(scope.Persistence, sourceOutboxItemId, false, cancellationToken);
        return row is null ? null : RuntimeIngressAdmissionValidation.Replay(Read(row, scope.Persistence, sourceOutboxItemId),
            scope, sourceOutboxItemId, requestFingerprint);
    }

    public async ValueTask<RuntimeIngressAdmissionReceipt> AdmitAsync(RuntimeIngressAdmissionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Require(request.Scope, cancellationToken); RequireOwnedBoundary();
        ArgumentNullException.ThrowIfNull(request.Candidate);
        var source = request.Candidate.StandaloneIngress ?? throw new InvalidDataException("Standalone admission requires its actual standalone source arm.");
        var replay = await FindAcceptedAsync(request.Scope, source.OutboxItemId, source.RequestFingerprint, cancellationToken);
        if (replay is not null) return replay;
        ArgumentNullException.ThrowIfNull(request.Targets);
        request = request with { Targets = Array.AsReadOnly(request.Targets.ToArray()) };
        RuntimeIngressAdmissionValidation.CheckCandidate(request);
        if (await PrepareWaitsAsync(request, cancellationToken) is { } capturedWaits)
            request = request with { Candidate = RuntimeIngressWaitCapture.Attach(request.Candidate, capturedWaits) };
        if (request.Candidate.SubscriptionDelivery!.Recipients.Any(recipient => recipient.ModelProtocol is not null) &&
            (modelInputs is not EfRuntimeModelExecutionReader reader || !reader.Uses(context, access)))
            throw new NotSupportedException("Model ingress requires the actual selected EF model input receipt reader.");
        var material = (EfWorkflowExecutableStore)executables.GetMaterialStore();
        var sourceReferences = (EfWorkflowExecutableSourceReferenceStore)references;
        RuntimeIngressAdmissionReceipt? accepted = null;
        try
        {
            await leases.ExecuteFencedAsync(request.Candidate.SubscriptionDelivery!.Recipients.Select(recipient => recipient.Material)
                .Concat(request.Candidate.SubscriptionDelivery.WaitDelivery?.Recipients.Select(recipient => recipient.Expected.RegistrationCapture!.Executable) ?? []).Distinct().ToArray(),
                "standalone-ingress:" + source.OutboxItemId + ":" + Guid.NewGuid().ToString("N"), async (grants, token) =>
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    try
                    {
                        var deadlines = await material.FenceRootWriteLeasesAsync(grants, time, token);
                        DateTimeOffset? occurrenceDeadline = null;
                        var existing = await LoadAsync(request.Scope.Persistence, source.OutboxItemId, false, token);
                        if (existing is not null)
                        {
                            accepted = RuntimeIngressAdmissionValidation.Replay(Read(existing, request.Scope.Persistence, source.OutboxItemId),
                                request.Scope, source.OutboxItemId, source.RequestFingerprint);
                            await transaction.RollbackAsync(token);
                            return;
                        }
                        var aggregate = await resources.FindAsync(request.Scope, source.TriggerId, token) ??
                            throw new TriggerOperationException("conflict", "The trigger was removed before admission.");
                        if (!await EfStandaloneTriggerStore.LockResourceAsync(context, request.Scope, source.TriggerId, request.ExpectedTriggerRevision, token))
                            throw new TriggerOperationException("conflict", "The trigger changed before admission.");
                        RuntimeIngressAdmissionValidation.CheckAggregate(request, aggregate);
                        await FenceWaitsAsync(request, token);
                        foreach (var target in request.Targets.OrderBy(target => target.DefinitionId, StringComparer.Ordinal).ThenBy(target => target.SlotName, StringComparer.Ordinal))
                        {
                            if (!await EfAdmissionTargetFence.LockSlotAsync(context, request.Scope.Persistence.Value, target.DefinitionId, target.SlotName, target.Slot, token))
                                throw new TriggerOperationException("conflict", "A target slot changed before admission.");
                            WorkflowExecutableSourceReference? actualReference = null;
                            if (target.Slot?.ActiveActivationId is { } activation)
                            {
                                // This SERIALIZABLE physical read fences a known absent reference too.
                                actualReference = (await sourceReferences.ReadPhysicalMaintenanceSourceAsync(request.Scope.Persistence.Value,
                                    WorkflowActivationReferenceIdentity.Create(activation), token))?.Value;
                                if (actualReference is not null && !await EfAdmissionTargetFence.LockSourceAsync(sourceReferences,
                                        request.Scope.Persistence.Value, actualReference, token))
                                    throw new TriggerOperationException("conflict", "A target source changed before admission.");
                            }
                            RuntimeIngressAdmissionValidation.CheckTarget(request, target, target.Slot, actualReference, time.GetUtcNow());
                        }
                        foreach (var recipient in request.Candidate.SubscriptionDelivery.Recipients)
                        {
                            var actual = await executables.FindMaterialAsync(recipient.Material.ArtifactId, token) ??
                                throw new InvalidDataException("The accepted target material was removed.");
                            RuntimeIngressAdmissionValidation.CheckMaterial(recipient, actual);
                            await ((EfRuntimePostCommitOutboxStore)outbox).FenceExecutionDependencyAsync(request.Scope.Persistence.Value,
                                recipient.StartIdentity.WorkflowExecutionId, token);
                            if (await executions.FindAsync(recipient.StartIdentity.WorkflowExecutionId, token) is not null)
                                throw new InvalidDataException("The deterministic target identity was already used before admission.");
                            var modelId = EfRuntimeOperationalStoreSupport.CompositeId(request.Scope.Persistence.Value, recipient.StartIdentity.WorkflowExecutionId);
                            if (await context.Set<RuntimeModelExecutionEntity>().AnyAsync(row => row.Id == modelId, token))
                                throw new InvalidDataException("The deterministic target identity already belongs to a model execution.");
                        }
                        EnsureLive();
                        if (request.ScheduleClaim is { } occurrence)
                        {
                            StandaloneScheduleAuthority.Validate(occurrence, source);
                            if (schedules is not EfRecurringTriggerScheduleStore recurring || !recurring.Uses(context, request.Scope.Persistence))
                                throw new NotSupportedException("The actual selected EF recurring schedule store is unavailable.");
                            occurrenceDeadline = await recurring.FenceStandaloneOccurrenceAsync(occurrence, time, token);
                        }
                        if (request.Reservation is not null)
                        {
                            Func<CancellationToken, ValueTask>? reservationFence = null;
                            GetReservationAdmissionFence(request, ref reservationFence);
                            if (reservationFence is null) throw new NotSupportedException("The selected ingress reservation cannot be fenced atomically.");
                            await reservationFence(token);
                        }
                        var row = EfRuntimePostCommitOutboxStore.ToEntity(request.Candidate, request.Scope.Persistence.Value,
                            EfRuntimePostCommitOutboxStore.RowId(request.Scope.Persistence.Value, source.OutboxItemId), 1,
                            RuntimePostCommitOutboxWriteVersion.For(context, request.Candidate));
                        context.RuntimePostCommitOutbox.Add(row);
                        await context.SaveChangesAsync(token);
                        EnsureLive();
                        await transaction.CommitAsync(token);
                        accepted = new(request.Candidate, false);

                        void EnsureLive()
                        {
                            token.ThrowIfCancellationRequested();
                            if (source.RecordedAt > time.GetUtcNow() || request.Targets.Any(target =>
                                request.Candidate.SubscriptionDelivery.Recipients.Any(recipient => recipient.SubscriptionId == target.SubscriptionId) &&
                                target.SourceReference?.IsLive(time.GetUtcNow()) != true))
                                throw new InvalidDataException("An accepted publication expired before physical admission committed.");
                            if (occurrenceDeadline is { } scheduleDeadline && scheduleDeadline <= time.GetUtcNow())
                                throw new TriggerOperationException("conflict", "The schedule occurrence expired before source acceptance.");
                            if (request.Candidate.SubscriptionDelivery.WaitDelivery?.Recipients.Any(recipient => recipient.Expected.ExpiresAt <= time.GetUtcNow()) == true)
                                throw new TriggerOperationException("conflict", "An ordinary wait expired before source acceptance.");
                            foreach (var deadline in deadlines)
                                if (time.GetUtcNow() >= deadline.ExpiresAt)
                                    throw new Core.Exceptions.WorkflowExecutableRootWriteLeaseLostException(deadline.Grant.ArtifactId, deadline.Grant.LeaseId);
                        }
                    }
                    catch
                    {
                        try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
                        throw;
                    }
                    finally { context.ChangeTracker.Clear(); }
                }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            // A simultaneous identical winner remains authoritative even when today's resource/material has since changed.
            context.ChangeTracker.Clear();
            var winner = await FindAcceptedAsync(request.Scope, source.OutboxItemId, source.RequestFingerprint, cancellationToken);
            if (winner is not null) return winner;
            throw;
        }
        return accepted ?? throw new InvalidOperationException("Ingress admission produced no physical acceptance.");
    }

    public ValueTask<RuntimePostCommitOutboxItem> ReadSourceAsync(RuntimeDeliverySourceContext source, CancellationToken cancellationToken = default) =>
        MutateAsync(source, item => item, cancellationToken);

    public async ValueTask<RuntimePostCommitOutboxItem> CommitDispatchAsync(RuntimeDeliverySourceContext source, RuntimeDispatchSeal seal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seal);
        var initial = await ReadSourceAsync(source, cancellationToken);
        var target = initial.SubscriptionDelivery!.Recipients.SingleOrDefault(recipient => recipient.RecipientId == seal.RecipientId) ??
            throw new InvalidDataException("The dispatch recipient was not accepted.");
        var native = target.ModelProtocol is null ? await executables.FindAsync(target.Material.ArtifactId, cancellationToken) : null;
        return await MutateAsync(source, item =>
        {
            if (seal.CommittedAt < item.RecordedAt || seal.CommittedAt > time.GetUtcNow())
                throw new InvalidDataException("The dispatch commitment time is outside the accepted source lifetime.");
            var delivery = item.SubscriptionDelivery!;
            var progress = delivery.Progress.SingleOrDefault(progress => progress.RecipientId == seal.RecipientId) ??
                throw new InvalidDataException("The dispatch recipient was not accepted.");
            var recipient = delivery.Recipients.Single(recipient => recipient.RecipientId == seal.RecipientId);
            var committed = new RuntimeCommittedRecipient(item, recipient, seal);
            if (recipient.ModelProtocol is null)
            {
                NativeIngressTargetEvidence.ValidateEnvelope(committed, NativeIngressTargetEvidence.ReadEnvelope(seal));
                NativeIngressTargetEvidence.ValidateExecutable(committed, native ?? throw new InvalidDataException("Accepted target material is missing."));
            }
            else
            {
                if (modelInputs is not EfRuntimeModelExecutionReader reader || !reader.Uses(context, access))
                    throw new NotSupportedException("Model ingress requires the actual selected EF model input receipt reader.");
                _ = RuntimeIngressModelTargetEvidence.ReadCommand(committed);
            }
            return item.WithSubscriptionDelivery(delivery.WithProgress(progress.CommitDispatch(seal)));
        }, cancellationToken);
    }

    public ValueTask<RuntimePostCommitOutboxItem> RecordReceiptAsync(RuntimeDeliverySourceContext source, RuntimeTargetAcceptanceReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(receipt);
        RequireScope(source.Scope, cancellationToken); RequireOwnedBoundary();
        return ChangeObservedAsync(source.Scope, source.SourceOutboxItemId, async (current, token) =>
        {
            RuntimeIngressAdmissionValidation.CheckClaim(current, source, time.GetUtcNow());
            var target = RuntimeIngressAdmissionValidation.ReceiptTarget(current, receipt);
            if (current.SubscriptionDelivery!.Progress.Single(progress => progress.RecipientId == receipt.RecipientId).Receipt is not null) return current;
            RuntimeTargetAcceptanceReceipt? observed;
            if (target.Recipient.ModelProtocol is null)
                observed = await evidence!.ReconcileAsync(current, target.Recipient, target.Seal, token);
            else
            {
                if (modelInputs is not EfRuntimeModelExecutionReader reader || !reader.Uses(context, access))
                    throw new NotSupportedException("Model ingress requires the actual selected EF model input receipt reader.");
                observed = await RuntimeIngressModelTargetEvidence.ReconcileAsync(target, evidence!.PhysicalQueue, modelInputs, time, token);
            }
            if (observed is null) throw new TriggerOperationException("unavailable", "The deterministic target has no observed physical acceptance; dispatch remains owed.");
            return RuntimeIngressAdmissionValidation.AcceptObserved(current, observed);
        }, cancellationToken, source);
    }

    public ValueTask<RuntimePostCommitOutboxItem> RecordRecipientFailureAsync(RuntimeDeliverySourceContext source, string recipientId,
        RuntimeRecipientRecoveryState recoveryState, string code, DateTimeOffset? retryAt, CancellationToken cancellationToken = default) =>
        MutateAsync(source, item => RuntimeIngressAdmissionValidation.RecordFailure(item, recipientId, recoveryState, code, retryAt), cancellationToken);

    public async ValueTask<RuntimeCommittedRecipient> ReadCommittedRecipientAsync(RuntimeCommittedRecipientAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        RequireScope(authority.Scope, cancellationToken); RequireOwnedBoundary();
        var row = await LoadAsync(authority.Scope, authority.SourceOutboxItemId, false, cancellationToken) ??
            throw new InvalidDataException("The actual accepted ingress source does not exist.");
        var item = Read(row, authority.Scope, authority.SourceOutboxItemId);
        return RuntimeIngressAdmissionValidation.Committed(item, authority);
    }

    public async ValueTask<RuntimeCommittedRecipient> ReadCommittedRecipientAsync(RuntimeIngressCommandOrigin origin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        RequireScope(origin.Scope, cancellationToken); RequireOwnedBoundary();
        var row = await LoadAsync(origin.Scope, origin.SourceOutboxItemId, false, cancellationToken) ??
            throw new InvalidDataException("The actual accepted ingress source does not exist.");
        var item = Read(row, origin.Scope, origin.SourceOutboxItemId);
        return RuntimeIngressAdmissionValidation.Committed(item, origin);
    }

    public async ValueTask<RuntimeIngressCancellationReceipt> CancelPendingAsync(RuntimeIngressCancellationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.RecipientIds);
        Require(request.Scope, cancellationToken); RequireOwnedBoundary();
        RuntimeIngressCancellationReceipt? result = null;
        await ChangeAsync(request.Scope.Persistence, request.SourceOutboxItemId, item =>
        {
            result = RuntimeIngressAdmissionValidation.Cancel(item, request);
            return result.Item;
        }, cancellationToken);
        return result!;
    }

    private ValueTask<RuntimePostCommitOutboxItem> MutateAsync(RuntimeDeliverySourceContext source,
        Func<RuntimePostCommitOutboxItem, RuntimePostCommitOutboxItem> mutate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireScope(source.Scope, cancellationToken); RequireOwnedBoundary();
        return ChangeAsync(source.Scope, source.SourceOutboxItemId, item =>
        {
            RuntimeIngressAdmissionValidation.CheckClaim(item, source, time.GetUtcNow());
            return mutate(item);
        }, cancellationToken, source);
    }

    private ValueTask<RuntimePostCommitOutboxItem> ChangeAsync(PersistenceScope scope, string id,
        Func<RuntimePostCommitOutboxItem, RuntimePostCommitOutboxItem> mutate, CancellationToken cancellationToken,
        RuntimeDeliverySourceContext? claim = null) => ChangeObservedAsync(scope, id, (item, _) => ValueTask.FromResult(mutate(item)), cancellationToken, claim);

    private async ValueTask<RuntimePostCommitOutboxItem> ChangeObservedAsync(PersistenceScope scope, string id,
        Func<RuntimePostCommitOutboxItem, CancellationToken, ValueTask<RuntimePostCommitOutboxItem>> mutate, CancellationToken cancellationToken,
        RuntimeDeliverySourceContext? claim = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                var row = await LoadAsync(scope, id, true, cancellationToken) ?? throw new InvalidDataException("The actual accepted ingress source does not exist.");
                var current = Read(row, scope, id);
                if (claim is not null && current.Intent is { } intent)
                {
                    var publisher = await executions.FindAsync(intent.WorkflowExecutionId, cancellationToken) ??
                        throw new InvalidDataException("The physical workflow publisher is missing.");
                    RuntimeIngressAdmissionValidation.CheckPublisher(current, publisher, scope);
                }
                var replacement = await mutate(current, cancellationToken);
                if (claim is not null) RuntimeIngressAdmissionValidation.CheckClaim(replacement, claim, time.GetUtcNow());
                if (!ReferenceEquals(current, replacement))
                {
                    EfRuntimePostCommitOutboxStore.Copy(row, replacement, scope.Value, checked(row.Revision + 1),
                        RuntimePostCommitOutboxWriteVersion.For(context, replacement));
                    await context.SaveChangesAsync(cancellationToken);
                }
                if (claim is not null) RuntimeIngressAdmissionValidation.CheckClaim(replacement, claim, time.GetUtcNow());
                await transaction.CommitAsync(cancellationToken);
                return replacement;
            }
            catch (Exception exception) when (attempt < 3 && EfRelationalExceptionClassifier.IsSaveConflict(exception,
                EfWriteConflict.Concurrency | EfWriteConflict.Transient))
            { await context.RollbackAndClearAsync(transaction); }
            finally { context.ChangeTracker.Clear(); }
        }
    }

    private Task<RuntimePostCommitOutboxEntity?> LoadAsync(PersistenceScope scope, string sourceId, bool tracking, CancellationToken token)
    {
        var id = EfRuntimePostCommitOutboxStore.RowId(scope.Value, sourceId);
        var query = context.RuntimePostCommitOutbox.Where(row => row.Id == id);
        return tracking ? query.SingleOrDefaultAsync(token) : query.AsNoTracking().SingleOrDefaultAsync(token);
    }

    private static RuntimePostCommitOutboxItem Read(RuntimePostCommitOutboxEntity row, PersistenceScope scope, string id) =>
        EfRuntimePostCommitOutboxStore.ReadChecked(row, scope.Value, id);

    private void RequireOwnedBoundary()
    {
        if (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null || context.Database.GetEnlistedTransaction() is not null ||
            System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Checked ingress requires a clean Runtime context and its own transaction.");
    }

    partial void GetReservationAdmissionFence(RuntimeIngressAdmissionRequest request, ref Func<CancellationToken, ValueTask>? fence);

    private void Require(TriggerScope scope, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope);
        RequireScope(scope.Persistence, token);
        RuntimeIngressAdmissionValidation.CheckScope(access, scope);
    }

    private void RequireScope(PersistenceScope scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!SupportsAdmission) throw new NotSupportedException("Standalone ingress requires one coherent EF resource, outbox, publication and retention family.");
        access.Current.RequireScope(); access.Current.EnsureScope(scope);
    }
}
