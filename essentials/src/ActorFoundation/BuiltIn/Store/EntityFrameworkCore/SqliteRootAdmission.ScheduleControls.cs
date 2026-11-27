using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    private sealed record ScheduleAddress(string Actor, int Partition);

    internal Task<Contract.ScheduleControlResult> ApplyScheduleControlAsync(string tenant, ScheduleControlIntent intent, CancellationToken token)
    {
        intent.Validate();
        var actorType = RootScheduleInventory.RequireActorType(scheduleActorType);
        return WriteAsync<Contract.ScheduleControlResult>(async (db, now, ct) =>
        {
            if (!db.UsesRootScheduleControls) throw new NotSupportedException("The profile does not admit trusted schedule controls.");
            var reference = await RootScheduleInventory.FindAsync(db, tenant, intent.Key, actorType, ct);
            var current = await RootScheduleControls.ReadAsync(db, tenant, intent.Key, ct);
            if (current.Classify(tenant, actorType, intent, reference) is { } prior) return prior;

            if (reference is null && intent.Kind == Contract.ScheduleControlKind.Set)
            {
                var specification = RootScheduleInventory.CreatorSpecification(intent.Key, actorType, intent.SetHeader!);
                reference = await InsertScheduleCreatorAsync(db, tenant, intent.Key, actorType, specification, ct);
            }
            else if (reference is not null)
            {
                var target = await db.Database.SqlQuery<ScheduleAddress>($"""
                    SELECT b."Actor", b."Partition" FROM "FoundationScheduleRoots" s
                    JOIN "FoundationRootBirths" b ON b."Reference" = s."Reference" AND b."Tenant" = s."Tenant"
                    WHERE s."Tenant" = {tenant} AND s."Key" = {intent.Key.Key} AND s."Definition" = {intent.Key.Definition}
                        AND s."ActorType" = {actorType} AND s."Reference" = {reference} AND b."Creation" = 1
                    """).SingleOrDefaultAsync(ct) ?? throw new StoreFormatException("The schedule control lost its exact schedule root.");
                var envelope = intent.Encode(tenant, actorType, reference);
                await EnqueueAsync(db, target.Actor, target.Partition, envelope.Id, InboxKey.FoundationOccurrenceScope,
                    envelope.Kind, envelope.Header, envelope.Payload, due: null, keepUntil: checked(now + RootScheduleControls.InboxRetentionMilliseconds),
                    replyTo: null, token: ct);
            }

            await RootScheduleControls.WriteAsync(db, tenant, actorType, intent, reference, current, ct);
            return new(Contract.ScheduleControlOutcome.Applied, reference is null ? null : new Contract.ActorRef(reference));
        }, token);
    }
}
