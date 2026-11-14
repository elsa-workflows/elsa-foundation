using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Capture.Core;
using Elsa.Workflows.Publishing.SourceCapture;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.NativeCapture.Tests;

public sealed class PublishedNativeSourceTests
{
    [Fact]
    public async Task Actual_compiler_publisher_and_EF_switch_preserve_selected_B_and_stored_producer_A()
    {
        await using var f = await PublishedNativeFixture.Create();
        await f.Seed("definition-A", "version-A", "1.0.0"); var first = await f.Publish("version-A");
        await f.Seed("definition-B", "version-B", "3.7.0"); var second = await f.Publish("version-B");
        Assert.Equal(first.ArtifactId, second.ArtifactId);
        var stored = await f.Materials.FindMaterialBoundedAsync(first.ArtifactId, PublishedNativeFixture.Partition, new()); Assert.NotNull(stored);
        Assert.Equal("definition-A", stored.Material.Identity.DefinitionId); Assert.Equal("version-A", stored.Material.Identity.DefinitionVersionId);
        var calls = 0;
        await f.Source().ExecuteAsync([Selection("version-B")], new(), async (capture, token) =>
        {
            calls++; var root = Assert.Single(capture.Roots); var selected = f.Codec.DecodeWorkflowSelection(capture.Content.GetPayload(root.Member));
            var body = f.Codec.DecodeWorkflow(capture.Content.GetPayload(root.Content));
            Assert.Equal("definition-B", selected.DefinitionId); Assert.Equal("version-B", selected.DefinitionVersionId); Assert.Equal("3.7.0", selected.Version);
            Assert.Equal(f.OriginalSources["version-B"], selected.StateSource); Assert.Equal(second.SourceReferenceId, selected.SourceReferenceId);
            Assert.Equal(stored.Material.Identity, selected.Artifact); Assert.Equal(stored.Material.Identity, body.Executable.Identity);
            Assert.Equal(f.Codec.EncodeWorkflow(new(PublishedNativeFixture.Scope, stored.Material.NativeWorkflow!)).GetPayloadBytes(), capture.Content.GetPayload(root.Content).ToArray());
            Assert.True(await f.Runtime.WorkflowExecutableRootWriteLeases.AnyAsync(token));
            var now = DateTimeOffset.UtcNow;
            Assert.Null(await f.Materials.TryBeginDeletionAsync(first.ArtifactId, "actual-source-gc", now.AddMinutes(1), now, token));
            await Task.Yield(); Assert.True(await f.Runtime.WorkflowExecutableRootWriteLeases.AnyAsync(token));
        });
        Assert.Equal(1, calls); Assert.Empty(await f.Runtime.WorkflowExecutableRootWriteLeases.ToListAsync());
    }

    [Fact]
    public async Task Multiple_selected_workflows_share_the_actual_stored_body_and_one_retention_lease()
    {
        await using var f = await PublishedNativeFixture.Create();
        await f.Seed("definition-A", "version-A", "1.0.0"); var first = await f.Publish("version-A");
        await f.Seed("definition-B", "version-B", "3.7.0"); var second = await f.Publish("version-B");
        Assert.Equal(first.ArtifactId, second.ArtifactId);
        await f.Source().ExecuteAsync([Selection("version-A"), Selection("version-B")], new(), async (capture, token) =>
        {
            Assert.Equal(2, capture.Roots.Count); Assert.Single(capture.Roots.Select(root => root.Content).Distinct());
            Assert.Equal(3, capture.Content.Members.Count);
            var selected = capture.Roots.Select(root => f.Codec.DecodeWorkflowSelection(capture.Content.GetPayload(root.Member))).OrderBy(value => value.DefinitionVersionId).ToArray();
            Assert.Equal(new[] { "version-A", "version-B" }, selected.Select(value => value.DefinitionVersionId));
            foreach (var version in selected) Assert.Equal(f.OriginalSources[version.DefinitionVersionId], version.StateSource);
            var body = f.Codec.DecodeWorkflow(capture.Content.GetPayload(capture.Roots[0].Content));
            Assert.Equal("version-A", body.Executable.Identity.DefinitionVersionId);
            Assert.All(selected, version => Assert.Equal(body.Executable.Identity, version.Artifact));
            Assert.Equal(1, await f.Runtime.WorkflowExecutableRootWriteLeases.CountAsync(token));
        });
        Assert.Empty(await f.Runtime.WorkflowExecutableRootWriteLeases.ToListAsync());
    }

    [Fact]
    public async Task An_unpublished_second_version_does_not_inherit_a_same_behavior_publication()
    {
        await using var f = await PublishedNativeFixture.Create(); await f.Seed("definition", "v1", "1.0.0"); await f.Publish("v1");
        await f.Seed("definition", "v2", "2.0.0");
        Assert.Equal("native_source_published_version_missing", (await Refused(f, Selection("v2"))).Code);
    }

    [Fact]
    public async Task Actual_replacement_keeps_the_retired_published_version_capturable_while_its_bytes_remain()
    {
        await using var f = await PublishedNativeFixture.Create(); await f.Seed("definition", "v1", "1.0.0"); var first = await f.Publish("v1");
        await f.Seed("definition", "v2", "2.0.0", "different"); var second = await f.Publish("v2"); Assert.NotEqual(first.ArtifactId, second.ArtifactId);
        Assert.Equal(PublicationStatus.Retired, (await f.Journals.FindAsync(first.PublicationId))!.Status);
        await f.Source().ExecuteAsync([Selection("v1")], new(), (capture, _) =>
        { Assert.Equal(first.ArtifactId, Assert.Single(capture.Roots).Content.Identity); return ValueTask.CompletedTask; });
    }

    [Theory]
    [InlineData("Candidate")]
    [InlineData("PendingProjection")]
    [InlineData("Failed")]
    public async Task Canonical_references_without_an_activated_journal_status_do_not_qualify(string status)
    {
        await using var f = await PublishedNativeFixture.Create(); await f.Seed("definition", "v1", "1.0.0"); await f.Publish("v1");
        await f.Publishing.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_publication_records SET Status={status}");
        Assert.Equal("native_source_published_version_missing", (await Refused(f, Selection("v1"))).Code);
    }

    [Fact]
    public async Task A_timestamp_only_legacy_reference_is_explicitly_unsupported()
    {
        await using var f = await PublishedNativeFixture.Create(); await f.Seed("definition", "published", "1.0.0"); var published = await f.Publish("published");
        await f.Seed("legacy-definition", "legacy-version", "2.0.0"); var now = DateTimeOffset.UtcNow;
        // Deliberate unsupported counterexample, never evidence that the owner published this version.
        await f.References.SaveAsync(new("legacy-reference", published.ArtifactId, "WorkflowDefinitionVersion", "legacy-version", "2.0.0",
            "legacy-definition", "legacy-version", "2.0.0", now, now, WorkflowExecutableReferenceScope.Published));
        Assert.Equal("native_source_legacy_publication_unsupported", (await Refused(f, Selection("legacy-version"))).Code);
    }

    [Fact]
    public async Task Missing_actual_journal_proof_cannot_be_replaced_by_a_Published_reference()
    {
        await using var f = await PublishedNativeFixture.Create(); await f.Seed("definition", "v1", "1.0.0"); await f.Publish("v1");
        await f.Publishing.Database.ExecuteSqlRawAsync("DELETE FROM elsa_publication_records");
        Assert.Equal("native_source_publication_proof_missing", (await Refused(f, Selection("v1"))).Code);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("selected-material")]
    [InlineData("retained-closure")]
    public async Task Each_distinct_source_export_policy_must_admit_the_actual_read(string denied)
    {
        await using var f = await PublishedNativeFixture.Create(); await f.Seed("definition", "v1", "1.0.0"); await f.Publish("v1");
        f.Authorization.AllowVersion = denied != "version"; f.Authorization.AllowSelected = denied != "selected-material"; f.Authorization.AllowMaterial = denied != "retained-closure";
        Assert.Equal("native_source_export_forbidden", (await Refused(f, Selection("v1"))).Code);
        Assert.Equal(denied == "version" ? 0 : 1, f.VersionReads);
        Assert.Equal(denied == "retained-closure" ? 1 : 0, f.SelectedMaterialReads);
        Assert.Empty(await f.Runtime.WorkflowExecutableRootWriteLeases.ToListAsync());
    }

    [Theory]
    [InlineData("sourceId")]
    [InlineData("sourceVersion")]
    public async Task Stored_source_labels_must_match_the_selected_version_independently_of_the_producer(string field)
    {
        await using var f = await PublishedNativeFixture.Create(); await f.Seed("definition", "v1", "1.0.0"); await f.Publish("v1");
        var row = await f.Runtime.WorkflowExecutableSourceReferences.SingleAsync();
        var json = System.Text.Json.Nodes.JsonNode.Parse(row.ContentJson)!;
        json["reference"]![field] = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(field == "sourceId" ? "other-version" : "9.0.0");
        row.ContentJson = json.ToJsonString(); await f.Runtime.SaveChangesAsync(); f.Runtime.ChangeTracker.Clear();
        Assert.Equal("native_source_publication_association_invalid", (await Refused(f, Selection("v1"))).Code);
    }

    [Fact]
    public async Task Callback_failure_and_requested_cancellation_release_actual_retention()
    {
        await using var f = await PublishedNativeFixture.Create(); await f.Seed("definition", "v1", "1.0.0"); await f.Publish("v1");
        await Assert.ThrowsAsync<ExpectedFailure>(() => f.Source().ExecuteAsync([Selection("v1")], new(), (_, _) => throw new ExpectedFailure()).AsTask());
        Assert.Empty(await f.Runtime.WorkflowExecutableRootWriteLeases.ToListAsync());
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Source().ExecuteAsync([Selection("v1")], new(), (_, token) =>
        { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }, cancellation.Token).AsTask());
        Assert.Empty(await f.Runtime.WorkflowExecutableRootWriteLeases.ToListAsync());
    }

    [Fact]
    public async Task Source_context_change_and_requested_version_mismatch_refuse()
    {
        await using var f = await PublishedNativeFixture.Create(); await f.Seed("definition", "v1", "1.0.0"); await f.Publish("v1");
        Assert.Equal("native_source_selected_version_mismatch", (await Refused(f, new(NativeReleaseCaptureSelections.WorkflowDefinitionVersion, "v1", new("semver", "2.0.0", null), ReleaseRootAction.ActivateServing))).Code);
        f.Context.Current = PublishedNativeFixture.Scope with { Partition = "other" };
        Assert.Equal("native_source_scope_mismatch", (await Refused(f, Selection("v1"))).Code);
    }

    [Fact]
    public void Optional_feature_registers_no_source_export_authority()
    {
        var services = new ServiceCollection(); var feature = new WorkflowsPublishingReleaseCaptureFeature(); feature.ConfigureServices(services);
        Assert.Throws<InvalidOperationException>(() => feature.PostConfigureServices(services));
    }
    private static ReleaseCaptureSelection Selection(string id) => new(NativeReleaseCaptureSelections.WorkflowDefinitionVersion, id, null, ReleaseRootAction.ActivateServing);
    private static Task<ReleaseCaptureException> Refused(PublishedNativeFixture f, ReleaseCaptureSelection selection) =>
        Assert.ThrowsAsync<ReleaseCaptureException>(() => f.Source().ExecuteAsync([selection], new(), (_, _) => throw new InvalidOperationException("A refused source must not reach capture.")).AsTask());
    private sealed class ExpectedFailure : Exception { }
}
