using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.WorkflowPreparationEnrollmentCodec;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

// Owns the supplied fresh physical transaction through disposal. Only a native participant whose
// private frame passed RequireActiveContext can enter. There is no supported native frame yet:
// target continuity, all-writer exclusion and additive schema admission remain separate gates.
internal sealed class WorkflowPreparationEnrollmentTransaction
{
    private readonly IWorkflowPreparationEfCommitParticipant participant;
    private readonly IWorkflowPreparationCommitContext supplied;
    private readonly IWorkflowPreparationEfEnrollmentCommitContext frame;
    private readonly EfSharedTransaction transaction;
    private readonly RuntimeDbContext db;
    private readonly WorkflowPreparationOwnerStamp expected;
    private bool entered;

    internal WorkflowPreparationEnrollmentTransaction(IWorkflowPreparationEfCommitParticipant participant,
        IWorkflowPreparationCommitContext supplied, EfSharedTransaction transaction)
    {
        this.participant = participant;
        this.supplied = supplied;
        this.transaction = transaction;
        frame = participant.RequireActiveContext(supplied) as IWorkflowPreparationEfEnrollmentCommitContext
            ?? throw new NotSupportedException("The selected native frame does not support enrollment settlement.");
        db = transaction.Context<RuntimeDbContext>();
        expected = frame.Owner.Owner;
        RequireFrame();
    }

    // Absence only leads to an INSERT on the permanent unique preparation key. The database must
    // settle any competing acceptance transaction before this INSERT can win. Exceptions are not
    // interpreted as durable results; the whole native operation must be rolled back/reconciled.
    internal ValueTask<WorkflowPreparationEfEnrollmentResult> SettleAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(intendedHeader: null, acceptance: null, cancellationToken);

    // The native writer must route EVERY actual header/protection entry through this operation.
    // The callback stages the real header and root protection in this exact transaction. This method
    // cannot be admitted merely because one coordinator uses it; RequireEnrollmentProtocolReadyAsync
    // must also exclude/settle old untracked writers and verify all actual acceptance paths.
    internal ValueTask<WorkflowPreparationEfEnrollmentResult> AcceptAsync(
        WorkflowPreparationSnapshot intendedHeader,
        Func<CancellationToken, ValueTask> writeHeaderAndProtection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intendedHeader);
        ArgumentNullException.ThrowIfNull(writeHeaderAndProtection);
        return ExecuteAsync(intendedHeader, writeHeaderAndProtection, cancellationToken);
    }

    private async ValueTask<WorkflowPreparationEfEnrollmentResult> ExecuteAsync(
        WorkflowPreparationSnapshot? intendedHeader,
        Func<CancellationToken, ValueTask>? acceptance,
        CancellationToken cancellationToken)
    {
        if (entered) throw new InvalidOperationException("An enrollment transaction executes once.");
        entered = true;
        WorkflowPreparationEfEnrollmentResult? result = null;
        Exception? primary = null;
        try
        {
            RequireFrame();
            await frame.RequireEnrollmentProtocolReadyAsync(cancellationToken);
            var binding = RequireBinding();
            if (binding is not null) await frame.RequireTargetContinuityAsync(binding, cancellationToken);
            var key = WorkflowPreparationRetentionCodec.PhysicalId(expected.Id);
            var row = await db.Set<WorkflowPreparationEnrollmentEntity>().AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == key, cancellationToken);
            var barrier = row is null ? null : Read(row, expected.Id);
            var header = await FindHeaderAsync(key, cancellationToken);

            // Real pre-S1 retained/terminal evidence wins over absent enrollment metadata. No new
            // intent/barrier is manufactured beside that header; native custody is independently required.
            if (header is not null)
            {
                RequireMatchingHeader(expected, header);
                if (barrier is null) await frame.RequirePredecessorHeaderCustodyAsync(header, cancellationToken);
                else
                {
                    RequireExactBinding(barrier, binding);
                    RequireAcceptedHeader(barrier, header);
                }
                result = new(WorkflowPreparationEfEnrollmentStatus.Accepted, binding,
                    barrier?.Incarnation, header);
            }
            else if (barrier is not null)
            {
                RequireExactBinding(barrier, binding);
                if (barrier.Outcome == Outcome.Accepted)
                    throw new WorkflowPreparationException("enrollment-accepted-header-missing", expected.Id);
                if (acceptance is not null)
                    throw new WorkflowPreparationException("enrollment-permanently-aborted", expected.Id);
                result = new(WorkflowPreparationEfEnrollmentStatus.Aborted, binding, barrier.Incarnation);
            }
            else if (binding is null)
            {
                result = new(WorkflowPreparationEfEnrollmentStatus.Pending, Code: "enrollment-intent-unproved");
            }
            else if (acceptance is null)
            {
                // Join/canonical reads must prove the real rejected terminal decision, not a caller flag.
                if (frame.Owner.Decision?.Kind != WorkflowPreparationDecisionKind.Rejected)
                    result = new(WorkflowPreparationEfEnrollmentStatus.Pending, Code: "enrollment-terminal-decision-required");
                else
                {
                    barrier = new(binding, Outcome.Aborted, Guid.NewGuid().ToString("N"), null, null);
                    db.Set<WorkflowPreparationEnrollmentEntity>().Add(Create(barrier));
                    await db.SaveChangesAsync(cancellationToken);
                    result = new(WorkflowPreparationEfEnrollmentStatus.Aborted, binding, barrier.Incarnation);
                }
            }
            else
            {
                if (frame.Owner.Decision is not null)
                    throw new WorkflowPreparationException("enrollment-terminal-owner-cannot-accept", expected.Id);
                // Claim the key BEFORE the callback can stage any header/protection. Never derive a
                // successful outcome from a uniqueness/provider error or a snapshot read of absence.
                // The actual native writer supplies its genuine planned header identity/selection,
                // already validated by the checked retention codec. No placeholder token/header exists.
                if (intendedHeader is null || intendedHeader.State != WorkflowPreparationState.Retained ||
                    intendedHeader.AcceptedOwner != expected)
                    throw new WorkflowPreparationException("enrollment-invalid-acceptance-header", expected.Id);
                _ = WorkflowPreparationRetentionCodec.Create(intendedHeader, RuntimeArtifactEfModule.PreparationSchemaVersion);
                RequireMatchingHeader(expected, intendedHeader);
                var incarnation = Guid.NewGuid().ToString("N");
                barrier = new(binding, Outcome.Accepted, incarnation,
                    intendedHeader.Token.IncarnationId, intendedHeader.Selection.Fingerprint);
                db.Set<WorkflowPreparationEnrollmentEntity>().Add(Create(barrier));
                await db.SaveChangesAsync(cancellationToken);
                await acceptance(cancellationToken);
                header = await FindHeaderAsync(key, cancellationToken)
                    ?? throw new WorkflowPreparationException("enrollment-acceptance-header-missing", expected.Id);
                if (header.Token != intendedHeader.Token || header.State != intendedHeader.State)
                    throw new WorkflowPreparationException("enrollment-acceptance-header-changed", expected.Id);
                RequireAcceptedHeader(barrier, header);
                result = new(WorkflowPreparationEfEnrollmentStatus.Accepted, binding, incarnation, header);
            }

            if (result.Status is not (WorkflowPreparationEfEnrollmentStatus.Accepted or WorkflowPreparationEfEnrollmentStatus.Aborted))
            {
                await transaction.RollbackAsync();
                return result;
            }
            await RevalidateAsync(result, binding, key, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result; // finally must finish successfully before any committed result escapes.
        }
        catch (EfCommitOutcomeUnknownException error)
        {
            primary = new WorkflowPreparationOutcomeUnknownException(expected.Id, error);
            throw primary;
        }
        catch (Exception error)
        {
            primary = error;
            throw;
        }
        finally
        {
            // EfSharedTransaction records cleanup failures without replacing the primary exception.
            // A cleanup failure after a commit attempt must never expose an unqualified result.
            await transaction.DisposeAsync();
            if (transaction.CleanupFailure is { } cleanup)
            {
                if (primary is not null) primary.Data["EnrollmentCleanupFailure"] = cleanup;
                else if (transaction.CommitWasAttempted)
                    throw new WorkflowPreparationOutcomeUnknownException(expected.Id, cleanup);
                else throw new WorkflowPreparationException("enrollment-cleanup-failed", expected.Id, cleanup);
            }
        }
    }

    private async ValueTask RevalidateAsync(WorkflowPreparationEfEnrollmentResult result,
        WorkflowPreparationEfEnrollmentBinding? binding, string key, CancellationToken cancellationToken)
    {
        RequireFrame();
        await frame.RequireEnrollmentProtocolReadyAsync(cancellationToken);
        var current = RequireBinding();
        if (binding is null ? current is not null : current is null || !SameBinding(binding, current))
            throw new WorkflowPreparationException("enrollment-owner-intent-changed", expected.Id);
        if (current is not null) await frame.RequireTargetContinuityAsync(current, cancellationToken);
        var persistedHeader = await FindHeaderAsync(key, cancellationToken);
        var persistedRow = await db.Set<WorkflowPreparationEnrollmentEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == key, cancellationToken);
        var persistedBarrier = persistedRow is null ? null : Read(persistedRow, expected.Id);
        if (result.Status == WorkflowPreparationEfEnrollmentStatus.Aborted)
        {
            if (persistedHeader is not null || persistedBarrier is null || persistedBarrier.Outcome != Outcome.Aborted ||
                persistedBarrier.Incarnation != result.BarrierIncarnation)
                throw new WorkflowPreparationException("enrollment-abort-not-staged", expected.Id);
            RequireExactBinding(persistedBarrier, current);
        }
        else
        {
            if (persistedHeader is null || persistedHeader.Token != result.Header?.Token ||
                persistedHeader.State != result.Header?.State)
                throw new WorkflowPreparationException("enrollment-header-not-staged", expected.Id);
            RequireMatchingHeader(expected, persistedHeader);
            if (persistedBarrier is null)
            {
                if (result.BarrierIncarnation is not null)
                    throw new WorkflowPreparationException("enrollment-barrier-disappeared", expected.Id);
                await frame.RequirePredecessorHeaderCustodyAsync(persistedHeader, cancellationToken);
            }
            else
            {
                RequireExactBinding(persistedBarrier, current);
                RequireAcceptedHeader(persistedBarrier, persistedHeader);
                if (persistedBarrier.Incarnation != result.BarrierIncarnation)
                    throw new WorkflowPreparationException("enrollment-barrier-changed", expected.Id);
            }
            await frame.RequireAcceptedHeaderProtectionAsync(persistedHeader, cancellationToken);
        }
        RequireFrame();
    }

    private WorkflowPreparationEfEnrollmentBinding? RequireBinding()
    {
        if (frame.Owner.Lease is not IWorkflowPreparationEfEnrollmentOwnerLease lease)
            throw new NotSupportedException("The genuine owner lease has no durable enrollment facet.");
        var binding = lease.RequireCommittedEnrollment();
        if (binding is null) return null;
        ValidateBinding(binding);
        if (binding.Owner != expected)
            throw new WorkflowPreparationException("enrollment-current-owner-mismatch", expected.Id);
        return binding;
    }

    private void RequireFrame()
    {
        if (!ReferenceEquals(participant.RequireActiveContext(supplied), frame) ||
            !ReferenceEquals(frame.RuntimeContext, db) ||
            !ReferenceEquals(frame.Connection, transaction.Connection) ||
            !ReferenceEquals(frame.Transaction, transaction.Transaction) ||
            !ReferenceEquals(db.Database.GetDbConnection(), frame.Connection) ||
            !ReferenceEquals(db.Database.CurrentTransaction?.GetDbTransaction(), frame.Transaction))
            throw new InvalidOperationException("Enrollment requires the selected provider's actual fresh physical transaction.");
        frame.EnsureActive();
        frame.Owner.EnsureActive();
        if (frame.Owner.Owner != expected)
            throw new WorkflowPreparationException("enrollment-owner-changed", expected.Id);
    }

    private static void RequireExactBinding(Barrier barrier, WorkflowPreparationEfEnrollmentBinding? binding)
    {
        if (binding is null || !SameBinding(barrier.Binding, binding))
            throw new WorkflowPreparationException("enrollment-barrier-binding", barrier.Binding.Owner.Id);
    }

    private async ValueTask<WorkflowPreparationSnapshot?> FindHeaderAsync(string key, CancellationToken cancellationToken)
    {
        var row = await db.Set<WorkflowPreparationRetentionEntity>().AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == key, cancellationToken);
        return row is null ? null : WorkflowPreparationRetentionCodec.Read(row, expected.Id);
    }
}
