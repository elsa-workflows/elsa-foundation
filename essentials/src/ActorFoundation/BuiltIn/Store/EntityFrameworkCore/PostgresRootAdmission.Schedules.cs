using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    private sealed record ScheduleWrite(bool SourceReady, bool Claimed, bool Enqueued);

    internal async Task<Contract.Admission> AdmitScheduleAsync(string tenant, ScheduleRootKey key, string typeData,
        CancellationToken cancellationToken)
    {
        key.Validate();
        var actorType = RootScheduleInventory.RequireActorType(scheduleActorType);
        var specification = RootScheduleInventory.CreatorSpecification(key, actorType, typeData);
        await using var db = create();
        if (!db.UsesRootScheduleInventory || db.UsesRootScheduleControls)
            throw new NotSupportedException("The profile does not admit unversioned schedule roots.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var pin = await PinScheduleStoreAsync(db, writing: true, cancellationToken);
        if (await RootScheduleInventory.FindAsync(db, tenant, key, actorType, cancellationToken, Bind) is { } existing)
            return await CompleteAsync(existing, duplicate: true);

        var (result, reference) = await InsertScheduleCreatorAsync(db, tenant, key, actorType, specification, pin, Bind, cancellationToken);
        if (!result.SourceReady) throw new StoreFormatException("The pinned schedule store changed during admission.");
        if (!result.Claimed)
        {
            // Read Committed observes the contender that resolved the unique key after the statement snapshot.
            var winner = await RootScheduleInventory.FindAsync(db, tenant, key, actorType, cancellationToken, Bind)
                ?? throw new StoreFormatException("A concurrent schedule claim has no admitted creator.");
            return await CompleteAsync(winner, duplicate: true);
        }
        if (!result.Enqueued) throw new StoreFormatException("The schedule claim has no creator.");
        return await CompleteAsync(reference, duplicate: false);

        FormattableString Bind(FormattableString sql) => BindScheduleSource(sql, pin);

        async Task<Contract.Admission> CompleteAsync(string exactRef, bool duplicate)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(duplicate ? Contract.AdmissionOutcome.Duplicate : Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(exactRef));
        }
    }

    private static async Task<(ScheduleWrite Write, string Reference)> InsertScheduleCreatorAsync(KernelDb db, string tenant,
        ScheduleRootKey key, string actorType, StartSpec specification, PinnedRootSource pin,
        Func<FormattableString, FormattableString> bind, CancellationToken cancellationToken)
    {
        var issuer = pin.Issuer ?? throw new StoreFormatException("The pinned root store has no issuer.");
        var creator = RootScheduleInventory.BuildCreator(key, tenant, specification, issuer.Key, issuer.Partitions, out var reference);
        var (header, payload) = EncodeCreator(creator);
        if (!FitsEnvelope(header, payload)) throw new MetadataLimitExceeded(nameof(specification), EfStore.RootContentBytes);
        var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!;
        var result = (await db.Database.SqlQuery<ScheduleWrite>(bind($"""
            WITH source AS MATERIALIZED (/*schedule-source*/), claimed AS (
                INSERT INTO "FoundationScheduleRoots" ("Tenant", "Key", "Definition", "Reference", "ActorType")
                SELECT {tenant}, {key.Key}, {key.Definition}, {reference}, {actorType} FROM source WHERE "SourceReady"
                ON CONFLICT ("Tenant", "Key") DO NOTHING RETURNING "Reference"
            ), birth AS (
                INSERT INTO "FoundationRootBirths" ("Tenant", "Reference", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
                SELECT {tenant}, "Reference", {creator.Target.Id}, {creator.Target.Partition}, 1, {creator.InboxKey.Id}, {fingerprint}
                FROM claimed
                RETURNING "Actor", "Partition", "Creator"
            ), creator AS (
                INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Partition", "Kind", "Due", "Payload", "Start",
                    "Done", "Seen", "Attempts", "Trial", "JobGeneration")
                SELECT birth."Creator", 0, birth."Actor", birth."Partition", {creator.Kind}, 0, {payload}::text, {header},
                    false, 0, 0, 0, 0 FROM birth
                RETURNING "Id"
            )
            SELECT source."SourceReady", EXISTS (SELECT 1 FROM claimed) AS "Claimed",
                EXISTS (SELECT 1 FROM creator) AS "Enqueued" FROM source
            """)).ToListAsync(cancellationToken)).Single();
        return (result, reference);
    }

    internal async Task<RootScheduleInventory.Page> ReadScheduleRootsAsync(string tenant, string definition, string? after, int limit,
        CancellationToken token)
    {
        var actorType = RootScheduleInventory.RequireActorType(scheduleActorType);
        await using var db = create();
        if (!db.UsesRootScheduleInventory) throw new NotSupportedException("The profile does not list schedule roots.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token);
        var pin = await PinScheduleStoreAsync(db, writing: false, token);
        return await RootScheduleInventory.ListAsync(db, tenant, definition, actorType, after, limit, token, sql => BindScheduleSource(sql, pin));
    }

    private static async Task<PinnedRootSource> PinScheduleStoreAsync(KernelDb db, bool writing, CancellationToken token, bool loadIssuer = false)
    {
        var pin = await PinRootNamespaceAsync(db, loadIssuer: writing || loadIssuer, token)
            ?? throw new StoreFormatException("PostgreSQL schedule inventory requires a pinned root store.");
        try
        {
            await db.Database.ExecuteSqlRawAsync(writing
                ? """LOCK TABLE "FoundationScheduleRoots" IN ROW EXCLUSIVE MODE"""
                : """LOCK TABLE "FoundationScheduleRoots" IN ACCESS SHARE MODE""", token);
        }
        catch (Npgsql.PostgresException error) when (IsUnavailableSelectedSchema(error))
        {
            throw new StoreFormatException("The qualified schedule inventory is unreadable.", error);
        }
        await RootScheduleInventory.RequirePostgresDefinitionsAsync(db, token);
        return pin;
    }

    internal static async Task BindScheduleOwnerAsync(KernelDb db, string? auxiliaryActor, CancellationToken token)
    {
        if (!RootScheduleOwnerMode.Applies(db))
        {
            if (db.UsesRootScheduleControls && auxiliaryActor is null)
                throw new NotSupportedException("This PostgreSQL root profile requires a schedule owner.");
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        var pin = await PinRootNamespaceAsync(db, loadIssuer: false, token)
            ?? throw new StoreFormatException("Binding the schedule owner mode requires a pinned root store.");
        await LockScheduleOwnerModeAsync(db, token);
        await RootScheduleOwnerMode.BindAsync(db, auxiliaryActor, token, sql => BindScheduleOwnerSource(sql, pin));
        await transaction.CommitAsync(token);
    }

    private static async Task RequireScheduleOwnerAsync(KernelDb db, PinnedRootSource pin, string actorType, CancellationToken token)
    {
        if (!RootScheduleOwnerMode.Applies(db)) return;
        await LockScheduleOwnerModeAsync(db, token);
        await RootScheduleOwnerMode.RequireAsync(db, actorType, token, sql => BindScheduleOwnerSource(sql, pin));
    }

    private static async Task LockScheduleOwnerModeAsync(KernelDb db, CancellationToken token)
    {
        try { await db.Database.ExecuteSqlRawAsync("""LOCK TABLE "FoundationScheduleOwnerMode" IN ACCESS SHARE MODE""", token); }
        catch (Npgsql.PostgresException error) when (IsUnavailableSelectedSchema(error))
        {
            throw new StoreFormatException("The schedule owner mode is unreadable.", error);
        }
    }

    private static FormattableString BindScheduleOwnerSource(FormattableString sql, PinnedRootSource pin) =>
        WithAdmissionSource(sql, pin, "/*schedule-source*/", 1, """('"FoundationScheduleOwnerMode"')""");

    private static FormattableString BindScheduleSource(FormattableString sql, PinnedRootSource pin) =>
        WithAdmissionSource(sql, pin, "/*schedule-source*/", 3,
            """('"FoundationScheduleRoots"'), ('"FoundationRootBirths"'), ('"Messages"')""");
}
