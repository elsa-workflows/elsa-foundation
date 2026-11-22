using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed partial class RuleRoundStoreTests
{
    [Theory]
    [InlineData("run-json")]
    [InlineData("pending-receipt-json")]
    [InlineData("pending-receipt-missing")]
    public async Task Claim_skips_a_corrupt_earlier_candidate_and_claims_the_next_healthy_run(string corruption)
    {
        var invocations = new[] { NewRun(4), NewRun(4) }
            .OrderBy(invocation => RunKey(invocation.Identity), StringComparer.Ordinal)
            .ToArray();
        var corrupt = invocations[0];
        var healthy = invocations[1];
        var store = CreateStore();

        Assert.Equal(RuleAdmissionDisposition.Admitted, (await store.AdmitAsync(new(corrupt))).Disposition);
        if (corruption != "run-json")
            await AcceptPendingReceiptAsync(store, corrupt);
        Assert.Equal(RuleAdmissionDisposition.Admitted, (await store.AdmitAsync(new(healthy))).Disposition);

        await using (var db = fixture.Contexts.CreateDbContext())
        {
            var runKey = RunKey(corrupt.Identity);
            if (corruption == "run-json")
            {
                var run = await db.Runs.SingleAsync(row => row.RunKey == runKey);
                run.FactsJson = "{";
            }
            else
            {
                var receipt = await db.Receipts.SingleAsync(row => row.RunKey == runKey && row.Ordinal == 0);
                if (corruption == "pending-receipt-json")
                    receipt.ReceiptJson = "{";
                else
                    db.Receipts.Remove(receipt);
            }
            await db.SaveChangesAsync();
        }

        var claim = await store.ClaimWorkAsync(new("recovery-worker", [healthy.ExpectedImplementation], [1], [1], [1]));

        Assert.Equal(RuleWorkClaimDisposition.Claimed, claim.Disposition);
        Assert.NotNull(claim.Token);
        Assert.Equal(RunKey(healthy.Identity), RunKey(claim.Token.Lease.Identity));
    }

    private async Task AcceptPendingReceiptAsync(EfRuleRunStore store, RuleInvocationEnvelope invocation)
    {
        var claim = await store.ClaimWorkAsync(new("receipt-seeder", [invocation.ExpectedImplementation], [1], [1], [1]));
        Assert.Equal(RuleWorkClaimDisposition.Claimed, claim.Disposition);
        var work = await store.ReadWorkAsync(claim.Token!);
        Assert.Equal(RuleWorkReadDisposition.EvaluateRound, work.Disposition);
        var receipt = evaluator.Evaluate(work.RoundInput!);
        var accepted = await store.AcceptRoundAsync(claim.Token!,
            new(receipt, new("receipt-seed-attempt", "receipt-seeder", claim.Token!.Lease.Fence)));
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        Assert.Equal(RuleLeaseDisposition.Released, (await store.ReleaseAsync(claim.Token.Lease)).Disposition);
    }
}
