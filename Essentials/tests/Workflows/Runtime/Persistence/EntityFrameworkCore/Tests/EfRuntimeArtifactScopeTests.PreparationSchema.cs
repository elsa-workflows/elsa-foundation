using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactScopeTests
{
    [Fact]
    public async Task Preparation_metadata_is_unanchored_and_uses_the_runtime_family_and_content_pipeline()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var entity = fixture.Context.Model.FindEntityType(typeof(WorkflowPreparationRetentionEntity))!;
        Assert.Equal(RuntimeArtifactEfModule.PreparationRetentionTableName, entity.GetTableName());
        Assert.Empty(entity.GetForeignKeys());
        Assert.DoesNotContain(entity.GetProperties(), property => property.Name is "ArtifactId" or "DefinitionId" or "DefinitionVersionId");
        Assert.True(entity.FindProperty(nameof(WorkflowPreparationRetentionEntity.Revision))!.IsConcurrencyToken);
        Assert.True(entity.FindProperty(nameof(WorkflowPreparationRetentionEntity.IncarnationId))!.IsConcurrencyToken);
        Assert.Equal("TEXT", entity.FindProperty(nameof(WorkflowPreparationRetentionEntity.ContentJson))!.GetColumnType());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(WorkflowPreparationRetentionEntity.ScopeKeyHash), nameof(WorkflowPreparationRetentionEntity.PreparationOrderKey)]));
        var families = EfSchemaModuleFamilies.ForContext(fixture.Context.GetType())!;
        Assert.Equal(RuntimeArtifactEfModule.SchemaFamily, families.FamilyOf(typeof(WorkflowPreparationRetentionEntity))!.Family);
        var declaration = EfSchemaFamilyCatalog.Discover([typeof(RuntimeDbContext).Assembly])
            .Single(family => family.Name == RuntimeArtifactEfModule.SchemaFamily);
        Assert.DoesNotContain(typeof(WorkflowPreparationRetentionEntity), declaration.ContentAddressed);
    }

    [Fact]
    public async Task Consumer_owned_runtime_context_inherits_preparation_mapping_without_a_material_anchor()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PreparationConsumerContext>().UseSqlite(connection).Options;
        await using (var consumer = new PreparationConsumerContext(options))
        {
            await consumer.Database.EnsureCreatedAsync();
            consumer.Set<WorkflowPreparationRetentionEntity>().Add(PreparationRow(PreparationSnapshot(templateOnly: true)));
            await consumer.SaveChangesAsync();
        }
        await using var reader = new PreparationConsumerContext(options);
        var row = await reader.Set<WorkflowPreparationRetentionEntity>().AsNoTracking().SingleAsync();
        var value = WorkflowPreparationRetentionCodec.Read(row);
        Assert.IsType<WorkflowPreparationTemplateRoot>(Assert.Single(value.Selection.Roots));
        Assert.IsType<WorkflowPreparationTemplatePin>(Assert.Single(value.Selection.Closure));
        Assert.Empty(await reader.WorkflowExecutables.ToArrayAsync());
        Assert.Empty(await reader.ExecutableActivityTemplates.ToArrayAsync());
        Assert.Empty(await reader.WorkflowExecutableSourceReferences.ToArrayAsync());
    }

    [Fact]
    public async Task Terminal_preparation_history_round_trips_after_all_material_and_references_are_absent()
    {
        await using var database = await Database.CreateAsync();
        var accepted = PreparationSnapshot();
        var decision = new WorkflowPreparationDecision(WorkflowPreparationDecisionKind.Published,
            accepted.AcceptedOwner with { OperationRevision = 9, OperationState = "Published" }, "canonical-decision",
            "decision-fingerprint", accepted.Selection.Fingerprint, ["published-reference"]);
        var terminal = accepted with { State = WorkflowPreparationState.Transferred, ClosedFrom = accepted.Token,
            Token = accepted.Token with { Revision = accepted.Token.Revision + 1 }, TerminalDecision = decision };
        await using (var writer = database.Open("tenant-a"))
        {
            writer.Context.WorkflowPreparationRetentions.Add(PreparationRow(terminal));
            await writer.Context.SaveChangesAsync();
        }
        await using var reader = database.Open("tenant-a");
        var row = await reader.Context.WorkflowPreparationRetentions.AsNoTracking().SingleAsync();
        var found = WorkflowPreparationRetentionCodec.Read(row, terminal.Id);
        Assert.Equal(WorkflowPreparationState.Transferred, found.State);
        Assert.Equal(terminal.Token, found.Token);
        Assert.Equal(accepted.Token, found.ClosedFrom);
        Assert.Equal(["published-reference"], found.TerminalDecision!.PublishedReferenceIds);
        Assert.Empty(await reader.Context.WorkflowExecutables.ToArrayAsync());
        Assert.Empty(await reader.Context.ExecutableActivityTemplates.ToArrayAsync());
        Assert.Empty(await reader.Context.WorkflowExecutableSourceReferences.ToArrayAsync());
    }

    [Fact]
    public void Preparation_codec_preserves_all_typed_pins_lossless_ids_and_owned_collections()
    {
        var snapshot = PreparationSnapshot();
        var roots = snapshot.Selection.Roots.ToArray();
        var pins = snapshot.Selection.Closure.ToArray();
        var row = PreparationRow(snapshot with { Selection = snapshot.Selection with { Roots = roots, Closure = pins } });
        roots[0] = new WorkflowPreparationTemplateRoot(new("mutated", "mutated"));
        pins[0] = new WorkflowPreparationTemplatePin(new("mutated", "mutated"), "other", "other", 999);
        var found = WorkflowPreparationRetentionCodec.Read(row, snapshot.Id);
        Assert.Equal(snapshot.Id, found.Id);
        Assert.Equal(snapshot.Selection.Roots.ToArray(), found.Selection.Roots.ToArray());
        Assert.Equal(snapshot.Selection.Closure.ToArray(), found.Selection.Closure.ToArray());
        Assert.Equal(snapshot.Selection.Fingerprint, found.Selection.Fingerprint);
        Assert.Equal(snapshot.Selection.PreparedContentFingerprint, found.Selection.PreparedContentFingerprint);
        Assert.Throws<NotSupportedException>(() => ((IList<WorkflowPreparationRoot>)found.Selection.Roots)[0] = roots[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<WorkflowPreparationMaterialPin>)found.Selection.Closure)[0] = pins[0]);
        Assert.DoesNotContain("$type", row.ContentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Elsa.Workflows", row.ContentJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Preparation_physical_identity_frames_global_scope_tenant_scope_owner_and_operation_without_aliasing()
    {
        var original = PreparationSnapshot().Id;
        var ids = new[] { original, original with { Scope = null }, original with { Scope = new("0") },
            original with { OwnerAddress = "a", OperationId = "bc" }, original with { OwnerAddress = "ab", OperationId = "c" } };
        Assert.Equal(ids.Length, ids.Select(WorkflowPreparationRetentionCodec.PhysicalId).Distinct(StringComparer.Ordinal).Count());
        var global = PreparationSnapshot(scope: null, global: true);
        var row = PreparationRow(global);
        Assert.Null(WorkflowPreparationRetentionCodec.Read(row, global.Id).Id.Scope);
        Assert.Throws<InvalidDataException>(() => WorkflowPreparationRetentionCodec.Read(row, original));
    }

    [Theory]
    [InlineData("id")] [InlineData("scope")] [InlineData("scope-hash")] [InlineData("owner")]
    [InlineData("operation")] [InlineData("order")] [InlineData("revision")] [InlineData("incarnation")] [InlineData("state")]
    public void Preparation_codec_refuses_key_scope_token_and_state_projection_corruption(string defect)
    {
        var row = PreparationRow(PreparationSnapshot());
        switch (defect)
        {
            case "id": row.Id = "other"; break;
            case "scope": row.ScopeKey = EfRelationalIdentity.Encode("other"); break;
            case "scope-hash": row.ScopeKeyHash = "other"; break;
            case "owner": row.OwnerAddress = EfRelationalIdentity.Encode("other"); break;
            case "operation": row.OperationId = EfRelationalIdentity.Encode("other"); break;
            case "order": row.PreparationOrderKey = "other"; break;
            case "revision": row.Revision++; break;
            case "incarnation": row.IncarnationId = "other"; break;
            case "state": row.State = WorkflowPreparationState.RejectedReleased.ToString(); break;
        }
        Assert.Throws<InvalidDataException>(() => WorkflowPreparationRetentionCodec.Read(row));
    }

    [Theory]
    [InlineData("format")] [InlineData("missing-state")] [InlineData("missing-owner-revision")] [InlineData("missing-scope")]
    [InlineData("unknown-root")] [InlineData("missing-pin-revision")] [InlineData("null-closure")] [InlineData("unknown-property")]
    public void Preparation_codec_refuses_unknown_or_truncated_wire_instead_of_defaulting_to_valid_values(string defect)
    {
        var row = PreparationRow(PreparationSnapshot());
        var json = JsonNode.Parse(row.ContentJson)!.AsObject();
        switch (defect)
        {
            case "format": json["formatVersion"] = 99; break;
            case "missing-state": json.Remove("state"); break;
            case "missing-owner-revision": json["acceptedOwner"]!.AsObject().Remove("operationRevision"); break;
            case "missing-scope": json["id"]!.AsObject().Remove("scope"); break;
            case "unknown-root": json["selection"]!["roots"]![0]!["kind"] = EfRelationalIdentity.Encode("unknown"); break;
            case "missing-pin-revision": json["selection"]!["closure"]![2]!["content"]!.AsObject().Remove("revision"); break;
            case "null-closure": json["selection"]!["closure"] = null; break;
            case "unknown-property": json["ignored"] = true; break;
        }
        row.ContentJson = json.ToJsonString();
        Assert.Throws<InvalidDataException>(() => WorkflowPreparationRetentionCodec.Read(row));
    }

    [Fact]
    public void Preparation_codec_reports_unknown_family_skew_before_damaged_projections_and_refuses_legacy_stamps()
    {
        var row = PreparationRow(PreparationSnapshot());
        row.SchemaVersion = "99.0.0";
        row.Id = "damaged";
        row.ContentJson = "not-json";
        var skew = Assert.Throws<EfSchemaVersionSkewException>(() => WorkflowPreparationRetentionCodec.Read(row));
        Assert.Equal(RuntimeArtifactEfModule.SchemaFamily, skew.Family);
        Assert.Equal("99.0.0", skew.Found);
        row.SchemaVersion = RuntimeArtifactEfModule.SchemaVersion;
        Assert.Throws<InvalidDataException>(() => WorkflowPreparationRetentionCodec.Read(row));
    }

    [Theory]
    [InlineData("missing-closed-token")] [InlineData("wrong-incarnation")] [InlineData("wrong-decision-kind")] [InlineData("impossible-revision")]
    public void Terminal_preparation_metadata_requires_matching_historical_closure_and_canonical_owner(string defect)
    {
        var accepted = PreparationSnapshot();
        var owner = accepted.AcceptedOwner with { OperationRevision = 8, OperationState = "Rejected" };
        var decision = new WorkflowPreparationDecision(WorkflowPreparationDecisionKind.Rejected, owner,
            "canonical-decision", "decision-fingerprint", accepted.Selection.Fingerprint, []);
        var terminal = accepted with { State = WorkflowPreparationState.RejectedReleased, ClosedFrom = accepted.Token,
            Token = accepted.Token with { Revision = 2 }, TerminalDecision = decision };
        terminal = defect switch
        {
            "missing-closed-token" => terminal with { ClosedFrom = null },
            "wrong-incarnation" => terminal with { TerminalDecision = decision with { VerifiedAt = owner with { LedgerIncarnation = "replacement" } } },
            "wrong-decision-kind" => terminal with { TerminalDecision = decision with { Kind = WorkflowPreparationDecisionKind.Published } },
            "impossible-revision" => terminal with { Token = terminal.Token with { Revision = 4 } },
            _ => throw new ArgumentException("Unknown test case.", nameof(defect))
        };
        Assert.Throws<InvalidDataException>(() => PreparationRow(terminal));
    }

    [Fact]
    public async Task Legacy_artifact_and_reference_content_remains_readable_without_rewriting_accepted_bytes()
    {
        await using var database = await Database.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        await fixture.Executable.SaveAsync(Executable("old-artifact"));
        await fixture.Store.SaveAsync(Reference("old-reference", "old-artifact"));
        var artifact = await fixture.Context.WorkflowExecutables.AsNoTracking().SingleAsync();
        var reference = await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, artifact.SchemaVersion);
        Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, reference.SchemaVersion);
        Assert.Equal(artifact.ContentJson, RuntimeArtifactEfModule.Chain.Upcast<WorkflowExecutableEntity>(artifact.SchemaVersion,
            (nameof(artifact.ContentJson), artifact.ContentJson))[nameof(artifact.ContentJson)]);
        Assert.Equal(reference.ContentJson, RuntimeArtifactEfModule.Chain.Upcast<WorkflowExecutableSourceReferenceEntity>(reference.SchemaVersion,
            (nameof(reference.ContentJson), reference.ContentJson))[nameof(reference.ContentJson)]);
        Assert.NotNull(await fixture.Executable.FindAsync("old-artifact"));
        Assert.NotNull(await fixture.Store.FindAsync("old-reference"));
        Assert.Equal(artifact.ContentJson, (await fixture.Context.WorkflowExecutables.AsNoTracking().SingleAsync()).ContentJson);
        Assert.Equal(reference.ContentJson, (await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync()).ContentJson);
        Assert.Empty(await fixture.Context.WorkflowPreparationRetentions.ToArrayAsync());
    }

    private static WorkflowPreparationRetentionEntity PreparationRow(WorkflowPreparationSnapshot snapshot) =>
        WorkflowPreparationRetentionCodec.Create(snapshot, RuntimeArtifactEfModule.PreparationSchemaVersion);

    private static WorkflowPreparationSnapshot PreparationSnapshot(string? scope = "tenant-a", bool global = false, bool templateOnly = false)
    {
        var id = new WorkflowPreparationId(global ? null : new PersistenceScope(scope!), "owner\u0000\ud800", "operation\udfff");
        var native = new WorkflowExecutableIdentity("native\ud800", "definition", "version", "1", "sha256:native");
        var model = native with { ArtifactId = "model\u0000", ArtifactHash = "sha256:model" };
        var template = new ExecutableActivityTemplateIdentity("template\udfff", "sha256:template");
        IReadOnlyList<WorkflowPreparationRoot> roots = templateOnly ? [new WorkflowPreparationTemplateRoot(template)] :
            [new WorkflowPreparationExecutableRoot(WorkflowPreparationExecutableKind.Native, native),
                new WorkflowPreparationExecutableRoot(WorkflowPreparationExecutableKind.Model, model), new WorkflowPreparationTemplateRoot(template)];
        IReadOnlyList<WorkflowPreparationMaterialPin> pins = templateOnly ? [new WorkflowPreparationTemplatePin(template, "template-row", "template-hash-claim", 12)] :
            [new WorkflowPreparationExecutablePin(WorkflowPreparationExecutableKind.Native, native, "native-row"),
                new WorkflowPreparationExecutablePin(WorkflowPreparationExecutableKind.Model, model, "model-row"),
                new WorkflowPreparationTemplatePin(template, "template-row", "template-hash-claim", 12)];
        var selection = new WorkflowPreparationSelection("snapshot", "source-fingerprint", "source-profile", "compiler-profile",
            "prepared-profile", "prepared-envelope-fingerprint", roots, pins, "physical-selection-fingerprint");
        var owner = new WorkflowPreparationOwnerStamp(id, "ledger-incarnation", 3, "Prepared", "request-fingerprint",
            selection.SnapshotId, selection.SourceFingerprint, selection.SourceProfile, selection.CompilerProfile,
            selection.PreparedProfile, selection.PreparedContentFingerprint);
        return new(id, selection, owner, WorkflowPreparationState.Retained,
            new(id, "retention-incarnation", 1, selection.Fingerprint), null, null);
    }

    private sealed class PreparationConsumerContext(DbContextOptions<PreparationConsumerContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder modelBuilder) { }
    }
}
