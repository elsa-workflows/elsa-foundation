using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    internal static async Task VerifyScheduleOccurrenceStoreAsync(KernelDb db, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        await PinScheduleStoreAsync(db, writing: false, token, loadIssuer: true);
    }

    internal async Task<bool> IsScheduledOccurrenceRetiredAsync(string tenant, RootScheduleOccurrence occurrence, StartSpec pinnedWorkflow,
        string actorReference, CancellationToken token)
    {
        occurrence.Validate();
        RequireCreator(occurrence.CreatorOperation, pinnedWorkflow);
        await using var db = create();
        if (!db.UsesRootScheduleSettlement) throw new NotSupportedException("The profile does not confirm scheduled workflow retirement.");
        // Read the catalog after taking the lineage DDL lock. The final statement observes the birth, tombstone and
        // cohort in one snapshot, so it does not use a snapshot opened before a concurrent DDL transaction finished.
        await using var transaction = await db.Database.BeginTransactionAsync(db.UsesRootScheduleLineage
            ? System.Data.IsolationLevel.ReadCommitted : System.Data.IsolationLevel.RepeatableRead, token);
        var pin = await PinScheduleStoreAsync(db, writing: false, token, loadIssuer: true);
        var issuer = pin.Issuer ?? throw new StoreFormatException("The pinned root store has no issuer.");
        var birth = ScheduledWorkflowBirth.Create(tenant, occurrence, pinnedWorkflow, issuer.Key, issuer.Partitions);
        if (birth.Reference != actorReference) return false;
        return await birth.IsRetiredAsync(db, tenant, token, query => BindOccurrenceSource(query, pin, db.UsesRootScheduleLineage, retirement: true));
    }

    internal async Task<Contract.Admission> AdmitScheduledOccurrenceAsync(string tenant, RootScheduleOccurrence occurrence,
        StartSpec pinnedWorkflow, CancellationToken token)
    {
        occurrence.Validate();
        RequireCreator(occurrence.CreatorOperation, pinnedWorkflow);
        var actorType = RootScheduleInventory.RequireActorType(scheduleActorType);
        using var attempt = pressure?.Begin(tenant, RootAdmissionKind.Start);
        await using var db = create();
        if (!db.UsesRootScheduleSettlement) throw new NotSupportedException("The profile does not admit scheduled workflow occurrences.");
        RequirePressureProfile(db);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token);
        var pin = await PinScheduleStoreAsync(db, writing: true, token);
        if (await RootScheduleInventory.FindAsync(db, tenant, occurrence.Schedule, actorType, token, Bind) != occurrence.ScheduleReference)
            return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch);

        var issuer = pin.Issuer ?? throw new StoreFormatException("The pinned root store has no issuer.");
        var birth = ScheduledWorkflowBirth.Create(tenant, occurrence, pinnedWorkflow, issuer.Key, issuer.Partitions);
        if (await birth.ReadReceiptAsync(db, tenant, token, Bind))
            return await CompleteAsync(Contract.AdmissionOutcome.Duplicate);

        var creator = birth.Creator;
        try { attempt?.Require(creator.Target.Partition); }
        catch (RootAdmissionRefused)
        {
            if (!await birth.ReadReceiptAsync(db, tenant, token, Bind)) throw;
            return await CompleteAsync(Contract.AdmissionOutcome.Duplicate);
        }
        var (header, payload) = EncodeCreator(creator);
        if (!FitsEnvelope(header, payload)) throw new MetadataLimitExceeded(nameof(pinnedWorkflow), EfStore.RootContentBytes);
        FormattableString write = $"""
            /* accepted schedule occurrence */
            WITH source AS MATERIALIZED (/*schedule-source*/), birth AS (
                INSERT INTO "FoundationRootBirths" ("Tenant", "Reference", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
                SELECT {tenant}, {birth.Reference}, {creator.Target.Id}, {creator.Target.Partition}, 1, {creator.InboxKey.Id}, {birth.Fingerprint}
                FROM source WHERE "SourceReady" ON CONFLICT DO NOTHING
                RETURNING "Tenant", "Reference", "Actor", "Partition", "Creator"
            )/*lineage-insert*/, creator AS (
                INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Partition", "Kind", "Due", "Payload", "Start",
                    "Done", "Seen", "Attempts", "Trial", "JobGeneration")
                SELECT birth."Creator", 0, birth."Actor", birth."Partition", {creator.Kind}, 0, {payload}::text, {header},
                    false, 0, 0, 0, 0 FROM birth RETURNING "Id"
            )
            SELECT source."SourceReady", EXISTS (SELECT 1 FROM birth) AS "Claimed",
                EXISTS (SELECT 1 FROM creator) /*lineage-check*/ AS "Enqueued" FROM source
            """;
        write = System.Runtime.CompilerServices.FormattableStringFactory.Create(write.Format
            .Replace("/*lineage-insert*/", db.UsesRootScheduleLineage ? """
                , lineage AS (
                    INSERT INTO "FoundationScheduleLineage" ("Tenant", "Reference", "RootReference", "Retired")
                    SELECT "Tenant", "Reference", "Reference", false FROM birth RETURNING "Reference")
                """ : "", StringComparison.Ordinal)
            .Replace("/*lineage-check*/", db.UsesRootScheduleLineage ? "AND EXISTS (SELECT 1 FROM lineage)" : "", StringComparison.Ordinal),
            write.GetArguments());
        var result = (await db.Database.SqlQuery<ScheduleWrite>(Bind(write)).ToListAsync(token)).Single();
        if (!result.SourceReady) throw new StoreFormatException("The pinned schedule store changed during occurrence admission.");
        if (result.Claimed)
        {
            if (!result.Enqueued) throw new StoreFormatException("The scheduled workflow birth has an incomplete creator or lineage receipt.");
            return await CompleteAsync(Contract.AdmissionOutcome.Accepted);
        }
        if (!await birth.ReadReceiptAsync(db, tenant, token, Bind))
            throw new StoreFormatException("A concurrent scheduled start has no committed birth.");
        return await CompleteAsync(Contract.AdmissionOutcome.Duplicate);

        FormattableString Bind(FormattableString query) => BindOccurrenceSource(query, pin, db.UsesRootScheduleLineage);

        async Task<Contract.Admission> CompleteAsync(Contract.AdmissionOutcome outcome)
        {
            await transaction.CommitAsync(token);
            if (outcome == Contract.AdmissionOutcome.Accepted) attempt?.Committed();
            return new(outcome, new Contract.ActorRef(birth.Reference));
        }
    }

    private static FormattableString BindOccurrenceSource(FormattableString query, PinnedRootSource pin, bool lineage, bool retirement = false)
    {
        var tables = """('"FoundationScheduleRoots"'), ('"FoundationRootBirths"'), ('"Messages"')""";
        if (retirement) tables += """, ('"Instances"'), ('"Journal"')""";
        if (lineage) tables += """, ('"FoundationScheduleLineage"')""";
        return WithAdmissionSource(query, pin, "/*schedule-source*/", 3 + (retirement ? 2 : 0) + (lineage ? 1 : 0), tables);
    }
}
