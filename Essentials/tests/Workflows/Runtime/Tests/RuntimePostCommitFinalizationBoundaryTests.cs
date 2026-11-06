using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePostCommitFinalizationBoundaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_completion_preserves_its_JSON_shape_and_round_trip(bool requireConstructorParameters)
    {
        var claim = Claim();
        var completion = new RuntimePostCommitOutboxClaimCompletion(claim, Delivered(claim));
        var json = JsonSerializer.Serialize(completion);
        Assert.Equal(JsonSerializer.Serialize(new
        {
            completion.Claim, completion.DeliveryResult, completion.WorkflowDispatch, completion.FollowUpOutboxItem
        }), json);
        // The nested outbox has its own existing optional-member reader policy. Isolate the completion envelope's
        // constructor policy here; changing older outbox wire requirements is outside this additive participant.
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(RuntimePostCommitOutboxItem))
                foreach (var property in info.Properties) property.IsRequired = false;
        });
        var restored = JsonSerializer.Deserialize<RuntimePostCommitOutboxClaimCompletion>(json,
            new JsonSerializerOptions { RespectRequiredConstructorParameters = requireConstructorParameters, TypeInfoResolver = resolver })!;
        Assert.Null(restored.SuccessfulFinalization);
        Assert.True(claim.Item.IsEquivalentTo(restored.Claim.Item));
    }


    [Theory]
    [InlineData(RuntimePostCommitOutboxStatus.FailedRetryable)]
    [InlineData(RuntimePostCommitOutboxStatus.FailedFinal)]
    public void A_successful_participant_cannot_be_attached_to_a_failure_result(RuntimePostCommitOutboxStatus status)
    {
        var claim = Claim();
        var result = new RuntimePostCommitOutboxDeliveryResult(claim.OutboxItemId, status, Now.AddSeconds(1), "test failure");
        var error = Assert.Throws<ArgumentException>(() => RuntimePostCommitOutboxClaimCompletion.PrepareSourceFinalization(claim,
            result, Finalization()));
        Assert.Equal(nameof(RuntimePostCommitOutboxClaimCompletion.SuccessfulFinalization), error.ParamName);
    }

    [Fact]
    public void A_legacy_completion_provider_defaults_to_unsupported()
    {
        IRuntimePostCommitOutboxClaimCompletionStore provider = new LegacyCompletionStore();
        Assert.False(provider.CanFinalizeSource(null!, Finalization()));
    }

    [Fact]
    public void Legacy_transition_cannot_discard_a_prepared_source_participant()
    {
        var claim = Claim();
        Assert.Throws<RuntimePostCommitSourceBoundaryException>(() =>
            RuntimePostCommitOutboxClaimTransitions.ResolveCompletion(Prepared(claim), claim.Item, null, null));
    }

    [Fact]
    public async Task In_memory_completion_keeps_the_claim_owed_when_source_finalization_is_unavailable()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        await store.AddPendingForTestingAsync(Pending());
        var claim = Assert.Single(await store.ClaimAsync(new("worker", Now, TimeSpan.FromMinutes(1), 1)));

        await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => store.CompleteClaimAsync(Prepared(claim)).AsTask());

        Assert.True(claim.Item.IsEquivalentTo(await store.FindAsync(claim.OutboxItemId)));
        Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.Persisted,
            await store.CompleteClaimAsync(new(claim, Delivered(claim))));
    }

    [Fact]
    public void Coalesced_completion_keeps_the_overlay_claim_owed_when_source_finalization_is_unavailable()
    {
        var session = new RuntimeCoalescingSession("sender", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions());
        session.BufferDeferred(new("commit", new("checkpoint", "test", "sender", Now, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], [], postCommitOutbox:
                [new("source", RuntimeStateChangeOperation.Upsert, Pending(), new Dictionary<string, string>())]),
            [], new Dictionary<string, string>()));
        var claim = Assert.Single(session.ClaimOutbox(new("worker", Now, TimeSpan.FromMinutes(1), 1)));

        Assert.Throws<RuntimePostCommitSourceBoundaryException>(() => session.CompleteOutboxClaim(Prepared(claim), null));

        Assert.True(session.TryFindOutboxItem(claim.OutboxItemId, out var retained));
        Assert.True(claim.Item.IsEquivalentTo(retained));
        Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.Persisted,
            session.CompleteOutboxClaim(new(claim, Delivered(claim)), null));
    }

    private static RuntimePostCommitOutboxItem Pending() => new("source",
        new("send", "sender", "test.send", Now, null, null, null), RuntimePostCommitOutboxStatus.Pending, Now, Now);
    private static RuntimePostCommitOutboxClaim Claim() => RuntimePostCommitOutboxClaimTransitions.Claim(Pending(),
        new("worker", Now, TimeSpan.FromMinutes(1), 1));
    private static RuntimePostCommitOutboxDeliveryResult Delivered(RuntimePostCommitOutboxClaim claim) =>
        new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(1));
    private static RuntimePostCommitSourceFinalization Finalization() => new(1, "operation", "fingerprint", new Plan());
    private static RuntimePostCommitOutboxClaimCompletion Prepared(RuntimePostCommitOutboxClaim claim) =>
        RuntimePostCommitOutboxClaimCompletion.PrepareSourceFinalization(claim, Delivered(claim), Finalization());
    private sealed class Plan : IRuntimeSourcePlan { public string Family => "test"; public int Version => 1; }
    private sealed class LegacyCompletionStore : IRuntimePostCommitOutboxClaimCompletionStore
    {
        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(RuntimePostCommitOutboxClaimCompletion completion,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
