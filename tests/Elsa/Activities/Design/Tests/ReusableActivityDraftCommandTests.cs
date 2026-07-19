using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Tests.Fixtures;
using Elsa.Primitives.Contracts;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ReusableActivityDraftCommandTests
{
    [Fact]
    public async Task Create_definition_persists_design_authority_and_initial_draft_atomically()
    {
        var harness = new Harness();

        var result = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);

        Assert.Equal("tenant-a", result.Definition.TenantId);
        Assert.Equal(ActivityContentAuthorityKind.Design, result.Definition.ContentAuthority.Kind);
        Assert.Equal(1, Assert.Single(result.Drafts).Revision);
        Assert.Single(harness.Stores.Definitions);
        Assert.Single(await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync(result.Definition.DefinitionId));
        Assert.NotNull(await harness.Stores.FindDraftLayoutAsync(result.Drafts[0].DraftId));
    }

    [Fact]
    public async Task Update_definition_replaces_only_presentation_metadata()
    {
        var harness = new Harness();
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var before = Assert.Single(harness.Stores.Definitions);

        var result = await harness.Service.UpdateDefinitionAsync(new(
            created.Definition.DefinitionId,
            "Finance",
            "Calculate invoice total",
            "Updated description"), default);

        var persisted = Assert.Single(harness.Stores.Definitions);
        Assert.Equal("Finance", result.Definition.Category);
        Assert.Equal("Calculate invoice total", result.Definition.DisplayName);
        Assert.Equal("Updated description", result.Definition.Description);
        Assert.Equal(before.ActivityTypeKey, persisted.ActivityTypeKey);
        Assert.Equal(before.TenantId, persisted.TenantId);
        Assert.Equal(created.Definition.ContentAuthority, result.Definition.ContentAuthority);
        Assert.Equal(created.Definition.HeadVersionId, result.Definition.HeadVersionId);
        Assert.Equal(created.Drafts, result.Drafts);
    }

    [Theory]
    [InlineData("", "Display")]
    [InlineData("   ", "Display")]
    [InlineData("Category", "")]
    [InlineData("Category", "   ")]
    public async Task Update_definition_rejects_blank_required_presentation_metadata(string category, string displayName)
    {
        var harness = new Harness();
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            harness.Service.UpdateDefinitionAsync(new(created.Definition.DefinitionId, category, displayName, null), default));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("activity.request.invalid", exception.ErrorCode);
        Assert.Equal("Orders", Assert.Single(harness.Stores.Definitions).Category);
    }

    [Fact]
    public async Task Update_definition_requires_design_authority()
    {
        var harness = new Harness();
        var source = await harness.SeedDefinitionAsync(ActivityContentAuthorityKind.ProviderSource);

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            harness.Service.UpdateDefinitionAsync(new(source.DefinitionId, "Updated", "Updated", null), default));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("activity.definition.content-authority", exception.ErrorCode);
        Assert.Equal("Seed", harness.Stores.Definitions.Single(x => x.Id == source.DefinitionId).Category);
    }

    [Fact]
    public async Task Update_definition_requires_tenant_visibility()
    {
        var stores = new InMemoryReusableActivityStores();
        var owner = new Harness(tenantId: "tenant-owner", stores: stores);
        var caller = new Harness(tenantId: "tenant-caller", stores: stores);
        var source = await owner.SeedDefinitionAsync(ActivityContentAuthorityKind.Design);

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            caller.Service.UpdateDefinitionAsync(new(source.DefinitionId, "Updated", "Updated", null), default));

        Assert.Equal(403, exception.StatusCode);
        Assert.Equal("activity.tenant.reference-denied", exception.ErrorCode);
        Assert.Equal("Seed", stores.Definitions.Single(x => x.Id == source.DefinitionId).Category);
    }

    [Fact]
    public async Task Definition_key_uniqueness_is_tenant_scoped_and_global_authoring_is_explicit()
    {
        var stores = new InMemoryReusableActivityStores();
        var tenantA = new Harness(tenantId: "tenant-a", stores: stores);
        var tenantB = new Harness(tenantId: "tenant-b", stores: stores);
        var global = new Harness(tenantId: null, stores: stores);

        var first = await tenantA.Service.CreateDefinitionAsync(tenantA.CreateDefinitionCommand(), default);
        var second = await tenantB.Service.CreateDefinitionAsync(tenantB.CreateDefinitionCommand(), default);
        var globalResult = await global.Service.CreateDefinitionAsync(global.CreateDefinitionCommand(), default);

        Assert.Equal("tenant-a", first.Definition.TenantId);
        Assert.Equal("tenant-b", second.Definition.TenantId);
        Assert.Null(globalResult.Definition.TenantId);
        Assert.Equal(3, stores.Definitions.Count);
    }

    [Fact]
    public async Task Draft_read_omits_provider_payload_without_provider_author_permission()
    {
        var harness = new Harness(canReadProviderPayload: false);
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);

        var view = await harness.Service.GetDraftViewAsync(created.Drafts[0].DraftId, default);
        var persisted = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(created.Drafts[0].DraftId);

        Assert.Null(view.Provider.Payload);
        Assert.Equal(42, persisted!.State.Provider.Payload.GetProperty("secret").GetInt32());
    }

    [Fact]
    public async Task Replace_is_full_state_atomic_and_stale_revision_has_safe_metadata()
    {
        var harness = new Harness();
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var draftId = created.Drafts[0].DraftId;
        var replacementProvider = Harness.Manifest("{\"replacement\":true}");
        var replacementLayout = new[] { new ActivityLayoutRecord("root", Harness.Json("{\"x\":10}")) };

        var replaced = await harness.Service.ReplaceDraftAsync(
            new(draftId, 1, Harness.Contract("replacement").ToView(), replacementProvider, replacementLayout),
            default);
        var stale = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Service.ReplaceDraftAsync(
            new(draftId, 1, Harness.Contract("stale").ToView(), Harness.Manifest("{}"), []),
            default));

        Assert.Equal(2, replaced.Revision);
        Assert.Equal("replacement", replaced.Contract.ContractSchemaVersion);
        Assert.Equal("activity.draft.stale-revision", stale.ErrorCode);
        var diagnostic = Assert.Single(stale.Diagnostics);
        Assert.Equal("1", diagnostic.Metadata!["expectedRevision"]);
        Assert.Equal("2", diagnostic.Metadata["actualRevision"]);
        var persisted = await harness.Service.GetDraftViewAsync(draftId, default);
        Assert.Equal("replacement", persisted.Contract.ContractSchemaVersion);
        Assert.Equal(10, persisted.Layout[0].Data.GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task Full_state_replace_preserves_provider_neutral_draft_options()
    {
        var harness = new Harness();
        var seeded = await harness.SeedDefinitionAsync(
            ActivityContentAuthorityKind.Design,
            new Dictionary<string, string> { ["mode"] = "strict" });
        var draft = Assert.Single(await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync(seeded.DefinitionId));

        await harness.Service.ReplaceDraftAsync(new(draft.Id, 1, Harness.Contract().ToView(), Harness.Manifest("{}"), []), default);

        var persisted = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(draft.Id);
        Assert.Equal("strict", persisted!.State.Options["mode"]);
    }

    [Fact]
    public async Task Source_owned_definition_rejects_draft_mutation_with_content_authority_code()
    {
        var harness = new Harness();
        var source = await harness.SeedDefinitionAsync(ActivityContentAuthorityKind.ProviderSource);

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Service.CreateDraftAsync(
            new(source.DefinitionId, null, Harness.Manifest("{}"), Harness.Contract().ToView(), []),
            default));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("activity.definition.content-authority", exception.ErrorCode);
        Assert.Single(await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync(source.DefinitionId));
    }

    [Fact]
    public async Task Clone_uses_exact_version_contract_manifest_and_layout_without_mutating_source()
    {
        var harness = new Harness();
        var source = await harness.SeedDefinitionAsync(ActivityContentAuthorityKind.Design);
        harness.SeedVersion(source.DefinitionId, "version-1", Harness.Manifest("{\"source\":true}"), Harness.Contract("source"));

        var clone = await harness.Service.CreateDraftAsync(new(source.DefinitionId, "version-1"), default);

        Assert.Equal("version-1", clone.SourceVersionId);
        Assert.Equal("source", clone.Contract.ContractSchemaVersion);
        Assert.True(clone.Provider.Payload!.Value.GetProperty("source").GetBoolean());
        Assert.Equal("source-node", Assert.Single(clone.Layout).NodeId);
        var publication = await ((IActivityDefinitionVersionPublicationStore)harness.Stores).FindAsync("version-1");
        Assert.True(publication!.Provider.Payload.GetProperty("source").GetBoolean());
    }

    [Fact]
    public async Task Source_owned_fork_creates_new_design_identity_with_exact_audit_provenance()
    {
        var harness = new Harness();
        var source = await harness.SeedDefinitionAsync(ActivityContentAuthorityKind.ProviderSource);
        harness.SeedVersion(source.DefinitionId, "source-version", Harness.Manifest("{\"source\":true}"), Harness.Contract("source"));

        var fork = await harness.Service.ForkDefinitionAsync(new(
            source.DefinitionId,
            "source-version",
            "acme.forked",
            "Custom",
            "Forked",
            null,
            "target.provider",
            "1"), default);

        Assert.NotEqual(source.DefinitionId, fork.Definition.DefinitionId);
        Assert.Equal(ActivityContentAuthorityKind.Design, fork.Definition.ContentAuthority.Kind);
        Assert.Equal(WellKnownActivityContentAuthorities.Design, fork.Definition.ContentAuthority.AuthorityKey);
        Assert.Equal(new(source.DefinitionId, "source-version", "1.0.0"), fork.Definition.ForkedFrom);
        Assert.Equal("source-version", Assert.Single(fork.Drafts).SourceVersionId);
        Assert.Equal(ActivityContentAuthorityKind.ProviderSource, (await harness.Stores.FindAsync(source.DefinitionId))!.ContentAuthority.Kind);
    }

    [Fact]
    public async Task Provider_migration_creates_a_new_draft_and_preserves_source_revision_and_original()
    {
        var harness = new Harness();
        var source = await harness.SeedDefinitionAsync(ActivityContentAuthorityKind.Design);
        harness.SeedVersion(source.DefinitionId, "source-version", Harness.Manifest("{\"source\":true}"), Harness.Contract("source"));
        var clone = await harness.Service.CreateDraftAsync(new(source.DefinitionId, "source-version"), default);

        var migrated = await harness.Service.MigrateDraftAsync(new(
            clone.DraftId,
            clone.Revision,
            "target.provider",
            "1"), default);

        Assert.NotEqual(clone.DraftId, migrated.DraftId);
        Assert.Equal("source-version", migrated.SourceVersionId);
        Assert.Equal("target.provider", migrated.Provider.ProviderKey);
        Assert.Equal(clone.Contract.ContractSchemaVersion, migrated.Contract.ContractSchemaVersion);
        Assert.Equal(clone.Contract.Outcomes.Select(x => x.ReferenceKey), migrated.Contract.Outcomes.Select(x => x.ReferenceKey));
        Assert.Equal(ActivityDefinitionDraftStatus.Active, (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(clone.DraftId))!.Status);
        Assert.Equal("elsa.activity-graph", (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(clone.DraftId))!.State.Provider.ProviderKey);
    }

    [Fact]
    public async Task Validation_findings_are_the_result_and_are_stored_for_the_exact_revision()
    {
        var harness = new Harness(invalidValidation: true);
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var draftId = created.Drafts[0].DraftId;

        var result = await harness.Service.ValidateDraftAsync(new(draftId, 1), default);

        Assert.False(result.IsValid);
        Assert.Equal("activity.test.invalid", Assert.Single(result.Diagnostics).Code);
        var persisted = await ((IActivityDraftValidationStore)harness.Stores).FindAsync(draftId, 1);
        Assert.NotNull(persisted);
        Assert.False(persisted!.IsValid);
    }

    [Fact]
    public async Task Definition_read_uses_activity_type_key_when_an_adopted_definition_has_no_display_name()
    {
        var harness = new Harness();
        var adopted = await harness.SeedDefinitionAsync(ActivityContentAuthorityKind.ProviderSource, displayName: null);

        var view = await harness.Service.GetDefinitionAsync(adopted.DefinitionId, default);

        Assert.Equal($"seed.{adopted.DefinitionId}", view.Definition.DisplayName);
    }

    [Fact]
    public async Task Discard_requires_the_exact_active_revision()
    {
        var harness = new Harness();
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var draftId = created.Drafts[0].DraftId;

        await harness.Service.DiscardDraftAsync(new(draftId, 1), default);
        var discarded = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(draftId);
        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() =>
            harness.Service.DiscardDraftAsync(new(draftId, 1), default));

        Assert.Equal(ActivityDefinitionDraftStatus.Discarded, discarded!.Status);
        Assert.Equal(2, discarded.Revision);
        Assert.Equal("activity.draft.stale-revision", exception.ErrorCode);
    }

    private sealed class Harness
    {
        private readonly FixedIdentityGenerator _ids;
        private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        private readonly TestAuthoringContext _context;

        public Harness(
            bool invalidValidation = false,
            string? tenantId = "tenant-a",
            InMemoryReusableActivityStores? stores = null,
            bool canReadProviderPayload = true)
        {
            _ids = new(tenantId ?? "global");
            _context = new(tenantId, canReadProviderPayload);
            Stores = stores ?? new();
            var registry = new ActivityProviderRegistry([new MigratingProvider("elsa.activity-graph"), new MigratingProvider("target.provider")]);
            Service = new(
                Stores,
                Stores,
                Stores,
                Stores,
                Stores,
                Stores,
                Stores,
                Stores,
                Stores,
                Stores,
                Stores,
                Stores,
                registry,
                new StubValidator(_time, invalidValidation),
                _ids,
                _time,
                _context);
        }

        public InMemoryReusableActivityStores Stores { get; }
        public ReusableActivityAuthoringService Service { get; }

        public CreateReusableActivityDefinition CreateDefinitionCommand() => new(
            "acme.calculate",
            "Orders",
            "Calculate",
            null,
            Manifest("{\"secret\":42}"),
            Contract().ToView(),
            [new("root", Json("{\"x\":1}"))]);

        public async Task<ActivityDefinitionAuthoringState> SeedDefinitionAsync(
            ActivityContentAuthorityKind authority,
            IReadOnlyDictionary<string, string>? options = null,
            string? displayName = "Seed")
        {
            var now = _time.GetUtcNow();
            var definitionId = $"seed-definition-{_ids.Generate()}";
            var draftId = $"seed-draft-{_ids.Generate()}";
            var definition = new ActivityDefinition
            {
                Id = definitionId,
                TenantId = _context.TenantId,
                ActivityTypeKey = $"seed.{definitionId}",
                Category = "Seed",
                DisplayName = displayName,
                CreatedAt = now,
                LastModifiedAt = now
            };
            var authoring = new ActivityDefinitionAuthoringState
            {
                Id = $"authoring-{_ids.Generate()}",
                TenantId = _context.TenantId,
                DefinitionId = definitionId,
                ContentAuthority = new(authority, authority == ActivityContentAuthorityKind.Design ? WellKnownActivityContentAuthorities.Design : "source.provider"),
                CreatedAt = now,
                LastModifiedAt = now
            };
            var draft = new ActivityDefinitionDraft
            {
                Id = draftId,
                TenantId = _context.TenantId,
                DefinitionId = definitionId,
                Revision = 1,
                State = new(Contract(), Manifest("{}"), options ?? new Dictionary<string, string>()),
                CreatedAt = now,
                LastModifiedAt = now
            };
            var layout = new ActivityDefinitionDraftLayout
            {
                Id = $"layout-{_ids.Generate()}",
                TenantId = _context.TenantId,
                DraftId = draftId,
                Revision = 1,
                CreatedAt = now,
                LastModifiedAt = now
            };
            await Stores.ExecuteAsync(new CreateActivityDefinitionRequest(definition, authoring, draft, layout));
            return authoring;
        }

        public void SeedVersion(string definitionId, string versionId, ActivityProviderManifest manifest, ActivityContract contract)
        {
            var now = _time.GetUtcNow();
            Stores.SeedPublication(new()
            {
                Id = $"publication-{versionId}",
                TenantId = _context.TenantId,
                DefinitionVersionId = versionId,
                DefinitionId = definitionId,
                Version = "1.0.0",
                Contract = contract,
                Provider = manifest,
                TemplateId = $"template-{versionId}",
                TemplateHash = $"sha256-{versionId}",
                SourceReferenceId = $"source-reference-{versionId}",
                ProviderFingerprint = "provider/1",
                DirectDependencyCount = 0,
                ClosedTemplateCount = 1,
                RuntimeRequirements = [new("elsa.graph-activity", "1")],
                PublishedAt = now,
                CreatedAt = now,
                LastModifiedAt = now
            }, new()
            {
                Id = $"version-layout-{versionId}",
                TenantId = _context.TenantId,
                DefinitionVersionId = versionId,
                Records = [new("source-node", Json("{\"x\":7}"))],
                CreatedAt = now,
                LastModifiedAt = now
            });
        }

        public static ActivityContract Contract(string schema = "1") => new(
            schema,
            [],
            [],
            [new("done", "Done", true)]);

        public static ActivityProviderManifest Manifest(string json) => new("elsa.activity-graph", "1", Json(json));

        public static JsonElement Json(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }

    private sealed class TestAuthoringContext(string? tenantId, bool canReadProviderPayload) : IActivityAuthoringContext
    {
        public string? TenantId => tenantId;
        public bool CanAuthorProvider(string providerKey) => true;
        public bool CanReadProviderPayload(string providerKey) => canReadProviderPayload;
    }

    private sealed class FixedIdentityGenerator(string prefix) : IIdentityGenerator
    {
        private int _current;
        public string Generate() => $"{prefix}-{++_current}";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubValidator(TimeProvider timeProvider, bool invalid) : IActivityDraftValidator
    {
        public ValueTask<ActivityDraftValidation> ValidateAsync(ActivityDraftValidationRequest request, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ActivityDiagnostic> diagnostics = invalid
                ? [new("activity.test.invalid", ActivityDiagnosticSeverity.Error, "Invalid for test.", new("ActivityDraft", request.DraftId), Metadata: new Dictionary<string, string>())]
                : [];
            return ValueTask.FromResult(new ActivityDraftValidation(request.DraftId, request.Revision, !invalid, timeProvider.GetUtcNow(), diagnostics));
        }
    }

    private sealed class MigratingProvider(string key) : IActivityProvider
    {
        public string ProviderKey => key;
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };

        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderManifest manifest, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityContractProposal(Harness.Contract(), []));

        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);

        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityManifestMigration(new(ProviderKey, request.TargetSchemaVersion, request.Source.Payload.Clone()), []));
    }
}
