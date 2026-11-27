using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class RetentionPolicyChecks
{
    internal static async Task CheckAsync(Contract.IActorRetentionPolicies policies, Contract.BoundScope tenant,
        Contract.BoundScope other, Func<KernelDb> open, long terminalAt, CancellationToken token)
    {
        var keep = new Contract.ActorRetentionWindows();
        var expire = keep with { Finished = TimeSpan.Zero };
        var defaultPolicy = new Contract.ActorRetentionPolicy(0, keep);
        Task<Contract.ActorRetentionPolicyChange> ApplyAsync(long revision, Contract.ActorRetentionWindows windows, long maximum,
            string? confirmation = null) =>
            policies.TryApplyRetentionPolicyAsync(tenant, revision, windows, maximum, confirmation, token).AsTask();

        Require(await policies.ReadRetentionPolicyAsync(tenant, token) == defaultPolicy &&
            await policies.ReadRetentionPolicyAsync(other, token) == defaultPolicy,
            "unconfigured tenants have an indefinite retention policy at revision zero");
        Require(await ApplyAsync(0, keep, 0) is
            { Outcome: Contract.ActorRetentionPolicyChangeOutcome.Unchanged, Quote.NewlyEligible: 0 } unchanged &&
            unchanged.Current == defaultPolicy && unchanged.Quote.AsOf >= terminalAt,
            "an unchanged retention policy returns the provider clock without advancing the revision");
        var overLimit = await ApplyAsync(0, expire, 0);
        Require(overLimit is { Outcome: Contract.ActorRetentionPolicyChangeOutcome.OverLimit, Quote.NewlyEligible: 1, Confirmation.Length: > 0 } &&
            overLimit.Quote.AsOf >= terminalAt && overLimit.Current == defaultPolicy && await policies.ReadRetentionPolicyAsync(tenant, token) == defaultPolicy,
            "the activation transaction seals the fresh count and preserves policy when impact exceeds the reviewed count");
        var receipt = overLimit.Confirmation!;
        var damaged = (receipt[0] == 'A' ? "B" : "A") + receipt[1..];
        Require((await ApplyAsync(0, expire, 0, damaged)).Outcome == Contract.ActorRetentionPolicyChangeOutcome.OverLimit,
            "the activation transaction refuses a modified receipt");

        var concurrent = await Task.WhenAll(ApplyAsync(0, expire, 0, receipt), ApplyAsync(0, expire, 0, receipt));
        Require(concurrent.Count(change => change.Outcome == Contract.ActorRetentionPolicyChangeOutcome.Applied) == 1 &&
            concurrent.Count(change => change.Outcome == Contract.ActorRetentionPolicyChangeOutcome.Stale) == 1 &&
            concurrent.All(change => change.Current == new Contract.ActorRetentionPolicy(1, expire)) &&
            concurrent.Single(change => change.Outcome == Contract.ActorRetentionPolicyChangeOutcome.Applied).Quote.NewlyEligible == 1,
            "concurrent activations at the same revision apply once and return the durable winner to the stale request");
        Require(await policies.ReadRetentionPolicyAsync(tenant, token) == new Contract.ActorRetentionPolicy(1, expire) &&
            await policies.ReadRetentionPolicyAsync(other, token) == defaultPolicy,
            "the activated retention policy persists on a new read and is isolated to its exact tenant");

        Require(await ApplyAsync(0, keep, 0) is
            { Outcome: Contract.ActorRetentionPolicyChangeOutcome.Stale, Current.Revision: 1, Quote.NewlyEligible: 0 },
            "a replayed revision cannot change the policy and returns a new quote against the effective windows");
        Require(await ApplyAsync(1, keep, 0) is
            { Outcome: Contract.ActorRetentionPolicyChangeOutcome.Applied, Current.Revision: 2, Quote.NewlyEligible: 0 } &&
            await policies.ReadRetentionPolicyAsync(tenant, token) == new Contract.ActorRetentionPolicy(2, keep),
            "removing the finite window retains monotonic revision history even when all statuses return to indefinite retention");

        Require(await ApplyAsync(1, expire, 1) is
            { Outcome: Contract.ActorRetentionPolicyChangeOutcome.Stale, Current.Revision: 2, Quote.NewlyEligible: 1 },
            "a stale finite policy request quotes its new impact against the current indefinite windows");

        Require(await ApplyAsync(2, expire, 0, receipt) is
            { Outcome: Contract.ActorRetentionPolicyChangeOutcome.OverLimit, Current.Revision: 2, Quote.NewlyEligible: 1 },
            "the store rejects the previously confirmed receipt after a subsequent revision");

        await using var db = open();
        // Restore once both operations below have refused the committed tamper.
        var tag = await db.Database.SqlQuery<byte[]>($"""
            SELECT "Authentication" AS "Value" FROM "FoundationRetentionPolicies" WHERE "Tenant" = {tenant.Tenant}
            """).SingleAsync(token);
        var forged = tag.ToArray();
        forged[0] ^= 0xff;
        Task<int> SetTagAsync(byte[] value) => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "FoundationRetentionPolicies" SET "Authentication" = {value} WHERE "Tenant" = {tenant.Tenant}
            """, token);
        await SetTagAsync(forged);
        try
        {
            await ExpectUntrustedAsync(() => policies.ReadRetentionPolicyAsync(tenant, token).AsTask());
            await ExpectUntrustedAsync(() => ApplyAsync(2, expire, 1));
        }
        finally
        {
            await SetTagAsync(tag);
        }
        Require(await policies.ReadRetentionPolicyAsync(tenant, token) == new Contract.ActorRetentionPolicy(2, keep),
            "provider policy reads and writes refuse an unauthenticated committed row without changing the policy");
    }

    private static async Task ExpectUntrustedAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (StoreFormatException) { return; }
        throw new InvalidOperationException("An unauthenticated tenant retention policy should be refused.");
    }

    private static void Require(bool condition, string witness)
    {
        if (!condition) throw new InvalidOperationException(witness);
        Console.WriteLine("PASS " + witness);
    }
}
