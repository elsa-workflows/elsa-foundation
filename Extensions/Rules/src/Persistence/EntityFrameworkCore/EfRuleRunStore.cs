using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

/// <summary>Rules-owned admission and settlement transactions. Worker operations are implemented separately.</summary>
internal sealed partial class EfRuleRunStore(IDbContextFactory<RulesDbContext> contexts, IRuleAdmissionValidator validator,
    TimeSpan? workLeaseDuration = null, IRuleRoundValidator? roundValidator = null)
{
    private readonly TimeSpan _workLeaseDuration = ValidateLeaseDuration(workLeaseDuration ?? TimeSpan.FromMinutes(1));
    private const int MaximumWriteAttempts = 8;

    public async ValueTask<RuleAdmissionConfirmation> AdmitAsync(RuleRunAdmission admission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admission);
        var result = await SettleAsync(admission.Invocation, Operation.Admit, null, cancellationToken);
        var disposition = result.Disposition switch
        {
            Settlement.Admitted => RuleAdmissionDisposition.Admitted,
            Settlement.Refused => RuleAdmissionDisposition.Refused,
            Settlement.AlreadyRefused => RuleAdmissionDisposition.AlreadyRefused,
            Settlement.AlreadyCancelled => RuleAdmissionDisposition.AlreadyCancelled,
            Settlement.Conflict => RuleAdmissionDisposition.Conflict,
            Settlement.Invalid => RuleAdmissionDisposition.Invalid,
            Settlement.Unavailable => RuleAdmissionDisposition.Unavailable,
            _ => RuleAdmissionDisposition.Replay
        };
        return new(disposition, admission.Invocation.Identity, result.Revision, result.TerminalId);
    }

    public async ValueTask<RuleRefusalConfirmation> RefuseAdmissionAsync(RuleAdmissionRefusal refusal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        var result = await SettleAsync(refusal.Invocation, Operation.Refuse, refusal.Diagnostic, cancellationToken);
        var disposition = result.Disposition switch
        {
            Settlement.Refused => RuleRefusalDisposition.Refused,
            Settlement.AlreadyRefused => RuleRefusalDisposition.Replay,
            Settlement.AlreadyCancelled => RuleRefusalDisposition.AlreadyCancelled,
            Settlement.Conflict => RuleRefusalDisposition.Conflict,
            Settlement.Invalid => RuleRefusalDisposition.Invalid,
            Settlement.Unavailable => RuleRefusalDisposition.Unavailable,
            _ => RuleRefusalDisposition.ExistingAdmission
        };
        return new(disposition, refusal.Invocation.Identity, result.TerminalId);
    }

    public async ValueTask<RuleCancellationConfirmation> CancelAsync(RuleCancellationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await SettleAsync(request.Invocation, Operation.Cancel, null, cancellationToken);
        var disposition = result.Disposition switch
        {
            Settlement.Cancelled => RuleCancellationDisposition.Cancelled,
            Settlement.AlreadyCancelled => RuleCancellationDisposition.Replay,
            Settlement.Conflict => RuleCancellationDisposition.Conflict,
            Settlement.Invalid => RuleCancellationDisposition.Invalid,
            Settlement.Unavailable => RuleCancellationDisposition.Unavailable,
            _ => RuleCancellationDisposition.TerminalUnchanged
        };
        return new(disposition, request.Invocation.Identity, result.Revision, result.TerminalId);
    }

    private async ValueTask<SettlementResult> SettleAsync(RuleInvocationEnvelope invocation, Operation operation,
        RuleRunDiagnostic? diagnostic, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();
        PreparedInvocation prepared;
        try { prepared = Prepare(invocation); }
        catch (NotSupportedException) { return new(Settlement.Unavailable); }
        catch (Exception error) when (IsInvalidRecord(error)) { return new(Settlement.Invalid); }

        RuleAdmissionValidationResult? validation = null;
        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                var row = await db.Runs.SingleOrDefaultAsync(candidate => candidate.RunKey == prepared.Key, cancellationToken);
                if (row is not null)
                {
                    // The indexed hash is only an address. Compare the retained full identity before exposing anything.
                    if (!string.Equals(row.IdentityBase64, prepared.Identity, StringComparison.Ordinal)
                        || !string.Equals(row.Fingerprint64, prepared.Fingerprint, StringComparison.Ordinal))
                        return new(Settlement.Conflict);
                    var state = RuleRowCodec.Read(row);
                    var status = (RuleRunStatus)row.Status;
                    if (status == RuleRunStatus.Cancelled) return Existing(Settlement.AlreadyCancelled, row);
                    if (status == RuleRunStatus.Refused) return Existing(Settlement.AlreadyRefused, row);
                    if (operation != Operation.Cancel || status != RuleRunStatus.Running)
                        return Existing(Settlement.Replay, row);

                    row.ModelRevision = checked(row.ModelRevision + 1);
                    RuleRowCodec.Settle(row, state.Invocation, state.Facts, RuleTerminalKind.Cancelled, null);
                    await SaveAsync(db, cancellationToken);
                    return Existing(Settlement.Cancelled, row);
                }

                if (operation != Operation.Cancel && validation is null)
                {
                    // No database transaction is held while the runtime checks its installed interpreter.
                    try { validation = await validator.ValidateAsync(invocation, cancellationToken)
                        ?? new(RuleAdmissionValidationDisposition.Unavailable, null); }
                    catch (Exception error) when (error is NotSupportedException || IsInvalidRecord(error)
                                                 || EfRelationalExceptionClassifier.IsProviderFailure(error))
                    {
                        validation = new(RuleAdmissionValidationDisposition.Unavailable, null);
                    }
                    // Even Invalid/Unavailable must re-read: cancellation or another admission may have won meanwhile.
                    // This one validation pass is not a lost-write attempt; reserve the fresh read even at the retry limit.
                    attempt--;
                    continue;
                }

                var decision = DecideNew(operation, validation, diagnostic);
                if (decision is Settlement.Invalid or Settlement.Unavailable) return new(decision);
                row = RuleRowCodec.Create(invocation, prepared.Key, prepared.Identity, prepared.Json, prepared.Fingerprint);
                db.Runs.Add(row);
                if (decision == Settlement.Admitted)
                {
                    row.Admitted = true;
                    var facts = RuleRecordCodec.CanonicalFacts(invocation.InitialFacts);
                    row.FactsJson = Encoding.UTF8.GetString(RuleRecordCodec.FactsBytes(facts));
                    row.FactsSha25664 = RuleRecordCodec.FactsHash(facts);
                    for (var i = 0; i < facts.Length; i++)
                        db.Facts.Add(new RuleFactHistoryRecord
                        {
                            RunKey = row.RunKey, FactIndex = i, Atom = facts[i].Atom,
                            FirstVisibleModelRevision = row.ModelRevision, SchemaVersion = RulesEfModule.SchemaVersion
                        });
                }
                else
                    RuleRowCodec.Settle(row, invocation, [], decision == Settlement.Cancelled ? RuleTerminalKind.Cancelled : RuleTerminalKind.Refused,
                        decision == Settlement.Refused ? validation!.Diagnostic : null);
                await SaveAsync(db, cancellationToken);
                return Existing(decision, row);
            }
            catch (Exception error) when (IsWriteConflict(error))
            {
                // Dispose the failed transaction/context and reconcile from fresh authoritative state.
                // Never merge stale tracked facts, terminal metadata or lease state into the winner.
            }
            catch (Exception error) when (IsStoreUnavailable(error))
            {
                return new(Settlement.Unavailable);
            }
        }
        return new(Settlement.Unavailable);
    }

    private static Settlement DecideNew(Operation operation, RuleAdmissionValidationResult? validation, RuleRunDiagnostic? requestedDiagnostic)
    {
        if (operation == Operation.Cancel) return Settlement.Cancelled;
        return validation?.Disposition switch
        {
            RuleAdmissionValidationDisposition.Ready when operation == Operation.Admit => Settlement.Admitted,
            RuleAdmissionValidationDisposition.Ready => Settlement.Invalid,
            RuleAdmissionValidationDisposition.Invalid => Settlement.Invalid,
            RuleAdmissionValidationDisposition.Refused when validation.Diagnostic is not null
                && (operation == Operation.Admit || validation.Diagnostic.Code == requestedDiagnostic?.Code) => Settlement.Refused,
            RuleAdmissionValidationDisposition.Refused => Settlement.Invalid,
            _ => Settlement.Unavailable
        };
    }

    private static async Task<bool> SaveAsync(RulesDbContext db, CancellationToken cancellationToken, LeaseWindow? leaseWindow = null)
    {
        foreach (var entry in db.ChangeTracker.Entries<RuleRunRecord>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified).ToArray())
        {
            var row = entry.Entity;
            RuleRoundReceiptRecord? pending = null;
            if (row.PendingReceiptId64 is { } receiptId)
                pending = db.Receipts.Local.SingleOrDefault(receipt => receipt.RunKey == row.RunKey && receipt.ReceiptId64 == receiptId)
                    ?? await db.Receipts.AsNoTracking().Where(receipt => receipt.RunKey == row.RunKey && receipt.ReceiptId64 == receiptId)
                        .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidDataException("rules.missing-pending-receipt");
            RuleRowCodec.CheckProjectionSize(row, pending);
        }
        // Explicitly atomic even if a host disables EF's automatic transactions.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (leaseWindow is not null)
        {
            // The tracked write retains schema admission and row CAS; the row lock lasts through commit.
            // Reading live database time after ALL writes also catches expiry while waiting for the write.
            var now = await db.ReadUtcTicksAsync(cancellationToken);
            if (now < leaseWindow.SampledAt || now >= leaseWindow.ExpiresAt)
                return false; // Disposal rolls back every row. Never expose a token from this attempt.
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async ValueTask<RuleRunReadResult> FindAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default)
    {
        var read = await ReadAsync(identity, cancellationToken);
        if (read.Unavailable) return new(RuleRunReadDisposition.Unavailable, null);
        if (read.Row is not { } row || read.State is not { } state) return new(RuleRunReadDisposition.NotFound, null);
        return new(RuleRunReadDisposition.Found, new(identity, new(row.Fingerprint64), state.Invocation.Artifact.ArtifactId,
            state.Invocation.Artifact.Sha256, row.PinHash64, (RuleRunStatus)row.Status, row.ModelRevision, row.CommittedRounds,
            state.Facts.Length, row.PendingReceiptId64, row.TerminalId64, state.Terminal?.Kind));
    }

    public async ValueTask<RuleTerminalReadResult> ReadTerminalAsync(RuleRunIdentity identity, CancellationToken cancellationToken = default)
    {
        var read = await ReadAsync(identity, cancellationToken);
        if (read.Unavailable) return new(RuleTerminalReadDisposition.Unavailable, null);
        if (read.State is not { } state) return new(RuleTerminalReadDisposition.NotFound, null);
        return state.Terminal is { } terminal
            ? new(RuleTerminalReadDisposition.Found, terminal)
            : new(RuleTerminalReadDisposition.NotTerminal, null);
    }

    private async ValueTask<ReadResult> ReadAsync(RuleRunIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = RuleRecordCodec.ScopeBytes(identity);
        var key = RuleRecordCodec.Hash(scope);
        try
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            var row = await db.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.RunKey == key, cancellationToken);
            // An address collision is not a match for this exact identity. Never expose the other row.
            if (row is null || !string.Equals(row.IdentityBase64, Convert.ToBase64String(scope), StringComparison.Ordinal))
                return new(null, null, false);
            return new(row, RuleRowCodec.Read(row), false);
        }
        catch (Exception error) when (IsStoreUnavailable(error))
        {
            return new(null, null, true);
        }
    }

    private static PreparedInvocation Prepare(RuleInvocationEnvelope invocation)
    {
        var bytes = RuleRecordCodec.InvocationBytes(invocation);
        var scope = RuleRecordCodec.ScopeBytes(invocation.Identity);
        return new(RuleRecordCodec.Hash(scope), Convert.ToBase64String(scope), Encoding.UTF8.GetString(bytes), RuleRecordCodec.Hash(bytes));
    }

    private static bool IsInvalidRecord(Exception error) => error is InvalidDataException or ArgumentException or JsonException or FormatException or OverflowException or KeyNotFoundException;
    private static SettlementResult Existing(Settlement disposition, RuleRunRecord row) => new(disposition, row.ModelRevision, row.TerminalId64);
    private enum Operation { Admit, Refuse, Cancel }
    private enum Settlement { Admitted, Refused, Cancelled, Replay, AlreadyCancelled, AlreadyRefused, Conflict, Invalid, Unavailable }
    private sealed record SettlementResult(Settlement Disposition, long? Revision = null, string? TerminalId = null);
    private sealed record PreparedInvocation(string Key, string Identity, string Json, string Fingerprint);
    private sealed record ReadResult(RuleRunRecord? Row, RuleRowCodec.State? State, bool Unavailable);
}
