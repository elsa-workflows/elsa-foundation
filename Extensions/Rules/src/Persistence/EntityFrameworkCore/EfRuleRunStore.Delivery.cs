using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Persistence.EntityFrameworkCore;

internal sealed partial class EfRuleRunStore
{
    private readonly TimeSpan _deliveryLeaseDuration = ValidateLeaseDuration(deliveryLeaseDuration ?? TimeSpan.FromMinutes(1));

    public async ValueTask<RuleDeliveryClaimResult> ClaimDeliveryAsync(
        RuleDeliveryClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.ResultSchemas.Count == 0)
            return new(RuleDeliveryClaimDisposition.NoEligibleDelivery, null);

        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                var now = await db.ReadUtcTicksAsync(cancellationToken);
                string? after = null;
                var unavailable = false;
                while (true)
                {
                    var query = db.Runs.AsNoTracking().Where(row => row.DeliveryId64 != null
                        && !row.DeliveryAcknowledged
                        && (row.DeliveryExpiresAtUtcTicks == null || row.DeliveryExpiresAtUtcTicks <= now));
                    if (after is not null)
                        query = query.Where(row => string.Compare(row.RunKey, after) > 0);
                    var keys = await query.OrderBy(row => row.RunKey).Select(row => row.RunKey)
                        .Take(RuleLimits.MaxPageEntries).ToArrayAsync(cancellationToken);
                    if (keys.Length == 0)
                        return new(unavailable ? RuleDeliveryClaimDisposition.Unavailable : RuleDeliveryClaimDisposition.NoEligibleDelivery, null);

                    foreach (var key in keys)
                    {
                        db.ChangeTracker.Clear();
                        var row = await db.Runs.SingleOrDefaultAsync(candidate => candidate.RunKey == key, cancellationToken);
                        if (row is null || row.DeliveryId64 is null || row.DeliveryAcknowledged
                            || row.DeliveryExpiresAtUtcTicks > now)
                            continue;

                        RuleRowCodec.State state;
                        RuleTerminalEnvelope envelope;
                        try
                        {
                            state = RuleRowCodec.Read(row);
                            if (state.Terminal is null || state.Invocation.NativeDestination is null)
                                throw new InvalidDataException("rules.delivery-without-terminal");
                            envelope = ReadEnvelope(row);
                        }
                        catch (Exception error) when (error is NotSupportedException || IsInvalidRecord(error))
                        {
                            unavailable = true;
                            continue;
                        }

                        if (!request.ResultSchemas.Contains(envelope.Result.ResultSchemaVersion))
                            continue;

                        row.DeliveryOwner = request.Owner;
                        row.DeliveryFence = checked(row.DeliveryFence + 1);
                        row.DeliveryExpiresAtUtcTicks = NewDeliveryExpiry(now);
                        var expiresAt = row.DeliveryExpiresAtUtcTicks.Value;
                        if (!await SaveAsync(db, cancellationToken, new(now, expiresAt)))
                            return new(RuleDeliveryClaimDisposition.Unavailable, null);
                        return new(RuleDeliveryClaimDisposition.Claimed,
                            new(state.Invocation.Identity,
                                row.DeliveryId64, request.Owner, row.DeliveryFence, new(expiresAt, TimeSpan.Zero)));
                    }

                    after = keys[^1];
                }
            }
            catch (Exception error) when (IsWriteConflict(error)) { }
            catch (Exception error) when (IsStoreUnavailable(error))
            {
                return new(RuleDeliveryClaimDisposition.Unavailable, null);
            }
        }

        return new(RuleDeliveryClaimDisposition.Unavailable, null);
    }

    public async ValueTask<RuleDeliveryReadResult> ReadDeliveryAsync(
        RuleDeliveryLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();
        var (key, identity) = DeliveryAddress(lease.Identity);

        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                var row = await db.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.RunKey == key, cancellationToken);
                if (row is null || !string.Equals(row.IdentityBase64, identity, StringComparison.Ordinal))
                    return new(RuleDeliveryReadDisposition.NotFound, null);
                if (!string.Equals(row.DeliveryId64, lease.DeliveryId, StringComparison.Ordinal))
                    return new(RuleDeliveryReadDisposition.LeaseLost, null);

                var state = RuleRowCodec.Read(row);
                if (state.Terminal is null || state.Invocation.NativeDestination is null)
                    return new(RuleDeliveryReadDisposition.NotFound, null);
                var envelope = ReadEnvelope(row);
                var now = await db.ReadUtcTicksAsync(cancellationToken);
                if (!await IsCurrentAsync(db, row, cancellationToken))
                    continue;
                if (!OwnsDelivery(row, lease, now) || row.DeliveryAcknowledged)
                    return new(RuleDeliveryReadDisposition.LeaseLost, null);
                return new(RuleDeliveryReadDisposition.Ready, envelope);
            }
            catch (Exception error) when (IsStoreUnavailable(error))
            {
                return new(RuleDeliveryReadDisposition.Unavailable, null);
            }
        }

        return new(RuleDeliveryReadDisposition.Unavailable, null);
    }

    public async ValueTask<RuleTransitionConfirmation> AcknowledgeDeliveryAsync(
        RuleDeliveryLease lease,
        RuleDeliveryAcknowledgement acknowledgement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(acknowledgement);
        cancellationToken.ThrowIfCancellationRequested();
        var identity = lease.Identity;
        byte[] acknowledgementBytes;
        string acknowledgementJson;
        try
        {
            ArgumentNullException.ThrowIfNull(acknowledgement.Proof);
            acknowledgementBytes = RuleRecordCodec.DeliveryAcknowledgementBytes(acknowledgement.Proof);
            acknowledgementJson = Encoding.UTF8.GetString(acknowledgementBytes);
        }
        catch (Exception error) when (IsInvalidRecord(error))
        {
            return DeliveryConfirmation(RuleTransitionDisposition.Invalid, identity);
        }
        catch (NotSupportedException)
        {
            return DeliveryConfirmation(RuleTransitionDisposition.Unavailable, identity);
        }

        var (key, identityBase64) = DeliveryAddress(identity);
        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            RuleInvocationEnvelope invocation;
            RuleTerminalEnvelope envelope;
            long sampledAt;
            try
            {
                // Dispose the read context before invoking the native validator; no Rules transaction or
                // provider lock is held while it checks durable parent state.
                await using (var db = await contexts.CreateDbContextAsync(cancellationToken))
                {
                    var row = await db.Runs.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.RunKey == key, cancellationToken);
                    if (row is null)
                        return DeliveryConfirmation(RuleTransitionDisposition.LeaseLost, identity);
                    if (!string.Equals(row.IdentityBase64, identityBase64, StringComparison.Ordinal))
                        return DeliveryConfirmation(RuleTransitionDisposition.Conflict, identity);
                    if (!string.Equals(row.DeliveryId64, lease.DeliveryId, StringComparison.Ordinal))
                        return DeliveryConfirmation(RuleTransitionDisposition.LeaseLost, identity);

                    var state = RuleRowCodec.Read(row);
                    if (state.Terminal is null || state.Invocation.NativeDestination is null)
                        return DeliveryConfirmation(RuleTransitionDisposition.LeaseLost, identity);
                    invocation = state.Invocation;
                    envelope = ReadEnvelope(row);
                    if (row.DeliveryAcknowledged)
                        return string.Equals(row.DeliveryAcknowledgementJson, acknowledgementJson, StringComparison.Ordinal)
                            ? DeliveryConfirmation(RuleTransitionDisposition.Replay, identity, lease.DeliveryId, row.TerminalId64, row.ModelRevision)
                            : DeliveryConfirmation(RuleTransitionDisposition.Conflict, identity);

                    sampledAt = await db.ReadUtcTicksAsync(cancellationToken);
                    if (!await IsCurrentAsync(db, row, cancellationToken))
                        continue;
                    if (!OwnsDelivery(row, lease, sampledAt))
                        return DeliveryConfirmation(RuleTransitionDisposition.LeaseLost, identity);
                    if (!ProofMatches(envelope, acknowledgement.Proof))
                        return DeliveryConfirmation(RuleTransitionDisposition.Invalid, identity);
                }
            }
            catch (Exception error) when (IsStoreUnavailable(error))
            {
                return DeliveryConfirmation(RuleTransitionDisposition.Unavailable, identity);
            }

            var validator = deliveryProofValidator;
            var validationDisposition = RuleDeliveryProofValidationDisposition.Unavailable;
            if (validator is not null)
            {
                try
                {
                    var validation = await validator.ValidateAsync(invocation, envelope, acknowledgement.Proof, cancellationToken);
                    validationDisposition = validation?.Disposition ?? RuleDeliveryProofValidationDisposition.Unavailable;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    validationDisposition = RuleDeliveryProofValidationDisposition.Unavailable;
                }
            }

            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                var row = await db.Runs.SingleOrDefaultAsync(candidate => candidate.RunKey == key, cancellationToken);
                if (row is null)
                    return DeliveryConfirmation(RuleTransitionDisposition.LeaseLost, identity);
                if (!string.Equals(row.IdentityBase64, identityBase64, StringComparison.Ordinal))
                    return DeliveryConfirmation(RuleTransitionDisposition.Conflict, identity);
                if (!string.Equals(row.DeliveryId64, lease.DeliveryId, StringComparison.Ordinal))
                    return DeliveryConfirmation(RuleTransitionDisposition.LeaseLost, identity);

                var state = RuleRowCodec.Read(row);
                if (state.Terminal is null || state.Invocation.NativeDestination is null)
                    return DeliveryConfirmation(RuleTransitionDisposition.LeaseLost, identity);
                var currentEnvelope = ReadEnvelope(row);
                if (row.DeliveryAcknowledged)
                    return string.Equals(row.DeliveryAcknowledgementJson, acknowledgementJson, StringComparison.Ordinal)
                        ? DeliveryConfirmation(RuleTransitionDisposition.Replay, identity, lease.DeliveryId, row.TerminalId64, row.ModelRevision)
                        : DeliveryConfirmation(RuleTransitionDisposition.Conflict, identity);
                if (!FrozenEnvelopesEqual(envelope, currentEnvelope) || !ProofMatches(currentEnvelope, acknowledgement.Proof))
                    return DeliveryConfirmation(RuleTransitionDisposition.Conflict, identity);

                sampledAt = await db.ReadUtcTicksAsync(cancellationToken);
                if (!await IsCurrentAsync(db, row, cancellationToken))
                    continue;
                if (!OwnsDelivery(row, lease, sampledAt))
                    return DeliveryConfirmation(RuleTransitionDisposition.LeaseLost, identity);
                if (validationDisposition == RuleDeliveryProofValidationDisposition.Unavailable)
                    return DeliveryConfirmation(RuleTransitionDisposition.ProofUnavailable, identity);
                if (validationDisposition != RuleDeliveryProofValidationDisposition.Equivalent)
                    return DeliveryConfirmation(RuleTransitionDisposition.Invalid, identity);

                // SaveAsync wraps the row CAS in a transaction and rolls back if the lease expires after
                // the write but before a fresh database-clock sample can authorize its commit.
                var expiresAt = row.DeliveryExpiresAtUtcTicks!.Value;
                row.DeliveryAcknowledged = true;
                row.DeliveryAcknowledgementJson = acknowledgementJson;
                row.DeliveryOwner = null;
                row.DeliveryExpiresAtUtcTicks = null;
                if (!await SaveAsync(db, cancellationToken, new(sampledAt, expiresAt)))
                    return DeliveryConfirmation(RuleTransitionDisposition.LeaseLost, identity);
                return DeliveryConfirmation(RuleTransitionDisposition.Acknowledged, identity,
                    lease.DeliveryId, row.TerminalId64, row.ModelRevision);
            }
            catch (Exception error) when (IsWriteConflict(error)) { }
            catch (Exception error) when (IsStoreUnavailable(error))
            {
                return DeliveryConfirmation(RuleTransitionDisposition.Unavailable, identity);
            }
        }

        return DeliveryConfirmation(RuleTransitionDisposition.Unavailable, identity);
    }

    private long NewDeliveryExpiry(long now) =>
        new DateTimeOffset(now, TimeSpan.Zero).Add(_deliveryLeaseDuration).UtcTicks;

    private static (string Key, string Identity) DeliveryAddress(RuleRunIdentity identity)
    {
        var scope = RuleRecordCodec.ScopeBytes(identity);
        return (RuleRecordCodec.Hash(scope), Convert.ToBase64String(scope));
    }

    private static bool OwnsDelivery(RuleRunRecord row, RuleDeliveryLease lease, long now) =>
        !row.DeliveryAcknowledged && row.DeliveryOwner == lease.Owner && row.DeliveryFence == lease.Fence
        && row.DeliveryExpiresAtUtcTicks > now && row.DeliveryExpiresAtUtcTicks > 0;

    private static RuleTerminalEnvelope ReadEnvelope(RuleRunRecord row)
    {
        if (row.DeliveryPayloadJson is null)
            throw new InvalidDataException("rules.missing-delivery-record");
        var envelope = RuleRecordCodec.ReadDeliveryRecord(Encoding.UTF8.GetBytes(row.DeliveryPayloadJson));
        if (!string.Equals(envelope.DeliveryId, row.DeliveryId64, StringComparison.Ordinal)
            || !string.Equals(envelope.CanonicalPayloadSha256, row.DeliveryPayloadSha25664, StringComparison.Ordinal))
            throw new InvalidDataException("rules.delivery-projection");
        return envelope;
    }

    private static bool ProofMatches(RuleTerminalEnvelope envelope, RuleDeliveryProof proof) =>
        string.Equals(proof.DestinationIdentity, RuleRecordCodec.DestinationIdentity(envelope.Destination), StringComparison.Ordinal)
        && string.Equals(proof.TerminalId, envelope.Result.TerminalId, StringComparison.Ordinal)
        && string.Equals(proof.PayloadSha256, envelope.CanonicalPayloadSha256, StringComparison.Ordinal);

    private static bool FrozenEnvelopesEqual(RuleTerminalEnvelope left, RuleTerminalEnvelope right) =>
        RuleRecordCodec.DeliveryRecordBytes(left.DeliveryId, left.Destination, left.Result)
            .AsSpan().SequenceEqual(RuleRecordCodec.DeliveryRecordBytes(right.DeliveryId, right.Destination, right.Result));

    private static RuleTransitionConfirmation DeliveryConfirmation(RuleTransitionDisposition disposition,
        RuleRunIdentity identity, string? transitionId = null, string? terminalId = null, long? modelRevision = null) =>
        new(disposition, identity, transitionId, null, terminalId, null, modelRevision);
}
