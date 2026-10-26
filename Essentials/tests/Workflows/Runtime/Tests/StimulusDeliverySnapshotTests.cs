using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Immutable/wire source contracts only; no matching, provider CAS or recovery proof is implied.</summary>
public sealed class StimulusDeliverySnapshotTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1.0.0", "hash");

    [Fact]
    public void Whole_cohort_round_trips_every_binding_even_with_shared_artifact_and_every_exact_wait()
    {
        var metadata = new Dictionary<string, string> { ["start"] = "one" };
        var starts = new List<StimulusStartRecipient> { Start("binding-a", metadata), Start("binding-b") };
        var resumes = new List<StimulusResumeRecipient> { Wait("wait-a"), Wait("wait-b") };
        var saved = Snapshot(starts, resumes);
        starts.Clear(); resumes.Clear(); metadata.Clear();
        var restored = RoundTrip(saved);
        Assert.Equal(["binding-a", "binding-b"], restored.Starts.Select(item => item.Binding.TriggerBindingId));
        Assert.All(restored.Starts, item => Assert.Equal("artifact", item.Executable.ArtifactId));
        Assert.Equal("one", restored.Starts[0].Binding.Metadata["start"]);
        Assert.Equal("activation", restored.Starts[0].Binding.ActivationId);
        Assert.Equal("slot", restored.Starts[0].Binding.SlotId);
        Assert.Equal(["wait-a", "wait-b"], restored.Resumes.Select(item => item.BookmarkId));
        Assert.True(restored.MatchesSource(Context()));
    }

    [Fact]
    public void Empty_is_an_explicit_authoritative_snapshot_and_large_sets_are_never_truncated()
    {
        var empty = RoundTrip(Snapshot([], []));
        Assert.Empty(empty.Starts);
        Assert.Empty(empty.Resumes);
        var wire = JsonSerializer.SerializeToNode(empty, Web)!.AsObject();
        Assert.Equal(0, wire["starts"]!.AsArray().Count);
        Assert.Equal(0, wire["resumes"]!.AsArray().Count);
        Assert.Equal(300, RoundTrip(Snapshot([], Enumerable.Range(0, 300).Select(index => Wait("wait-" + index)).ToArray())).Resumes.Count);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("sourceOutboxItemId")]
    [InlineData("intentFingerprint")]
    [InlineData("scope")]
    [InlineData("selectedAt")]
    [InlineData("stimulusType")]
    [InlineData("stimulusHash")]
    [InlineData("starts")]
    [InlineData("resumes")]
    public void Truncated_or_null_present_snapshot_cannot_become_an_empty_or_fresh_set(string field)
    {
        var wire = JsonSerializer.SerializeToNode(Snapshot([Start()], [Wait()]), Web)!.AsObject();
        Assert.True(wire.Remove(field));
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<StimulusDeliverySnapshot>(Web));
        wire[field] = null;
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<StimulusDeliverySnapshot>(Web));
    }

    [Theory]
    [InlineData("persistenceScope")]
    [InlineData("tenantId")]
    [InlineData("partition")]
    [InlineData("runKind")]
    [InlineData("testScope")]
    public void Scope_fields_must_be_declared_even_when_nullable_or_legacy(string field)
    {
        var wire = JsonSerializer.SerializeToNode(Snapshot([], []), Web)!.AsObject();
        Assert.True(wire["scope"]!.AsObject().Remove(field));
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<StimulusDeliverySnapshot>(Web));
    }

    [Theory]
    [InlineData("activationId")]
    [InlineData("slotId")]
    [InlineData("metadata")]
    [InlineData("correlationScope")]
    [InlineData("cardinality")]
    [InlineData("isActive")]
    public void Saved_binding_cannot_lose_provenance_through_legacy_record_defaults(string field)
    {
        var wire = JsonSerializer.SerializeToNode(Snapshot([Start()], []), Web)!.AsObject();
        Assert.True(wire["starts"]![0]!["binding"]!.AsObject().Remove(field));
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<StimulusDeliverySnapshot>(Web));
    }

    [Theory]
    [InlineData("duplicate-start")]
    [InlineData("duplicate-wait")]
    [InlineData("wrong-order")]
    [InlineData("mixed-start")]
    [InlineData("mixed-wait")]
    [InlineData("null-start")]
    [InlineData("null-wait")]
    public void Incomplete_or_conflicting_cohorts_refuse_before_storage(string defect)
    {
        var starts = defect switch
        {
            "duplicate-start" => new[] { Start(), Start() },
            "wrong-order" => [Start("binding-b"), Start("binding-a")],
            "mixed-start" => [new StimulusStartRecipient(Binding() with { StimulusHash = "other" }, Pin)],
            "null-start" => [null!],
            _ => [Start()]
        };
        var waits = defect switch
        {
            "duplicate-wait" => new[] { Wait(), Wait() },
            "mixed-wait" => [StimulusResumeRecipient.From(Bookmark() with { StimulusHash = "other" })],
            "null-wait" => [null!],
            _ => [Wait()]
        };
        Assert.ThrowsAny<ArgumentException>(() => Snapshot(starts, waits));
    }

    [Fact]
    public void Source_scope_digest_and_unknown_version_cannot_be_silently_accepted()
    {
        var source = Context();
        Assert.False(new StimulusDeliverySnapshot(1, "other", source.IntentFingerprint, Scope(), Now, "event", "named", [], []).MatchesSource(source));
        Assert.False(new StimulusDeliverySnapshot(1, source.SourceOutboxItemId, new string('0', 64), Scope(), Now, "event", "named", [], []).MatchesSource(source));
        var otherScope = new StimulusDeliveryScope(new PersistenceScope("other"), null, null, WorkflowRunKind.Unknown, null);
        Assert.False(new StimulusDeliverySnapshot(1, source.SourceOutboxItemId, source.IntentFingerprint, otherScope, Now, "event", "named", [], []).MatchesSource(source));
        var wire = JsonSerializer.SerializeToNode(Snapshot([], []), Web)!.AsObject();
        wire["version"] = 2;
        Assert.Throws<JsonException>(() => wire.Deserialize<StimulusDeliverySnapshot>(Web));
    }

    [Theory]
    [InlineData("artifact")]
    [InlineData("definition")]
    [InlineData("version")]
    [InlineData("hash")]
    [InlineData("missing-version-id")]
    [InlineData("inactive")]
    [InlineData("metadata")]
    [InlineData("cardinality")]
    public void A_saved_start_requires_complete_matching_pin_and_active_provenance(string defect)
    {
        var pin = defect switch
        {
            "artifact" => Pin with { ArtifactId = "other" },
            "definition" => Pin with { DefinitionId = "other" },
            "version" => Pin with { ArtifactVersion = "other" },
            "hash" => Pin with { ArtifactHash = "other" },
            "missing-version-id" => Pin with { DefinitionVersionId = "" },
            _ => Pin
        };
        var binding = defect switch
        {
            "inactive" => Binding() with { IsActive = false },
            "metadata" => Binding() with { Metadata = null! },
            "cardinality" => Binding() with { Cardinality = (TriggerCardinality)99 },
            _ => Binding()
        };
        Assert.ThrowsAny<ArgumentException>(() => new StimulusStartRecipient(binding, pin));
    }

    [Fact]
    public void Test_run_scope_remains_distinct_after_round_trip_and_cannot_match_another_scope()
    {
        var test = new WorkflowTestScope("test", Now.AddHours(1), "tenant", new("partition"));
        var source = Workflow() with { RunKind = WorkflowRunKind.TestRun, TestScope = test };
        var saved = StimulusDeliveryScope.From(source, new("tenant"));
        var restored = JsonSerializer.Deserialize<StimulusDeliveryScope>(JsonSerializer.Serialize(saved, Web), Web)!;
        Assert.Equal(test, restored.TestScope);
        Assert.True(restored.Matches(source, new("tenant")));
        Assert.False(restored.Matches(source with { TestScope = new("other", test.ExpiresAt, test.TenantId, test.Partition) }, new("tenant")));
        Assert.False(restored.Matches(source, new("other-provider-scope")));
    }

    [Fact]
    public void Exact_wait_identity_rejects_recreated_registration_but_does_not_copy_payload()
    {
        var bookmark = Bookmark();
        var recipient = StimulusResumeRecipient.From(bookmark);
        Assert.True(recipient.Matches(bookmark));
        Assert.False(recipient.Matches(bookmark with { CreatedAt = Now.AddTicks(1) }));
        Assert.False(recipient.Matches(bookmark with { ActivityExecutionId = "other" }));
        Assert.False(recipient.Matches(bookmark with { ResumeTargetId = "other" }));
        Assert.False(recipient.Matches(bookmark with { ExpiresAt = Now.AddMinutes(1) }));
        var wire = JsonSerializer.SerializeToNode(recipient, Web)!.AsObject();
        Assert.False(wire.ContainsKey("payload"));
        Assert.True(wire.Remove("expiresAt"));
        Assert.Throws<JsonException>(() => wire.Deserialize<StimulusResumeRecipient>(Web));
    }

    [Fact]
    public void Legacy_scope_is_preserved_and_test_scope_facts_match_the_authoritative_publisher()
    {
        var legacy = Workflow() with { Partition = null, RunKind = WorkflowRunKind.Unknown, TenantId = null };
        var savedLegacy = StimulusDeliveryScope.From(legacy, new PersistenceScope("tenant"));
        var restored = JsonSerializer.Deserialize<StimulusDeliveryScope>(JsonSerializer.Serialize(savedLegacy, Web), Web)!;
        Assert.Null(restored.Partition);
        Assert.Equal(WorkflowRunKind.Unknown, restored.RunKind);
        Assert.True(restored.Matches(legacy, new PersistenceScope("tenant")));
        var scope = Scope();
        Assert.True(scope.Matches(Workflow(), new PersistenceScope("tenant")));
        Assert.False(scope.Matches(Workflow() with { RunKind = WorkflowRunKind.TestRun }, new PersistenceScope("tenant")));
        Assert.False(scope.Matches(Workflow() with { TenantId = "other" }, new PersistenceScope("tenant")));
        Assert.False(scope.Matches(Workflow() with { Partition = new("other") }, new PersistenceScope("tenant")));
    }

    private static StimulusDeliverySnapshot RoundTrip(StimulusDeliverySnapshot snapshot) =>
        JsonSerializer.Deserialize<StimulusDeliverySnapshot>(JsonSerializer.Serialize(snapshot, Web), Web)!;
    private static StimulusDeliverySnapshot Snapshot(IReadOnlyList<StimulusStartRecipient> starts, IReadOnlyList<StimulusResumeRecipient> resumes) =>
        new(1, Context().SourceOutboxItemId, Context().IntentFingerprint, Scope(), Now, "event", "named", starts, resumes);
    private static StimulusDeliveryScope Scope() => StimulusDeliveryScope.From(Workflow(), new PersistenceScope("tenant"));
    private static WorkflowExecutionState Workflow() => new("publisher", Pin, WorkflowExecutionStatus.Running, null,
        Now, Now, null, null, null, null, "tenant", new Dictionary<string, string>())
        { Partition = new("partition"), RunKind = WorkflowRunKind.PublishedRun };
    private static StimulusStartRecipient Start(string id = "binding-a", IReadOnlyDictionary<string, string>? metadata = null) => new(Binding(id, metadata), Pin);
    private static WorkflowTriggerBinding Binding(string id = "binding-a", IReadOnlyDictionary<string, string>? metadata = null) =>
        new(id, "artifact", "definition", "1.0.0", "hash", "start-node", "event", "named", null,
            metadata ?? new Dictionary<string, string>(), Now, "activation", "slot");
    private static StimulusResumeRecipient Wait(string id = "wait") => StimulusResumeRecipient.From(Bookmark(id));
    private static BookmarkState Bookmark(string id = "wait") => new(id, "waiter", "activity", "node", "resume", "event", "named",
        JsonSerializer.SerializeToElement(new { secret = "not copied" }), new Dictionary<string, string>(), Now, null);
    private static PostCommitDeliveryContext Context()
    {
        var intent = new RuntimePostCommitIntent("send", "publisher", "publish", Now, "activity", "key", null);
        var item = new RuntimePostCommitOutboxItem(RuntimePostCommitOutboxIdentity.CreateLogicalValue("commit", intent.IntentId),
            intent, RuntimePostCommitOutboxStatus.Pending, Now, null);
        return new(RuntimePostCommitOutboxClaimTransitions.Claim(item, new RuntimePostCommitOutboxClaimRequest("owner", Now, TimeSpan.FromMinutes(1), 1)), new("tenant"));
    }
}
