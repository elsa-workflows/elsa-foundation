using System.Transactions;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Opt-in EF Core checkpoint writer for bounded Runtime participant slices.</summary>
/// <remarks>
/// The durable replay marker, supported participant projections, and execution fence share one transaction.
/// Creating or changing an executable root runs inside its established write-lease boundary. An eligible unchanged
/// native root can instead use an update-only check inside the fenced transaction; replay remains resolvable before
/// a lease is required. Unsupported participants are rejected explicitly until their EF adapters
/// can stage changes through this same context.
/// </remarks>
public sealed class EfRuntimeCheckpointCommitStore(
    RuntimeDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    TimeProvider? timeProvider = null,
    IWorkflowExecutableRootWriteLeaseManager? rootWriteLeaseManager = null) : IRuntimeCheckpointCommitStore
{
    internal IRuntimeCoalescingSessionAccessor? CoalescingSessions { private get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    bool IRuntimeCheckpointCommitStore.SupportsExactBookmarkResume => true;
    bool IRuntimeCheckpointCommitStore.SupportsModelCheckpoint => true;
    bool IRuntimeCheckpointCommitStore.SupportsModelParentAdmission => true;
    bool IRuntimeCheckpointCommitStore.CanCommitModelState(IRuntimeModelExecutionReader reader) =>
        reader is EfRuntimeModelExecutionReader selected && selected.Uses(context, accessContextAccessor);
    bool IRuntimeCheckpointCommitStore.SupportsModelAdmission => true;
    bool IRuntimeCheckpointCommitStore.CanCommitModelAdmission(IRuntimeModelExecutionReader reader, IWorkflowExecutionStateStore executions,
        IActivityExecutionStateStore activities, IRuntimePostCommitOutboxStore outbox)
    {
        if (reader is not EfRuntimeModelExecutionReader selected || !selected.Uses(context, accessContextAccessor) ||
            executions is not EfWorkflowExecutionStateStore native || activities is not EfActivityExecutionStateStore activity ||
            outbox is not EfRuntimePostCommitOutboxStore source || !ReferenceEquals(native.Context, context) ||
            !ReferenceEquals(activity.Context, context)) return false;
        try
        {
            var scope = accessContextAccessor.Current.RequireScope();
            return native.AccessContext.RequireScope() == scope && activity.AccessContext.RequireScope() == scope && source.Uses(context, scope);
        }
        catch (InvalidOperationException) { return false; }
    }

    bool IRuntimeCheckpointCommitStore.CanCommitExactBookmarkResume(IBookmarkStateStore bookmarks,
        IWorkflowExecutionStateStore physicalExecutions, IActivityExecutionStateStore activities)
    {
        if (bookmarks is not EfBookmarkStateStore bookmarkStore || physicalExecutions is not EfWorkflowExecutionStateStore executions ||
            activities is not EfActivityExecutionStateStore activityStore || !ReferenceEquals(bookmarkStore.Context, context) ||
            !ReferenceEquals(executions.Context, context) || !ReferenceEquals(activityStore.Context, context)) return false;
        try
        {
            var scope = accessContextAccessor.Current.RequireScope();
            return bookmarkStore.AccessContext.RequireScope() == scope && executions.AccessContext.RequireScope() == scope &&
                activityStore.AccessContext.RequireScope() == scope;
        }
        catch (InvalidOperationException) { return false; }
    }

    private async ValueTask StageExactBookmarkResumeAsync(RuntimeCheckpointCommit commit, string scope,
        Dictionary<string, WorkflowTestScopeRecord> touchedTestScopes, CancellationToken cancellationToken)
    {
        if (commit.ExpectedBookmarkResume is not { } expected) return;
        commit.EnsureExactBookmarkResumeShape();
        if (accessContextAccessor.Current.RequireScope().Value != scope)
            throw new InvalidOperationException("The exact bookmark checkpoint changed physical scope.");
        var id = EfBookmarkStateStore.CreateId(scope, expected.WorkflowExecutionId, expected.BookmarkId);
        var row = await context.Bookmarks.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (row is null) throw new BookmarkResumeSupersededException(expected.WorkflowExecutionId, expected.BookmarkId);
        var bookmark = EfBookmarkStateStore.MapChecked(row, scope, expected.WorkflowExecutionId, expected.BookmarkId, id);
        var workflowId = EfWorkflowExecutionStateStore.CreateId(scope, expected.WorkflowExecutionId);
        var workflowRow = await context.WorkflowExecutionStates.SingleOrDefaultAsync(candidate => candidate.Id == workflowId, cancellationToken);
        var activityId = ActivityExecutionEfSupport.CreateId("state", scope, expected.WorkflowExecutionId, expected.ActivityExecutionId);
        var activityRow = await context.ActivityExecutionStates.SingleOrDefaultAsync(candidate => candidate.Id == activityId, cancellationToken);
        if (workflowRow is null || activityRow is null)
            throw new RuntimeSchedulerWorkUnavailableException("runtime.bookmark.owner-unavailable", "The saved wait's owning execution is unavailable.");
        var workflow = EfWorkflowExecutionStateStore.ReadChecked(workflowRow, scope, expected.WorkflowExecutionId);
        var activity = EfActivityExecutionStateStore.ReadChecked(activityRow, scope, expected.WorkflowExecutionId, expected.ActivityExecutionId);
        accessContextAccessor.Current.EnsureTenantScope(workflow.TenantId);
        var observed = new BookmarkStateObservation(bookmark, row.Revision, new PersistenceScope(scope));
        if (!expected.MatchesAdmission(observed, workflow, activity, new(scope), _timeProvider.GetUtcNow()))
            throw new BookmarkResumeSupersededException(expected.WorkflowExecutionId, expected.BookmarkId);
        if (workflow.TestScope is { } testScope)
        {
            try { await EfRuntimeCheckpointTestScopeParticipantStaging.AssertOpenAndStageAsync(context, testScope,
                _timeProvider.GetUtcNow(), scope, touchedTestScopes, cancellationToken); }
            catch (TestScopeAdmissionException) { throw new BookmarkResumeSupersededException(expected.WorkflowExecutionId, expected.BookmarkId); }
        }
        // Protect the exact generation and both live owner headers even when this checkpoint changes only
        // activity state. Normal participant staging reuses these tracked rows and their original CAS revisions.
        workflowRow.SchemaVersion = ModelWorkWriteVersion.For(context, workflow, workflowRow.SchemaVersion);
        row.Revision = checked(row.Revision + 1);
        workflowRow.Revision = checked(workflowRow.Revision + 1);
        activityRow.Revision = checked(activityRow.Revision + 1);
    }

    private void EnsureExactBookmarkDeadline(RuntimeCheckpointCommit commit)
    {
        var expected = commit.ExpectedBookmarkResume ?? commit.ExpectedModelParentWait;
        if (expected is null) return;
        var now = _timeProvider.GetUtcNow();
        if (expected.ExpiresAt <= now || expected.RegistrationCapture?.Scope.TestScope?.IsExpired(now) == true)
            throw new BookmarkResumeSupersededException(expected.WorkflowExecutionId, expected.BookmarkId);
    }

    private async ValueTask StageModelParentWaitAsync(RuntimeCheckpointCommit commit, string scope,
        Dictionary<string, WorkflowTestScopeRecord> touchedTestScopes, CancellationToken cancellationToken)
    {
        if (commit.ExpectedModelParentWait is not { } expected) return;
        commit.EnsureModelParentAdmissionShape();
        var bookmarkId = EfBookmarkStateStore.CreateId(scope, expected.WorkflowExecutionId, expected.BookmarkId);
        var bookmarkRow = await context.Bookmarks.SingleOrDefaultAsync(row => row.Id == bookmarkId, cancellationToken);
        var workflowId = EfWorkflowExecutionStateStore.CreateId(scope, expected.WorkflowExecutionId);
        var workflowRow = await context.WorkflowExecutionStates.SingleOrDefaultAsync(row => row.Id == workflowId, cancellationToken);
        var activityId = ActivityExecutionEfSupport.CreateId("state", scope, expected.WorkflowExecutionId, expected.ActivityExecutionId);
        var activityRow = await context.ActivityExecutionStates.SingleOrDefaultAsync(row => row.Id == activityId, cancellationToken);
        if (bookmarkRow is null || workflowRow is null || activityRow is null)
            throw new RuntimeSchedulerWorkUnavailableException("runtime.model.parent-wait-unavailable", "The native parent wait is no longer available for model admission.");
        var workflow = EfWorkflowExecutionStateStore.ReadChecked(workflowRow, scope, expected.WorkflowExecutionId);
        var activity = EfActivityExecutionStateStore.ReadChecked(activityRow, scope, expected.WorkflowExecutionId, expected.ActivityExecutionId);
        var bookmark = EfBookmarkStateStore.MapChecked(bookmarkRow, scope, expected.WorkflowExecutionId, expected.BookmarkId, bookmarkId);
        var state = commit.ModelState!.State;
        if (!expected.MatchesAdmission(new(bookmark, bookmarkRow.Revision, new(scope)), workflow, activity, new(scope), _timeProvider.GetUtcNow()) ||
            workflow.TenantId != state.TenantId || workflow.Authority is not { } authority ||
            !WorkflowExecutionAuthoritySnapshot.Matches(state.Authority, authority.SystemIdentity, authority.RootInitiator, authority.Metadata))
            throw new RuntimeSchedulerWorkUnavailableException("runtime.model.parent-wait-unavailable", "The native parent wait changed before model admission.");
        if (workflow.TestScope is { } testScope)
            await EfRuntimeCheckpointTestScopeParticipantStaging.AssertOpenAndStageAsync(context, testScope,
                _timeProvider.GetUtcNow(), scope, touchedTestScopes, cancellationToken);
        // Order cancellation and child admission without consuming or changing the real parent wait's generation.
        workflowRow.SchemaVersion = ModelWorkWriteVersion.For(context, workflow, workflowRow.SchemaVersion);
        context.Entry(bookmarkRow).Property(row => row.Revision).IsModified = true;
        context.Entry(workflowRow).Property(row => row.Revision).IsModified = true;
        context.Entry(activityRow).Property(row => row.Revision).IsModified = true;
    }

    public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        try { return await CommitCoreAsync(commit, decision, cancellationToken); }
        catch (Exception exception) when ((commit.ExpectedModelExecution is not null || commit.ExpectedModelParentWait is not null) &&
            (exception is IOException or DbUpdateConcurrencyException or
                WorkflowExecutableRootWriteLeaseLostException or WorkflowExecutableRootWriteLeaseUnavailableException ||
             exception is OperationCanceledException && !cancellationToken.IsCancellationRequested ||
             EfRelationalExceptionClassifier.IsProviderFailure(exception)))
        {
            throw new RuntimeSchedulerWorkUnavailableException(commit.ExpectedModelParentWait is null
                    ? "runtime.model.native-admission-unavailable" : "runtime.model.parent-admission-unavailable",
                "The authorizing execution changed or is unavailable; cross-model admission remains owed.", exception);
        }
        catch (Exception exception) when (commit.ExpectedBookmarkResume is not null &&
            (exception is IOException or DbUpdateConcurrencyException or
                WorkflowExecutableRootWriteLeaseLostException or WorkflowExecutableRootWriteLeaseUnavailableException ||
             exception is OperationCanceledException && !cancellationToken.IsCancellationRequested ||
             EfRelationalExceptionClassifier.IsProviderFailure(exception)))
        {
            throw new RuntimeSchedulerWorkUnavailableException("runtime.bookmark.admission-unavailable",
                "Exact bookmark admission is unavailable.", exception);
        }
        catch (Exception exception) when (commit.ModelState is not null && exception is not DbUpdateConcurrencyException &&
            (exception is IOException or TimeoutException ||
             exception is OperationCanceledException && !cancellationToken.IsCancellationRequested ||
             EfRelationalExceptionClassifier.IsProviderFailure(exception)))
        {
            throw new RuntimeSchedulerWorkUnavailableException("runtime.model.checkpoint-unavailable",
                $"Model checkpoint '{commit.CommitId}' for execution '{commit.WorkflowExecutionId}', input '{commit.ModelState.Input.InputId}' " +
                $"in scope '{commit.ModelState.State.Scope.Value}' is unavailable.", exception);
        }
    }

    private async ValueTask<RuntimeCheckpointCommitStoreResult> CommitCoreAsync(
        RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.CommitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.WorkflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        commit.EnsureModelStateShape();
        commit.EnsureModelAdmissionShape();
        commit.EnsureModelParentAdmissionShape();
        if (commit.ExpectedModelExecution is { } expectedModel)
        {
            accessContextAccessor.Current.EnsureScope(expectedModel.Scope);
            accessContextAccessor.Current.EnsureTenantScope(expectedModel.TenantId);
            EfRuntimeOperationalStoreSupport.ValidateIdentity(expectedModel.ExecutionId, nameof(expectedModel.ExecutionId));
            if (decision.Mode != RuntimeCheckpointPersistenceMode.Immediate)
                throw new NotSupportedException("Model-owned native worker admission requires an immediate physical checkpoint.");
        }
        if (commit.ModelState is { } model)
        {
            accessContextAccessor.Current.EnsureScope(model.State.Scope);
            accessContextAccessor.Current.EnsureTenantScope(model.State.TenantId);
            EfRuntimeOperationalStoreSupport.ValidateIdentity(model.State.ExecutionId, nameof(model.State.ExecutionId));
            EfRuntimeOperationalStoreSupport.ValidateIdentity(model.Input.InputId, nameof(model.Input.InputId));
            if (decision.Mode != RuntimeCheckpointPersistenceMode.Immediate)
                throw new NotSupportedException("Model decisions require an immediate physical checkpoint.");
        }

        var scope = EfRuntimeOperationalStoreSupport.RequireScope(accessContextAccessor);
        var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, commit.CommitId);

        // The commit's structural rules were applied by RuntimeCheckpointCommitValidator in the application layer before
        // the commit reached this store. What is checked here is only reserved-key integrity and this provider's storage
        // limits and capability, still before marker reads, transaction creation, or any other provider I/O.
        RuntimeExecutionOwnershipStateId.EnsureNotWritten(commit.StateChanges.Operational);
        ValidateStorageLimits(commit);
        ValidateThinSlice(commit);
        if (commit.StateChanges.WorkflowExecution is { } workflowExecution)
        {
            accessContextAccessor.Current.EnsureTenantScope(workflowExecution.State.TenantId);
            if (workflowExecution.State.TestScope is { } testScope)
                accessContextAccessor.Current.EnsureTenantScope(testScope.TenantId);
        }
        foreach (var dispatch in commit.StateChanges.WorkflowDispatches)
        {
            accessContextAccessor.Current.EnsureTenantScope(dispatch.State.TenantId);
            if (dispatch.State.TestScope is { } testScope)
                accessContextAccessor.Current.EnsureTenantScope(testScope.TenantId);
        }
        EfRuntimeAlterationCheckpointParticipationGate.Validate(context, commit, scope);

        var existing = await FindMarkerAsync(scope, commit.CommitId, cancellationToken);
        if (existing is not null)
        {
            var replay = ResolveReplay(commit, fingerprint, existing);
            EfRuntimeAlterationCheckpointParticipationGate.MarkDurable(context, commit, scope);
            return replay;
        }

        if (commit.StateChanges.WorkflowExecution?.State.ModelWorkOrigin is not null &&
            commit.Checkpoint.Name == Elsa.Workflows.Runtime.Core.Constants.RuntimeCheckpointNames.WorkflowStarted &&
            commit.ExpectedModelExecution is null)
            throw new NotSupportedException("Initial native model work requires its authorizing model checkpoint condition.");

        if ((commit.ExpectedBookmarkResume is not null || commit.ModelState is not null || commit.ExpectedModelExecution is not null) && (context.ChangeTracker.HasChanges() || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || System.Transactions.Transaction.Current is not null))
            throw new InvalidOperationException("Exact bookmark checkpoint admission requires a clean context and its own transaction.");

        var pinned = commit.ModelState?.State.Executable ?? commit.StateChanges.WorkflowExecution?.State.PinnedExecutable;
        if (pinned is not null)
        {
            if (rootWriteLeaseManager is null)
                throw new InvalidOperationException(
                    "A workflow execution checkpoint write requires IWorkflowExecutableRootWriteLeaseManager.");

            // The session only hints that a root was persisted. The actual row must still exist with the exact
            // same pin inside the fenced transaction, and its revision CAS must succeed. Never turn this path
            // into an unprotected insert if retention, alteration or another writer changed that row.
            if (commit.ModelState is null && commit.ExpectedModelExecution is null && commit.ExpectedModelParentWait is null &&
                commit.StateChanges.WorkflowExecution?.State.ModelWorkOrigin is null && commit.ExpectedFence is not null &&
                CoalescingSessions?.Current is { HasDurableRecord: true } session && session.WorkflowExecutionId == commit.WorkflowExecutionId &&
                !context.ChangeTracker.HasChanges() && context.Database.CurrentTransaction is null &&
                context.Database.GetEnlistedTransaction() is null && Transaction.Current is null)
            {
                try
                {
                    var updated = await CommitNewAsync(cancellationToken, pinned);
                    EfRuntimeAlterationCheckpointParticipationGate.MarkDurable(context, commit, scope);
                    return updated;
                }
                catch (EfRuntimeCheckpointParticipantStaging.ExistingRootLeaseRequiredException)
                {
                    // No SaveChanges occurred. Rollback/disposal completed before entering the ordinary leased path.
                }
            }

            RuntimeCheckpointCommitStoreResult? result = null;
            async ValueTask ExecuteCheckpointAsync(CancellationToken leaseCancellationToken) =>
                result = await CommitNewAsync(leaseCancellationToken);

            // The lease is this attempt's alone (#2286). Two attempts of one commit (a retry that overlaps a slow first
            // attempt) would otherwise share one lease, and the first to release it would leave the other's closure unfenced.
            await rootWriteLeaseManager.ExecuteAsync(
                pinned,
                $"checkpoint:{commit.CommitId}:{Guid.NewGuid():N}",
                ExecuteCheckpointAsync,
                cancellationToken);
            var completed = result ?? throw new InvalidOperationException("The checkpoint lease callback did not produce a result.");
            EfRuntimeAlterationCheckpointParticipationGate.MarkDurable(context, commit, scope);
            return completed;
        }

        var immediate = await CommitNewAsync(cancellationToken);
        EfRuntimeAlterationCheckpointParticipationGate.MarkDurable(context, commit, scope);
        return immediate;

        async ValueTask<RuntimeCheckpointCommitStoreResult> CommitNewAsync(CancellationToken writeCancellationToken, WorkflowExecutableIdentity? requiredExistingRoot = null)
        {
            var marker = ToEntity(commit, scope, id, fingerprint);
            var touchedTestScopes = new Dictionary<string, WorkflowTestScopeRecord>(StringComparer.Ordinal);

            await using var transaction = await context.Database.BeginTransactionAsync(writeCancellationToken);
            try
            {
                // Stage against the rows as they are now. An unchanged tracked entity is only a snapshot from earlier
                // work on this context, and one execution commits from more than one scope (a nested drain commits in
                // its own scope between two commits of the outer one); a stale snapshot would carry a superseded
                // revision into the concurrency check and refuse this commit. Pending sibling R14-R18 mutations that a
                // caller staged on this context are kept, so this is not a tracker clear.
                foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Unchanged).ToArray())
                    entry.State = EntityState.Detached;

                // Fence validation/touch is deliberately first. The workflow and scheduler rows then join the same
                // transaction, and the immutable marker is added only after every supported participant is staged.
                if (commit.ExpectedFence is { } expectedFence)
                {
                    await EfRuntimeCheckpointParticipantStaging.StageExecutionFenceAsync(
                        context,
                        scope,
                        commit.WorkflowExecutionId,
                        expectedFence,
                        _timeProvider,
                        writeCancellationToken);
                }

                await StageExactBookmarkResumeAsync(commit, scope, touchedTestScopes, writeCancellationToken);
                await StageModelParentWaitAsync(commit, scope, touchedTestScopes, writeCancellationToken);

                if (commit.ExpectedModelExecution is { } admission)
                {
                    var nativeId = EfWorkflowExecutionStateStore.CreateId(scope, commit.WorkflowExecutionId);
                    if (await context.WorkflowExecutionStates.AnyAsync(row => row.Id == nativeId, writeCancellationToken))
                        throw new InvalidOperationException("Model-owned native worker admission cannot replace an existing native execution.");
                    await EfModelCheckpointStaging.StageAdmissionAsync(context, admission, scope, writeCancellationToken);
                }

                if (commit.ModelState is { } modelChange)
                    await EfModelCheckpointStaging.StageAsync(context, modelChange, scope, writeCancellationToken);

                if (commit.StateChanges.WorkflowExecution is { } workflowChange)
                {
                    await EnsureNativeModelResultAsync(commit, workflowChange.State, scope, writeCancellationToken);
                    await EfRuntimeCheckpointParticipantStaging.StageWorkflowExecutionAsync(
                        context,
                        workflowChange,
                        scope,
                        commit.Metadata.ContainsKey(TestRunDeliveryReceipt.MetadataKey) ? _timeProvider.GetUtcNow() : commit.Checkpoint.OccurredAt,
                        touchedTestScopes,
                        writeCancellationToken,
                        requiredExistingRoot);
                }

                if (commit.StateChanges.Scheduler is { } schedulerChange)
                {
                    await EfRuntimeCheckpointParticipantStaging.StageSchedulerAsync(
                        context,
                        schedulerChange,
                        scope,
                        writeCancellationToken);
                }

                // Each participant type below stages its whole collection, because the insert/update/delete decision
                // needs only the row carrying each participant's immutable physical identity. Reading those one at a
                // time made a commit's round-trip count equal to its participant count, and no validator caps any of
                // these collections; the seams now take one chunked tracked load per type instead.
                await EfRuntimeCheckpointActivityExecutionParticipantStaging.StageActivityExecutionsAsync(
                    context, commit.StateChanges.ActivityExecutions, scope, commit.WorkflowExecutionId, writeCancellationToken);

                await EfRuntimeCheckpointInspectionParticipantStaging.StageAsync(
                    context, commit.StateChanges.ActivityExecutionInspections, scope, commit.WorkflowExecutionId, writeCancellationToken);

                await EfRuntimeCheckpointIncidentParticipantStaging.StageIncidentsAsync(
                    context, commit.StateChanges.Incidents, scope, commit.WorkflowExecutionId, writeCancellationToken);

                await EfRuntimeCheckpointRunHealthParticipantStaging.StageWorkflowRunHealthAsync(
                    context, commit.WorkflowExecutionId, commit.StateChanges.WorkflowExecution,
                    commit.StateChanges.Incidents, scope, writeCancellationToken);

                await EfRuntimeCheckpointParticipantStaging.StageBookmarksAsync(
                    context, commit.StateChanges.Bookmarks, scope, writeCancellationToken);

                await EfRuntimeCheckpointParticipantStaging.StageDurableValuesAsync(
                    context, commit.StateChanges.DurableValues, scope, writeCancellationToken);

                await EfRuntimeCheckpointActivityScopeCleanupParticipantStaging.StageAsync(
                    context, commit.StateChanges.ActivityScopeCleanups, scope, writeCancellationToken);

                await EfRuntimeCheckpointParticipantStaging.StageOperationalAsync(
                    context, commit.StateChanges.Operational, scope, writeCancellationToken);

                await EfRuntimeCheckpointDispatchParticipantStaging.StageAsync(
                    context,
                    commit,
                    scope,
                    touchedTestScopes,
                    writeCancellationToken);

                await EfRuntimeCheckpointBackgroundActivityParticipantStaging.StageAsync(
                    context,
                    commit,
                    scope,
                    writeCancellationToken);

                await EfRuntimeCheckpointOutboxParticipantStaging.StageAsync(
                    context,
                    commit,
                    scope,
                    writeCancellationToken);

                foreach (var consumed in commit.StateChanges.ConsumedSchedulerWorkItems)
                    await EfRuntimeCheckpointParticipantStaging.StageConsumedSchedulerWorkAsync(
                        context, consumed, scope, writeCancellationToken);

                if (commit.StateChanges.AlterationJobTerminalChange is { } terminalJob)
                    await EfRuntimeCheckpointAlterationJobParticipantStaging.StageAsync(
                        context, terminalJob, scope, commit.WorkflowExecutionId, writeCancellationToken);

                // Flush all participant rows first, then add the immutable marker as the final write in this
                // transaction. The marker is the durable commit proof, so it must never precede a participant failure.
                EnsureExactBookmarkDeadline(commit);
                await context.SaveChangesAsync(writeCancellationToken);
                context.RuntimeCheckpointCommits.Add(marker);
                await context.SaveChangesAsync(writeCancellationToken);
                EnsureExactBookmarkDeadline(commit);
                await transaction.CommitAsync(writeCancellationToken);
                return ResultFor(commit, marker);
            }
            catch (EfRuntimeCheckpointParticipantStaging.ExistingRootLeaseRequiredException)
            {
                await RollbackAndRestoreAsync(transaction);
                throw;
            }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
            {
                await RollbackAndRestoreAsync(transaction);
                var winner = await ReconcileMarkerAsync(exception, writeCancellationToken);
                if (winner is not null)
                    return ResolveReplay(commit, fingerprint, winner);
                if (commit.ExpectedBookmarkResume is not null || commit.ExpectedModelParentWait is not null)
                    throw new RuntimeSchedulerWorkUnavailableException("runtime.bookmark.admission-unavailable", "Exact bookmark admission changed concurrently.", exception);
                throw;
            }
            catch (OperationCanceledException)
            {
                await RollbackAndRestoreAsync(transaction);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RollbackAndRestoreAsync(transaction);

                // A provider can acknowledge the commit ambiguously. The immutable marker is the only authoritative
                // reconciliation signal: if it is visible, return the original result; otherwise preserve the failure.
                var reconciled = await ReconcileMarkerAsync(exception, writeCancellationToken);
                if (reconciled is not null)
                    return ResolveReplay(commit, fingerprint, reconciled);
                if (commit.ExpectedBookmarkResume is not null && (exception is DbUpdateConcurrencyException or IOException ||
                    EfRelationalExceptionClassifier.IsProviderFailure(exception)))
                    throw new RuntimeSchedulerWorkUnavailableException("runtime.bookmark.admission-unavailable", "Exact bookmark admission is unavailable.", exception);
                throw;
            }
        }

        async ValueTask<RuntimeCheckpointCommitEntity?> ReconcileMarkerAsync(Exception original, CancellationToken token)
        {
            try { return await FindMarkerAsync(scope, commit.CommitId, token); }
            catch (Exception lookupFailure) when (commit.ExpectedBookmarkResume is not null &&
                (lookupFailure is IOException || lookupFailure is OperationCanceledException && !token.IsCancellationRequested ||
                    EfRelationalExceptionClassifier.IsProviderFailure(lookupFailure)))
            {
                throw new RuntimeSchedulerWorkUnavailableException("runtime.bookmark.admission-unavailable",
                    "Exact bookmark admission could not reconcile its durable checkpoint.", new AggregateException(original, lookupFailure));
            }
        }
    }

    /// <summary>
    /// Identity lengths this provider's columns can hold for activity-scope cleanup targets. These describe EF's storage
    /// model, not the checkpoint contract, and are checked before any provider I/O.
    /// </summary>
    private static void ValidateStorageLimits(RuntimeCheckpointCommit commit)
    {
        foreach (var cleanup in commit.StateChanges.ActivityScopeCleanups)
        {
            foreach (var bookmarkId in cleanup.BookmarkIds)
            {
                if (bookmarkId.Length > BookmarkStateEfModule.BookmarkIdentityMaximumLength)
                    throw new ArgumentException("Activity-scope cleanup bookmark ID exceeds the bookmark persistence contract.");
            }
            foreach (var timerId in cleanup.TimerIds)
                EfRuntimeOperationalStoreSupport.ValidateIdentity(timerId, nameof(cleanup.TimerIds));
            foreach (var workItemId in cleanup.SchedulerWorkItemIds)
                EfRuntimeOperationalStoreSupport.ValidateIdentity(workItemId, nameof(cleanup.SchedulerWorkItemIds));
        }
    }

    private async ValueTask<RuntimeCheckpointCommitEntity?> FindMarkerAsync(
        string scope,
        string commitId,
        CancellationToken cancellationToken)
    {
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, commitId);
        var row = await context.RuntimeCheckpointCommits.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return row is null ? null : ReadChecked(row, scope, commitId);
    }

    private async ValueTask EnsureNativeModelResultAsync(RuntimeCheckpointCommit commit, WorkflowExecutionState state,
        string scope, CancellationToken cancellationToken)
    {
        if (state.ModelWorkOrigin is not { } origin || !state.Status.IsTerminal()) return;
        var id = EfWorkflowExecutionStateStore.CreateId(scope, state.WorkflowExecutionId);
        var prior = await context.WorkflowExecutionStates.SingleOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (prior is not null && EfWorkflowExecutionStateStore.ReadChecked(prior, scope, state.WorkflowExecutionId).Status.IsTerminal()) return;
        // The committer materializes intents before this physical boundary. Balanced persistence can fold
        // those outbox changes and clear their original intent list; only the applied changes prove delivery.
        if (!commit.StateChanges.PostCommitOutbox.Any(change => change.Operation == RuntimeStateChangeOperation.Upsert &&
            change.State.Intent.Kind == origin.ResultIntentKind && change.State.Intent.WorkflowExecutionId == state.WorkflowExecutionId &&
            change.State.Intent.Payload is { ValueKind: JsonValueKind.Object } &&
            change.State.StimulusDeliverySourceScope == origin.Scope &&
            change.State.Status is RuntimePostCommitOutboxStatus.Pending or RuntimePostCommitOutboxStatus.Delivering or RuntimePostCommitOutboxStatus.FailedRetryable))
            throw new NotSupportedException("Native model work cannot become terminal without its atomic result-delivery responsibility.");
    }

    private static void ValidateThinSlice(RuntimeCheckpointCommit commit)
    {
        var changes = commit.StateChanges;
        if (changes.BackgroundActivityWork.Admissions.Count > 0 && commit.ExpectedFence is null)
            throw new RuntimeCheckpointCommitValidationException("Background activity work admission requires the current workflow execution fence.");
        if (changes.WorkflowExecution is { Operation: not RuntimeStateChangeOperation.Upsert })
            throw new NotSupportedException("The R19 EF checkpoint slice supports workflow-execution upserts only.");
        if (changes.Scheduler is { Operation: not RuntimeStateChangeOperation.Upsert })
            throw new NotSupportedException("The R19 EF checkpoint slice supports scheduler upserts only.");
    }

    private static RuntimeCheckpointCommitEntity ToEntity(
        RuntimeCheckpointCommit commit,
        string scope,
        string id,
        string fingerprint) => new()
    {
        Id = id,
        ScopeKey = EfRuntimeOperationalStoreSupport.Encode(scope),
        ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash(scope),
        CommitId = EfRuntimeOperationalStoreSupport.Encode(commit.CommitId),
        CommitIdHash = EfRuntimeOperationalStoreSupport.Hash(commit.CommitId),
        WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode(commit.WorkflowExecutionId),
        WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash(commit.WorkflowExecutionId),
        WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order(commit.WorkflowExecutionId),
        OccurredAtUtcTicks = commit.Checkpoint.OccurredAt.UtcTicks,
        Fingerprint = fingerprint,
        ContentJson = JsonSerializer.Serialize(
            new MarkerDocument(
                commit.CommitId,
                commit.WorkflowExecutionId,
                commit.Checkpoint.OccurredAt,
                fingerprint,
                PendingOutboxIds(commit),
                ConsumedSchedulerWorkIds(commit)),
            JsonOptions),
        PendingPostCommitWorkIdsJson = JsonSerializer.Serialize(PendingOutboxIds(commit), JsonOptions),
        ConsumedSchedulerWorkItemIdsJson = JsonSerializer.Serialize(ConsumedSchedulerWorkIds(commit), JsonOptions),
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private static string[] PendingOutboxIds(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.PostCommitOutbox.Select(change => change.StateId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static string[] ConsumedSchedulerWorkIds(RuntimeCheckpointCommit commit) =>
        commit.StateChanges.ConsumedSchedulerWorkItems.Select(item => item.WorkItemId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    internal static RuntimeCheckpointCommitEntity ReadChecked(
        RuntimeCheckpointCommitEntity row,
        string scope,
        string expectedCommitId)
    {
        if (EfSchemaVersion.NotReadable(RuntimeOperationalStateEfModule.Chain, row.SchemaVersion) ||
            row.Revision != 1 ||
            row.ScopeKey != EfRuntimeOperationalStoreSupport.Encode(scope) ||
            row.ScopeKeyHash != EfRuntimeOperationalStoreSupport.Hash(scope) ||
            row.CommitId != EfRuntimeOperationalStoreSupport.Encode(expectedCommitId) ||
            row.CommitIdHash != EfRuntimeOperationalStoreSupport.Hash(expectedCommitId) ||
            row.Id != EfRuntimeOperationalStoreSupport.CompositeId(scope, expectedCommitId))
        {
            throw new InvalidDataException("The persisted runtime checkpoint marker identity or scope projection is corrupt.");
        }

        if (string.IsNullOrWhiteSpace(row.WorkflowExecutionId) ||
            row.WorkflowExecutionIdHash != EfRuntimeOperationalStoreSupport.Hash(EfRuntimeOperationalStoreSupport.Decode(row.WorkflowExecutionId)) ||
            row.WorkflowExecutionIdOrderKey != EfRuntimeOperationalStoreSupport.Order(EfRuntimeOperationalStoreSupport.Decode(row.WorkflowExecutionId)) ||
            string.IsNullOrWhiteSpace(row.Fingerprint) ||
            row.Fingerprint.Length != 64)
        {
            throw new InvalidDataException("The persisted runtime checkpoint marker projection is corrupt.");
        }

        var workflowExecutionId = EfRuntimeOperationalStoreSupport.Decode(row.WorkflowExecutionId);
        EfSchemaRowContent upcast;
        MarkerDocument content;
        try
        {
            upcast = Content(row);
            content = JsonSerializer.Deserialize<MarkerDocument>(upcast[nameof(row.ContentJson)]!, JsonOptions)
                      ?? throw new InvalidDataException();
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted runtime checkpoint marker content is invalid.", exception);
        }

        if (!StringComparer.Ordinal.Equals(content.CommitId, expectedCommitId) ||
            !StringComparer.Ordinal.Equals(content.WorkflowExecutionId, workflowExecutionId) ||
            content.OccurredAt.UtcTicks != row.OccurredAtUtcTicks ||
            !StringComparer.Ordinal.Equals(content.Fingerprint, row.Fingerprint) ||
            !IdsEqual(content.PendingPostCommitWorkIds, upcast[nameof(row.PendingPostCommitWorkIdsJson)]!, "pending post-commit work") ||
            !IdsEqual(content.ConsumedSchedulerWorkItemIds, upcast[nameof(row.ConsumedSchedulerWorkItemIdsJson)]!, "consumed scheduler work"))
        {
            throw new InvalidDataException("The persisted runtime checkpoint marker content does not match its projections.");
        }

        return row;
    }

    /// <summary>
    /// The content columns of the marker <paramref name="row"/>, upcast together from its stamp to the current version
    /// (#2144). The id sets restate the content document's, and are compared with it and returned to a
    /// replay, so they are content too (#2140).
    /// </summary>
    private static EfSchemaRowContent Content(RuntimeCheckpointCommitEntity row) =>
        RuntimeOperationalStateEfModule.Chain.Upcast<RuntimeCheckpointCommitEntity>(
            row.SchemaVersion,
            (nameof(row.ContentJson), row.ContentJson),
            (nameof(row.PendingPostCommitWorkIdsJson), row.PendingPostCommitWorkIdsJson),
            (nameof(row.ConsumedSchedulerWorkItemIdsJson), row.ConsumedSchedulerWorkItemIdsJson));

    private static bool IdsEqual(IReadOnlyCollection<string> content, string projectionJson, string label) =>
        content.SequenceEqual(DeserializeIds(projectionJson, label), StringComparer.Ordinal);

    private static RuntimeCheckpointCommitStoreResult ResolveReplay(
        RuntimeCheckpointCommit commit,
        string fingerprint,
        RuntimeCheckpointCommitEntity marker)
    {
        if (!StringComparer.Ordinal.Equals(marker.Fingerprint, fingerprint))
            throw new RuntimeCheckpointReplayConflictException(commit.CommitId);

        var workflowExecutionId = EfRuntimeOperationalStoreSupport.Decode(marker.WorkflowExecutionId);
        if (!StringComparer.Ordinal.Equals(workflowExecutionId, commit.WorkflowExecutionId) ||
            marker.OccurredAtUtcTicks != commit.Checkpoint.OccurredAt.UtcTicks)
        {
            throw new InvalidDataException("The persisted runtime checkpoint marker does not match its checkpoint identity.");
        }

        var upcast = Content(marker);
        return new RuntimeCheckpointCommitStoreResult(
            DeserializeIds(upcast[nameof(marker.PendingPostCommitWorkIdsJson)]!, "pending post-commit work"))
        {
            ConsumedSchedulerWorkItemIds = DeserializeIds(upcast[nameof(marker.ConsumedSchedulerWorkItemIdsJson)]!, "consumed scheduler work")
        };
    }

    private static RuntimeCheckpointCommitStoreResult ResultFor(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointCommitEntity marker) =>
        ResolveReplay(commit, marker.Fingerprint, marker);

    private static IReadOnlyCollection<string> DeserializeIds(string json, string label)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json, JsonOptions);
            if (values is null || values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new InvalidDataException();
            return values;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            throw new InvalidDataException($"The persisted runtime checkpoint marker contains invalid {label} identifiers.", exception);
        }
    }

    private sealed record MarkerDocument(
        string CommitId,
        string WorkflowExecutionId,
        DateTimeOffset OccurredAt,
        string Fingerprint,
        IReadOnlyCollection<string> PendingPostCommitWorkIds,
        IReadOnlyCollection<string> ConsumedSchedulerWorkItemIds);

    private async ValueTask RollbackAndRestoreAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch
        {
            // Preserve the original provider failure. The reconciliation read below remains authoritative when it is
            // available; a provider that cannot roll back is still handled by its transaction disposal.
        }

        await transaction.DisposeAsync();

        // A failed unit of work must not leave Added/Modified participant rows queued for an accidental later
        // SaveChanges call. The caller can reload and retry from durable state; preserving stale tracked values here
        // would turn a rollback into a hidden second attempt.
        context.ChangeTracker.Clear();
    }
}
