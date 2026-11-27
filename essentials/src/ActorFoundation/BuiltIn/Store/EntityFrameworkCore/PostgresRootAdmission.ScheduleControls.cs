using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    internal async Task<Contract.ScheduleControlResult> ApplyScheduleControlAsync(string tenant, ScheduleControlIntent intent, CancellationToken token)
    {
        intent.Validate();
        var actorType = RootScheduleInventory.RequireActorType(scheduleActorType);
        await using var db = create();
        if (!db.UsesRootScheduleControls) throw new NotSupportedException("The profile does not admit trusted schedule controls.");
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        var pin = await PinScheduleStoreAsync(db, writing: true, token);
        await RequireScheduleOwnerAsync(db, pin, actorType, token);
        try { await db.Database.ExecuteSqlRawAsync("""LOCK TABLE "FoundationScheduleControls" IN ROW EXCLUSIVE MODE""", token); }
        catch (Npgsql.PostgresException error) when (IsUnavailableSelectedSchema(error))
        {
            throw new StoreFormatException("The qualified schedule controls are unreadable.", error);
        }
        await RootScheduleControls.RequirePostgresDefinitionsAsync(db, token);
        // The first Stop has no inventory or watermark row to lock. Serialize both first writers and later updates.
        var lockIdentity = DurableKeyJson.Write(new[] { "schedule-control", tenant, intent.Key.Key });
        await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT pg_advisory_xact_lock(hashtextextended({lockIdentity}, 0))""", token);

        var reference = await RootScheduleInventory.FindAsync(db, tenant, intent.Key, actorType, token, Bind);
        var current = await RootScheduleControls.ReadAsync(db, tenant, intent.Key, token, Bind);
        if (current.Classify(tenant, actorType, intent, reference) is { } prior)
        {
            await transaction.CommitAsync(token);
            return prior;
        }

        if (reference is null && intent.Kind == Contract.ScheduleControlKind.Set)
        {
            var specification = RootScheduleInventory.CreatorSpecification(intent.Key, actorType, intent.SetHeader!);
            var creator = await InsertScheduleCreatorAsync(db, tenant, intent.Key, actorType, specification, pin, Bind, token);
            if (creator.Write is not { SourceReady: true, Claimed: true, Enqueued: true })
                throw new StoreFormatException("The pinned schedule control could not create its exact schedule root.");
            reference = creator.Reference;
        }
        else if (reference is not null)
        {
            var envelope = intent.Encode(tenant, actorType, reference);
            if (await db.Database.ExecuteSqlInterpolatedAsync(Bind($"""
                WITH source AS MATERIALIZED (/*schedule-source*/)
                INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Partition", "Kind", "Due", "Payload", "Start",
                    "Done", "Seen", "Attempts", "Trial", "JobGeneration", "KeepUntil")
                SELECT {envelope.Id}, {InboxKey.FoundationOccurrenceScope}, b."Actor", b."Partition", {envelope.Kind}, 0,
                    {envelope.Payload}, {envelope.Header}, false, 0, 0, 0, 0,
                    floor(extract(epoch from clock_timestamp()) * 1000)::bigint + {RootScheduleControls.InboxRetentionMilliseconds}
                FROM source, "FoundationScheduleRoots" s
                JOIN "FoundationRootBirths" b ON b."Reference" = s."Reference" AND b."Tenant" = s."Tenant"
                WHERE source."SourceReady" AND s."Tenant" = {tenant} AND s."Key" = {intent.Key.Key} AND s."Definition" = {intent.Key.Definition}
                    AND s."ActorType" = {actorType} AND s."Reference" = {reference} AND b."Creation" = 1
                """), token) != 1)
                throw new StoreFormatException("The pinned schedule control could not enqueue to its exact schedule root.");
        }

        await RootScheduleControls.WriteAsync(db, tenant, actorType, intent, reference, current, token, Bind);
        await transaction.CommitAsync(token);
        return new(Contract.ScheduleControlOutcome.Applied, reference is null ? null : new Contract.ActorRef(reference));

        FormattableString Bind(FormattableString sql) => WithAdmissionSource(sql, pin, "/*schedule-source*/", 4,
            """('"FoundationScheduleControls"'), ('"FoundationScheduleRoots"'), ('"FoundationRootBirths"'), ('"Messages"')""");
    }
}
