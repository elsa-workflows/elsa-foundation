using Kernel.Durable;
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

    internal static async Task CheckPendingAcrossExtensionAsync(Contract.IActorRetentionPolicies policies, Contract.BoundScope tenant,
        EfStore.RootImage image, Func<KernelDb> open, Func<ValueTask<Contract.Admission>> admit, Func<Func<Task>, Task> freshOwner,
        Contract.ActorRetentionWindows extended, CancellationToken token)
    {
        await using var db = open();
        var partition = new InstanceRef(image.Actor.Id).Partition;
        var inbox = db.Messages.AsNoTracking().Where(input => input.Partition == partition && input.Target == image.Actor.Id &&
            input.InboxScope == InboxKey.FoundationOccurrenceScope && input.Kind == Kinds.Delete);
        var position = db.Partitions.AsNoTracking().Where(row => row.Id == partition).Select(row => row.Position);
        var before = await position.SingleAsync(token);
        var immediate = new Contract.ActorRetentionWindows(Finished: TimeSpan.Zero);
        var active = await policies.ReadRetentionPolicyAsync(tenant, token);
        if (active.Windows != immediate)
        {
            var enabled = await policies.TryApplyRetentionPolicyAsync(tenant, active.Revision, immediate, long.MaxValue, cancellationToken: token);
            Require(enabled.Outcome == Contract.ActorRetentionPolicyChangeOutcome.Applied, "the fixture activates immediate retention");
            active = enabled.Current;
        }
        Require(await admit() is { Outcome: Contract.AdmissionOutcome.Accepted } && await inbox.CountAsync(input => !input.Done, token) == 1,
            "the trusted retention delete is pending while the preceding owner is stopped");
        var extension = await policies.TryApplyRetentionPolicyAsync(tenant, active.Revision, extended, 0, cancellationToken: token);
        Require(extension is { Outcome: Contract.ActorRetentionPolicyChangeOutcome.Applied, Quote.NewlyEligible: 0 },
            "a later durable retention policy extends eligibility before the pending command can settle");
        Require(await admit() is { Outcome: Contract.AdmissionOutcome.Refused } && await inbox.CountAsync(token) == 1,
            "a stale sweep cannot reserve the new policy revision while the actor is ineligible");

        await freshOwner(async () =>
        {
            while (!await inbox.AnyAsync(input => input.Done, token)) await Task.Delay(20, token);
            Require(await db.Instances.AsNoTracking().SingleAsync(row => row.Id == image.Actor.Id, token) is
                { Retired: false, Status: InstanceStatus.Finished } retained &&
                retained.Revision == image.Revision && await position.SingleAsync(token) == before &&
                !await db.Journal.AnyAsync(entry => entry.Instance == image.Actor.Id && entry.Revision < 0, token),
                "a restarted owner with a stale immediate window consumes the command without retiring the actor or moving the journal");

            Require(await policies.TryApplyRetentionPolicyAsync(tenant, extension.Current.Revision, immediate, long.MaxValue, cancellationToken: token) is
                { Outcome: Contract.ActorRetentionPolicyChangeOutcome.Applied },
                "a newer durable policy makes the same terminal selection eligible again");
            Require(await admit() is { Outcome: Contract.AdmissionOutcome.Accepted },
                "the new policy revision admits the same terminal stamp without waiting for the earlier receipt");
            while (!await db.Instances.AnyAsync(row => row.Id == image.Actor.Id && row.Retired, token)) await Task.Delay(20, token);
            Require(await inbox.CountAsync(input => input.Done, token) == 2 && await position.SingleAsync(token) == before + 1 &&
                await db.Journal.CountAsync(entry => entry.Instance == image.Actor.Id && entry.Revision == -1, token) == 1 &&
                await admit() is { Outcome: Contract.AdmissionOutcome.Duplicate },
                "the new revision retires once and keeps same-revision duplicates idempotent after retirement");
        });
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
