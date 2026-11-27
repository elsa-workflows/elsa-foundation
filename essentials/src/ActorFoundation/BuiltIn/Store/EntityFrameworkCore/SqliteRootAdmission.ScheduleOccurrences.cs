using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    internal Task<bool> IsScheduledOccurrenceRetiredAsync(string tenant, RootScheduleOccurrence occurrence, StartSpec pinnedWorkflow,
        string actorReference, CancellationToken token)
    {
        occurrence.Validate();
        RequireCreator(occurrence.CreatorOperation, pinnedWorkflow);
        return InAdmissionTransactionAsync(async (db, ct) =>
        {
            if (!db.UsesRootScheduleSettlement) throw new NotSupportedException("The profile does not confirm scheduled workflow retirement.");
            var issuer = await ReadIssuerAsync(db, ct);
            var birth = ScheduledWorkflowBirth.Create(tenant, occurrence, pinnedWorkflow, issuer.Key, issuer.Partitions);
            return birth.Reference == actorReference && await birth.IsRetiredAsync(db, tenant, ct);
        }, token, readOnly: true);
    }

    internal async Task<Contract.Admission> AdmitScheduledOccurrenceAsync(string tenant, RootScheduleOccurrence occurrence,
        StartSpec pinnedWorkflow, CancellationToken token)
    {
        occurrence.Validate();
        RequireCreator(occurrence.CreatorOperation, pinnedWorkflow);
        var actorType = RootScheduleInventory.RequireActorType(scheduleActorType);
        using var attempt = pressure?.Begin(tenant, RootAdmissionKind.Start);
        return await WriteAsync<Contract.Admission>(async (db, ct) =>
        {
            if (!db.UsesRootScheduleSettlement) throw new NotSupportedException("The profile does not admit scheduled workflow occurrences.");
            if (await RootScheduleInventory.FindAsync(db, tenant, occurrence.Schedule, actorType, ct) != occurrence.ScheduleReference)
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.TargetMismatch);

            var issuer = await ReadIssuerAsync(db, ct);
            var birth = ScheduledWorkflowBirth.Create(tenant, occurrence, pinnedWorkflow, issuer.Key, issuer.Partitions);
            var reference = new Contract.ActorRef(birth.Reference);
            if (await birth.ReadReceiptAsync(db, tenant, ct))
                return new(Contract.AdmissionOutcome.Duplicate, reference);

            var creator = birth.Creator;
            attempt?.Require(creator.Target.Partition);
            var (header, payload) = EncodeCreator(creator);
            if (!FitsEnvelope(header, payload)) throw new MetadataLimitExceeded(nameof(pinnedWorkflow), EfStore.RootContentBytes);
            RequireOne(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationRootBirths" ("Tenant", "Reference", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
                VALUES ({tenant}, {birth.Reference}, {creator.Target.Id}, {creator.Target.Partition}, 1, {creator.InboxKey.Id}, {birth.Fingerprint})
                """, ct));
            if (db.UsesRootScheduleLineage)
                RequireOne(await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "FoundationScheduleLineage" ("Tenant", "Reference", "RootReference", "Retired")
                    VALUES ({tenant}, {birth.Reference}, {birth.Reference}, false)
                    """, ct));
            await EnqueueAsync(db, creator.Target.Id, creator.Target.Partition, creator.InboxKey.Id, 0, creator.Kind, header,
                payload, due: null, keepUntil: null, replyTo: null, token: ct);
            return new(Contract.AdmissionOutcome.Accepted, reference);
        }, token, attempt);
    }
}
