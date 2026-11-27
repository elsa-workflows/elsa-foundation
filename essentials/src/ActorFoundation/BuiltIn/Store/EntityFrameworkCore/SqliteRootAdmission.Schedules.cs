using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    internal Task<Contract.Admission> AdmitScheduleAsync(string tenant, ScheduleRootKey key, string typeData, CancellationToken token)
    {
        key.Validate();
        var actorType = RootScheduleInventory.RequireActorType(scheduleActorType);
        var specification = RootScheduleInventory.CreatorSpecification(key, actorType, typeData);
        return WriteAsync<Contract.Admission>(async (db, ct) =>
        {
            if (!db.UsesRootScheduleInventory) throw new NotSupportedException("The profile does not admit schedule roots.");
            if (await RootScheduleInventory.FindAsync(db, tenant, key, actorType, ct) is { } existing)
                return new(Contract.AdmissionOutcome.Duplicate, new Contract.ActorRef(existing));

            var issuer = await ReadIssuerAsync(db, ct);
            var creator = RootScheduleInventory.BuildCreator(key, tenant, specification, issuer.Key, issuer.Partitions, out var reference);
            var (header, payload) = EncodeCreator(creator);
            if (!FitsEnvelope(header, payload)) throw new MetadataLimitExceeded(nameof(specification), EfStore.RootContentBytes);
            var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!;
            RequireOne(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationRootBirths" ("Tenant", "Reference", "Actor", "Partition", "Creation", "Creator", "Fingerprint")
                VALUES ({tenant}, {reference}, {creator.Target.Id}, {creator.Target.Partition}, 1, {creator.InboxKey.Id}, {fingerprint})
                """, ct));
            RequireOne(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationScheduleRoots" ("Tenant", "Key", "Definition", "Reference", "ActorType")
                VALUES ({tenant}, {key.Key}, {key.Definition}, {reference}, {actorType})
                """, ct));
            await EnqueueAsync(db, creator.Target.Id, creator.Target.Partition, creator.InboxKey.Id, 0, creator.Kind, header,
                payload, due: null, keepUntil: null, replyTo: null, token: ct);
            return new(Contract.AdmissionOutcome.Accepted, new Contract.ActorRef(reference));
        }, token);
    }

    internal Task<RootScheduleInventory.Page> ReadScheduleRootsAsync(string tenant, string definition, string? after, int limit,
        CancellationToken token)
    {
        var actorType = RootScheduleInventory.RequireActorType(scheduleActorType);
        return InAdmissionTransactionAsync((db, ct) =>
        {
            if (!db.UsesRootScheduleInventory) throw new NotSupportedException("The profile does not list schedule roots.");
            return RootScheduleInventory.ListAsync(db, tenant, definition, actorType, after, limit, ct);
        }, token, readOnly: true);
    }
}
