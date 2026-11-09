using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Source-carrier identity controls; provider claim/CAS authorization is a separate gate.</summary>
public sealed class PostCommitDeliveryContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Context_preserves_complete_commit_specific_claim_and_scope_without_reinterpreting_colons()
    {
        var claim = Claim(Intent(intentId: "send:1"));
        var scope = new PersistenceScope("tenant");
        var context = new PostCommitDeliveryContext(claim, scope);
        Assert.Same(claim, context.Claim);
        Assert.Same(scope, context.Scope);
        Assert.Equal("source:commit", context.SourceCommitId);
        Assert.Equal("source:commit:send:1", context.SourceOutboxItemId);
        Assert.Same(claim.Item.Intent, context.Intent);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeIntent(context.Intent), context.IntentFingerprint);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("fence")]
    [InlineData("started")]
    [InlineData("visible")]
    [InlineData("source")]
    [InlineData("empty-commit")]
    [InlineData("blank-commit")]
    public void Incoherent_or_unaddressable_claim_cannot_become_a_source_context(string defect)
    {
        var claim = Claim(Intent(), defect switch
        {
            "source" => "unrelated-source",
            "empty-commit" => ":send",
            "blank-commit" => " :send",
            _ => null
        });
        claim = new RuntimePostCommitOutboxClaim(claim.Item, defect == "owner" ? "other-owner" : claim.OwnerId,
            claim.FencingToken + (defect == "fence" ? 1 : 0),
            claim.ClaimedAt.AddTicks(defect == "started" ? 1 : 0),
            claim.VisibleAfter.AddTicks(defect == "visible" ? 1 : 0));
        Assert.ThrowsAny<ArgumentException>(() => new PostCommitDeliveryContext(claim, new PersistenceScope("tenant")));
    }

    [Fact]
    public void Intent_identity_preserves_opaque_utf16_and_nullable_field_boundaries()
    {
        Assert.NotEqual(Fingerprint(Intent(intentId: "\ud800")), Fingerprint(Intent(intentId: "\ud801")));
        Assert.NotEqual(Fingerprint(Intent(activityId: null)), Fingerprint(Intent(activityId: "")));
        Assert.NotEqual(Fingerprint(Intent(intentId: "a", workflowId: "bc")), Fingerprint(Intent(intentId: "ab", workflowId: "c")));
        Assert.NotEqual(Fingerprint(Intent(payload: null)), Fingerprint(Intent(payload: "null")));
    }

    [Fact]
    public void Durable_json_round_trip_metadata_order_and_equivalent_timestamp_offsets_keep_source_identity()
    {
        var first = Intent(payload: "{\"z\":1,\"a\":\"é\"}", metadata: new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" });
        var second = Intent(payload: "{ \"a\":\"\\u00e9\", \"z\":1 }", recordedAt: Now.ToOffset(TimeSpan.FromHours(2)),
            metadata: new Dictionary<string, string> { ["a"] = "first", ["z"] = "last" });
        Assert.True(first.IsEquivalentTo(second));
        Assert.Equal(Fingerprint(first), Fingerprint(second));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var restored = JsonSerializer.Deserialize<RuntimePostCommitIntent>(JsonSerializer.Serialize(first, options), options)!;
        Assert.Equal(Fingerprint(first), Fingerprint(restored));
    }

    [Theory]
    [InlineData("1", "1.0")]
    [InlineData("1", "10e-1")]
    [InlineData("1e20", "100000000000000000000")]
    [InlineData("1.2300", "123e-2")]
    [InlineData("-0", "0.000e+20")]
    [InlineData("-12.300", "-123e-1")]
    [InlineData("1.00000000000000000001", "100000000000000000001e-20")]
    public void Equivalent_numeric_json_keeps_the_same_intent_identity(string firstNumber, string secondNumber)
    {
        var first = Intent(payload: "{\"n\":" + firstNumber + "}");
        var second = Intent(payload: "{\"n\":" + secondNumber + "}");
        Assert.True(first.IsEquivalentTo(second));
        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }

    [Fact]
    public void Numeric_identity_preserves_precision_and_distinguishes_numbers_from_strings()
    {
        Assert.NotEqual(Fingerprint(Intent(payload: "{\"n\":1.00000000000000000001}")),
            Fingerprint(Intent(payload: "{\"n\":1.00000000000000000002}")));
        Assert.NotEqual(Fingerprint(Intent(payload: "1")), Fingerprint(Intent(payload: "\"1e0\"")));
    }

    [Fact]
    public void Delivery_boundary_preserves_source_and_cause_without_exposing_provider_data_in_its_message()
    {
        var cause = new IOException("private-provider-diagnostics");
        var failure = new StimulusDeliveryBoundaryException("private-source", cause, isUnavailable: true);
        Assert.Same(cause, failure.InnerException);
        Assert.Equal("private-source", failure.SourceOutboxItemId);
        Assert.True(failure.IsUnavailable);
        Assert.DoesNotContain("private-provider-diagnostics", failure.Message);
        Assert.DoesNotContain("private-source", failure.Message);
    }

    [Fact]
    public void New_numeric_normalization_does_not_rewrite_historical_checkpoint_replay_identity()
    {
        var first = Intent(payload: "{\"n\":1}");
        var second = Intent(payload: "{\"n\":1.0}");
        var metadata = new Dictionary<string, string>();
        var changes = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []);
        var checkpoint = new RuntimeCheckpoint("checkpoint", "test", "publisher", Now, [], metadata);
        var bundle = new RuntimeCheckpointCommit("commit", checkpoint, changes, [first], metadata);
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(bundle),
            RuntimeCheckpointCommitFingerprint.Compute(bundle with { PostCommitIntents = [second] }));
        Assert.Equal(Fingerprint(first), Fingerprint(second));
    }

    [Theory]
    [InlineData("{\"x\":1,\"x\":2}", "{\"x\":2,\"x\":1}")]
    [InlineData("{\"a\":[1,2]}", "{\"a\":[2,1]}")]
    [InlineData("{\"n\":1e1,\"x\":\"x\"}", "{\"x\":\"x\",\"n\":10}")]
    public void Structured_json_identity_agrees_with_existing_intent_equivalence(string firstJson, string secondJson)
    {
        var first = Intent(payload: firstJson);
        var second = Intent(payload: secondJson);
        Assert.Equal(first.IsEquivalentTo(second), Fingerprint(first) == Fingerprint(second));
    }

    [Theory]
    [InlineData("intent")]
    [InlineData("workflow")]
    [InlineData("kind")]
    [InlineData("time")]
    [InlineData("activity")]
    [InlineData("key")]
    [InlineData("payload")]
    [InlineData("metadata")]
    [InlineData("wait")]
    [InlineData("delay")]
    public void Each_persisted_intent_facet_changes_source_identity(string changed)
    {
        var other = Intent(intentId: changed == "intent" ? "changed" : "send", workflowId: changed == "workflow" ? "changed" : "publisher",
            kind: changed == "kind" ? "changed" : "publish", recordedAt: changed == "time" ? Now.AddTicks(1) : Now,
            activityId: changed == "activity" ? "changed" : "activity", key: changed == "key" ? "changed" : "key",
            payload: changed == "payload" ? "{\"n\":2}" : "{\"n\":1}",
            metadata: new Dictionary<string, string> { ["routing"] = changed == "metadata" ? "changed" : "saved" },
            wait: changed == "wait", notBefore: changed == "delay" ? Now.AddMinutes(1) : null);
        Assert.NotEqual(Fingerprint(Intent()), Fingerprint(other));
    }

    private static string Fingerprint(RuntimePostCommitIntent intent) => RuntimeCheckpointCommitFingerprint.ComputeIntent(intent);

    private static RuntimePostCommitIntent Intent(string intentId = "send", string workflowId = "publisher", string kind = "publish",
        DateTimeOffset? recordedAt = null, string? activityId = "activity", string? key = "key", string? payload = "{\"n\":1}",
        IReadOnlyDictionary<string, string>? metadata = null, bool wait = false, DateTimeOffset? notBefore = null)
    {
        using var document = payload is null ? null : JsonDocument.Parse(payload);
        return new RuntimePostCommitIntent(intentId, workflowId, kind, recordedAt ?? Now, activityId, key,
            document?.RootElement.Clone(), metadata ?? new Dictionary<string, string> { ["routing"] = "saved" },
            dependsOnWaitRegistrationId: wait ? "wait" : null,
            waitFailurePolicy: wait ? RuntimeWaitDependentIntentFailurePolicy.KeepWaitingManualIntervention : null,
            notBefore: notBefore);
    }

    private static RuntimePostCommitOutboxClaim Claim(RuntimePostCommitIntent intent, string? sourceId = null)
    {
        var item = new RuntimePostCommitOutboxItem(sourceId ?? RuntimePostCommitOutboxIdentity.CreateLogicalValue("source:commit", intent.IntentId),
            intent, RuntimePostCommitOutboxStatus.Pending, Now, null);
        return RuntimePostCommitOutboxClaimTransitions.Claim(item,
            new RuntimePostCommitOutboxClaimRequest("owner", Now, TimeSpan.FromMinutes(1), 1));
    }
}
