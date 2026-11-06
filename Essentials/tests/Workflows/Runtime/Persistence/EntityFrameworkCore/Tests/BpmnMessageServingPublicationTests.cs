using System.Text.Json;
using System.Data.Common;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Authored actual SQLite source/material/slot switches; no model driver/input/delivery capability.</summary>
public sealed class BpmnMessageServingPublicationTests
{
    [Fact]
    public async Task Receive_only_authored_model_serves_from_canonical_slot_before_any_model_run_or_wait_exists()
    {
        await using var f = await Fixture.Create(); var model = f.Model(messageStart: false);
        var result = await f.Publish(model, "definition", "v1", "publish", 0);
        Assert.True(result.Activation.Succeeded);
        Assert.Equal("definition", result.Activation.Slot.WorkflowDefinitionId);
        Assert.Equal(["Finish"], (await f.Interests.ObservePublicationAsync(f.Scope)).Names);
        Assert.Empty(await f.Context.Set<RuntimeModelExecutionEntity>().AsNoTracking().ToArrayAsync());
        var root = MessagePublicationInterestDocument.Read(await f.Context.MessagePublicationInterests.AsNoTracking().SingleAsync(), Fixture.ScopeKey,
            result.Activation.Slot.ActiveActivationId!);
        Assert.True(root.Descriptor.IsModel); Assert.Equal("definition", root.Descriptor.Source.DefinitionId);
        Assert.NotEqual(root.Descriptor.Source.DefinitionId, result.Material.Executable.DefinitionId);
        var source = (await f.References.FindAsync(result.Material.SourceReferenceId))!;
        Assert.Equal(WorkflowActivationReferenceIdentity.Create(source.ActivationId!), source.SourceReferenceId);
        Assert.Equal(WorkflowActivationSlotIdentity.Create("definition", "default"), source.SlotId);
        Assert.Equal("v1", source.DefinitionVersionId); Assert.Equal(BpmnMessageServingPublicationBridge.SourceKind, source.SourceKind);
        Assert.Throws<NotSupportedException>(() => f.Interests.RequireCompleteAcceptanceCoverage());
        Assert.Throws<Elsa.Activities.Bpmn.Exceptions.BpmnModelValidationException>(() => BpmnDirectExecutionDriver.ValidateLegacyInterestMaterial(model));
    }
    [Fact]
    public async Task Actual_native_model_native_replacements_keep_one_WorkflowDefinition_slot_and_retire_prior_sources()
    {
        await using var f = await Fixture.Create(); var native1 = await f.Native("native1", "native-v1", 0);
        Assert.True(native1.Succeeded); Assert.Equal(["Native"], (await f.Interests.ObservePublicationAsync(f.Scope)).Names);
        var model = await f.Publish(f.Model(), "definition", "model-v2", "model", native1.Slot.Revision);
        Assert.True(model.Activation.Succeeded); Assert.Equal(native1.Slot.SlotId, model.Activation.Slot.SlotId);
        Assert.NotNull((await f.References.FindAsync(native1.Reference!.SourceReferenceId))!.DeletedAt);
        Assert.Equal(["Finish", "Open"], (await f.Interests.ObservePublicationAsync(f.Scope)).Names);
        var native3 = await f.Native("native3", "native-v3", model.Activation.Slot.Revision);
        Assert.True(native3.Succeeded); Assert.Equal(native1.Slot.SlotId, native3.Slot.SlotId);
        Assert.NotNull((await f.References.FindAsync(model.Material.SourceReferenceId))!.DeletedAt);
        Assert.Equal(["Native"], (await f.Interests.ObservePublicationAsync(f.Scope)).Names);
        var oldModelUnpublish = await f.Bridge.DeactivateAsync(model.Material.SourceReferenceId, "default", native3.Slot.Revision, f.Caller);
        Assert.False(oldModelUnpublish.Succeeded); Assert.Equal(native3.Slot.ActiveActivationId, (await f.Authority.FindAsync("definition", "default"))!.ActiveActivationId);
    }
    [Fact]
    public async Task Same_model_content_published_under_different_authored_definitions_uses_distinct_canonical_owners()
    {
        await using var f = await Fixture.Create(); var model = f.Model();
        var first = await f.Publish(model, "definition", "v1", "one", 0);
        var second = await f.Publish(model, "other\ud800", "other-version", "two", 0);
        Assert.True(first.Activation.Succeeded); Assert.True(second.Activation.Succeeded);
        Assert.Equal(first.Material.Executable.ArtifactId, second.Material.Executable.ArtifactId);
        Assert.NotEqual(first.Activation.Slot.SlotId, second.Activation.Slot.SlotId);
        var roots = await f.Context.MessagePublicationInterests.AsNoTracking().ToArrayAsync(); Assert.Equal(2, roots.Length);
        var owners = roots.Select(row => MessagePublicationInterestDocument.Read(row, Fixture.ScopeKey, EfRelationalIdentity.Decode(row.ActivationId)).Descriptor.Source.DefinitionId).ToArray();
        Assert.Contains("definition", owners); Assert.Contains("other\ud800", owners);
    }
    [Fact]
    public async Task Explicit_stale_CAS_does_not_create_a_source_or_replace_the_current_publication()
    {
        await using var f = await Fixture.Create(); var first = await f.Native("native", "v1", 0); Assert.True(first.Succeeded);
        var candidate = f.Model(); await f.Material.SaveModelAsync(candidate); var before = await f.Snapshot();
        var result = await f.Publish(candidate, "definition", "v2", "stale", 0);
        Assert.False(result.Activation.Succeeded); Assert.Equal(WorkflowActivationOutcome.Conflict, result.Activation.Outcome);
        Assert.Equal(before, await f.Snapshot());
    }
    [Theory]
    [InlineData("artifact")]
    [InlineData("version")]
    [InlineData("slot")]
    public async Task Model_preparation_rechecks_actual_canonical_source_facts_instead_of_echoing_requested_provenance(string field)
    {
        await using var f = await Fixture.Create(); var model = f.Model(); var reference = await f.SaveModelSource(model, "unserved");
        var requested = field switch { "artifact" => reference with { ArtifactId = "different" },
            "version" => reference with { DefinitionVersionId = "different" }, _ => reference with { SlotId = "different" } };
        var before = await f.Snapshot(); await Assert.ThrowsAsync<NotSupportedException>(() => f.Interests.PrepareModelAsync(model, requested, "default", default).AsTask());
        Assert.Equal(before, await f.Snapshot()); Assert.Empty((await f.Interests.ObservePublicationAsync(f.Scope)).Names);
    }
    [Theory]
    [InlineData("hash")]
    [InlineData("different-behavior-same-id")]
    public async Task Already_active_request_still_checks_exact_physical_model_hash_before_returning_success(string variant)
    {
        await using var f = await Fixture.Create(); var model = f.Model(); var published = await f.Publish(model, "definition", "v1", "same", 0);
        var source = (await f.References.FindAsync(published.Material.SourceReferenceId))!;
        var other = variant == "hash" ? model : f.Model(catchName: "Other");
        var identity = other.Identity with { ArtifactId = model.Identity.ArtifactId, ArtifactHash = variant == "hash" ? "different" : other.Identity.ArtifactHash };
        var changed = new RuntimeModelMaterial(1, identity, other.Protocol, other.Profile, other.Definition, other.InputContract, other.Outputs, other.Dependencies, other.RuntimeRequirements);
        var before = await f.Snapshot(); await Assert.ThrowsAsync<NotSupportedException>(() => f.Models.ActivateAsync(new(changed, source, "default", 0, WorkflowActivationSource.Publishing)).AsTask());
        Assert.Equal(before, await f.Snapshot());
    }
    [Fact]
    public async Task Same_request_replay_uses_canonical_slot_and_preserves_incarnation_epoch_and_reference_count()
    {
        await using var f = await Fixture.Create(); var model = f.Model(); var first = await f.Publish(model, "definition", "v1", "same", 0);
        var before = await f.Snapshot(); var second = await f.Publish(model, "definition", "v1", "same", 0);
        Assert.True(second.Activation.Succeeded); Assert.Equal(WorkflowActivationOutcome.AlreadyActive, second.Activation.Outcome);
        Assert.Equal(first.Material.IncarnationId, second.Material.IncarnationId); Assert.Equal(before, await f.Snapshot());
    }
    [Theory]
    [InlineData("clock")]
    [InlineData("access")]
    [InlineData("material")]
    [InlineData("bindings")]
    public async Task Claimed_equivalent_holders_cannot_substitute_the_actual_selected_domain_runtime_tuple(string alias)
    {
        await using var f = await Fixture.Create(); await using var other = f.Open(); var before = await f.Snapshot();
        if (alias == "bindings")
            Assert.Throws<NotSupportedException>(() => f.Interests.CreateModelCoordinator(f.Switch, f.Leases, new EfWorkflowTriggerBindingStore(f.Context, f.Access)));
        else
        {
            var bridge = new BpmnMessageServingPublicationBridge(alias == "material" ? new EfWorkflowExecutableStore(other, f.Access) : f.Material,
                f.References, alias == "access" ? new Access() : f.Access, alias == "clock" ? new OtherClock() : f.Clock, f.Models);
            await Assert.ThrowsAsync<NotSupportedException>(() => bridge.PublishAsync(f.Model(), BpmnModelPublicationProvenance.ForAuthoredDefinition("definition", "v1", "1"),
                "wrong", f.Caller, "default", 0).AsTask());
        }
        Assert.Equal(before, await f.Snapshot());
    }
    [Fact]
    public async Task Missing_model_strategy_refuses_and_compensates_prepared_native_projections_without_serving_any_slot()
    {
        await using var f = await Fixture.Create(includeModelProvider: false);
        await Assert.ThrowsAsync<Elsa.Workflows.Runtime.Core.Exceptions.RuntimeSchedulerWorkUnavailableException>(() => f.Publish(f.Model(), "definition", "v1", "unsupported", 0));
        Assert.Null((await f.Authority.FindAsync("definition", "default"))?.ActiveActivationId);
        var source = Assert.Single(await f.Context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
        Assert.NotNull((await f.References.FindAsync(EfRelationalIdentity.Decode(source.SourceReferenceId)))!.DeletedAt);
        Assert.Empty(await f.Context.MessagePublicationInterests.AsNoTracking().ToArrayAsync());
    }
    [Fact]
    public async Task Actual_slot_save_failure_rolls_back_the_model_switch_and_compensates_its_preparation()
    {
        await using var f = await Fixture.Create(); f.Failure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Publish(f.Model(), "definition", "v1", "failed", 0));
        f.Failure.Enabled = false; Assert.Null((await f.Authority.FindAsync("definition", "default"))?.ActiveActivationId);
        Assert.Empty((await f.Interests.ObservePublicationAsync(f.Scope)).Names); Assert.Equal(0, (await f.Interests.ObservePublicationAsync(f.Scope)).Epoch);
        var row = Assert.Single(await f.Context.MessagePublicationInterests.AsNoTracking().ToArrayAsync());
        Assert.Equal(MessagePublicationInterestDocument.Closed, row.State);
    }
    [Fact]
    public async Task Native_descriptor_explicitly_records_its_kind_and_missing_prototype_flag_or_model_kind_cannot_bypass_checks()
    {
        await using var f = await Fixture.Create(); var native = await f.Native("native", "v1", 0); Assert.True(native.Succeeded);
        var row = await f.Context.MessagePublicationInterests.AsNoTracking().SingleAsync();
        var root = MessagePublicationInterestDocument.Read(row, Fixture.ScopeKey, native.Slot.ActiveActivationId!); Assert.False(root.Descriptor.IsModel);
        Assert.False(JsonDocument.Parse(row.ContentJson).RootElement.GetProperty("descriptor").GetProperty("isModel").GetBoolean());
        var json = System.Text.Json.Nodes.JsonNode.Parse(row.ContentJson)!.AsObject(); json["descriptor"]!.AsObject().Remove("isModel"); row.ContentJson = json.ToJsonString();
        Assert.Throws<InvalidDataException>(() => MessagePublicationInterestDocument.Read(row, Fixture.ScopeKey, native.Slot.ActiveActivationId!));
    }
    [Fact]
    public async Task Model_descriptor_tag_is_independently_checked_against_actual_material_even_with_matching_recomputed_fingerprint()
    {
        await using var f = await Fixture.Create(); var published = await f.Publish(f.Model(), "definition", "v1", "model", 0);
        var row = await f.Context.MessagePublicationInterests.SingleAsync(); var root = MessagePublicationInterestDocument.Read(row, Fixture.ScopeKey, published.Activation.Slot.ActiveActivationId!);
        var changed = root.Descriptor with { IsModel = false }; row.ContentJson = RuntimeArtifactJson.Serialize(root with { Descriptor = changed });
        row.DescriptorFingerprint = MessagePublicationInterestDocument.Fingerprint(changed); await f.Context.SaveChangesAsync(); f.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Interests.FindStateAsync(published.Activation.Slot.ActiveActivationId!, default).AsTask());
    }
    [Fact]
    public async Task Same_activation_replay_cannot_claim_a_slot_owned_by_a_different_source()
    {
        await using var f = await Fixture.Create(); var model = f.Model(); var reference = await f.SaveModelSource(model, "foreign");
        var first = await f.Models.ActivateAsync(new(model, reference, "default", 0, WorkflowActivationSource.ArtifactReconciliation("owner"))); Assert.True(first.Succeeded);
        var before = await f.Snapshot();
        var wrongOwner = await f.Models.ActivateAsync(new(model, reference, "default", first.Slot.Revision, WorkflowActivationSource.Publishing));
        Assert.False(wrongOwner.Succeeded); Assert.Equal(WorkflowActivationConflict.ForeignSource, wrongOwner.Conflict); Assert.Equal(before, await f.Snapshot());
    }
    [Fact]
    public async Task Late_scope_drift_cannot_return_or_compensate_a_committed_publication_through_the_wrong_scope()
    {
        await using var f = await Fixture.Create(); f.Drift.Enabled = true;
        f.Drift.Change = () => f.Access.Current = PersistenceAccessContext.Scoped(new("changed-scope"));
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Publish(f.Model(), "definition", "v1", "same", 0));
        Assert.Equal(1, f.Drift.Changes); f.Drift.Enabled = false;
        f.Access.Current = PersistenceAccessContext.Scoped(new(Fixture.ScopeKey));
        var committed = (await f.Authority.FindAsync("definition", "default"))!; Assert.NotNull(committed.ActiveActivationId);
        var replay = await f.Publish(f.Model(), "definition", "v1", "same", 0);
        Assert.True(replay.Activation.Succeeded); Assert.Equal(WorkflowActivationOutcome.AlreadyActive, replay.Activation.Outcome);
        Assert.Equal(committed.Revision, replay.Activation.Slot.Revision); Assert.Equal(1, (await f.Interests.ObservePublicationAsync(f.Scope)).Epoch);
    }
    [Fact]
    public async Task New_serving_root_uses_trusted_authored_version_facts_without_relabeling_the_existing_FA7_resource()
    {
        await using var f = await Fixture.Create();
        var process = new BpmnProcessDefinition("legacy", Elements: [new("start", BpmnElementTypes.StartEvent), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("done", "start", "end")]);
        var legacyDefinition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), []);
        var provisional = new RuntimeModelMaterial(1, new("seed", "legacy-carrier", "legacy-carrier-version", "1.0.0", "seed"),
            new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), BpmnModelProtocol.Profile,
            JsonSerializer.SerializeToElement(legacyDefinition, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new(1, []), [], [], []);
        var hash = provisional.ComputeContentHash(); var pin = new WorkflowExecutableIdentity(hash, "bpmn-model:" + hash, hash, "1.0.0", hash);
        var legacy = new RuntimeModelMaterial(1, pin, provisional.Protocol, provisional.Profile, provisional.Definition, provisional.InputContract, [], [], []);
        BpmnDirectExecutionDriver.ValidateLegacyInterestMaterial(legacy); await f.Material.SaveModelAsync(legacy);
        var at = f.Clock.GetUtcNow(); var old = new WorkflowExecutableSourceReference("bpmn-source:old-resource", hash, "BpmnModel.Authored.v1",
            "authored-old-version", "7", "definition", pin.DefinitionVersionId, pin.ArtifactVersion, at, at, WorkflowExecutableReferenceScope.Published, TenantId: Fixture.ScopeKey);
        await f.Leases.ExecuteFencedAsync([pin], "legacy-resource", async (grants, token) =>
            _ = await f.References.SaveWithRootWriteLeasesAsync(old, pin, grants, token));
        var before = (await f.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync()).ContentJson;
        var published = await f.Publish(f.Model(), "definition", "authored-new-version", "new-serving-root", 0); Assert.True(published.Activation.Succeeded);
        var preserved = (await f.References.FindAsync(old.SourceReferenceId))!;
        Assert.Equal("BpmnModel.Authored.v1", preserved.SourceKind); Assert.Null(preserved.ActivationId); Assert.Null(preserved.SlotId);
        Assert.Equal(before, (await f.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync(row => row.SourceReferenceId == EfRelationalIdentity.Encode(old.SourceReferenceId) && row.ScopeKey == EfRelationalIdentity.Encode(Fixture.ScopeKey))).ContentJson);
        var serving = (await f.References.FindAsync(published.Material.SourceReferenceId))!;
        Assert.Equal("WorkflowDefinitionVersion", serving.SourceKind); Assert.Equal("definition", serving.DefinitionId);
        Assert.Equal("authored-new-version", serving.SourceId); Assert.Equal("authored-new-version", serving.DefinitionVersionId);
        Assert.Equal("carrier-definition", published.Material.Executable.DefinitionId); Assert.Equal("carrier-version", published.Material.Executable.DefinitionVersionId);
    }
    [Fact]
    public async Task Canonical_model_source_kind_cannot_pass_the_native_only_active_material_guard()
    {
        await using var f = await Fixture.Create(); var model = f.Model(); var publication = await f.Publish(model, "definition", "v1", "model", 0);
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "bpmn-native-guard-fixture-key-32-chars", AllowEphemeralDevelopmentKey = false }));
        var executions = new EfWorkflowExecutionStateStore(f.Context, f.Access, codec, f.Clock); var ownership = new EfExecutionLivenessStateStore(f.Context, f.Access, codec);
        var writer = new EfRuntimeCheckpointCommitStore(f.Context, f.Access, f.Clock, f.Leases);
        var active = await EfMessageActiveInterestStore.BindAsync(f.Context, writer, executions, ownership, f.References, f.Material, f.Interests, f.Access, f.Clock);
        var at = f.Clock.GetUtcNow(); await ownership.SaveAsync(new(RuntimeExecutionOwnershipStateId.For("fake-native"), "fake-native", new("lease", "fake-native", "owner", at, at.AddMinutes(5), 1), null, null, null));
        var source = (await f.References.FindAsync(publication.Material.SourceReferenceId))!;
        var state = new WorkflowExecutionState("fake-native", publication.Material.Executable, WorkflowExecutionStatus.Running, null, at, at, at, null, null, null, Fixture.ScopeKey, new Dictionary<string, string>())
            { RunKind = WorkflowRunKind.PublishedRun, PinnedSource = WorkflowExecutableSourceProvenance.From(source), Partition = new("partition"), Authority = new("runtime", "caller") };
        var commit = new RuntimeCheckpointCommit("fake-native", new("fake-native", "WorkflowStarted", "fake-native", at, [], new Dictionary<string, string>()),
            new(new("fake-native", RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>()), null, [], [], [], [], []), [], new Dictionary<string, string>()) { ExpectedFence = new("lease", "owner", 1) };
        await Assert.ThrowsAsync<NotSupportedException>(() => writer.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate, "fixture")).AsTask());
        Assert.Null(await executions.FindAsync("fake-native")); Assert.Empty(await f.Context.MessageActiveInterestsRows.AsNoTracking().ToArrayAsync());
        Assert.Equal(["Finish", "Open"], (await f.Interests.ObservePublicationAsync(f.Scope)).Names);
        Assert.Throws<NotSupportedException>(() => active.RequireCompleteAcceptanceCoverage());
    }
    [Fact]
    public async Task Own_failed_activation_retries_same_request_under_root_lease_and_preserves_source_incarnation()
    {
        await using var f = await Fixture.Create(); f.Failure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Publish(f.Model(), "definition", "v1", "retry", 0));
        f.Failure.Enabled = false;
        var failed = Assert.Single(await f.Context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
        var source = (await f.References.FindAsync(EfRelationalIdentity.Decode(failed.SourceReferenceId)))!;
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason, source.DeletedReason);
        var retried = await f.Publish(f.Model(), "definition", "v1", "retry", 0); Assert.True(retried.Activation.Succeeded);
        var current = Assert.Single(await f.Context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
        Assert.Equal(failed.IncarnationId, current.IncarnationId); Assert.Equal(source.CreatedAt, (await f.References.FindAsync(retried.Material.SourceReferenceId))!.CreatedAt);
        Assert.Equal(["Finish", "Open"], (await f.Interests.ObservePublicationAsync(f.Scope)).Names);
    }
    [Theory]
    [InlineData("unpublished")]
    [InlineData("activation-replaced")]
    public async Task Other_retirement_reasons_cannot_be_resumed_by_reusing_publication_request(string reason)
    {
        await using var f = await Fixture.Create(); var published = await f.Publish(f.Model(), "definition", "v1", "retired", 0);
        await f.References.RetireAsync(published.Material.SourceReferenceId, f.Clock.GetUtcNow(), reason);
        var before = await f.Snapshot();
        await Assert.ThrowsAsync<Elsa.Workflows.Runtime.Core.Exceptions.RuntimeCheckpointReplayConflictException>(() => f.Publish(f.Model(), "definition", "v1", "retired", 0));
        Assert.Equal(before, await f.Snapshot());
    }
    [Fact]
    public async Task Model_preparation_refuses_scope_drift_after_actual_source_read_before_cross_scope_material_can_be_used()
    {
        await using var f = await Fixture.Create(); var model = f.Model(); var source = await f.SaveModelSource(model, "drift-preparation", nullTenant: true);
        var first = (await f.Material.FindMaterialAsync(model.Identity.ArtifactId))!;
        f.Access.Current = PersistenceAccessContext.Scoped(new("tenant-b")); await f.Material.SaveModelAsync(model);
        var other = (await f.Material.FindMaterialAsync(model.Identity.ArtifactId))!; Assert.NotEqual(first.IncarnationId, other.IncarnationId);
        f.Access.Current = PersistenceAccessContext.Scoped(new(Fixture.ScopeKey)); f.SourceDrift.Change = () => f.Access.Current = PersistenceAccessContext.Scoped(new("tenant-b"));
        f.SourceDrift.Enabled = true;
        await Assert.ThrowsAsync<NotSupportedException>(() => f.Interests.PrepareModelAsync(model, source, "default", default).AsTask());
        Assert.Equal(1, f.SourceDrift.Changes); f.SourceDrift.Enabled = false;
        f.Access.Current = PersistenceAccessContext.Scoped(new(Fixture.ScopeKey));
        Assert.Empty(await f.Context.MessagePublicationInterests.AsNoTracking().ToArrayAsync());
        Assert.Empty(await f.Context.MessageInterestScopeControls.AsNoTracking().ToArrayAsync());
        await f.Interests.PrepareModelAsync(model, source, "default", default);
        var root = MessagePublicationInterestDocument.Read(Assert.Single(await f.Context.MessagePublicationInterests.AsNoTracking().ToArrayAsync()), Fixture.ScopeKey, "drift-preparation");
        Assert.Equal(first.IncarnationId, root.Descriptor.MaterialIncarnation);
    }
    private sealed class SourceReadDrift : DbCommandInterceptor
    {
        internal bool Enabled; internal Action? Change; internal int Changes;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains(RuntimeArtifactEfModule.SourceReferenceTableName, StringComparison.Ordinal))
            { Enabled = false; Changes++; Change!(); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        internal const string ScopeKey = "tenant-a"; private readonly TemporarySqliteDatabase database = new("bpmn-message-serving");
        internal readonly Access Access = new(); internal readonly TimeProvider Clock = TimeProvider.System; internal readonly Failure Failure = new(); internal readonly Drift Drift = new(); internal readonly SourceReadDrift SourceDrift = new();
        internal RuntimeSqliteDbContext Context = null!; internal EfWorkflowExecutableStore Material = null!; internal EfWorkflowExecutableSourceReferenceStore References = null!;
        internal EfWorkflowActivationAuthority Authority = null!; internal EfWorkflowTriggerBindingStore Bindings = null!; internal EfWorkflowActivationSwitch Switch = null!;
        internal EfMessagePublicationInterestStore Interests = null!; internal WorkflowExecutableRootWriteLeaseManager Leases = null!;
        internal MessageModelPublicationCoordinator Models = null!; internal BpmnMessageServingPublicationBridge Bridge = null!;
        internal BpmnModelCaller Caller => new("trusted-publisher", ScopeKey); internal MessageScope Scope => new(new(ScopeKey), ScopeKey);
        internal RuntimeSqliteDbContext Open() => new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(database.ConnectionString).AddInterceptors(Failure, Drift, SourceDrift).Options);
        internal static async Task<Fixture> Create(bool includeModelProvider = true)
        {
            var f = new Fixture(); f.Context = f.Open(); await f.Context.Database.EnsureCreatedAsync();
            var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "bpmn-publication-fixture-key-32-chars", AllowEphemeralDevelopmentKey = false }));
            f.Material = new(f.Context, f.Access); f.References = new(f.Context, f.Access, codec, f.Clock); f.Authority = new(f.Context, f.Access); f.Bindings = new(f.Context, f.Access);
            var providers = new List<IMessageMaterialInterestProvider> { new NativeProvider() }; if (includeModelProvider) providers.Add(new BpmnMessageMaterialInterestProvider());
            f.Interests = new(f.Context, f.Authority, f.References, f.Material, f.Access, providers, f.Clock);
            f.Switch = new(f.Interests, f.Authority, f.Bindings, f.References, f.Access, f.Clock);
            f.Leases = new(f.Material, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), f.Clock);
            f.Models = f.Interests.CreateModelCoordinator(f.Switch, f.Leases, f.Bindings);
            f.Bridge = new(f.Material, f.References, f.Access, f.Clock, f.Models); return f;
        }
        internal RuntimeModelMaterial Model(bool messageStart = true, string catchName = "Finish")
        {
            var process = new BpmnProcessDefinition("messages", Elements: [new("start", BpmnElementTypes.StartEvent,
                    eventDefinitions: messageStart ? [new(BpmnEventDefinitionTypes.Message, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = "Open" })] : []),
                new("receive", BpmnElementTypes.IntermediateCatchEvent, bindingRef: "receive-work", eventDefinitions: [new(BpmnEventDefinitionTypes.Message,
                    new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = catchName })]), new("end", BpmnElementTypes.EndEvent)],
                SequenceFlows: [new("to-receive", "start", "receive"), new("to-end", "receive", "end")]);
            var bindings = new List<BpmnMessageBinding> { new("receive", "receive-work", BpmnMessageBinding.Catch, catchName, BpmnMessageBinding.CapturedKey, new("Json")) };
            if (messageStart) bindings.Add(new("start", null, BpmnMessageBinding.Start, "Open", BpmnMessageBinding.AcceptedKey, new("Json")));
            return BpmnMessageMaterial.Create(process, "CorrelationKey", bindings, new("seed", "carrier-definition", "carrier-version", "carrier-label", "seed"));
        }
        internal async Task<BpmnMessageServingPublicationBridge.Publication> Publish(RuntimeModelMaterial model, string definition, string version, string request, long expected) =>
            await Bridge.PublishAsync(model, BpmnModelPublicationProvenance.ForAuthoredDefinition(definition, version, version), request, Caller, "default", expected);
        internal async Task<WorkflowActivationResult> Native(string artifact, string version, long expected)
        {
            var at = Clock.GetUtcNow(); var executable = new WorkflowExecutable(new(artifact, "definition", version, version, "hash:" + artifact),
                new ExecutableNode("native", "native", "Fixture.Native", "1", "fixture", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget>(), at, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
            await Material.SaveAsync(executable);
            var reference = new WorkflowExecutableSourceReference("ignored", artifact, "WorkflowDefinitionVersion", version, version, "definition", version, version, at, at, WorkflowExecutableReferenceScope.Published, TenantId: ScopeKey);
            var coordinator = Interests.CreateCoordinator(Switch, Leases, new EmptyIndexer(Bindings), Bindings);
            return await coordinator.ActivateAsync(new(executable, reference, "default", artifact, WorkflowActivationSource.Publishing, expected));
        }
        internal async Task<WorkflowExecutableSourceReference> SaveModelSource(RuntimeModelMaterial model, string activation, bool nullTenant = false)
        {
            await Material.SaveModelAsync(model); var snapshot = (await Material.FindMaterialAsync(model.Identity.ArtifactId))!; var at = Clock.GetUtcNow();
            var reference = new WorkflowExecutableSourceReference(WorkflowActivationReferenceIdentity.Create(activation), model.Identity.ArtifactId, "WorkflowDefinitionVersion", "v1", "1", "definition", "v1", "1", at, at,
                WorkflowExecutableReferenceScope.Published, ActivationId: activation, SlotId: WorkflowActivationSlotIdentity.Create("definition", "default"), TenantId: nullTenant ? null : ScopeKey);
            WorkflowExecutableSourceReference? result = null; await Leases.ExecuteFencedAsync([snapshot.Identity], "fixture:" + activation,
                async (grants, token) => result = await References.SaveWithRootWriteLeasesAsync(reference, snapshot.Identity, grants, token)); return result!;
        }
        internal async Task<string> Snapshot()
        {
            Context.ChangeTracker.Clear(); return RuntimeArtifactJson.Serialize(new {
                Slots = await Context.WorkflowActivationSlots.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync(),
                Roots = await Context.MessagePublicationInterests.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync(),
                Epochs = await Context.MessageInterestScopeControls.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync(),
                Sources = await Context.WorkflowExecutableSourceReferences.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync() });
        }
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await database.DisposeAsync(); }
    }
    private sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new(Fixture.ScopeKey)); }
    private sealed class OtherClock : TimeProvider { }
    private sealed class NativeProvider : IMessageMaterialInterestProvider
    {
        public string ProviderId => "fixture.native"; public RuntimeExecutionProtocol? ModelProtocol => null;
        public MessageMaterialInterestResult Describe(RuntimeExecutionMaterial material) => MessageMaterialInterestResult.Recognized([new("native", ["Native"])]);
    }
    private sealed class EmptyIndexer(IWorkflowTriggerBindingStore bindings) : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(WorkflowExecutable executable, string activationId, string slotId, CancellationToken cancellationToken = default)
        { await bindings.PrepareActivationAsync(activationId, [], cancellationToken); return []; }
    }
    private sealed class Drift : DbTransactionInterceptor
    {
        internal bool Enabled; internal Action? Change; internal int Changes;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken cancellationToken = default)
        {
            if (Enabled && data.Context?.ChangeTracker.Entries<WorkflowActivationSlotEntity>().Any() == true)
            { Changes++; Change!(); }
            return Task.CompletedTask;
        }
    }
    private sealed class Failure : SaveChangesInterceptor
    {
        internal bool Enabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && data.Context!.ChangeTracker.Entries<WorkflowActivationSlotEntity>().Any(entry => entry.State is EntityState.Added or EntityState.Modified))
                throw new InvalidOperationException("Injected model publication switch failure.");
            return ValueTask.FromResult(result);
        }
    }
}
