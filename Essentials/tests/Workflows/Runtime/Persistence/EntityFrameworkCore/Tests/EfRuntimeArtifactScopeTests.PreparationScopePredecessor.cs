using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactScopeTests
{
    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.2.0")]
    public async Task Artifact_only_family_writers_cover_all_six_siblings_and_preserve_legacy_lease_headers(string finalized)
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var original = Executable("family-original");
        var reference = Reference("family-source", original.Identity.ArtifactId) with { TenantId = "tenant-a" };
        await fixture.Executables.SaveAsync(original);
        await fixture.Sources.SaveAsync(reference);
        var sourceContent = (await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync()).ContentJson;
        var oldLease = Assert.IsType<WorkflowExecutableRootWriteLease>(await fixture.Executables.TryAcquireRootWriteLeaseAsync(
            original.Identity.ArtifactId, "old-lease", now.AddMinutes(5), now));
        await fixture.FinalizeArtifactAsync(finalized);

        var retired = reference with { DeletedAt = now };
        Assert.True(await fixture.Sources.TryRetireAsync(reference, retired));
        Assert.True(await fixture.Sources.TryRestoreAsync(retired, reference));
        var writtenReference = await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(finalized, writtenReference.SchemaVersion);
        Assert.Equal(sourceContent, writtenReference.ContentJson);
        Assert.Null((await fixture.Sources.FindAsync(reference.SourceReferenceId))!.PublicationScope);
        Assert.True(await fixture.Executables.RenewRootWriteLeaseAsync(oldLease, now.AddMinutes(6), now.AddSeconds(1)));
        Assert.Equal(RuntimeArtifactEfModule.SchemaVersion,
            (await fixture.Context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync()).SchemaVersion);
        await fixture.Executables.ReleaseRootWriteLeaseAsync(oldLease);

        await fixture.Executables.SaveAsync(Executable("family-new"));
        Assert.Contains(await fixture.Context.WorkflowExecutables.AsNoTracking().ToArrayAsync(), row => row.SchemaVersion == finalized);
        Assert.Contains(await fixture.Context.WorkflowExecutableCoordinations.AsNoTracking().ToArrayAsync(), row => row.SchemaVersion == finalized);
        await fixture.Templates.SaveAsync(Template("family-template", "sha256:family-template"));
        Assert.Equal(finalized, (await fixture.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal(finalized, (await fixture.Context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync()).SchemaVersion);
        var modelPin = new WorkflowExecutableIdentity("family-model", "model-definition", "model-version", "1", "pending");
        RuntimeModelMaterial Model(WorkflowExecutableIdentity identity) => new(1, identity, new("test.family", 1), "test/1",
            JsonSerializer.SerializeToElement(new { start = "wait" }), new(1, []), [], [], []);
        var model = Model(modelPin);
        await ((IWorkflowExecutableStore)fixture.Executables).SaveModelAsync(Model(modelPin with { ArtifactHash = model.ComputeContentHash() }));
        Assert.Equal(finalized, (await fixture.Context.WorkflowExecutables.AsNoTracking().SingleAsync(row => row.IsModelMaterial)).SchemaVersion);
        var lease = Assert.IsType<WorkflowExecutableRootWriteLease>(await fixture.Executables.TryAcquireRootWriteLeaseAsync(
            original.Identity.ArtifactId, "new-lease", now.AddMinutes(5), now));
        Assert.Equal(finalized, (await fixture.Context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync()).SchemaVersion);
        await fixture.Executables.ReleaseRootWriteLeaseAsync(lease);
    }

    [Theory]
    [InlineData("publicationScope", false)]
    [InlineData("PublicationScope", false)]
    [InlineData("PUBLICATIONSCOPE", false)]
    [InlineData("publicationScope", true)]
    [InlineData("PublicationScope", true)]
    public void Scope_predecessor_refuses_forged_markers_at_the_envelope_or_reference_boundary(string spelling, bool envelope)
    {
        // Deliberately a source-format specimen, not an authenticated archived-writer capture.
        var document = new JsonObject { ["collection"] = "workflowExecutableSourceReference", ["reference"] = new JsonObject() };
        (envelope ? document : document["reference"]!.AsObject())[spelling] = null;
        Assert.Throws<InvalidDataException>(() => RuntimeArtifactEfModule.Chain.Upcast<WorkflowExecutableSourceReferenceEntity>(
            RuntimeArtifactEfModule.SchemaVersion, ("ContentJson", document.ToJsonString())));
    }

    [Fact]
    public void Scope_predecessor_returns_valid_predecessor_document_bytes_unchanged()
    {
        // This literal tests the upcaster's byte-preserving rule only; authenticated frozen source bytes
        // and the old-writer consumer qualification remain a separate pending capture gate.
        const string content = "{ \"collection\" : \"workflowExecutableSourceReference\", \"reference\" : {} }";
        var row = new EfSchemaRowContent(typeof(WorkflowExecutableSourceReferenceEntity), ("ContentJson", content));
        var result = new RuntimeScopedPublicationV1Upcaster().Upcast(row);
        Assert.Same(row, result);
        Assert.Equal(content, result["ContentJson"]);
        Assert.Equal(["1.0.0", "1.1.0", "1.2.0"], RuntimeArtifactEfModule.Chain.ReadableVersions);
    }

    [Fact]
    public async Task Scoped_predecessor_reads_and_compensation_preserve_physical_scope_independently_from_source_tenant()
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        await fixture.FinalizeArtifactAsync(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        var value = ScopedPredecessorReference();
        await SeedScopedPredecessorAsync(fixture, value);
        var found = (await fixture.Sources.FindAsync(value.SourceReferenceId))!;
        Assert.Equal("logical-tenant", found.TenantId);
        Assert.Equal(new PersistenceScope("tenant-a"), found.PublicationScope);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(value, found));
        Assert.False(WorkflowExecutableSourceReferenceComparer.SameIdentity(found, found with { PublicationScope = null }));
        Assert.False(WorkflowExecutableSourceReferenceComparer.SameSnapshot(found, found with { PublicationScope = new("different") }));
        var retired = found.Retire(DateTimeOffset.UtcNow, "test");
        Assert.True(await fixture.Sources.TryRetireAsync(found, retired));
        Assert.True(await fixture.Sources.TryRestoreAsync(retired, found));
        var restored = (await fixture.Sources.FindAsync(value.SourceReferenceId))!;
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(found, restored));
        var restoredRow = await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        var decoded = RuntimeArtifactJson.Deserialize<WorkflowExecutableSourceReference>(
            JsonNode.Parse(restoredRow.ContentJson)!["reference"]!.ToJsonString());
        Assert.Equal(found.PublicationScope, decoded.PublicationScope);
        Assert.Equal(found.TenantId, decoded.TenantId);
        // Producer remains disabled: an existing marker is preservable through checked compensation,
        // but is not caller authority for generic create/adopt or fenced publication.
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Sources.SaveAsync(found).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Sources.StageCreateOrAdoptAsync(found, CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Sources.SaveWithRootWriteLeasesAsync(found,
            Executable("scope-artifact").Identity, []).AsTask());
        Assert.Single(await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData("physical-scope")]
    [InlineData("activation")]
    [InlineData("slot")]
    [InlineData("scope-kind")]
    public async Task Scoped_predecessor_rejects_scope_or_logical_provenance_disagreement(string defect)
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        await fixture.FinalizeArtifactAsync(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        var valid = ScopedPredecessorReference();
        var malformed = defect switch
        {
            "physical-scope" => valid with { PublicationScope = new("other-physical-scope") },
            "activation" => valid with { ActivationId = "other-activation" },
            "slot" => valid with { SlotId = "not-the-definition-slot" },
            "scope-kind" => valid with { Scope = WorkflowExecutableReferenceScope.TestRun },
            _ => throw new ArgumentException("Unknown case.", nameof(defect))
        };
        await SeedScopedPredecessorAsync(fixture, malformed);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Sources.FindAsync(valid.SourceReferenceId).AsTask());
    }

    [Fact]
    public void Legacy_serialization_omits_the_new_scope_marker_and_preserves_existing_constructor_shape()
    {
        var reference = Reference("legacy-source", "legacy-artifact");
        var legacy = JsonNode.Parse(RuntimeArtifactJson.Serialize(reference))!.AsObject();
        Assert.False(legacy.ContainsKey("publicationScope"));
        Assert.Null(reference.PublicationScope);
        var scoped = reference with { PublicationScope = new("tenant-a") };
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(scoped,
            RuntimeArtifactJson.Deserialize<WorkflowExecutableSourceReference>(RuntimeArtifactJson.Serialize(scoped))));
    }

    [Fact]
    public async Task Memory_generic_and_fenced_writes_refuse_caller_scope_assertions_while_legacy_publication_works()
    {
        var material = new InMemoryWorkflowExecutableStore();
        var sources = new InMemoryWorkflowExecutableSourceReferenceStore(material);
        var executable = Executable("memory-scope-artifact");
        await material.SaveAsync(executable);
        var now = DateTimeOffset.UtcNow;
        var lease = Assert.IsType<WorkflowExecutableRootWriteLease>(await material.TryAcquireRootWriteLeaseAsync(
            executable.Identity.ArtifactId, "memory-scope-lease", now.AddMinutes(5), now));
        var legacy = Reference("memory-source", executable.Identity.ArtifactId);
        var forged = legacy with { PublicationScope = new("caller-asserted") };
        await Assert.ThrowsAsync<NotSupportedException>(() => sources.SaveAsync(forged).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => sources.SaveWithRootWriteLeasesAsync(forged,
            executable.Identity, [lease]).AsTask());
        Assert.Null(await sources.FindAsync(legacy.SourceReferenceId));
        var winner = await sources.SaveWithRootWriteLeasesAsync(legacy, executable.Identity, [lease]);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(legacy, winner));
        Assert.Null(winner.PublicationScope);
        var ordinary = legacy with { SourceReferenceId = "memory-ordinary" };
        await sources.SaveAsync(ordinary);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(ordinary, (await sources.FindAsync(ordinary.SourceReferenceId))!));
    }

    [Fact]
    public async Task Lease_renewal_loses_when_the_observed_schema_changes_before_its_header_only_update()
    {
        var race = new PreparationLeaseSchemaRace();
        await using var fixture = await PreparationGateFixture.CreateAsync(race);
        var executable = Executable("schema-race-artifact");
        await fixture.Executables.SaveAsync(executable);
        var now = DateTimeOffset.UtcNow;
        var lease = Assert.IsType<WorkflowExecutableRootWriteLease>(await fixture.Executables.TryAcquireRootWriteLeaseAsync(
            executable.Identity.ArtifactId, "schema-race-lease", now.AddMinutes(5), now));
        var before = await fixture.Context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        await fixture.FinalizePreparationAsync();
        race.RowId = before.Id;
        race.Armed = true;
        Assert.False(await fixture.Executables.RenewRootWriteLeaseAsync(lease, now.AddMinutes(6), now.AddSeconds(1)));
        Assert.True(race.Changed);
        var after = await fixture.Context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, after.SchemaVersion);
        Assert.Equal(before.ExpiresAtUtcTicks, after.ExpiresAtUtcTicks);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.IncarnationId, after.IncarnationId);
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.2.0")]
    public async Task Legacy_template_reference_fencing_preserves_material_after_family_finalization(string finalized)
    {
        // The source fix keeps the desired successful outcome; executable qualification remains pending.
        await using var fixture = await PreparationGateFixture.CreateAsync();
        var template = Template("legacy-fenced-template", "sha256:legacy-fenced-template");
        await fixture.Templates.SaveAsync(template);
        var before = await fixture.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        await fixture.FinalizeArtifactAsync(finalized);
        await fixture.Executables.SaveAsync(TemplateWorkflow(template, "wrapper"));
        var after = await fixture.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.Equal(before.IncarnationId, after.IncarnationId);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(finalized, after.SchemaVersion);
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.2.0")]
    public async Task Legacy_template_doomed_reference_maintenance_preserves_material_after_family_finalization(string finalized)
    {
        // Both template and hash-claim rows must follow the actual gate without changing material bytes.
        await using var fixture = await PreparationGateFixture.CreateAsync();
        var template = Template("legacy-maintained-template", "sha256:legacy-maintained-template");
        var reference = Reference("legacy-template-source", template.TemplateId);
        await fixture.Templates.SaveWithReferenceAsync(template, reference);
        var before = await fixture.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        await fixture.FinalizeArtifactAsync(finalized);
        var now = DateTimeOffset.UtcNow;
        Assert.True(await fixture.Sources.RetireAsync(reference.SourceReferenceId, now));
        var retired = (await fixture.Sources.FindAsync(reference.SourceReferenceId))!;
        Assert.True(await fixture.Templates.TryDeleteDoomedReferenceAsync(retired, now));
        var after = await fixture.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.Equal(before.IncarnationId, after.IncarnationId);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(finalized, after.SchemaVersion);
        Assert.Equal(finalized,
            (await fixture.Context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Empty(await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
    }

    private sealed class PreparationLeaseSchemaRace : DbCommandInterceptor
    {
        public string RowId { get; set; } = "";
        public bool Armed { get; set; }
        public bool Changed { get; private set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed || Changed || !command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) ||
                !command.CommandText.Contains(RuntimeArtifactEfModule.WorkflowExecutableRootWriteLeaseTableName, StringComparison.Ordinal) ||
                !command.CommandText.Contains("ExpiresAtUtcTicks", StringComparison.Ordinal)) return result;
            // Simulate a separately observed schema-header change, without changing expiry/revision.
            // The renewal must compare the stamp it actually read, even though both formats are readable.
            await using var change = command.Connection!.CreateCommand();
            change.Transaction = command.Transaction;
            change.CommandText = $"UPDATE \"{RuntimeArtifactEfModule.WorkflowExecutableRootWriteLeaseTableName}\" SET \"SchemaVersion\" = @version WHERE \"Id\" = @id";
            var version = change.CreateParameter(); version.ParameterName = "@version"; version.Value = RuntimeArtifactEfModule.ScopedPublicationSchemaVersion;
            var id = change.CreateParameter(); id.ParameterName = "@id"; id.Value = RowId;
            change.Parameters.Add(version); change.Parameters.Add(id);
            Changed = await change.ExecuteNonQueryAsync(cancellationToken) == 1;
            return result;
        }
    }

    private static WorkflowExecutableSourceReference ScopedPredecessorReference()
    {
        const string activation = "scope-activation";
        return Reference(WorkflowActivationReferenceIdentity.Create(activation), "scope-artifact") with
        {
            TenantId = "logical-tenant", PublicationScope = new("tenant-a"), ActivationId = activation,
            SlotId = WorkflowActivationSlotIdentity.Create("definition", "test")
        };
    }

    private static async Task SeedScopedPredecessorAsync(PreparationGateFixture fixture, WorkflowExecutableSourceReference value)
    {
        // A physical test fixture at the declared predecessor schema, not a successful producer invocation.
        await fixture.Sources.SaveAsync(value with { TenantId = "tenant-a", PublicationScope = null });
        var row = await fixture.Context.WorkflowExecutableSourceReferences.SingleAsync();
        row.ContentJson = new JsonObject { ["collection"] = "workflowExecutableSourceReference",
            ["artifactId"] = EfRelationalIdentity.Encode(value.ArtifactId),
            ["reference"] = JsonNode.Parse(RuntimeArtifactJson.Serialize(value)) }.ToJsonString();
        row.SchemaVersion = RuntimeArtifactEfModule.ScopedPublicationSchemaVersion;
        row.Scope = value.Scope.ToString();
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }
}
