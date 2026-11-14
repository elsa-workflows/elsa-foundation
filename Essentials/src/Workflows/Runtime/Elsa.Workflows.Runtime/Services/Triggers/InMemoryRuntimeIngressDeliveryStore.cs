using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal sealed partial class InMemoryRuntimeIngressDeliveryStore(IStandaloneTriggerStore resources, IRuntimePostCommitOutboxStore outbox,
    IWorkflowActivationAuthority slots, IWorkflowExecutableStore executables, IWorkflowExecutableSourceReferenceStore references,
    IWorkflowExecutionStateStore executions, IWorkflowExecutableRootWriteLeaseManager leases,
    IPersistenceAccessContextAccessor access, TimeProvider time, NativeIngressTargetEvidence? evidence = null,
    IRecurringTriggerScheduleStore? schedules = null, IEnumerable<IStimulusDeliveryIntentAdapter>? stimulusAdapters = null,
    NativeIngressWaitTargetEvidence? waitEvidence = null, IBookmarkResumeResolver? resumeResolver = null,
    IRuntimeCheckpointCommitStore? checkpoints = null) : IRuntimeIngressDeliveryStore
{
    public bool SupportsAdmission => resources is InMemoryStandaloneTriggerStore resourceStore &&
        outbox is InMemoryRuntimeCheckpointCommitStore sourceStore && ReferenceEquals(resourceStore.State, sourceStore.State) &&
        slots is InMemoryWorkflowActivationAuthority && executables.GetMaterialStore() is InMemoryWorkflowExecutableStore material &&
        references is InMemoryWorkflowExecutableSourceReferenceStore sourceReferences && ReferenceEquals(sourceReferences.FencedArtifactStore, material) &&
        evidence is not null && evidence.PhysicalQueue is InMemoryWorkflowSchedulerWorkQueue && evidence.PhysicalExecutions is InMemoryWorkflowExecutionStateStore proofExecutions &&
        ReferenceEquals(proofExecutions.CheckpointState, sourceStore.State) && ReferenceEquals(proofExecutions, executions) &&
        ((IStimulusDeliveryStore)sourceStore).CanRetainExecutables(outbox, executables, references, executions);

    private InMemoryRuntimeCheckpointStoreState State => ((InMemoryRuntimeCheckpointCommitStore)outbox).State;

    public ValueTask<RuntimeIngressAdmissionReceipt?> FindAcceptedAsync(TriggerScope scope, string sourceOutboxItemId,
        string requestFingerprint, CancellationToken cancellationToken = default)
    {
        try
        {

            Require(scope, cancellationToken);
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceOutboxItemId); ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
            lock (State.SyncRoot)
                return ValueTask.FromResult(State.OutboxItems.TryGetValue(sourceOutboxItemId, out var item)
                    ? RuntimeIngressAdmissionValidation.Replay(item, scope, sourceOutboxItemId, requestFingerprint) : null);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw InvalidAuthority(sourceOutboxItemId, exception);
        }
    }

    public async ValueTask<RuntimeIngressAdmissionReceipt> AdmitAsync(RuntimeIngressAdmissionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {

            ArgumentNullException.ThrowIfNull(request);
            Require(request.Scope, cancellationToken);
            ArgumentNullException.ThrowIfNull(request.Candidate);
            var source = request.Candidate.StandaloneIngress ?? throw new InvalidDataException("Standalone admission requires its actual standalone source arm.");
            var replay = await FindAcceptedAsync(request.Scope, source.OutboxItemId, source.RequestFingerprint, cancellationToken);
            if (replay is not null) return replay;
            ArgumentNullException.ThrowIfNull(request.Targets);
            request = request with { Targets = Array.AsReadOnly(request.Targets.ToArray()) };
            RuntimeIngressAdmissionValidation.CheckCandidate(request);
            if (await PrepareWaitsAsync(request, cancellationToken) is { } capturedWaits)
                request = request with { Candidate = RuntimeIngressWaitCapture.Attach(request.Candidate, capturedWaits) };
            var material = (InMemoryWorkflowExecutableStore)executables.GetMaterialStore();
            var sourceReferences = (InMemoryWorkflowExecutableSourceReferenceStore)references;
            var authority = (InMemoryWorkflowActivationAuthority)slots;
            if (request.Candidate.SubscriptionDelivery!.Recipients.Any(recipient => recipient.ModelProtocol is not null))
                throw new NotSupportedException("The selected native material provider cannot retain model material.");
            RuntimeIngressAdmissionReceipt? accepted = null;
            try
            {
                await leases.ExecuteFencedAsync(request.Candidate.SubscriptionDelivery.Recipients.Select(recipient => recipient.Material)
                    .Concat(request.Candidate.SubscriptionDelivery.WaitDelivery?.Recipients.Select(recipient => recipient.Expected.RegistrationCapture!.Executable) ?? []).Distinct().ToArray(),
                "standalone-ingress:" + source.OutboxItemId + ":" + Guid.NewGuid().ToString("N"), async (grants, token) =>
                {
                    accepted = await State.ExecuteWriteGateAsync(async gateToken =>
                    {
                        var ownership = (InMemoryExecutionLivenessStateStore)((InMemoryWorkflowExecutionStateStore)executions).ExecutionLivenessStateStore!;
                        return await ownership.ExecuteOwnershipAtomicAsync(async ownershipToken =>
                        {
                            await FenceWaitsAsync(request, ownershipToken);
                            foreach (var recipient in request.Candidate.SubscriptionDelivery.Recipients)
                                if (await executions.IsDeletionTombstonedAsync(recipient.StartIdentity.WorkflowExecutionId, ownershipToken) ||
                                    await executions.FindAsync(recipient.StartIdentity.WorkflowExecutionId, ownershipToken) is not null)
                                    throw new InvalidDataException("The deterministic target identity was already used or retired before admission.");
                            return material.ExecuteUnderRootWriteLeases(grants, time,
                        () => material.ExecuteUnderMaterialGate(actualMaterial => sourceReferences.ExecuteUnderReferenceGate(actualReferences =>
                        {
                            // Publication switches use reference -> authority. Holding the actual authority gate fences creation of absent slots too.
                            lock (authority.Gate)
                            lock (State.SyncRoot)
                            {
                                gateToken.ThrowIfCancellationRequested();
                                if (State.OutboxItems.TryGetValue(source.OutboxItemId, out var winner))
                                    return RuntimeIngressAdmissionValidation.Replay(winner, request.Scope, source.OutboxItemId, source.RequestFingerprint);
                                var aggregate = State.StandaloneTriggers.GetValueOrDefault((request.Scope.Persistence.Value, request.Scope.TenantId, source.TriggerId)) ??
                                    throw new TriggerOperationException("conflict", "The trigger was removed before admission.");
                                RuntimeIngressAdmissionValidation.CheckAggregate(request, aggregate);
                                foreach (var target in request.Targets)
                                {
                                    var actualSlot = authority.Current(target.DefinitionId, target.SlotName);
                                    var actualReference = actualSlot?.ActiveActivationId is { } activation
                                        ? actualReferences.GetValueOrDefault(WorkflowActivationReferenceIdentity.Create(activation)) : null;
                                    RuntimeIngressAdmissionValidation.CheckTarget(request, target, actualSlot, actualReference, time.GetUtcNow());
                                }
                                foreach (var recipient in request.Candidate.SubscriptionDelivery.Recipients)
                                {
                                    var actual = actualMaterial.SingleOrDefault(value => value.Identity.ArtifactId == recipient.Material.ArtifactId) ??
                                        throw new InvalidDataException("The accepted target material was removed.");
                                    RuntimeIngressAdmissionValidation.CheckMaterial(recipient, new RuntimeExecutionMaterial(actual, "native-memory"));
                                }
                                if (source.RecordedAt > time.GetUtcNow()) throw new InvalidDataException("The source admission time is in the future.");
                                if (request.Targets.Any(target => request.Candidate.SubscriptionDelivery.Recipients.Any(recipient =>
                                    recipient.SubscriptionId == target.SubscriptionId) && target.SourceReference?.IsLive(time.GetUtcNow()) != true))
                                    throw new InvalidDataException("An accepted target source expired before admission.");
                                return material.ExecuteUnderRootWriteLeases(grants, time, () =>
                                {
                                    RuntimeIngressAdmissionReceipt Commit(DateTimeOffset? occurrenceDeadline = null)
                                    {
                                        if (request.Reservation is not null)
                                        {
                                            var checkedReservation = false;
                                            CheckReservationAdmission(request, ref checkedReservation);
                                            if (!checkedReservation) throw new NotSupportedException("The selected ingress reservation cannot be fenced atomically.");
                                        }
                                        if (occurrenceDeadline is { } deadline && deadline <= time.GetUtcNow())
                                            throw new TriggerOperationException("conflict", "The schedule occurrence expired before source acceptance.");
                                        if (request.Candidate.SubscriptionDelivery.WaitDelivery?.Recipients.Any(recipient =>
                                            recipient.Expected.ExpiresAt <= time.GetUtcNow()) == true)
                                            throw new TriggerOperationException("conflict", "An ordinary wait expired before source acceptance.");
                                        State.OutboxItems.Add(source.OutboxItemId, request.Candidate);
                                        return new(request.Candidate, false);
                                    }
                                    if (request.ScheduleClaim is not { } occurrence) return Commit();
                                    StandaloneScheduleAuthority.Validate(occurrence, source);
                                    if (schedules is not InMemoryRecurringTriggerScheduleStore recurring)
                                        throw new NotSupportedException("The actual selected native recurring schedule store is unavailable.");
                                    return recurring.ExecuteUnderStandaloneOccurrenceClaim(occurrence, time, deadline => Commit(deadline));
                                }, ownershipToken);
                            }
                        })), ownershipToken);
                        }, gateToken);
                    }, token);
                    }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                var winner = await FindAcceptedAsync(request.Scope, source.OutboxItemId, source.RequestFingerprint, cancellationToken);
                if (winner is not null) return winner;
                throw;
            }
            return accepted ?? throw new InvalidOperationException("Ingress admission produced no physical acceptance.");
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw InvalidAuthority(request.Candidate.OutboxItemId, exception);
        }
    }

    public ValueTask<RuntimePostCommitOutboxItem> ReadSourceAsync(RuntimeDeliverySourceContext source, CancellationToken cancellationToken = default) =>
        MutateAsync(source, item => item, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxItem> CommitDispatchAsync(RuntimeDeliverySourceContext source, RuntimeDispatchSeal seal,
        CancellationToken cancellationToken = default) => MutateAsync(source, item =>
        {
            ArgumentNullException.ThrowIfNull(seal);
            if (seal.CommittedAt < item.RecordedAt || seal.CommittedAt > time.GetUtcNow())
                throw new InvalidDataException("The dispatch commitment time is outside the accepted source lifetime.");
            var delivery = item.SubscriptionDelivery!;
            var progress = delivery.Progress.SingleOrDefault(progress => progress.RecipientId == seal.RecipientId) ??
                throw new InvalidDataException("The dispatch recipient was not accepted.");
            var recipient = delivery.Recipients.Single(recipient => recipient.RecipientId == seal.RecipientId);
            var committed = new RuntimeCommittedRecipient(item, recipient, seal);
            NativeIngressTargetEvidence.ValidateEnvelope(committed, NativeIngressTargetEvidence.ReadEnvelope(seal));
            ((InMemoryWorkflowExecutableStore)executables.GetMaterialStore()).ExecuteUnderMaterialGate(material =>
            {
                var native = material.SingleOrDefault(candidate => candidate.Identity.ArtifactId == recipient.Material.ArtifactId) ??
                    throw new InvalidDataException("The accepted target material was removed.");
                NativeIngressTargetEvidence.ValidateExecutable(committed, native);
                return true;
            });
            return item.WithSubscriptionDelivery(delivery.WithProgress(progress.CommitDispatch(seal)));
        }, cancellationToken);

    public async ValueTask<RuntimePostCommitOutboxItem> RecordReceiptAsync(RuntimeDeliverySourceContext source, RuntimeTargetAcceptanceReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        try
        {

            ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(receipt);
            RequireScope(source.Scope, cancellationToken);
            return await State.ExecuteWriteGateAsync(async token =>
            {
                RuntimeCommittedRecipient target;
                lock (State.SyncRoot)
                {
                    var current = RuntimeIngressAdmissionValidation.CheckClaim(Find(source.SourceOutboxItemId), source, time.GetUtcNow());
                    target = RuntimeIngressAdmissionValidation.ReceiptTarget(current, receipt);
                    if (current.SubscriptionDelivery!.Progress.Single(progress => progress.RecipientId == receipt.RecipientId).Receipt is not null) return current;
                }
                await CheckPublisherAsync(target.Item, source.Scope, token);
                var observed = await evidence!.ReconcileAsync(target.Item, target.Recipient, target.Seal, token) ??
                    throw new TriggerOperationException("unavailable", "The deterministic target has no observed physical acceptance; dispatch remains owed.");
                lock (State.SyncRoot)
                {
                    var current = RuntimeIngressAdmissionValidation.CheckClaim(Find(source.SourceOutboxItemId), source, time.GetUtcNow());
                    var replacement = RuntimeIngressAdmissionValidation.AcceptObserved(current, observed);
                    State.OutboxItems[current.OutboxItemId] = replacement;
                    return replacement;
                }
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw InvalidAuthority(source.SourceOutboxItemId, exception);
        }
    }

    public ValueTask<RuntimePostCommitOutboxItem> RecordRecipientFailureAsync(RuntimeDeliverySourceContext source, string recipientId,
        RuntimeRecipientRecoveryState recoveryState, string code, DateTimeOffset? retryAt, CancellationToken cancellationToken = default) =>
        MutateAsync(source, item => RuntimeIngressAdmissionValidation.RecordFailure(item, recipientId, recoveryState, code, retryAt), cancellationToken);

    public ValueTask<RuntimeCommittedRecipient> ReadCommittedRecipientAsync(RuntimeCommittedRecipientAuthority authority,
        CancellationToken cancellationToken = default)
    {
        try
        {

            ArgumentNullException.ThrowIfNull(authority);
            RequireScope(authority.Scope, cancellationToken);
            lock (State.SyncRoot)
            {
                var item = Find(authority.SourceOutboxItemId);
                return ValueTask.FromResult(RuntimeIngressAdmissionValidation.Committed(item, authority));
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw InvalidAuthority(authority.SourceOutboxItemId, exception);
        }
    }

    public ValueTask<RuntimeCommittedRecipient> ReadCommittedRecipientAsync(RuntimeIngressCommandOrigin origin,
        CancellationToken cancellationToken = default)
    {
        try
        {

            ArgumentNullException.ThrowIfNull(origin);
            RequireScope(origin.Scope, cancellationToken);
            lock (State.SyncRoot)
            {
                var item = Find(origin.SourceOutboxItemId);
                return ValueTask.FromResult(RuntimeIngressAdmissionValidation.Committed(item, origin));
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw InvalidAuthority(origin.SourceOutboxItemId, exception);
        }
    }

    public async ValueTask<RuntimeIngressCancellationReceipt> CancelPendingAsync(RuntimeIngressCancellationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {

            ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.RecipientIds);
            Require(request.Scope, cancellationToken);
            return await State.ExecuteWriteGateAsync(_ =>
            {
                lock (State.SyncRoot)
                {
                    var result = RuntimeIngressAdmissionValidation.Cancel(Find(request.SourceOutboxItemId), request);
                    State.OutboxItems[request.SourceOutboxItemId] = result.Item;
                    return ValueTask.FromResult(result);
                }
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw InvalidAuthority(request.SourceOutboxItemId, exception);
        }
    }

    private async ValueTask<RuntimePostCommitOutboxItem> MutateAsync(RuntimeDeliverySourceContext source,
        Func<RuntimePostCommitOutboxItem, RuntimePostCommitOutboxItem> mutate, CancellationToken cancellationToken)
    {
        try
        {

            ArgumentNullException.ThrowIfNull(source);
            RequireScope(source.Scope, cancellationToken);
            return await State.ExecuteWriteGateAsync(async token =>
            {
                RuntimePostCommitOutboxItem observed;
                lock (State.SyncRoot)
                    observed = RuntimeIngressAdmissionValidation.CheckClaim(Find(source.SourceOutboxItemId), source, time.GetUtcNow());
                await CheckPublisherAsync(observed, source.Scope, token);
                lock (State.SyncRoot)
                {
                    var current = RuntimeIngressAdmissionValidation.CheckClaim(Find(source.SourceOutboxItemId), source, time.GetUtcNow());
                    var replacement = mutate(current);
                    State.OutboxItems[current.OutboxItemId] = replacement;
                    return replacement;
                }
            }, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw InvalidAuthority(source.SourceOutboxItemId, exception);
        }
    }

    private RuntimePostCommitOutboxItem Find(string id) => State.OutboxItems.GetValueOrDefault(id) ??
        throw new InvalidDataException("The actual accepted ingress source does not exist.");

    private async ValueTask CheckPublisherAsync(RuntimePostCommitOutboxItem item, PersistenceScope scope, CancellationToken token)
    {
        if (item.Intent is not { } intent) return;
        var publisher = await executions.FindAsync(intent.WorkflowExecutionId, token) ?? throw new InvalidDataException("The physical workflow publisher is missing.");
        RuntimeIngressAdmissionValidation.CheckPublisher(item, publisher, scope);
    }

    partial void CheckReservationAdmission(RuntimeIngressAdmissionRequest request, ref bool checkedReservation);

    private void Require(TriggerScope scope, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope);
        RequireScope(scope.Persistence, token);
        RuntimeIngressAdmissionValidation.CheckScope(access, scope);
    }

    private void RequireScope(PersistenceScope scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!SupportsAdmission) throw new NotSupportedException("Standalone ingress requires one coherent native resource, outbox, publication and retention family.");
        access.Current.RequireScope(); access.Current.EnsureScope(scope);
    }
    private static TriggerOperationException InvalidAuthority(string sourceId, Exception cause) =>
        new("conflict", $"Ingress delivery '{sourceId}' has invalid physical authority.", innerException: cause);

}
