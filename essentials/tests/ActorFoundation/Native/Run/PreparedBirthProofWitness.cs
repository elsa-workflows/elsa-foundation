using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PreparedBirthProofWitness
{
    internal static async Task<string> DifferentDigestAsync(KernelDb db, Contract.BoundScope scope, PendingRootStart offer,
        CancellationToken token)
    {
        var (stage, key) = await ReadIssuanceAsync(db, scope, offer, token);
        var changed = stage with { Fingerprint = "v1:" + new string(stage.Fingerprint[3] == 'A' ? 'B' : 'A', 64) };
        return RootPreparedStarts.Offer(changed, new StartSpec(offer.Type, offer.TypeData), key).Issuance;
    }

    internal static async Task ExpireReceiptAsync(KernelDb db, Contract.BoundScope scope, PendingRootStart offer,
        CancellationToken token)
    {
        var (stage, _) = await ReadIssuanceAsync(db, scope, offer, token);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "FoundationRootReceipts" SET "Until" = 0
            WHERE "Tenant" = {stage.Tenant} AND "Operation" = {stage.Operation} AND "Reference" = {stage.Reference}
            """, token) != 1 ||
            await db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM "FoundationRootReceipts"
            WHERE "Tenant" = {stage.Tenant} AND "Operation" = {stage.Operation} AND "Reference" = {stage.Reference} AND "Until" = 0
            """, token) != 1)
            throw new InvalidOperationException("The exact ordinary operation receipt could not be expired and removed.");
        await transaction.CommitAsync(token);
    }

    private static async Task<(PreparedRootStart Stage, byte[] Key)> ReadIssuanceAsync(KernelDb db, Contract.BoundScope scope,
        PendingRootStart offer, CancellationToken token)
    {
        var key = await db.Database.SqlQueryRaw<byte[]>("""SELECT "Key" AS "Value" FROM "FoundationRootIssuer" """).SingleAsync(token);
        var stage = RootPreparedStarts.Verify(key, scope, offer.Actor, offer.Issuance) ??
            throw new InvalidOperationException("The fixture's issuer could not verify the actual preparation.");
        return (stage, key);
    }
}
