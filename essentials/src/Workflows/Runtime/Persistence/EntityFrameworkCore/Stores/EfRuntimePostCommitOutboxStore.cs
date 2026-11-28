using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core post-commit outbox adapter (R20).</summary>
/// <remarks>
/// Pending persistence, delivery lookup, claims, delivery completion, and the R21 dispatch projection are implemented
/// against the shared relational context. The adapter remains opt-in until the complete runtime checkpoint writer
/// can compose every participant, but completion never hides a partial dispatch write.
/// </remarks>
public sealed partial class EfRuntimePostCommitOutboxStore : IRuntimePostCommitOutboxStore,
    IPostCommitOutboxLookupStore,
    IRuntimePostCommitOutboxClaimStore,
    IRuntimePostCommitOutboxClaimCompletionStore,
    IWorkflowDispatchRedriveStore,
    IStimulusDeliveryStore,
    IEfRuntimePostCommitOutboxOwner
{
    private readonly RuntimeDbContext context;
    internal EfRuntimePostCommitOutboxStorage Storage { get; }
    EfRuntimePostCommitOutboxStorage IEfRuntimePostCommitOutboxOwner.Storage => Storage;

    internal bool Uses(RuntimeDbContext expected, PersistenceScope scope) => Storage.Uses(expected, scope);

    internal ValueTask FenceExecutionDependencyAsync(string scope, string workflowExecutionId, CancellationToken cancellationToken) =>
        Storage.FenceExecutionDependencyAsync(scope, workflowExecutionId, cancellationToken);
    private readonly IPersistenceAccessContextAccessor accessContextAccessor;
    private readonly TimeProvider timeProvider;
    private readonly WorkflowExecutableStartSourceResolver? retentionSources;
    private readonly IWorkflowExecutableRootWriteLeaseManager? retentionLeases;
    private readonly EfWorkflowExecutableSourceReferenceStore? retentionReferences;

    bool IPostCommitOutboxLookupStore.CanInspectHistorySources(IRuntimeHistoryStore history) => Storage.CanInspectHistorySources(history);

    public EfRuntimePostCommitOutboxStore(RuntimeDbContext context, IPersistenceAccessContextAccessor accessContextAccessor)
        : this(context, accessContextAccessor, TimeProvider.System) { }

    public EfRuntimePostCommitOutboxStore(RuntimeDbContext context, IPersistenceAccessContextAccessor accessContextAccessor,
        TimeProvider? timeProvider)
        : this(context, accessContextAccessor, timeProvider, null, null, requireRetention: false) { }

    public EfRuntimePostCommitOutboxStore(RuntimeDbContext context, IPersistenceAccessContextAccessor accessContextAccessor,
        TimeProvider? timeProvider, IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager)
        : this(context, accessContextAccessor, timeProvider, sourceReferenceStore, rootWriteLeaseManager, requireRetention: true) { }

    private EfRuntimePostCommitOutboxStore(RuntimeDbContext context, IPersistenceAccessContextAccessor accessContextAccessor,
        TimeProvider? timeProvider, IWorkflowExecutableSourceReferenceStore? sourceReferenceStore,
        IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager, bool requireRetention)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.accessContextAccessor = accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        if (requireRetention)
        {
            ArgumentNullException.ThrowIfNull(sourceReferenceStore);
            ArgumentNullException.ThrowIfNull(rootWriteLeaseManager);
        }
        if (sourceReferenceStore is EfWorkflowExecutableSourceReferenceStore references && ReferenceEquals(references.Context, context))
        {
            retentionReferences = references;
            retentionSources = new(references.WithAccess(accessContextAccessor), this.timeProvider);
            retentionLeases = rootWriteLeaseManager;
        }
        Storage = new(context, accessContextAccessor, this.timeProvider, retentionReferences, retentionLeases,
            preparedStandaloneOnly: false, new EfOutboxWorkflowDispatchCompletion(context, accessContextAccessor));
    }

    async ValueTask IStimulusDeliveryStore.EnsureOrdinaryCollectionAllowedAsync(IRuntimePostCommitOutboxStore outbox, IWorkflowExecutableStore executables,
        IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outbox); cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(outbox, this) || executables.GetMaterialStore() is not EfWorkflowExecutableStore material ||
            references is not EfWorkflowExecutableSourceReferenceStore selectedReferences || executions is not EfWorkflowExecutionStateStore states ||
            !ReferenceEquals(material.Context, context) || !ReferenceEquals(selectedReferences.Context, context) || !ReferenceEquals(states.Context, context))
            throw new NotSupportedException("Ordinary collection requires its actual EF material/outbox/reference/execution context.");
        PersistenceScope? scope = null;
        try
        {
            EnsureCommittedSourceBoundary();
            if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Ordinary collection requires a clean context.");
            scope = accessContextAccessor.Current.RequireScope();
            if (material.AccessContext.RequireScope() != scope || selectedReferences.AccessContext.RequireScope() != scope || states.AccessContext.RequireScope() != scope)
                throw new NotSupportedException("Ordinary collection requires one actual EF material/outbox/reference/execution scope.");
            var sources = await ReadRetentionSourcesAsync(context, scope.Value, cancellationToken);
            if (sources.Any(source => source.StimulusDeliveryProtocolVersion is not null || source.SourceScope is not null || source.RecipientSnapshot is not null || source.SubscriptionDeliveryProtocolVersion is not null || source.SubscriptionDelivery is not null))
                throw new StimulusDeliveryRetentionException(scope, new InvalidOperationException("Ordinary collection cannot serve saved stimulus protocol data."));
            EnsureCommittedSourceBoundary();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (StimulusDeliveryRetentionException) { throw; }
        catch (Exception exception) when (exception is IOException or OperationCanceledException || EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw new StimulusDeliveryRetentionException(scope, exception); }
    }

    bool IStimulusDeliveryStore.CanRetainExecutables(IRuntimePostCommitOutboxStore outbox, IWorkflowExecutableStore executables,
        IWorkflowExecutableSourceReferenceStore references, IWorkflowExecutionStateStore executions) =>
        ReferenceEquals(outbox, this) && executions is EfWorkflowExecutionStateStore states &&
        ReferenceEquals(states.Context, context) && Storage.CanRetainExecutableFamily(executables, references, () => states.AccessContext.RequireScope());

    async ValueTask<IReadOnlyCollection<string>> IStimulusDeliveryStore.ListPinnedExecutableArtifactIdsAsync(
        IWorkflowExecutableStore executables, IWorkflowExecutableSourceReferenceStore references,
        IWorkflowExecutionStateStore executions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executables); ArgumentNullException.ThrowIfNull(references); ArgumentNullException.ThrowIfNull(executions);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!((IStimulusDeliveryStore)this).CanRetainExecutables(this, executables, references, executions))
                throw new NotSupportedException("Physical stimulus retention requires the actual EF stores, context and scope.");
            if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Physical stimulus retention requires a clean context.");
            return await ListPinnedExecutableArtifactIdsAsync(cancellationToken);
        }
        catch (StimulusDeliveryRetentionException exception) when (cancellationToken.IsCancellationRequested && exception.InnerException is OperationCanceledException)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw;
        }
    }

    private async ValueTask<StimulusDeliverySnapshot> InstallCohortAsync(PostCommitDeliveryContext source,
        StimulusDeliverySnapshot candidate, RuntimeWorkflowSubscriptionCandidate? subscriptions,
        Core.StandaloneTriggers.IStandaloneTriggerStore? subscriptionResources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.MatchesSource(source)) throw new ArgumentException("The proposed cohort does not match its source.", nameof(candidate));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var access = accessContextAccessor.Current; var scope = access.RequireScope(); access.EnsureScope(source.Scope);
            EnsureCommittedSourceBoundary();
            if (retentionSources is null || retentionLeases is null)
                throw new NotSupportedException("Verified cohort installation requires coherent native reference and root-lease stores.");
            if (context.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Verified cohort installation requires a clean context for its owned transaction.");
            Exception? lastConflict = null;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                context.ChangeTracker.Clear();
                try
                {
                    var initial = await ReadFacts();
                    _ = RuntimeWorkflowSubscriptionValidation.Existing(initial.Source, subscriptions);
                    _ = initial.Source.InstallRecipientSnapshot(source, candidate, initial.Publisher, scope, timeProvider.GetUtcNow());
                    if (initial.Source.RecipientSnapshot is { } existing) return Verified(existing);
                    if (candidate.SelectedAt < source.Intent.RecordedAt || candidate.SelectedAt > timeProvider.GetUtcNow())
                        throw new InvalidDataException("The recipient selection timestamp is outside this source delivery.");
                    var dependencies = candidate.GetExecutionDependencies(source.Intent).Concat(subscriptions?.Delivery.Recipients.Select(recipient => recipient.StartIdentity.WorkflowExecutionId) ?? []).Distinct(StringComparer.Ordinal).ToArray();
                    var roots = candidate.Starts.Select(start => start.Executable).Concat(subscriptions?.Delivery.Recipients.Select(recipient => recipient.Material) ?? []).Append(initial.Publisher.PinnedExecutable).Distinct().ToArray();
                    StimulusDeliverySnapshot? result = null;
                    await retentionLeases.ExecuteFencedAsync(roots, "stimulus:" + source.SourceOutboxItemId + ":" + Guid.NewGuid().ToString("N"), async (grants, token) =>
                    {
                        if (subscriptions is null && !NativeIngressCheckpointAuthority.HasBoundScope(initial.Publisher, scope.Value))
                            await retentionSources.ValidateCapturedInScopeAsync(candidate.Starts, scope, token);
                        else await retentionSources.ValidateCapturedInTenantAsync(candidate.Starts, scope, initial.Publisher.TenantId, token);
                        await using var transaction = subscriptions is null ? await context.Database.BeginTransactionAsync(token) :
                            await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
                        try
                        {
                            // Material is locked before ownership. All workflow/reference facts are read before
                            // ownership locks; deletion may hold another workflow root while scanning source rows.
                            var deadlines = await new EfWorkflowExecutableStore(context, accessContextAccessor).FenceRootWriteLeasesAsync(grants, timeProvider, token);
                            var facts = await ReadFacts(token);
                            _ = RuntimeWorkflowSubscriptionValidation.Existing(facts.Source, subscriptions);
                            if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(facts.Publisher.PinnedExecutable, initial.Publisher.PinnedExecutable))
                                throw new InvalidDataException("The publisher executable changed before cohort acceptance.");
                            _ = facts.Source.InstallRecipientSnapshot(source, candidate, facts.Publisher, scope, timeProvider.GetUtcNow());
                            if (facts.Source.RecipientSnapshot is { } winner) { result = Verified(winner); return; }
                            if (subscriptions is not null)
                                await ValidateWorkflowSubscriptionsAsync(subscriptions, subscriptionResources!, facts.Publisher, token);
                            foreach (var id in dependencies) await Storage.FenceExecutionDependencyAsync(scope.Value, id, token);
                            // Re-read physical source/claim after all awaited fences. No workflow-root read occurs here.
                            var row = await LoadAsync(scope.Value, source.SourceOutboxItemId, tracking: true, token) ??
                                throw new InvalidDataException("The exact recipient source is missing.");
                            await context.Entry(row).ReloadAsync(token);
                            if (context.Entry(row).State == EntityState.Detached) throw new InvalidDataException("The recipient source was removed.");
                            var current = ReadChecked(row, scope.Value, source.SourceOutboxItemId);
                            _ = RuntimeWorkflowSubscriptionValidation.Existing(current, subscriptions);
                            var accepted = new StimulusDeliverySnapshot(candidate.Version, candidate.SourceOutboxItemId,
                                candidate.IntentFingerprint, candidate.Scope, candidate.SelectedAt, candidate.StimulusType,
                                candidate.StimulusHash, candidate.Starts, candidate.Resumes, StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion);
                            var installed = current.InstallRecipientSnapshot(source, accepted, facts.Publisher, scope, timeProvider.GetUtcNow());
                            if (subscriptions is not null && current.SubscriptionDelivery is null)
                                installed = installed.WithSubscriptionDelivery(subscriptions.Delivery);
                            if (current.RecipientSnapshot is { } laterWinner) { result = Verified(laterWinner); return; }
                            token.ThrowIfCancellationRequested();
                            EnsureCombinedLive();
                            Copy(row, installed, scope.Value, checked(row.Revision + 1), WriteVersion(installed));
                            await context.SaveChangesAsync(token);
                            EnsureCombinedLive();
                            await transaction.CommitAsync(token);
                            result = Verified(installed.RecipientSnapshot!);

                            void EnsureCombinedLive()
                            {
                                if (subscriptions is null) return;
                                token.ThrowIfCancellationRequested();
                                if (deadlines.Any(deadline => timeProvider.GetUtcNow() >= deadline.ExpiresAt))
                                    throw new InvalidOperationException("An executable root lease expired before combined source acceptance.");
                                if (subscriptions.Triggers.SelectMany(trigger => trigger.Targets).Any(target =>
                                    subscriptions.Delivery.Recipients.Any(recipient => recipient.SubscriptionId == target.SubscriptionId) &&
                                    target.SourceReference?.IsLive(timeProvider.GetUtcNow()) != true))
                                    throw new InvalidDataException("An independent publication expired before combined source acceptance.");
                            }
                        }
                        catch
                        {
                            try { await transaction.RollbackAsync(CancellationToken.None); } catch { }
                            throw;
                        }
                    }, cancellationToken);
                    context.ChangeTracker.Clear();
                    return result ?? throw new InvalidOperationException("Verified cohort installation did not produce a persisted result.");
                }
                catch (Exception exception) when (exception is DbUpdateConcurrencyException ||
                    EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
                {
                    lastConflict = exception; context.ChangeTracker.Clear();
                }
            }
            throw new IOException("The recipient source remained contended; retry without dispatching.", lastConflict);

            async ValueTask<(RuntimePostCommitOutboxItem Source, WorkflowExecutionState Publisher)> ReadFacts(CancellationToken token = default)
            {
                if (token == default) token = cancellationToken;
                var row = await LoadAsync(scope.Value, source.SourceOutboxItemId, tracking: false, token) ??
                    throw new InvalidDataException("The exact recipient source is missing.");
                var publisherRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.Id == EfWorkflowExecutionStateStore.CreateId(scope.Value, source.Intent.WorkflowExecutionId), token) ??
                    throw new InvalidDataException("The authoritative publisher is missing.");
                var publisher = EfWorkflowExecutionStateStore.ReadChecked(publisherRow, scope.Value, source.Intent.WorkflowExecutionId);
                if (ReadChecked(row, scope.Value, source.SourceOutboxItemId).SubscriptionDeliveryProtocolVersion is null &&
                    !EfWorkflowExecutionStateStore.HasBoundIngressScope(publisher, scope.Value))
                    access.EnsureTenantScope(publisher.TenantId);
                row = await LoadAsync(scope.Value, source.SourceOutboxItemId, tracking: false, token) ??
                    throw new InvalidDataException("The recipient source was removed during publisher validation.");
                return (ReadChecked(row, scope.Value, source.SourceOutboxItemId), publisher);
            }
            static StimulusDeliverySnapshot Verified(StimulusDeliverySnapshot snapshot) =>
                snapshot.RetentionAcceptanceVersion == StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion ? snapshot :
                    throw new InvalidDataException("The existing data-only cohort has no verified retention acceptance; it must not be restamped.");
        }
        catch (RuntimePostCommitOutboxStaleClaimException) { context.ChangeTracker.Clear(); throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception)
        {
            context.ChangeTracker.Clear();
            throw new StimulusDeliveryBoundaryException(source.SourceOutboxItemId, exception,
                exception is WorkflowExecutableRootWriteLeaseLostException or WorkflowExecutableRootWriteLeaseUnavailableException ||
                EfRelationalExceptionClassifier.IsProviderFailure(exception) ||
                exception is not (InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or WorkflowExecutableReferenceRejectedException));
        }
    }

    async ValueTask<PostCommitDeliveryContext> IStimulusDeliveryStore.ReadSourceAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var access = accessContextAccessor.Current; var scope = access.RequireScope(); access.EnsureScope(source.Scope);
            EnsureCommittedSourceBoundary();
            if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Canonical stimulus source reads require a clean context.");
            var row = await LoadAsync(scope.Value, source.SourceOutboxItemId, tracking: false, cancellationToken)
                ?? throw new InvalidDataException("The exact stimulus source is missing.");
            var current = ReadChecked(row, scope.Value, source.SourceOutboxItemId);
            var publisherRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == EfWorkflowExecutionStateStore.CreateId(scope.Value, current.RequireWorkflowIntent().WorkflowExecutionId), cancellationToken)
                ?? throw new InvalidDataException("The authoritative stimulus publisher is missing.");
            var publisher = EfWorkflowExecutionStateStore.ReadChecked(publisherRow, scope.Value, current.RequireWorkflowIntent().WorkflowExecutionId);
            if (current.SubscriptionDeliveryProtocolVersion is null && !EfWorkflowExecutionStateStore.HasBoundIngressScope(publisher, scope.Value))
                access.EnsureTenantScope(publisher.TenantId);
            row = await LoadAsync(scope.Value, source.SourceOutboxItemId, tracking: false, cancellationToken)
                ?? throw new InvalidDataException("The stimulus source was removed during publisher validation.");
            current = ReadChecked(row, scope.Value, source.SourceOutboxItemId);
            if (current.SubscriptionDeliveryProtocolVersion is not null && (current.RecipientSnapshot is not null || current.SubscriptionDelivery is not null))
                RuntimeIngressAdmissionValidation.CheckSource(current, new(scope, publisher.TenantId));
            EnsureCommittedSourceBoundary();
            if (context.ChangeTracker.HasChanges() || accessContextAccessor.Current.RequireScope() != scope)
                throw new InvalidOperationException("The canonical stimulus source access boundary changed during validation.");
            return current.OpenStimulusDeliverySource(source, publisher, scope, timeProvider.GetUtcNow());
        }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        { throw new StimulusDeliveryBoundaryException(source.SourceOutboxItemId, exception,
            EfRelationalExceptionClassifier.IsProviderFailure(exception) ||
            exception is not (InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException)); }
    }

    public async ValueTask<PostCommitDeliveryContext> ReadSavedStartSourceAsync(WorkflowExecutableStimulusDeliveryAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority); cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var access = accessContextAccessor.Current; var scope = access.RequireScope(); access.EnsureScope(authority.Scope);
            EnsureCommittedSourceBoundary();
            var row = await LoadAsync(scope.Value, authority.SourceOutboxItemId, tracking: false, cancellationToken)
                ?? throw new InvalidDataException("The exact saved-start source is missing.");
            var current = ReadChecked(row, scope.Value, authority.SourceOutboxItemId);
            var publisherRow = await context.WorkflowExecutionStates.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == EfWorkflowExecutionStateStore.CreateId(scope.Value, current.RequireWorkflowIntent().WorkflowExecutionId), cancellationToken)
                ?? throw new InvalidDataException("The authoritative saved-start publisher is missing.");
            var publisher = EfWorkflowExecutionStateStore.ReadChecked(publisherRow, scope.Value, current.RequireWorkflowIntent().WorkflowExecutionId);
            if (current.SubscriptionDeliveryProtocolVersion is null && !EfWorkflowExecutionStateStore.HasBoundIngressScope(publisher, scope.Value))
                access.EnsureTenantScope(publisher.TenantId);
            // Publisher lookup awaited a separate read. Do not authorize the earlier claim snapshot if a peer
            // completed/reclaimed the source meanwhile, even when its old visibility deadline is still future.
            row = await LoadAsync(scope.Value, authority.SourceOutboxItemId, tracking: false, cancellationToken)
                ?? throw new InvalidDataException("The saved-start source was removed during publisher validation.");
            current = ReadChecked(row, scope.Value, authority.SourceOutboxItemId);
            if (current.SubscriptionDeliveryProtocolVersion is not null)
                RuntimeIngressAdmissionValidation.CheckSource(current, new(scope, publisher.TenantId));
            EnsureCommittedSourceBoundary();
            return current.OpenSavedStartSource(authority, publisher, scope, timeProvider.GetUtcNow());
        }
        catch (RuntimePostCommitOutboxStaleClaimException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        { throw new StimulusDeliveryBoundaryException(authority.SourceOutboxItemId, exception,
            EfRelationalExceptionClassifier.IsProviderFailure(exception) ||
            exception is not (InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException)); }
    }

    public async ValueTask<WorkflowDispatchRedriveResult> RedriveAsync(
        WorkflowDispatchRedriveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var dispatchRow = await context.WorkflowDispatches.SingleOrDefaultAsync(
            candidate => candidate.Id == WorkflowDispatchEfSupport.RowId(scope, request.DispatchId) &&
                         candidate.ScopeKey == EfRuntimeOperationalStoreSupport.Encode(scope) &&
                         candidate.ScopeKeyHash == EfRuntimeOperationalStoreSupport.Hash(scope), cancellationToken);
        var dispatch = dispatchRow is null ? null : WorkflowDispatchEfSupport.ReadChecked(dispatchRow, scope, request.DispatchId);
        if (dispatch is not null)
            accessContextAccessor.Current.EnsureTenantScope(dispatch.TenantId);
        var deadLetterId = dispatch is null ? null : WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch);
        var deadLetterRow = deadLetterId is null ? null : await LoadAsync(scope, deadLetterId, tracking: true, cancellationToken);
        var deadLetter = deadLetterRow is null ? null : ReadChecked(deadLetterRow, scope, deadLetterId);
        var transition = WorkflowDispatchRedriveTransitions.Evaluate(request, dispatch, deadLetter);
        if (!transition.HasMutation)
            return transition.Result;

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            WorkflowDispatchEfSupport.Copy(dispatchRow!, transition.WorkflowDispatch!, scope, checked(dispatchRow!.Revision + 1));
            Copy(deadLetterRow!, transition.OutboxItem!, scope, checked(deadLetterRow!.Revision + 1), WriteVersion(transition.OutboxItem!));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return transition.Result;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackAndDetachAsync(transaction, dispatchRow, deadLetterRow);
            throw new InvalidOperationException($"Workflow dispatch '{request.DispatchId}' changed concurrently; retry redrive.", exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollbackAndDetachAsync(transaction, dispatchRow, deadLetterRow);
            throw;
        }
    }

    internal static RuntimePostCommitOutboxEntity ToEntity(
        RuntimePostCommitOutboxItem item, string scope, string id, long revision, string writeVersion) =>
        RuntimePostCommitOutboxRow.ToEntity(item, scope, id, revision, writeVersion);

    internal static void Copy(
        RuntimePostCommitOutboxEntity row, RuntimePostCommitOutboxItem item, string scope, long revision, string writeVersion) =>
        RuntimePostCommitOutboxRow.Copy(row, item, scope, revision, writeVersion);

    internal static RuntimePostCommitOutboxItem ReadChecked(
        RuntimePostCommitOutboxEntity row, string scope, string? expectedOutboxItemId = null) =>
        RuntimePostCommitOutboxRow.ReadChecked(row, scope, expectedOutboxItemId);

    internal static string RowId(string scope, string outboxItemId) =>
        RuntimePostCommitOutboxRow.RowId(scope, outboxItemId);

    internal static string OutboxOrderKey(string value) =>
        RuntimePostCommitOutboxRow.OutboxOrderKey(value);

    public ValueTask SavePendingAsync(RuntimePostCommitOutboxItem item, CancellationToken cancellationToken = default) =>
        Storage.SavePendingAsync(item, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
        Storage.FindAsync(outboxItemId, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
        Storage.FindDurableAsync(outboxItemId, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxItem?> FindUndeliveredDurableAsync(string workflowExecutionId,
        IReadOnlyCollection<string> excludedIntentKinds, CancellationToken cancellationToken = default) =>
        Storage.FindUndeliveredDurableAsync(workflowExecutionId, excludedIntentKinds, cancellationToken);

    public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) =>
        Storage.ListPinnedExecutableArtifactIdsAsync(cancellationToken);

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query,
        CancellationToken cancellationToken = default) => Storage.GetDeliverableAsync(query, cancellationToken);

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(RuntimePostCommitOutboxClaimRequest request,
        CancellationToken cancellationToken = default) => Storage.ClaimAsync(request, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(RuntimePostCommitOutboxClaim claim, DateTimeOffset now,
        TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) => Storage.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

    public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListClaimedAsync(RuntimePostCommitOutboxClaimedQuery query,
        CancellationToken cancellationToken = default) => Storage.ListClaimedAsync(query, cancellationToken);

    public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) =>
        Storage.RecordDeliveryResultAsync(claim, result, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken = default) =>
        Storage.RecordDeliveryResultAsync(result, cancellationToken);

    public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(RuntimePostCommitOutboxClaimCompletion completion,
        CancellationToken cancellationToken = default) => Storage.CompleteClaimAsync(completion, cancellationToken);

    internal static ValueTask<IReadOnlyList<RuntimePostCommitOutboxItem>> ReadRetentionSourcesAsync(RuntimeDbContext context,
        string scope, CancellationToken cancellationToken) => EfRuntimePostCommitOutboxStorage.ReadRetentionSourcesAsync(context, scope, cancellationToken);

    internal static void ValidatePending(RuntimePostCommitOutboxItem item) => EfRuntimePostCommitOutboxStorage.ValidatePending(item);

    private void EnsureCommittedSourceBoundary() => Storage.EnsureCommittedSourceBoundary();
    private ValueTask<RuntimePostCommitOutboxEntity?> LoadAsync(string scope, string outboxItemId, bool tracking, CancellationToken cancellationToken) =>
        Storage.LoadAsync(scope, outboxItemId, tracking, cancellationToken);
    private string WriteVersion(RuntimePostCommitOutboxItem item) => Storage.WriteVersion(item);
    private ValueTask RollbackAndDetachAsync(IDbContextTransaction transaction, params object?[] entities) =>
        Storage.RollbackAndDetachAsync(transaction, entities);
}
