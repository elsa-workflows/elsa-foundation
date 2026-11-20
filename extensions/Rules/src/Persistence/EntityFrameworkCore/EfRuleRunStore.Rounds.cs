using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

internal sealed partial class EfRuleRunStore
{
    public async ValueTask<RuleWorkReadResult> ReadWorkAsync(RuleWorkToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        var address = WorkAddress(token.Lease);
        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                var row = await db.Runs.AsNoTracking().SingleOrDefaultAsync(row => row.RunKey == address.Key, cancellationToken);
                if (row is null || row.IdentityBase64 != address.Identity) return EmptyWork(RuleWorkReadDisposition.NotFound);
                if (row.Fingerprint64 != token.Lease.Fingerprint.Sha256) return EmptyWork(RuleWorkReadDisposition.LeaseLost);
                var state = RuleRowCodec.Read(row);
                if (state.Terminal is { } terminal) return new(RuleWorkReadDisposition.Terminal, null, null, null, terminal.TerminalId);
                RuleRoundReceiptRecord? record = null;
                if (row.PendingReceiptId64 is { } receiptId)
                {
                    record = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(receipt => receipt.RunKey == row.RunKey
                        && receipt.ReceiptId64 == receiptId, cancellationToken);
                }
                var now = await db.ReadUtcTicksAsync(cancellationToken);
                // A receipt read may straddle a consume/renew/cancel. Retry rather than join different revisions.
                if (!await IsCurrentAsync(db, row, cancellationToken)) continue;
                if (!OwnsWork(row, token.Lease, now)) return EmptyWork(RuleWorkReadDisposition.LeaseLost);
                RuleStoredRoundReceipt? pending = null;
                if (row.PendingReceiptId64 is not null)
                {
                    if (record is null) throw new InvalidDataException("rules.missing-pending-receipt");
                    RuleRowCodec.CheckProjectionSize(row, record);
                    pending = RuleRoundRecords.Read(row, record);
                    RuleRoundRecords.RequirePending(row, pending);
                }
                var refreshed = new RuleWorkToken(WorkLease(row, token.Lease.Identity), row.ModelRevision);
                return pending is null
                    ? new(RuleWorkReadDisposition.EvaluateRound, refreshed, RuleRoundRecords.Input(row, state), null, null)
                    : new(RuleWorkReadDisposition.ConsumeReceipt, refreshed, null, pending, null);
            }
            catch (Exception error) when (IsStoreUnavailable(error)) { return EmptyWork(RuleWorkReadDisposition.Unavailable); }
        }
        return EmptyWork(RuleWorkReadDisposition.Unavailable);
    }

    public async ValueTask<RuleReceiptConfirmation> AcceptRoundAsync(RuleWorkToken token, RuleRoundSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(submission);
        cancellationToken.ThrowIfCancellationRequested();
        var identity = token.Lease.Identity;
        var address = WorkAddress(token.Lease);
        string json;
        try
        {
            ArgumentNullException.ThrowIfNull(submission.Receipt);
            ArgumentNullException.ThrowIfNull(submission.Attempt);
            var bytes = RuleRecordCodec.ReceiptRecordBytes(submission.Receipt);
            _ = RuleRecordCodec.ReadReceiptRecord(bytes); // Validate the digest even for a purported replay.
            json = Encoding.UTF8.GetString(bytes);
        }
        catch (Exception error) when (IsInvalidRecord(error)) { return ReceiptResult(RuleReceiptDisposition.InvalidReceipt, identity); }
        catch (NotSupportedException) { return ReceiptResult(RuleReceiptDisposition.Unavailable, identity); }
        var candidate = submission.Receipt;
        if (candidate.Identity.Run != identity || candidate.Identity.Fingerprint != token.Lease.Fingerprint)
            return ReceiptResult(RuleReceiptDisposition.Conflict, identity);
        RuleRoundValidationResult? validation = null;
        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                var row = await db.Runs.SingleOrDefaultAsync(row => row.RunKey == address.Key, cancellationToken);
                if (row is null) return ReceiptResult(RuleReceiptDisposition.LeaseLost, identity);
                if (row.IdentityBase64 != address.Identity || row.Fingerprint64 != token.Lease.Fingerprint.Sha256)
                    return ReceiptResult(RuleReceiptDisposition.Conflict, identity);
                var state = RuleRowCodec.Read(row);
                var existing = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(receipt => receipt.RunKey == row.RunKey
                    && receipt.Ordinal == candidate.Identity.Ordinal, cancellationToken);
                if (!await IsCurrentAsync(db, row, cancellationToken)) continue;
                if (existing is not null)
                {
                    var recorded = RuleRoundRecords.Read(row, existing);
                    if (existing.ReceiptJson != json) return ReceiptResult(RuleReceiptDisposition.Conflict, identity);
                    // Only the original confirmation is replayed; attempt attribution and successor state stay untouched.
                    return new(RuleReceiptDisposition.Replay, identity, recorded.ReceiptId, recorded.AcceptedModelRevision, null, null);
                }
                var now = await db.ReadUtcTicksAsync(cancellationToken);
                if (!OwnsWork(row, token.Lease, now)) return ReceiptResult(RuleReceiptDisposition.LeaseLost, identity);
                if (row.ModelRevision != token.ExpectedModelRevision) return ReceiptResult(RuleReceiptDisposition.RevisionConflict, identity);
                if (row.PendingReceiptId64 is not null || candidate.Identity.Ordinal != row.CommittedRounds
                    || candidate.Identity.InputStateSha256 != row.FactsSha25664 || candidate.ImplementationPinHash != row.PinHash64)
                    return ReceiptResult(RuleReceiptDisposition.Conflict, identity);
                if (submission.Attempt.WorkerId != row.WorkOwner || submission.Attempt.LeaseFence != row.WorkFence)
                    return ReceiptResult(RuleReceiptDisposition.InvalidReceipt, identity);
                if (validation is null)
                {
                    // Runtime owns complete-step semantics. No DB transaction spans interpreter work.
                    try
                    {
                        validation = roundValidator is null ? new(RuleValidationDisposition.UnsupportedFormat, null)
                            : await roundValidator.ValidateAsync(new(RuleRoundRecords.Input(row, state)), candidate, cancellationToken)
                                ?? new(RuleValidationDisposition.UnsupportedFormat, null);
                    }
                    catch (Exception error) when (IsStoreUnavailable(error))
                    {
                        validation = new(RuleValidationDisposition.UnsupportedFormat, null);
                    }
                    attempt--; // Always reserve a fresh winner read, including after unsuccessful validation.
                    continue;
                }
                if (validation.Disposition != RuleValidationDisposition.Equivalent)
                    return ReceiptResult(validation.Disposition == RuleValidationDisposition.Invalid
                        ? RuleReceiptDisposition.InvalidReceipt : RuleReceiptDisposition.Unavailable, identity);
                var expiry = row.WorkExpiresAtUtcTicks!.Value;
                row.ModelRevision = checked(row.ModelRevision + 1);
                row.PendingReceiptId64 = RuleRoundRecords.ReceiptId(candidate);
                db.Receipts.Add(new RuleRoundReceiptRecord
                {
                    RunKey = row.RunKey, Ordinal = candidate.Identity.Ordinal, ReceiptId64 = row.PendingReceiptId64,
                    InputFactsSha25664 = candidate.Identity.InputStateSha256, ReceiptJson = json,
                    AcceptedAttemptId = submission.Attempt.AttemptId, AcceptedWorkerId = submission.Attempt.WorkerId,
                    AcceptedFence = submission.Attempt.LeaseFence, AcceptedModelRevision = row.ModelRevision,
                    SchemaVersion = RulesEfModule.SchemaVersion
                });
                if (!await SaveAsync(db, cancellationToken, new(now, expiry))) return ReceiptResult(RuleReceiptDisposition.LeaseLost, identity);
                return new(RuleReceiptDisposition.Accepted, identity, row.PendingReceiptId64, row.ModelRevision, null, null);
            }
            catch (Exception error) when (IsWriteConflict(error)) { }
            catch (Exception error) when (IsStoreUnavailable(error)) { return ReceiptResult(RuleReceiptDisposition.Unavailable, identity); }
        }
        return ReceiptResult(RuleReceiptDisposition.Unavailable, identity);
    }

    public async ValueTask<RuleTransitionConfirmation> ConsumeRoundAsync(RuleWorkToken token, RuleReceiptIdentity receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        var identity = token.Lease.Identity;
        var address = WorkAddress(token.Lease);
        if (!IsSha256(receipt.ReceiptId) || !IsSha256(receipt.SemanticReceiptSha256))
            return TransitionResult(RuleTransitionDisposition.Invalid, identity);
        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                var row = await db.Runs.SingleOrDefaultAsync(row => row.RunKey == address.Key, cancellationToken);
                if (row is null) return TransitionResult(RuleTransitionDisposition.LeaseLost, identity);
                if (row.IdentityBase64 != address.Identity || row.Fingerprint64 != token.Lease.Fingerprint.Sha256)
                    return TransitionResult(RuleTransitionDisposition.Conflict, identity);
                var state = RuleRowCodec.Read(row);
                var record = await db.Receipts.SingleOrDefaultAsync(candidate => candidate.RunKey == row.RunKey
                    && candidate.ReceiptId64 == receipt.ReceiptId, cancellationToken);
                if (!await IsCurrentAsync(db, row, cancellationToken)) continue;
                if (record is null) return TransitionResult(RuleTransitionDisposition.Conflict, identity);
                var stored = RuleRoundRecords.Read(row, record);
                if (stored.Receipt.SemanticReceiptSha256 != receipt.SemanticReceiptSha256)
                    return TransitionResult(RuleTransitionDisposition.Conflict, identity);
                if (stored.ConsumedModelRevision is not null) return ConsumptionResult(RuleTransitionDisposition.Replay, identity, record);
                if (state.Terminal is not null)
                    return new(RuleTransitionDisposition.TerminalUnchanged, identity, null, null, row.TerminalId64, null, row.ModelRevision);
                var now = await db.ReadUtcTicksAsync(cancellationToken);
                if (!OwnsWork(row, token.Lease, now)) return TransitionResult(RuleTransitionDisposition.LeaseLost, identity);
                if (row.ModelRevision != token.ExpectedModelRevision) return TransitionResult(RuleTransitionDisposition.RevisionConflict, identity);
                RuleRoundRecords.RequirePending(row, stored);
                RuleRowCodec.CheckProjectionSize(row, record);
                var oldFacts = state.Facts.Select(fact => fact.Atom).ToHashSet(StringComparer.Ordinal);
                if (stored.Receipt.AddedFacts.Any(fact => oldFacts.Contains(fact.Atom)))
                    throw new InvalidDataException("rules.receipt-addition-already-present");
                var facts = RuleRecordCodec.CanonicalFacts(state.Facts.Concat(stored.Receipt.AddedFacts).ToArray());
                var expiry = row.WorkExpiresAtUtcTicks!.Value;
                row.ModelRevision = checked(row.ModelRevision + 1);
                row.CommittedRounds = checked(row.CommittedRounds + 1);
                row.FactsJson = Encoding.UTF8.GetString(RuleRecordCodec.FactsBytes(facts));
                row.FactsSha25664 = RuleRecordCodec.FactsHash(facts);
                row.PendingReceiptId64 = null;
                for (var index = 0; index < stored.Receipt.AddedFacts.Count; index++)
                    db.Facts.Add(new RuleFactHistoryRecord
                    {
                        RunKey = row.RunKey, FactIndex = state.Facts.Length + index, Atom = stored.Receipt.AddedFacts[index].Atom,
                        FirstVisibleModelRevision = row.ModelRevision, SchemaVersion = RulesEfModule.SchemaVersion
                    });
                // A no-addition round establishes closure even at the exact budget boundary.
                if (stored.Receipt.AddedFacts.Count == 0)
                    RuleRowCodec.Settle(row, state.Invocation, facts, RuleTerminalKind.Succeeded, null);
                else if (row.CommittedRounds >= state.Invocation.MaxRounds)
                    RuleRowCodec.Settle(row, state.Invocation, facts, RuleTerminalKind.Faulted, new("rules.round-budget-exhausted"));
                record.ConsumedModelRevision = row.ModelRevision;
                record.ConsumptionTransitionId64 = RuleRoundRecords.ConsumptionId(stored);
                record.TerminalIdAtConsumption64 = row.TerminalId64;
                record.NextRoundIdAtConsumption64 = row.TerminalId64 is null ? RuleRoundRecords.RoundId(row.Fingerprint64, row.CommittedRounds) : null;
                if (!await SaveAsync(db, cancellationToken, new(now, expiry))) return TransitionResult(RuleTransitionDisposition.LeaseLost, identity);
                return ConsumptionResult(RuleTransitionDisposition.Consumed, identity, record);
            }
            catch (Exception error) when (IsWriteConflict(error)) { }
            catch (Exception error) when (IsStoreUnavailable(error)) { return TransitionResult(RuleTransitionDisposition.Unavailable, identity); }
        }
        return TransitionResult(RuleTransitionDisposition.Unavailable, identity);
    }

    // Checking after related rows are read avoids treating concurrent progress as corrupt retained data.
    // Writes still use the original tracked concurrency token in the committing transaction.
    private static async Task<bool> IsCurrentAsync(RulesDbContext db, RuleRunRecord row, CancellationToken cancellationToken)
    {
        var current = await db.Runs.AsNoTracking().Where(candidate => candidate.RunKey == row.RunKey)
            .Select(candidate => candidate.ConcurrencyToken).SingleOrDefaultAsync(cancellationToken);
        return current is not null && current.AsSpan().SequenceEqual(row.ConcurrencyToken);
    }

    private static (string Key, string Identity) WorkAddress(RuleWorkLease lease)
    {
        var bytes = RuleRecordCodec.ScopeBytes(lease.Identity);
        return (RuleRecordCodec.Hash(bytes), Convert.ToBase64String(bytes));
    }

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static RuleWorkReadResult EmptyWork(RuleWorkReadDisposition disposition) => new(disposition, null, null, null, null);
    private static RuleReceiptConfirmation ReceiptResult(RuleReceiptDisposition disposition, RuleRunIdentity identity) => new(disposition, identity, null, null, null, null);
    private static RuleTransitionConfirmation TransitionResult(RuleTransitionDisposition disposition, RuleRunIdentity identity) => new(disposition, identity, null, null, null, null, null);
    private static RuleTransitionConfirmation ConsumptionResult(RuleTransitionDisposition disposition, RuleRunIdentity identity, RuleRoundReceiptRecord record) =>
        new(disposition, identity, record.ConsumptionTransitionId64, record.ReceiptId64, record.TerminalIdAtConsumption64,
            record.NextRoundIdAtConsumption64, record.ConsumedModelRevision);
}
