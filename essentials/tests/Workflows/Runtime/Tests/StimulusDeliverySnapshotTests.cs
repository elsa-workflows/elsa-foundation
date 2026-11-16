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
    public void Source_capture_preserves_publication_attribution_separately_from_deduplicated_content_identity()
    {
        var source = Provenance();
        var captured = Snapshot([new StimulusStartRecipient(Binding(), Pin, source)], []);
        var restored = RoundTrip(captured);
        Assert.True(restored.Starts[0].SourceProvenanceCaptured);
        Assert.Equal(source, restored.Starts[0].SourceProvenance);
        Assert.Equal("published-version", restored.Starts[0].SourceProvenance!.DefinitionVersionId);
        Assert.Equal("version", restored.Starts[0].Executable.DefinitionVersionId);
        Assert.Equal("published-label", restored.Starts[0].SourceProvenance!.ArtifactVersion);
        Assert.Equal("1.0.0", restored.Starts[0].Executable.ArtifactVersion);
        Assert.True(captured.IsEquivalentTo(restored));
        Assert.False(captured.IsEquivalentTo(Snapshot([new StimulusStartRecipient(Binding(), Pin,
            source with { SourceReferenceId = "another-reference" })], [])));
        Assert.False(captured.IsEquivalentTo(Snapshot([Start()], [])));
    }

    [Fact]
    public void Captured_null_and_uncaptured_legacy_source_are_distinct_wire_and_equality_states()
    {
        var binding = Binding() with { ActivationId = null, SlotId = null };
        var absent = Snapshot([new StimulusStartRecipient(binding, Pin)], []);
        var capturedNull = Snapshot([new StimulusStartRecipient(binding, Pin, null)], []);
        Assert.False(RoundTrip(absent).Starts[0].SourceProvenanceCaptured);
        Assert.True(RoundTrip(capturedNull).Starts[0].SourceProvenanceCaptured);
        Assert.Null(RoundTrip(capturedNull).Starts[0].SourceProvenance);
        Assert.False(absent.IsEquivalentTo(capturedNull));
        var oldWire = JsonSerializer.SerializeToNode(absent, Web)!;
        Assert.False(oldWire["starts"]![0]!.AsObject().ContainsKey("sourceCapture"));
        var capturedWire = JsonSerializer.SerializeToNode(capturedNull, Web)!;
        Assert.Equal(1, capturedWire["starts"]![0]!["sourceCapture"]!["version"]!.GetValue<int>());
        Assert.True(capturedWire["starts"]![0]!["sourceCapture"]!.AsObject().ContainsKey("provenance"));
        Assert.Null(capturedWire["starts"]![0]!["sourceCapture"]!["provenance"]);
    }

    [Theory]
    [InlineData("sourceReferenceId")]
    [InlineData("sourceKind")]
    [InlineData("sourceId")]
    [InlineData("sourceVersion")]
    [InlineData("definitionId")]
    [InlineData("definitionVersionId")]
    [InlineData("artifactVersion")]
    [InlineData("publicationId")]
    [InlineData("slotId")]
    public void Captured_provenance_requires_every_field_including_nullable_attribution(string field)
    {
        var wire = JsonSerializer.SerializeToNode(Snapshot([new StimulusStartRecipient(Binding(), Pin, Provenance())], []), Web)!;
        Assert.True(wire["starts"]![0]!["sourceCapture"]!["provenance"]!.AsObject().Remove(field));
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<StimulusDeliverySnapshot>(Web));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("provenance")]
    public void Present_source_capture_cannot_be_truncated_into_reference_less_authority(string field)
    {
        var wire = JsonSerializer.SerializeToNode(Snapshot([new StimulusStartRecipient(Binding(), Pin, Provenance())], []), Web)!;
        Assert.True(wire["starts"]![0]!["sourceCapture"]!.AsObject().Remove(field));
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<StimulusDeliverySnapshot>(Web));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Unsupported_source_capture_versions_refuse_instead_of_becoming_uncaptured(int version)
    {
        var wire = JsonSerializer.SerializeToNode(Snapshot([new StimulusStartRecipient(Binding(), Pin, Provenance())], []), Web)!;
        wire["starts"]![0]!["sourceCapture"]!["version"] = version;
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<StimulusDeliverySnapshot>(Web));
    }

    [Theory]
    [InlineData("activation")]
    [InlineData("slot")]
    [InlineData("blank-reference")]
    [InlineData("blank-optional")]
    [InlineData("missing-selected-reference")]
    public void Source_capture_refuses_conflicting_or_malformed_selected_provenance(string defect)
    {
        var source = defect switch
        {
            "activation" => Provenance() with { PublicationId = "another-activation" },
            "slot" => Provenance() with { SlotId = "another-slot" },
            "blank-reference" => Provenance() with { SourceReferenceId = " " },
            "blank-optional" => Provenance() with { SourceVersion = " " },
            _ => null
        };
        Assert.ThrowsAny<ArgumentException>(() => new StimulusStartRecipient(Binding(), Pin, source));
    }

    private static WorkflowExecutableSourceProvenance Provenance() => new("source-reference", "definition", "published-definition",
        "2", "published-definition", "published-version", "published-label", "activation", "slot");

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
    public void Retention_witness_round_trips_without_changing_old_unmarked_wire_or_equivalence()
    {
        var old = Snapshot([Start()], [Wait()]);
        var accepted = new StimulusDeliverySnapshot(old.Version, old.SourceOutboxItemId, old.IntentFingerprint, old.Scope,
            old.SelectedAt, old.StimulusType, old.StimulusHash, old.Starts, old.Resumes, StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion);
        Assert.False(JsonSerializer.SerializeToNode(old, Web)!.AsObject().ContainsKey("retentionAcceptance"));
        Assert.Null(RoundTrip(old).RetentionAcceptanceVersion);
        Assert.Equal(1, RoundTrip(accepted).RetentionAcceptanceVersion);
        Assert.True(accepted.IsEquivalentTo(RoundTrip(accepted)));
        Assert.False(old.IsEquivalentTo(accepted));
        Assert.False(accepted.IsEquivalentTo(old));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":null}")]
    [InlineData("{\"version\":0}")]
    [InlineData("{\"version\":2}")]
    public void Present_retention_witness_requires_its_supported_version(string malformed)
    {
        var wire = JsonSerializer.SerializeToNode(Snapshot([], []), Web)!;
        wire["retentionAcceptance"] = JsonNode.Parse(malformed);
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<StimulusDeliverySnapshot>(Web));
    }

    [Fact]
    public void Execution_dependencies_preserve_original_per_artifact_keys_and_all_exact_recipients()
    {
        var source = Context();
        var saved = Snapshot([Start("binding-a"), Start("binding-b")], [Wait("wait-a"), Wait("wait-b")]);
        var dependencies = saved.GetExecutionDependencies(source.Intent);
        var originalStart = KeyedWorkflowStartIdentity.For("key", Pin.ArtifactId).WorkflowExecutionId;
        Assert.Equal(new[] { "publisher", "waiter", originalStart }.Order(StringComparer.Ordinal), dependencies);
        Assert.DoesNotContain(KeyedWorkflowStartIdentity.For("key:binding-a", Pin.ArtifactId).WorkflowExecutionId, dependencies);
        Assert.ThrowsAny<NotSupportedException>(() => ((IList<string>)dependencies).Add("injected"));
        Assert.Throws<ArgumentException>(() => saved.GetExecutionDependencies(new("send", "publisher", "publish", Now, "activity", "other", null)));
    }

    [Fact]
    public void Start_dependencies_require_original_key_but_empty_cohort_still_keeps_publisher()
    {
        var intent = new RuntimePostCommitIntent("send", "publisher", "publish", Now, "activity", null, null);
        StimulusDeliverySnapshot Saved(IReadOnlyList<StimulusStartRecipient> starts) => new(1, Context().SourceOutboxItemId,
            RuntimeCheckpointCommitFingerprint.ComputeIntent(intent), Scope(), Now, "event", "named", starts, []);
        Assert.Throws<InvalidDataException>(() => Saved([Start()]).GetExecutionDependencies(intent));
        Assert.Equal(["publisher"], Saved([]).GetExecutionDependencies(intent));
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

    [Fact]
    public void Exact_resume_capture_round_trips_immutable_generation_pin_scope_source_and_registration_facts()
    {
        var metadata = new Dictionary<string, string> { ["activation"] = "selected", ["slot"] = "pinned" };
        var original = Bookmark() with { Metadata = metadata };
        var captured = StimulusResumeRecipient.From(new BookmarkStateObservation(original, 7),
            Workflow() with { WorkflowExecutionId = "waiter", PinnedSource = Provenance() }, new("tenant"));
        metadata["activation"] = "changed";
        var snapshot = Snapshot([], [captured]); var wire = JsonSerializer.SerializeToNode(snapshot, Web)!;
        Assert.DoesNotContain("not copied", wire.ToJsonString());
        var restored = RoundTrip(snapshot).Resumes.Single();
        Assert.True(snapshot.IsEquivalentTo(RoundTrip(snapshot))); Assert.Equal(7, restored.RegistrationCapture!.Generation);
        Assert.Equal(Pin, restored.RegistrationCapture.Executable); Assert.Equal(Provenance(), restored.RegistrationCapture.PinnedSource);
        Assert.Equal("selected", restored.RegistrationCapture.Metadata["activation"]);
        Assert.True(restored.Matches(original with { Metadata = new Dictionary<string, string> { ["activation"] = "selected", ["slot"] = "pinned" } }));
        Assert.False(restored.Matches(original));
        Assert.False(snapshot.IsEquivalentTo(Snapshot([], [Wait()])));
        Assert.False(JsonSerializer.SerializeToNode(Wait(), Web)!.AsObject().ContainsKey("registrationCapture"));
    }

    [Theory]
    [InlineData("version")] [InlineData("generation")] [InlineData("executable")] [InlineData("scope")]
    [InlineData("pinnedSource")] [InlineData("metadata")] [InlineData("payloadFingerprint")]
    public void Exact_resume_capture_requires_every_field_including_explicit_null_source(string field)
    {
        var wire = JsonSerializer.SerializeToNode(CapturedWait(), Web)!;
        wire["registrationCapture"]!.AsObject().Remove(field);
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<StimulusResumeRecipient>(Web));
    }

    [Theory]
    [InlineData("version", "0")] [InlineData("version", "2")] [InlineData("generation", "0")]
    [InlineData("metadata", "null")] [InlineData("metadata", "{\"activation\":null}")]
    [InlineData("payloadFingerprint", "\"short\"")] [InlineData("executable", "null")]
    public void Exact_resume_capture_refuses_malformed_facts_without_falling_back_to_legacy(string field, string replacement)
    {
        var wire = JsonSerializer.SerializeToNode(CapturedWait(), Web)!;
        wire["registrationCapture"]![field] = JsonNode.Parse(replacement);
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<StimulusResumeRecipient>(Web));
    }

    [Fact]
    public void Exact_resume_capture_refuses_present_null_duplicate_capture_and_duplicate_metadata_keys()
    {
        var wire = JsonSerializer.SerializeToNode(CapturedWait(), Web)!;
        var json = wire.ToJsonString();
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<StimulusResumeRecipient>(json.Replace("\"generation\":7", "\"generation\":7,\"generation\":8"), Web));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<StimulusResumeRecipient>(json.Replace("\"metadata\":{}", "\"metadata\":{\"x\":\"a\",\"x\":\"b\"}"), Web));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<StimulusResumeRecipient>(json.Insert(json.LastIndexOf('}'), ",\"registrationCapture\":null"), Web));
        wire["registrationCapture"] = null;
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<StimulusResumeRecipient>(Web));
    }

    [Fact]
    public void Exact_checkpoint_fingerprint_includes_opaque_condition_but_legacy_null_keeps_old_wire()
    {
        var legacy = new RuntimeCheckpointCommit("commit", new("checkpoint", "name", "waiter", Now, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>());
        Assert.False(JsonSerializer.SerializeToNode(legacy, Web)!.AsObject().ContainsKey("expectedBookmarkResume"));
        // Compiled parent 678a519 writer over this exact historical envelope.
        Assert.Equal("af976cb76bbef3f226bc15cbbdae5d8b330ec98ae9f287ff18e15aadc2df1f1a", RuntimeCheckpointCommitFingerprint.Compute(legacy));
        var captured = legacy with { ExpectedBookmarkResume = CapturedWait() };
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(legacy), RuntimeCheckpointCommitFingerprint.Compute(captured));
        var left = captured with { ExpectedBookmarkResume = CapturedWait(new Dictionary<string, string> { ["opaque"] = "\ud800" }) };
        var right = captured with { ExpectedBookmarkResume = CapturedWait(new Dictionary<string, string> { ["opaque"] = "\ud801" }) };
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(left), RuntimeCheckpointCommitFingerprint.Compute(right));
        var differentKey = captured with { ExpectedBookmarkResume = CapturedWait(new Dictionary<string, string> { ["\ud800"] = "value" }) };
        var otherKey = captured with { ExpectedBookmarkResume = CapturedWait(new Dictionary<string, string> { ["\ud801"] = "value" }) };
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(differentKey), RuntimeCheckpointCommitFingerprint.Compute(otherKey));
    }

    private static StimulusResumeRecipient CapturedWait(IReadOnlyDictionary<string, string>? metadata = null) =>
        StimulusResumeRecipient.From(new BookmarkStateObservation(Bookmark() with { Metadata = metadata ?? new Dictionary<string, string>() }, 7),
            Workflow() with { WorkflowExecutionId = "waiter" }, new("tenant"));
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
