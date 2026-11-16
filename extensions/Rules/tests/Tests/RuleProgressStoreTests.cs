using System.Globalization;
using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Elsa.Rules.Runtime;
using Microsoft.EntityFrameworkCore;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed partial class RuleRoundStoreTests
{
    [Fact]
    public async Task Progress_pages_are_bounded_exclusive_and_keep_canonical_visibility_order()
    {
        var invocation = NewFactOnlyRun(260);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        var accepted = await store.AcceptRoundAsync(token, Submission(receipt, token, "empty-round"));
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        var pending = await store.ReadWorkAsync(token);
        var consumed = await store.ConsumeRoundAsync(pending.CurrentToken!,
            new(accepted.ReceiptId!, receipt.SemanticReceiptSha256));
        Assert.Equal(RuleTransitionDisposition.Consumed, consumed.Disposition);
        var beforeReads = await SnapshotAsync(invocation.Identity);

        var first = await store.ReadProgressAsync(invocation.Identity, null, RuleLimits.MaxPageEntries);
        Assert.Equal(RuleProgressReadDisposition.Page, first.Disposition);
        Assert.Equal(RuleLimits.MaxPageEntries, first.Page?.Items.Count);
        Assert.NotNull(first.Page?.Next);
        Assert.All(first.Page!.Items, item => Assert.Equal(RuleProgressItemKind.Fact, item.Kind));
        Assert.Equal(Enumerable.Range(0, RuleLimits.MaxPageEntries).Select(index => $"fact-{index:D3}"),
            first.Page.Items.Select(item => item.Key));

        var second = await store.ReadProgressAsync(invocation.Identity, first.Page.Next, RuleLimits.MaxPageEntries);
        Assert.Equal(RuleProgressReadDisposition.Page, second.Disposition);
        Assert.Null(second.Page?.Next);
        Assert.Equal(5, second.Page?.Items.Count);
        Assert.Equal(Enumerable.Range(256, 4).Select(index => $"fact-{index:D3}")
            .Append(0.ToString("D10", CultureInfo.InvariantCulture)), second.Page!.Items.Select(item => item.Key));
        var roundItem = Assert.Single(second.Page.Items, item => item.Kind == RuleProgressItemKind.Round);
        Assert.Equal(2L, roundItem.FirstVisibleModelRevision);
        Assert.Equal(accepted.ReceiptId, roundItem.Round?.ReceiptId);
        Assert.Equal(receipt.SemanticReceiptSha256, roundItem.Round?.SemanticReceiptSha256);
        Assert.Equal(2L, roundItem.Round?.AcceptedModelRevision);
        Assert.Equal(3L, roundItem.Round?.ConsumedModelRevision);
        Assert.Equal("empty-round", roundItem.Round?.AcceptedAttemptId);
        Assert.Equal(token.Lease.Owner, roundItem.Round?.AcceptedWorkerId);
        Assert.Equal(token.Lease.Fence, roundItem.Round?.AcceptedLeaseFence);
        Assert.Equal(0, roundItem.Round?.AddedFactCount);
        Assert.Equal(beforeReads, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Progress_cursor_keeps_its_snapshot_and_hides_later_fact_and_consumption_changes()
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        var accepted = await store.AcceptRoundAsync(token, Submission(receipt, token, "first-attempt"));
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);

        var first = await store.ReadProgressAsync(invocation.Identity, null, 1);
        var seed = Assert.Single(first.Page!.Items);
        Assert.Equal(RuleProgressItemKind.Fact, seed.Kind);
        Assert.Equal("seed", seed.Key);
        Assert.NotNull(first.Page.Next);

        var pending = await store.ReadWorkAsync(token);
        var consumed = await store.ConsumeRoundAsync(pending.CurrentToken!,
            new(accepted.ReceiptId!, receipt.SemanticReceiptSha256));
        Assert.Equal(RuleTransitionDisposition.Consumed, consumed.Disposition);
        var beforeReads = await SnapshotAsync(invocation.Identity);

        var stable = await store.ReadProgressAsync(invocation.Identity, first.Page.Next, RuleLimits.MaxPageEntries);
        Assert.Equal(RuleProgressReadDisposition.Page, stable.Disposition);
        var historicalRound = Assert.Single(stable.Page!.Items);
        Assert.Equal(RuleProgressItemKind.Round, historicalRound.Kind);
        Assert.Equal(accepted.ReceiptId, historicalRound.Round?.ReceiptId);
        Assert.Equal("first-attempt", historicalRound.Round?.AcceptedAttemptId);
        Assert.Equal(2L, historicalRound.Round?.AcceptedModelRevision);
        Assert.Null(historicalRound.Round?.ConsumedModelRevision);
        Assert.Equal(2, historicalRound.Round?.AddedFactCount);
        Assert.Null(stable.Page.Next);

        var fresh = await store.ReadProgressAsync(invocation.Identity, null, RuleLimits.MaxPageEntries);
        Assert.Equal(new[] { RuleProgressItemKind.Fact, RuleProgressItemKind.Round,
            RuleProgressItemKind.Fact, RuleProgressItemKind.Fact }, fresh.Page?.Items.Select(item => item.Kind));
        Assert.Equal(new[] { "seed", "0000000000", "left", "right" }, fresh.Page?.Items.Select(item => item.Key));
        Assert.Equal(3L, fresh.Page?.Items[1].Round?.ConsumedModelRevision);
        Assert.Equal(beforeReads, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Progress_rejects_malformed_wrong_run_future_and_missing_anchor_positions()
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        Assert.Equal(RuleReceiptDisposition.Accepted,
            (await store.AcceptRoundAsync(token, Submission(receipt, token, "accepted"))).Disposition);
        var first = await store.ReadProgressAsync(invocation.Identity, null, 1);
        var position = first.Page!.Next!;
        var beforeReads = await SnapshotAsync(invocation.Identity);

        var malformed = await store.ReadProgressAsync(invocation.Identity, new("not-a-cursor"), 1);
        Assert.Equal(RuleProgressReadDisposition.InvalidPosition, malformed.Disposition);
        Assert.Null(malformed.Page);

        var other = NewRun(4);
        var (otherStore, otherToken, otherInput) = await StartAsync(other);
        Assert.Equal(RuleReceiptDisposition.Accepted,
            (await otherStore.AcceptRoundAsync(otherToken, Submission(evaluator.Evaluate(otherInput), otherToken, "other"))).Disposition);
        var wrongRun = await otherStore.ReadProgressAsync(other.Identity, position, 1);
        Assert.Equal(RuleProgressReadDisposition.InvalidPosition, wrongRun.Disposition);
        Assert.Null(wrongRun.Page);

        Assert.True(RuleProgressCursor.TryDecode(position.Cursor, out var decoded));
        var future = new RuleProgressPosition(RuleProgressCursor.Encode(decoded.RunKey, decoded.Fingerprint,
            decoded.SnapshotRevision + 1, decoded.VisibleRevision, decoded.Kind, decoded.Rank, "seed"));
        var futureResult = await store.ReadProgressAsync(invocation.Identity, future, 1);
        Assert.Equal(RuleProgressReadDisposition.InvalidPosition, futureResult.Disposition);
        Assert.Null(futureResult.Page);

        var missingAnchor = new RuleProgressPosition(RuleProgressCursor.Encode(decoded.RunKey, decoded.Fingerprint,
            decoded.SnapshotRevision, decoded.VisibleRevision, RuleProgressItemKind.Fact, RuleLimits.MaxVocabularyFacts - 1,
            "missing-anchor"));
        var missing = await store.ReadProgressAsync(invocation.Identity, missingAnchor, 1);
        Assert.Equal(RuleProgressReadDisposition.InvalidPosition, missing.Disposition);
        Assert.Null(missing.Page);

        var unknownRun = NewRun(4);
        var notFound = await store.ReadProgressAsync(unknownRun.Identity, new("not-a-cursor"), 1);
        Assert.Equal(RuleProgressReadDisposition.NotFound, notFound.Disposition);
        Assert.Null(notFound.Page);
        Assert.Equal(beforeReads, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Progress_reports_unsupported_selected_receipt_as_unavailable()
    {
        var invocation = NewRun(4);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        Assert.Equal(RuleReceiptDisposition.Accepted,
            (await store.AcceptRoundAsync(token, Submission(receipt, token, "accepted"))).Disposition);
        var first = await store.ReadProgressAsync(invocation.Identity, null, 1);
        Assert.NotNull(first.Page?.Next);
        await using (var db = fixture.Contexts.CreateDbContext())
        {
            var stored = await db.Receipts.SingleAsync();
            stored.SchemaVersion = "future-schema";
            await db.SaveChangesAsync();
        }
        var before = await SnapshotAsync(invocation.Identity);

        var result = await store.ReadProgressAsync(invocation.Identity, first.Page!.Next, 1);

        Assert.Equal(RuleProgressReadDisposition.Unavailable, result.Disposition);
        Assert.Null(result.Page);
        Assert.Equal(before, await SnapshotAsync(invocation.Identity));
    }

    [Fact]
    public async Task Progress_cursor_stays_within_the_identifier_bound_for_a_maximum_length_fact_key()
    {
        var source = RuleStoreTestFixture.NewInvocation();
        var facts = new[] { new RuleFact(new string('x', RuleLimits.MaxIdentifierUtf8Bytes)), new RuleFact("z") };
        var artifact = RulesCodec.CreateArtifact(new RuleProgram(RuleLanguage.FormatVersion, RuleLanguage.Id,
            RuleLanguage.Version, facts, []));
        var invocation = new RuleInvocationEnvelope(source.Identity, artifact, facts, 1,
            source.ExpectedImplementation, source.InvocationSchemaVersion, null);
        var (store, token, input) = await StartAsync(invocation);
        var receipt = evaluator.Evaluate(input);
        var accepted = await store.AcceptRoundAsync(token, Submission(receipt, token, "empty-round"));
        var pending = await store.ReadWorkAsync(token);
        Assert.Equal(RuleTransitionDisposition.Consumed, (await store.ConsumeRoundAsync(pending.CurrentToken!,
            new(accepted.ReceiptId!, receipt.SemanticReceiptSha256))).Disposition);

        var first = await store.ReadProgressAsync(invocation.Identity, null, 1);

        Assert.Equal(new string('x', RuleLimits.MaxIdentifierUtf8Bytes), Assert.Single(first.Page!.Items).Key);
        Assert.NotNull(first.Page.Next);
        Assert.True(Encoding.UTF8.GetByteCount(first.Page.Next.Cursor) <= RuleLimits.MaxIdentifierUtf8Bytes);
        Assert.DoesNotContain(new string('x', RuleLimits.MaxIdentifierUtf8Bytes), first.Page.Next.Cursor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(257)]
    public async Task Progress_requires_a_page_size_within_the_contract_bound(int take)
    {
        var invocation = NewRun(4);
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await store.ReadProgressAsync(invocation.Identity, null, take);
        });
    }

    private static RuleInvocationEnvelope NewFactOnlyRun(int count)
    {
        var source = RuleStoreTestFixture.NewInvocation();
        var facts = Enumerable.Range(0, count).Select(index => new RuleFact($"fact-{index:D3}")).ToArray();
        var program = new RuleProgram(RuleLanguage.FormatVersion, RuleLanguage.Id, RuleLanguage.Version, facts, []);
        var artifact = RulesCodec.CreateArtifact(program);
        return new(source.Identity, artifact, facts, 1, source.ExpectedImplementation,
            source.InvocationSchemaVersion, null);
    }
}
