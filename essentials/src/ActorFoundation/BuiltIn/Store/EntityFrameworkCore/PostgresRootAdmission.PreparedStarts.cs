using System.Data;
using System.Runtime.CompilerServices;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    private const string PreparedStartRelations = """
        ('"FoundationPreparedRootStarts"'), ('"FoundationRootIssuer"'), ('"Partitions"'),
        ('"FoundationRootBirths"'), ('"Instances"'), ('"Journal"'), ('"Messages"')
        """;

    private sealed record PreparedStartCatalog(bool SourceReady, long? Table);
    private sealed record PreparedStartSource(uint Marker, uint Namespace, long Table);
    private sealed record PreparedStartPlan(PreparedRootStart Stage, PreparedStartSource Source, PendingRootStart Offer,
        Message Creator, string Header, string? Payload);

    internal async Task<Contract.Admission?> AdmitProtectedRootStartAsync(string tenant, string operation, StartSpec spec,
        ProtectRootStartAsync protectRootStart, CancellationToken token, CancellationToken caller)
    {
        var receipt = await AdmitProtectedRootAsync(tenant, operation, spec, null, null, protectRootStart, token, caller);
        return receipt is null ? null : new(receipt.Duplicate ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted,
            new Contract.ActorRef(receipt.Reference));
    }

    private async Task<Receipt?> AdmitProtectedRootAsync(string tenant, string operation, StartSpec spec,
        TimeSpan? keepFor, DirectReply? reply, ProtectRootStartAsync protectRootStart, CancellationToken token, CancellationToken commitToken)
    {
        RequireCreator(operation, spec);
        var windowMs = (long)ReceiptWindow(deduplicationWindow, keepFor).TotalMilliseconds;
        var storedOperation = reply is null ? RootPreparedStarts.OneWayOperation(operation) : operation;
        var digest = FoundationRootCreator.OperationDigest(storedOperation);
        using var attempt = pressure?.Begin(tenant, RootAdmissionKind.Start);
        var (duplicate, plan) = await PrepareAsync();
        if (plan is null) return duplicate;

        // PrepareAsync has committed and disposed its connection and locks. The host may use another database.
        var protectedStart = await RootPreparedStarts.ProtectAsync(protectRootStart, plan.Offer, token);

        await using var db = create();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var (pin, source) = await PinPreparedStartSourceAsync(db, loadIssuer: false, token);
        if (source != plan.Source) throw new StoreFormatException("The root preparation's physical source changed before admission.");
        var stage = plan.Stage;
        if (!protectedStart)
        {
            await RootPreparedStarts.ConsumeAsync(db, stage, token, query => BindPreparedStart(query, pin, source.Table, deadline: null));
            var winner = await ReadStartReceiptAsync(db, pin, tenant, digest, liveOnly: true, token);
            var open = await IsBeforeDeadlineAsync(db, stage.Deadline, token);
            await transaction.CommitAsync(token);
            if (!open) return null;
            if (winner is not null) return new(winner, true);
            throw new RootAdmissionRefused(Contract.RefusalReason.TargetMismatch, TimeSpan.Zero);
        }

        if (!await IsBeforeDeadlineAsync(db, stage.Deadline, token)) return null;
        if (!await RootPreparedStarts.ConsumeAsync(db, stage, token, query => BindPreparedStart(query, pin, source.Table, stage.Deadline)))
        {
            var winner = await ReadStartReceiptAsync(db, pin, tenant, digest, liveOnly: true, token);
            return winner is null ? null : new(winner, true);
        }

        // The preparation stays consumed only if the original receipt, birth, and creator transaction commits.
        var write = await WriteStartAsync(db, pin, tenant, digest, stage.Reference, windowMs, claimFirst: false,
            plan.Creator, stage.Fingerprint, fits: true, plan.Header, plan.Payload, reply, token);
        if (!await IsBeforeDeadlineAsync(db, stage.Deadline, token)) return null;
        if (!write.Claimed)
        {
            var winner = await ReadStartReceiptAsync(db, pin, tenant, digest, liveOnly: false, token)
                ?? throw new InvalidOperationException("The conflicting root receipt no longer exists.");
            await transaction.CommitAsync(token);
            return new(winner, true);
        }
        if (!write.Enqueued) throw new InvalidOperationException("A protected root start must commit its creator with the receipt.");
        // Once COMMIT starts, local one-way expiry cannot turn a landed birth into a NodeBusy refusal.
        token.ThrowIfCancellationRequested();
        await transaction.CommitAsync(commitToken);
        attempt?.Committed();
        return new(stage.Reference, false);

        async Task<(Receipt? Duplicate, PreparedStartPlan? Plan)> PrepareAsync()
        {
            await using var preparation = create();
            RequirePressureProfile(preparation);
            await using var transaction = await preparation.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var (pin, source) = await PinPreparedStartSourceAsync(preparation, loadIssuer: true, token);
            if (reply is not null && !await IsBeforeDeadlineAsync(preparation, reply.Deadline, token)) return (null, null);
            var existing = await ReadStartReceiptAsync(preparation, pin, tenant, digest, liveOnly: true, token);
            if (existing is not null) return (new(existing, true), null);

            var issuer = pin.Issuer ?? throw new StoreFormatException(RootIssuerMissing);
            var name = tenant + "_" + Guid.NewGuid().ToString("N");
            var preferred = InstanceRef.Spread(name, issuer.Partitions);
            var actor = InstanceRef.In(attempt?.Place(preferred, issuer.Partitions) ?? preferred, name);
            try { attempt?.Require(actor.Partition); }
            catch (RootAdmissionRefused)
            {
                // A concurrent ingress may have committed while the attempted placement was refused.
                existing = await ReadStartReceiptAsync(preparation, pin, tenant, digest, liveOnly: true, token);
                if (existing is null) throw;
                return (new(existing, true), null);
            }

            var reference = FoundationRootCreator.ReferenceFor(issuer.Key, tenant, new(actor.Id, 1));
            var creator = FoundationRootCreator.Create(actor, storedOperation, digest, spec, null, reply?.Address);
            var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!;
            var (header, payload) = EncodeCreator(creator);
            if (!FitsEnvelope(header, payload, reply?.Address))
            {
                existing = await ReadStartReceiptAsync(preparation, pin, tenant, digest, liveOnly: true, token);
                if (existing is not null) return (new(existing, true), null);
                throw new MetadataLimitExceeded(nameof(spec), EfStore.RootContentBytes);
            }

            var deadline = reply?.Deadline ?? RootPreparedStarts.OneWayDeadline(await RootEventAdmission.ReadPostgresClockAsync(preparation, token));
            var stage = new PreparedRootStart(tenant, reference, actor.Id, actor.Partition, digest, fingerprint, deadline);
            var offer = RootPreparedStarts.Offer(stage, spec, issuer.Key);
            if (!await RootPreparedStarts.RecordAsync(preparation, stage, token, query => BindPreparedStart(query, pin, source.Table, stage.Deadline)))
                return (null, null);
            await transaction.CommitAsync(token);
            return (null, new(stage, source, offer, creator, header, payload));
        }
    }

    private static async Task<(PinnedRootSource Pin, PreparedStartSource Source)> PinPreparedStartSourceAsync(KernelDb db,
        bool loadIssuer, CancellationToken token)
    {
        if (!db.UsesRootReplyProtocol || !db.UsesRootPreparedStarts)
            throw new NotSupportedException("Held root protection requires an installed prepared-start profile.");
        var pin = await PinRootNamespaceAsync(db, loadIssuer, token)
            ?? throw new StoreFormatException("Held root protection requires a pinned PostgreSQL root store.");
        try
        {
            await db.Database.ExecuteSqlRawAsync("""LOCK TABLE ONLY "FoundationPreparedRootStarts" IN ACCESS SHARE MODE""", token);
            var catalog = await db.Database.SqlQuery<PreparedStartCatalog>(WithAdmissionSource($"""
                WITH source AS MATERIALIZED (/*prepared-start-source*/)
                SELECT source."SourceReady", pg_catalog.to_regclass('"FoundationPreparedRootStarts"')::oid::bigint AS "Table" FROM source
                """, pin, RootPreparedStarts.Source, 7, PreparedStartRelations)).SingleAsync(token);
            if (!catalog.SourceReady || catalog.Table is not { } table)
                throw new StoreFormatException("The PostgreSQL held root preparation table changed its physical source.");
            return (pin, new(pin.Marker, pin.Namespace, table));
        }
        catch (PostgresException error) when (IsUnavailableSelectedSchema(error))
        {
            throw new StoreFormatException("The PostgreSQL held root preparation table is unreadable.", error);
        }
    }

    internal static async Task<(byte[] Key, Func<FormattableString, FormattableString> BindSource)> PinRootPreparedStartProofAsync(
        KernelDb db, CancellationToken token)
    {
        var (pin, source) = await PinPreparedStartSourceAsync(db, loadIssuer: true, token);
        var issuer = pin.Issuer ?? throw new StoreFormatException(RootIssuerMissing);
        return (issuer.Key, query => BindPreparedStart(query, pin, source.Table, deadline: null));
    }

    private static FormattableString BindPreparedStart(FormattableString query, PinnedRootSource pin, long table, long? deadline)
    {
        const string physical = "/*prepared-physical-source*/";
        var guard = $$"""
            SELECT source."SourceReady"
                AND pg_catalog.to_regclass('"FoundationPreparedRootStarts"')::oid::bigint = {{{query.ArgumentCount}}}
                AND ({{{query.ArgumentCount + 1}}}::bigint IS NULL OR
                    floor(extract(epoch from clock_timestamp()) * 1000)::bigint < {{{query.ArgumentCount + 1}}}) AS "SourceReady"
            FROM ({{physical}}) source
            """;
        var bound = FormattableStringFactory.Create(query.Format.Replace(RootPreparedStarts.Source, guard, StringComparison.Ordinal),
            [.. query.GetArguments(), table, deadline!]);
        return WithAdmissionSource(bound, pin, physical, 7, PreparedStartRelations);
    }

    private static Task<bool> IsBeforeDeadlineAsync(KernelDb db, long deadline, CancellationToken token) => db.Database.SqlQuery<bool>($"""
        SELECT ({deadline} > floor(extract(epoch from clock_timestamp()) * 1000)::bigint) AS "Value"
        """).SingleAsync(token);
}
