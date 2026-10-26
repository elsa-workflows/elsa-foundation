using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Compact data/union contracts only; persisted source admission and saved starts remain separate gates.</summary>
public sealed class StimulusDeliveryStartAuthorityTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1.0.0", "hash");

    [Fact]
    public void Compact_authority_round_trips_exact_source_claim_and_binding_without_copying_the_cohort()
    {
        var context = Context();
        var authority = WorkflowExecutableStartAuthority.FromStimulusDelivery(context, "binding");
        var json = JsonSerializer.Serialize(authority, Web);
        var restored = JsonSerializer.Deserialize<WorkflowExecutableStartAuthority>(json, Web)!;
        Assert.Equal(WorkflowExecutableStartAuthorityKind.StimulusDelivery, restored.Kind);
        Assert.Null(restored.RetainedDependency);
        var saved = restored.StimulusDelivery!;
        Assert.Equal(context.SourceOutboxItemId, saved.SourceOutboxItemId);
        Assert.Equal(context.IntentFingerprint, saved.IntentFingerprint);
        Assert.Equal(context.Scope, saved.Scope);
        Assert.Equal(context.Claim.OwnerId, saved.OwnerId);
        Assert.Equal(context.Claim.FencingToken, saved.FencingToken);
        Assert.Equal("binding", saved.TriggerBindingId);
        Assert.DoesNotContain("recipientSnapshot", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Original_union_constructor_and_wire_remain_live_or_dependency_only()
    {
        Assert.NotNull(typeof(WorkflowExecutableStartAuthority).GetConstructor(
            [typeof(WorkflowExecutableStartAuthorityKind), typeof(WorkflowExecutableRetainedDependencyAuthority)]));
        Assert.Equal("{\"kind\":0}", JsonSerializer.Serialize(new WorkflowExecutableStartAuthority(WorkflowExecutableStartAuthorityKind.LiveReference), Web));
        var retained = WorkflowExecutableStartAuthority.FromRetainedDependency("parent", "hash", "node");
        var wire = JsonSerializer.Serialize(retained, Web);
        Assert.DoesNotContain("stimulusDelivery", wire, StringComparison.Ordinal);
        Assert.Equal("parent", JsonSerializer.Deserialize<WorkflowExecutableStartAuthority>(wire, Web)!.RetainedDependency!.ParentArtifactId);
    }

    [Theory]
    [InlineData("sourceOutboxItemId")]
    [InlineData("intentFingerprint")]
    [InlineData("scope")]
    [InlineData("ownerId")]
    [InlineData("fencingToken")]
    [InlineData("triggerBindingId")]
    public void Compact_present_source_fields_cannot_disappear_into_defaults(string field)
    {
        var wire = JsonSerializer.SerializeToNode(Authority(), Web)!.AsObject();
        Assert.True(wire.Remove(field));
        Assert.ThrowsAny<JsonException>(() => wire.Deserialize<WorkflowExecutableStimulusDeliveryAuthority>(Web));
    }

    [Theory]
    [InlineData("short-digest")]
    [InlineData("uppercase-digest")]
    [InlineData("invalid-digest")]
    [InlineData("blank-source")]
    [InlineData("blank-owner")]
    [InlineData("blank-binding")]
    [InlineData("long-binding")]
    [InlineData("zero-fence")]
    [InlineData("negative-fence")]
    [InlineData("null-scope")]
    public void Incomplete_compact_source_claims_refuse_at_construction(string defect)
    {
        var context = Context();
        Assert.ThrowsAny<ArgumentException>(() => new WorkflowExecutableStimulusDeliveryAuthority(
            defect == "blank-source" ? " " : context.SourceOutboxItemId,
            defect switch { "short-digest" => "abc", "uppercase-digest" => new string('A', 64),
                "invalid-digest" => new string('g', 64), _ => context.IntentFingerprint },
            defect == "null-scope" ? null! : context.Scope,
            defect == "blank-owner" ? " " : context.Claim.OwnerId,
            defect switch { "zero-fence" => 0, "negative-fence" => -1, _ => context.Claim.FencingToken },
            defect switch { "blank-binding" => " ", "long-binding" => new string('a', 129), _ => "binding" }));
    }

    [Theory]
    [InlineData(WorkflowExecutableStartAuthorityKind.LiveReference)]
    [InlineData(WorkflowExecutableStartAuthorityKind.RetainedDependency)]
    [InlineData(WorkflowExecutableStartAuthorityKind.StimulusDelivery)]
    public void Authority_union_rejects_mixed_or_missing_matching_provenance(WorkflowExecutableStartAuthorityKind kind)
    {
        var dependency = new WorkflowExecutableRetainedDependencyAuthority("parent", "hash", "node");
        Assert.Throws<ArgumentException>(() => new WorkflowExecutableStartAuthority(kind, dependency, Authority()));
        if (kind == WorkflowExecutableStartAuthorityKind.StimulusDelivery)
            Assert.Throws<ArgumentException>(() => new WorkflowExecutableStartAuthority(kind, null, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saved_start_request_refuses_live_selection_or_live_requirement(bool select)
    {
        Assert.Throws<ArgumentException>(() => Request(select ? new(sourceReferenceId: "live") : null,
            select ? WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy : WorkflowExecutableProvenanceRequirement.RequireLiveReference));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Request_and_command_preserve_saved_authority_with_selected_or_legacy_null_attribution(bool referenceLess)
    {
        var request = Request(null, WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy);
        var restoredRequest = JsonSerializer.Deserialize<WorkflowExecutionStartDispatchRequest>(JsonSerializer.Serialize(request, Web), Web)!;
        Assert.Equal("binding", restoredRequest.StartAuthority!.StimulusDelivery!.TriggerBindingId);
        var source = referenceLess ? null : new WorkflowExecutableSourceProvenance("reference", "definition", "selected",
            "2", "selected", "selected-version", "selected-label", "publication", "slot");
        var payload = new WorkflowExecutionStartCommandPayload(Pin, Pin.ArtifactId, null, null, null, "start",
            WorkflowRunKind.PublishedRun, source, null, null, null, new("partition"),
            WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime"), request.StartAuthority);
        var restored = JsonSerializer.Deserialize<WorkflowExecutionStartCommandPayload>(JsonSerializer.Serialize(payload, Web), Web)!;
        Assert.Equal(source, restored.PinnedSource);
        Assert.Equal("binding", restored.StartAuthority!.StimulusDelivery!.TriggerBindingId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_saved_source_provider_default_refuses_without_install_fallback(bool cancel)
    {
        var legacy = new LegacyDeliveryStore(); IStimulusDeliveryStore store = legacy;
        var token = new CancellationToken(canceled: cancel);
        if (cancel)
        {
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadSavedStartSourceAsync(Authority(), token).AsTask());
            Assert.Equal(token, exception.CancellationToken);
        }
        else
        {
            var exception = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => store.ReadSavedStartSourceAsync(Authority()).AsTask());
            Assert.False(exception.IsUnavailable); Assert.IsType<NotSupportedException>(exception.InnerException);
            Assert.Equal(Context().SourceOutboxItemId, exception.SourceOutboxItemId);
        }
        Assert.Equal(0, legacy.Installs);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Legacy_retention_provider_refuses_instead_of_returning_no_roots(bool cancel)
    {
        var legacy = new LegacyDeliveryStore(); IStimulusDeliveryStore store = legacy;
        var token = new CancellationToken(cancel);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ListPinnedExecutableArtifactIdsAsync(token).AsTask());
        else await Assert.ThrowsAsync<NotSupportedException>(() => store.ListPinnedExecutableArtifactIdsAsync().AsTask());
        Assert.Equal(0, legacy.Installs);
    }

    private sealed class LegacyDeliveryStore : IStimulusDeliveryStore
    {
        public int Installs { get; private set; }
        public ValueTask<StimulusDeliverySnapshot> InstallAsync(PostCommitDeliveryContext source,
            StimulusDeliverySnapshot candidate, CancellationToken cancellationToken = default)
        { Installs++; throw new InvalidOperationException("Install is not a saved-source read fallback."); }
    }

    private static WorkflowExecutionStartDispatchRequest Request(WorkflowExecutableSourceSelection? selection,
        WorkflowExecutableProvenanceRequirement requirement) => new(Pin.ArtifactId, "runtime", null, null, null,
        null, null, null, "start", WorkflowRunKind.PublishedRun, selection, requirement, null, null, null, null,
        WorkflowExecutionAuthoritySnapshot.CreateRoot("runtime"), WorkflowExecutableStartAuthority.FromStimulusDelivery(Context(), "binding"));

    private static WorkflowExecutableStimulusDeliveryAuthority Authority() =>
        WorkflowExecutableStartAuthority.FromStimulusDelivery(Context(), "binding").StimulusDelivery!;
    private static PostCommitDeliveryContext Context()
    {
        var scope = new PersistenceScope("tenant");
        var item = new RuntimePostCommitOutboxItem("commit:send", new("send", "publisher", "publish", Now, null, "key", null),
            RuntimePostCommitOutboxStatus.Pending, Now, Now, null, 0, null, null, null, null, null, 0, null, 1, scope, null);
        return new(RuntimePostCommitOutboxClaimTransitions.Claim(item, new("owner", Now, TimeSpan.FromMinutes(1), 1)), scope);
    }
}
