using Kernel.Foundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Runtime;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class ChildBirthProofChecks
{
    private sealed record Birth(string Actor, string Fingerprint);
    internal sealed record Ticket(Contract.ActorRef Reference, string Issuance, FoundationChildIssuance Body, byte[] FixtureKey)
    {
        internal Task<FoundationChildBirthProof> ConfirmAsync(Func<KernelDb> open, Contract.BoundScope scope, CancellationToken token) =>
            EfStore.ConfirmRootChildBirthAsync(open, scope, Reference, Issuance, token).AsTask();
    }

    internal static async Task<Ticket> ReadBirthAsync(Func<KernelDb> open, Contract.BoundScope scope, Contract.ActorRef parent,
        Contract.ActorRef child, long parentRevision, CancellationToken token)
    {
        await using var db = open();
        var parentBirth = await ReadAsync(db, scope, parent, token);
        var childBirth = await ReadAsync(db, scope, child, token);
        var body = new FoundationChildIssuance(scope.Tenant, parentBirth.Actor, parentBirth.Fingerprint,
            parentRevision, childBirth.Actor, childBirth.Fingerprint);
        return Issue(await ReadIssuerAsync(db, token), body, body.Fence());
    }

    internal static async Task<Ticket> PlanAbsentAsync(Func<KernelDb> open, Ticket observed, long parentRevision, long ordinal,
        CancellationToken token, bool atCurrentEpoch = false)
    {
        var (partition, epoch) = observed.Body.Fence();
        if (atCurrentEpoch)
        {
            await using var db = open();
            epoch = await db.Partitions.Where(row => row.Id == partition).Select(row => row.Epoch).SingleAsync(token);
        }
        return Issue(observed.FixtureKey, observed.Body with
        {
            Child = InstanceRef.ForChild(partition, partition, epoch, ordinal).Id,
            ParentRevision = parentRevision
        }, (partition, epoch));
    }

    internal static Ticket InvalidFingerprint(Ticket observed) => Issue(observed.FixtureKey,
        observed.Body with { ChildFingerprint = "v1:" + new string('F', 64) }, observed.Body.Fence());

    internal static Ticket InvalidSignature(Ticket observed) => observed with
    {
        Issuance = observed.Issuance[..^1] + (observed.Issuance[^1] == 'A' ? 'B' : 'A')
    };

    internal static async Task<long> ReadEpochAsync(Func<KernelDb> open, Ticket planned, CancellationToken token)
    {
        await using var db = open();
        var (partition, _) = planned.Body.Fence();
        return await db.Partitions.Where(row => row.Id == partition).Select(row => row.Epoch).SingleAsync(token);
    }

    // The existing host tests fabricate only the would-be issuance. Owners commit the actual lifecycle rows.
    private static Ticket Issue(byte[] key, FoundationChildIssuance body, (int Partition, long Epoch) fence)
    {
        var child = new Contract.ActorRef(FoundationRootCreator.ReferenceFor(key, body.Tenant, new(body.Child, 1)));
        return new(child, FoundationChildIssuance.Issue(key, body, new PartitionFence(fence.Partition, fence.Epoch, 0)), body, key);
    }

    private static Task<Birth> ReadAsync(KernelDb db, Contract.BoundScope scope, Contract.ActorRef actor, CancellationToken token) =>
        db.Database.SqlQuery<Birth>($"""
            SELECT "Actor", "Fingerprint" FROM "FoundationRootBirths"
            WHERE "Tenant" = {scope.Tenant} AND "Reference" = {actor.Id} AND "Creation" = 1
            """).SingleAsync(token);

    private static Task<byte[]> ReadIssuerAsync(KernelDb db, CancellationToken token) => db.UsesSqliteRootRuntime
        ? SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token)
        : PostgresRootAdmission.ReadRootIssuerKeyAsync(db, token);
}
