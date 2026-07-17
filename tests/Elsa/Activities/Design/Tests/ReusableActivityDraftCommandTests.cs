using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Services;
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
        Assert.Equal("elsa.user.calculate.activity-def-tenant-a-1", result.Definition.ActivityTypeKey);
        Assert.Equal(1, result.Draft.Revision);
        Assert.Single(harness.Stores.Definitions);
        Assert.Single(await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync(result.Definition.DefinitionId));
        Assert.NotNull(await harness.Stores.FindDraftLayoutAsync(result.Draft.DraftId));
    }

    [Fact]
    public async Task Create_definition_persists_the_exact_normalized_activity_type_key_override()
    {
        var harness = new Harness();
        var command = harness.CreateDefinitionCommand() with
        {
            ActivityTypeKey = "  ELSA.USER.Invoice-Evaluator.Custom  "
        };

        var result = await harness.Service.CreateDefinitionAsync(command, default);

        Assert.Equal("elsa.user.invoice-evaluator.custom", result.Definition.ActivityTypeKey);
        Assert.Equal("elsa.user.invoice-evaluator.custom", Assert.Single(harness.Stores.Definitions).ActivityTypeKey);
    }

    [Fact]
    public async Task Create_definition_rejects_an_invalid_activity_type_key_override_without_writes()
    {
        var harness = new Harness();
        var command = harness.CreateDefinitionCommand() with { ActivityTypeKey = "elsa.other.invoice-evaluator.custom" };

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Service.CreateDefinitionAsync(command, default));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("activity.definition.key-invalid", exception.ErrorCode);
        Assert.Empty(harness.Stores.Definitions);
    }

    [Fact]
    public async Task Create_definition_rejects_an_override_when_the_active_key_policy_disallows_it()
    {
        var policy = new NonOverridableActivityTypeKeyPolicy();
        var harness = new Harness(typeKeyPolicy: policy);
        var command = harness.CreateDefinitionCommand() with { ActivityTypeKey = "elsa.user.invoice-evaluator.custom" };

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Service.CreateDefinitionAsync(command, default));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("activity.request.invalid", exception.ErrorCode);
        Assert.Equal(0, policy.NormalizeCalls);
        Assert.Empty(harness.Stores.Definitions);
    }

    [Fact]
    public async Task Create_definition_reports_the_existing_safe_conflict_without_suffixing_an_overridden_key()
    {
        var harness = new Harness();
        var command = harness.CreateDefinitionCommand() with { ActivityTypeKey = "elsa.user.invoice-evaluator.custom" };
        await harness.Service.CreateDefinitionAsync(command, default);

        var collision = command with { ActivityTypeKey = "  ELSA.USER.Invoice-Evaluator.Custom  " };
        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Service.CreateDefinitionAsync(collision, default));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("activity.definition.key-conflict", exception.ErrorCode);
        Assert.Equal("elsa.user.invoice-evaluator.custom", Assert.Single(harness.Stores.Definitions).ActivityTypeKey);
    }

    [Fact]
    public async Task Create_definition_rejects_contract_types_outside_capability_catalog_without_writes()
    {
        var harness = new Harness();
        var contract = new ActivityContract(
            "1",
            [new("order", "Order", new("acme.order", Elsa.Primitives.Models.CollectionKind.Single), true, null, "elsa.json")],
            [],
            []);
        var command = harness.CreateDefinitionCommand() with { Contract = contract.ToView() };

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Service.CreateDefinitionAsync(command, default));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("activity.contract.capability-rejected", exception.ErrorCode);
        Assert.Contains(exception.Diagnostics, x => x.Code == "activity.contract.type-unavailable");
        Assert.Empty(harness.Stores.Definitions);
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
        Assert.Equal("Finance", result.Category);
        Assert.Equal("Calculate invoice total", result.DisplayName);
        Assert.Equal("Updated description", result.Description);
        Assert.Equal(before.ActivityTypeKey, persisted.ActivityTypeKey);
        Assert.Equal(before.TenantId, persisted.TenantId);
        Assert.Equal(created.Definition.ContentAuthority, result.ContentAuthority);
        Assert.Equal(created.Definition.HeadVersionId, result.HeadVersionId);
        var unchangedDraft = Assert.Single(await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync(created.Definition.DefinitionId));
        Assert.Equal(created.Draft.DraftId, unchangedDraft.Id);
        Assert.Equal(created.Draft.Revision, unchangedDraft.Revision);
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

        const string activityTypeKey = "elsa.user.shared-activity.custom";
        var first = await tenantA.Service.CreateDefinitionAsync(tenantA.CreateDefinitionCommand() with { ActivityTypeKey = activityTypeKey }, default);
        var second = await tenantB.Service.CreateDefinitionAsync(tenantB.CreateDefinitionCommand() with { ActivityTypeKey = activityTypeKey }, default);
        var globalResult = await global.Service.CreateDefinitionAsync(global.CreateDefinitionCommand() with { ActivityTypeKey = activityTypeKey }, default);

        Assert.Equal("tenant-a", first.Definition.TenantId);
        Assert.Equal("tenant-b", second.Definition.TenantId);
        Assert.Null(globalResult.Definition.TenantId);
        Assert.All([first, second, globalResult], result => Assert.Equal(activityTypeKey, result.Definition.ActivityTypeKey));
        Assert.Equal(3, stores.Definitions.Count);
    }

    [Fact]
    public async Task Draft_read_omits_provider_payload_without_provider_author_permission()
    {
        var harness = new Harness(canReadProviderPayload: false);
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);

        var view = await harness.Service.GetDraftViewAsync(created.Draft.DraftId, default);
        var persisted = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(created.Draft.DraftId);

        Assert.Null(view.Provider.Payload);
        Assert.Equal(42, persisted!.State.Provider.Payload.GetProperty("secret").GetInt32());
    }

    [Fact]
    public async Task Hidden_draft_and_version_reads_are_indistinguishable_from_missing_resources()
    {
        var stores = new InMemoryReusableActivityStores();
        var owner = new Harness(tenantId: "tenant-owner", stores: stores);
        var caller = new Harness(tenantId: "tenant-caller", stores: stores);
        var seeded = await owner.SeedDefinitionAsync(ActivityContentAuthorityKind.Design);
        var draft = Assert.Single(await ((IActivityDefinitionDraftStore)stores).ListByDefinitionAsync(seeded.DefinitionId));
        owner.SeedVersion(seeded.DefinitionId, "version-hidden", Harness.Manifest("{}"), Harness.Contract());

        var hiddenDraft = await Assert.ThrowsAsync<ActivityAuthoringException>(() => caller.Service.GetDraftViewAsync(draft.Id, default));
        var missingDraft = await Assert.ThrowsAsync<ActivityAuthoringException>(() => caller.Service.GetDraftViewAsync("missing-draft", default));
        var hiddenVersion = await Assert.ThrowsAsync<ActivityAuthoringException>(() => caller.Service.GetVersionAsync("version-hidden", default));
        var missingVersion = await Assert.ThrowsAsync<ActivityAuthoringException>(() => caller.Service.GetVersionAsync("missing-version", default));

        Assert.Equal((missingDraft.StatusCode, missingDraft.ErrorCode, missingDraft.Title, missingDraft.Message),
            (hiddenDraft.StatusCode, hiddenDraft.ErrorCode, hiddenDraft.Title, hiddenDraft.Message));
        Assert.Equal((missingVersion.StatusCode, missingVersion.ErrorCode, missingVersion.Title, missingVersion.Message),
            (hiddenVersion.StatusCode, hiddenVersion.ErrorCode, hiddenVersion.Title, hiddenVersion.Message));
    }

    [Fact]
    public async Task Immutable_style_draft_read_preserves_unavailable_historical_type_facts()
    {
        var historicalContract = new ActivityContract(
            "legacy-contract",
            [new("legacy", "Legacy", new("retired.alias", Elsa.Primitives.Models.CollectionKind.HashSet), false, null, "retired.driver")],
            [],
            []);
        var harness = new Harness();
        var seeded = await harness.SeedDefinitionAsync(ActivityContentAuthorityKind.Design, contract: historicalContract);
        var draft = Assert.Single(await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync(seeded.DefinitionId));

        var view = await harness.Service.GetDraftViewAsync(draft.Id, default);

        var input = Assert.Single(view.Contract.Inputs);
        Assert.Equal("retired.alias", input.Type.Alias);
        Assert.Equal("HashSet", input.Type.CollectionKind);
        Assert.Equal("retired.driver", input.StorageDriverKey);
    }

    [Fact]
    public async Task Replace_is_full_state_atomic_and_stale_revision_has_safe_metadata()
    {
        var harness = new Harness();
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var draftId = created.Draft.DraftId;
        var replacementProvider = Harness.Manifest("{\"replacement\":true}");
        var replacementLayout = new[] { new ActivityLayoutRecord("root", Harness.Json("{\"x\":10}")) };

        var replaced = await harness.Service.ReplaceDraftAsync(
            new(draftId, 1, Harness.Contract("replacement").ToView(), replacementProvider, replacementLayout, "Review candidate"),
            default);
        var stale = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Service.ReplaceDraftAsync(
            new(draftId, 1, Harness.Contract("stale").ToView(), Harness.Manifest("{}"), []),
            default));

        Assert.Equal(2, replaced.Revision);
        Assert.Equal("replacement", replaced.Contract.ContractSchemaVersion);
        Assert.Equal("Review candidate", replaced.PresentationLabel);
        Assert.Equal("activity.draft.stale-revision", stale.ErrorCode);
        Assert.Equal(2, stale.Recovery?.CurrentRevision);
        Assert.Equal("activity-draft-conflict-copies", stale.Recovery?.Relation);
        Assert.Equal($"design/activities/drafts/{draftId}/conflict-copies", stale.Recovery?.Href);
        var diagnostic = Assert.Single(stale.Diagnostics);
        Assert.Equal("1", diagnostic.Metadata!["expectedRevision"]);
        Assert.Equal("2", diagnostic.Metadata["actualRevision"]);
        var persisted = await harness.Service.GetDraftViewAsync(draftId, default);
        Assert.Equal("replacement", persisted.Contract.ContractSchemaVersion);
        Assert.Equal("Review candidate", persisted.PresentationLabel);
        Assert.Equal(10, persisted.Layout[0].Data.GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task Conflict_copy_uses_submitted_full_state_preserves_server_lineage_and_rechecks_source_revision()
    {
        var harness = new Harness();
        var seeded = await harness.SeedDefinitionAsync(
            ActivityContentAuthorityKind.Design,
            new Dictionary<string, string> { ["mode"] = "strict" });
        var source = Assert.Single(await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync(seeded.DefinitionId));

        var copy = await harness.Service.CreateConflictCopyAsync(new(
            source.Id,
            1,
            Harness.Contract("recovered").ToView(),
            Harness.Manifest("{\"local\":true}"),
            [new("root", Harness.Json("{\"x\":42}"))],
            "Recovered local work"), default);

        Assert.NotEqual(source.Id, copy.DraftId);
        Assert.Equal(source.DefinitionId, copy.DefinitionId);
        Assert.Equal(source.SourceVersionId, copy.SourceVersionId);
        Assert.Equal(1, copy.Revision);
        Assert.Equal("Recovered local work", copy.PresentationLabel);
        Assert.Equal("recovered", copy.Contract.ContractSchemaVersion);
        Assert.Equal(42, copy.Layout.Single().Data.GetProperty("x").GetInt32());
        var persistedCopy = await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(copy.DraftId);
        Assert.Equal("strict", persistedCopy!.State.Options["mode"]);
        Assert.Equal(1, (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(source.Id))!.Revision);

        await harness.Service.UpdateDraftPresentationAsync(new(source.Id, 1, "Server changed"), default);
        var before = (await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync(source.DefinitionId)).Count;
        var stale = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Service.CreateConflictCopyAsync(new(
            source.Id,
            1,
            Harness.Contract("stale").ToView(),
            Harness.Manifest("{}"),
            [],
            "Must not persist"), default));

        Assert.Equal("activity.draft.stale-revision", stale.ErrorCode);
        Assert.Equal(before, (await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync(source.DefinitionId)).Count);
    }

    [Fact]
    public async Task Replace_rejects_contract_types_outside_capability_catalog_without_revision_change()
    {
        var harness = new Harness();
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var draftId = created.Draft.DraftId;
        var contract = new ActivityContract(
            "1",
            [],
            [new("result", "Result", new("acme.unknown", Elsa.Primitives.Models.CollectionKind.List), false, "unknown.driver")],
            []);

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Service.ReplaceDraftAsync(
            new(draftId, 1, contract.ToView(), Harness.Manifest("{}"), []), default));

        Assert.Equal("activity.contract.capability-rejected", exception.ErrorCode);
        Assert.Contains(exception.Diagnostics, x => x.Code == "activity.contract.type-unavailable");
        Assert.Equal(1, (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(draftId))!.Revision);
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
    public async Task Contract_proposal_is_exact_read_only_and_provider_neutral()
    {
        var provider = new ProposalProvider(_ => new(
            [new("outcome:approved", ActivityContractProposalOperation.Add, ActivityContractMemberKind.Outcome, "approved", Outcome: new("approved", "Approved", true))],
            []));
        var harness = new Harness(provider: provider);
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var draft = await harness.Service.GetDraftViewAsync(created.Draft.DraftId, default);

        var proposal = await harness.Proposals.ProposeAsync(new(
            draft.DraftId,
            draft.Revision,
            draft.Provider.ProviderKey,
            draft.Provider.SchemaVersion,
            draft.Provider.ManifestFingerprint), default);

        var change = Assert.Single(proposal.Changes);
        Assert.Equal("outcome:approved", change.ChangeId);
        Assert.Equal("Approved", change.Outcome!.Name);
        Assert.StartsWith("sha256:", proposal.ProposalFingerprint, StringComparison.Ordinal);
        Assert.Equal(1, provider.ProposalCalls);
        Assert.Equal(1, (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(draft.DraftId))!.Revision);
    }

    [Fact]
    public async Task Applying_exact_selected_proposal_changes_only_contract_and_revision()
    {
        var provider = new ProposalProvider(_ => new(
            [new("outcome:approved", ActivityContractProposalOperation.Add, ActivityContractMemberKind.Outcome, "approved", Outcome: new("approved", "Approved", true))],
            []));
        var harness = new Harness(provider: provider);
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var before = await harness.Service.GetDraftViewAsync(created.Draft.DraftId, default);
        var proposal = await harness.Proposals.ProposeAsync(new(
            before.DraftId,
            before.Revision,
            before.Provider.ProviderKey,
            before.Provider.SchemaVersion,
            before.Provider.ManifestFingerprint), default);

        var applied = await harness.Proposals.ApplyAsync(new(
            before.DraftId,
            before.Revision,
            before.Provider.ProviderKey,
            before.Provider.SchemaVersion,
            before.Provider.ManifestFingerprint,
            proposal.ProposalFingerprint,
            ["outcome:approved"]), default);

        Assert.Equal(2, applied.Revision);
        Assert.Contains(applied.Contract.Outcomes, x => x.ReferenceKey == "approved");
        Assert.Equal(before.Layout, applied.Layout);
        Assert.Equal(before.Provider, applied.Provider);
    }

    [Fact]
    public async Task Stale_proposal_binding_fails_before_provider_invocation()
    {
        var provider = new ProposalProvider(_ => new([], []));
        var harness = new Harness(provider: provider);
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var draft = await harness.Service.GetDraftViewAsync(created.Draft.DraftId, default);

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Proposals.ProposeAsync(new(
            draft.DraftId,
            draft.Revision + 1,
            draft.Provider.ProviderKey,
            draft.Provider.SchemaVersion,
            draft.Provider.ManifestFingerprint), default));

        Assert.Equal("activity.contract.proposal-stale", exception.ErrorCode);
        Assert.Equal(0, provider.ProposalCalls);
    }

    [Fact]
    public async Task Contract_proposal_requires_provider_authorization_before_invocation()
    {
        var stores = new InMemoryReusableActivityStores();
        var provider = new ProposalProvider(_ => new([], []));
        var owner = new Harness(stores: stores, provider: provider);
        var created = await owner.Service.CreateDefinitionAsync(owner.CreateDefinitionCommand(), default);
        var draft = await owner.Service.GetDraftViewAsync(created.Draft.DraftId, default);
        var caller = new Harness(stores: stores, provider: provider, canAuthorProvider: false);

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => caller.Proposals.ProposeAsync(new(
            draft.DraftId,
            draft.Revision,
            draft.Provider.ProviderKey,
            draft.Provider.SchemaVersion,
            draft.Provider.ManifestFingerprint), default));

        Assert.Equal(403, exception.StatusCode);
        Assert.Equal("activity.authorization.denied", exception.ErrorCode);
        Assert.Equal(0, provider.ProposalCalls);
    }

    [Fact]
    public async Task Contract_proposal_rejects_an_unavailable_exact_provider_schema()
    {
        var harness = new Harness();
        var seeded = await harness.SeedDefinitionAsync(
            ActivityContentAuthorityKind.Design,
            manifest: new("elsa.activity-graph", "missing", Harness.Json("{}")));
        var draft = Assert.Single(await ((IActivityDefinitionDraftStore)harness.Stores).ListByDefinitionAsync(seeded.DefinitionId));

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Proposals.ProposeAsync(new(
            draft.Id,
            draft.Revision,
            draft.State.Provider.ProviderKey,
            draft.State.Provider.SchemaVersion,
            ActivityProviderManifestFingerprint.Compute(draft.State.Provider)), default));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("activity.provider.schema-unavailable", exception.ErrorCode);
    }

    [Fact]
    public async Task Stale_proposal_fingerprint_fails_closed_without_write()
    {
        var provider = new ProposalProvider(_ => new(
            [new("outcome:approved", ActivityContractProposalOperation.Add, ActivityContractMemberKind.Outcome, "approved", Outcome: new("approved", "Approved", true))],
            []));
        var harness = new Harness(provider: provider);
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var draft = await harness.Service.GetDraftViewAsync(created.Draft.DraftId, default);

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Proposals.ApplyAsync(new(
            draft.DraftId,
            draft.Revision,
            draft.Provider.ProviderKey,
            draft.Provider.SchemaVersion,
            draft.Provider.ManifestFingerprint,
            "sha256:stale",
            ["outcome:approved"]), default));

        Assert.Equal("activity.contract.proposal-stale", exception.ErrorCode);
        Assert.Equal(1, (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(draft.DraftId))!.Revision);
    }

    [Fact]
    public async Task Changed_review_diagnostics_make_an_otherwise_identical_proposal_stale()
    {
        var phase = 0;
        var provider = new ProposalProvider(_ => new(
            [new("outcome:approved", ActivityContractProposalOperation.Add, ActivityContractMemberKind.Outcome, "approved", Outcome: new("approved", "Approved", true))],
            [new($"activity.test.warning.{++phase}", ActivityDiagnosticSeverity.Warning, "Review warning.", new("ActivityDraft", "draft"))]));
        var harness = new Harness(provider: provider);
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var draft = await harness.Service.GetDraftViewAsync(created.Draft.DraftId, default);
        var proposal = await harness.Proposals.ProposeAsync(new(
            draft.DraftId,
            draft.Revision,
            draft.Provider.ProviderKey,
            draft.Provider.SchemaVersion,
            draft.Provider.ManifestFingerprint), default);

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Proposals.ApplyAsync(new(
            draft.DraftId,
            draft.Revision,
            draft.Provider.ProviderKey,
            draft.Provider.SchemaVersion,
            draft.Provider.ManifestFingerprint,
            proposal.ProposalFingerprint,
            ["outcome:approved"]), default));

        Assert.Equal("activity.contract.proposal-stale", exception.ErrorCode);
        Assert.Equal(1, (await ((IActivityDefinitionDraftStore)harness.Stores).FindAsync(draft.DraftId))!.Revision);
    }

    [Fact]
    public async Task Malformed_provider_proposal_is_safely_rejected()
    {
        var provider = new ProposalProvider(_ => new(
            [new("bad", ActivityContractProposalOperation.Add, ActivityContractMemberKind.Outcome, "approved", Input: new("input", "Input", new("System.String", Elsa.Primitives.Models.CollectionKind.Single), false, null, "elsa.json"))],
            []));
        var harness = new Harness(provider: provider);
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var draft = await harness.Service.GetDraftViewAsync(created.Draft.DraftId, default);

        var exception = await Assert.ThrowsAsync<ActivityAuthoringException>(() => harness.Proposals.ProposeAsync(new(
            draft.DraftId,
            draft.Revision,
            draft.Provider.ProviderKey,
            draft.Provider.SchemaVersion,
            draft.Provider.ManifestFingerprint), default));

        Assert.Equal(502, exception.StatusCode);
        Assert.Equal("activity.provider.proposal-invalid", exception.ErrorCode);
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
            "Custom",
            "Forked",
            null,
            "target.provider",
            "1"), default);

        Assert.NotEqual(source.DefinitionId, fork.Definition.DefinitionId);
        Assert.Equal(ActivityContentAuthorityKind.Design, fork.Definition.ContentAuthority.Kind);
        Assert.Equal(WellKnownActivityContentAuthorities.Design, fork.Definition.ContentAuthority.AuthorityKey);
        Assert.Equal(new(source.DefinitionId, "source-version", "1.0.0"), fork.Definition.ForkedFrom);
        Assert.Equal("source-version", fork.Draft.SourceVersionId);
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
        var draftId = created.Draft.DraftId;

        var result = await harness.Service.ValidateDraftAsync(new(draftId, 1), default);

        Assert.False(result.IsValid);
        Assert.Equal("activity.test.invalid", Assert.Single(result.Diagnostics).Code);
        var persisted = await ((IActivityDraftValidationStore)harness.Stores).FindAsync(draftId, 1);
        Assert.NotNull(persisted);
        Assert.False(persisted!.IsValid);
    }

    [Fact]
    public async Task Discard_requires_the_exact_active_revision()
    {
        var harness = new Harness();
        var created = await harness.Service.CreateDefinitionAsync(harness.CreateDefinitionCommand(), default);
        var draftId = created.Draft.DraftId;

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
            bool canReadProviderPayload = true,
            IActivityProvider? provider = null,
            bool canAuthorProvider = true,
            IActivityTypeKeyPolicy? typeKeyPolicy = null)
        {
            _ids = new(tenantId ?? "global");
            _context = new(tenantId, canReadProviderPayload, canAuthorProvider);
            Stores = stores ?? new();
            var registry = new ActivityProviderRegistry([provider ?? new MigratingProvider("elsa.activity-graph"), new MigratingProvider("target.provider")]);
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
                Stores,
                Stores,
                registry,
                new StubValidator(_time, invalidValidation),
                new ActivityContractAuthoringValidator(new EmptyCapabilityCatalog()),
                typeKeyPolicy ?? new DefaultActivityTypeKeyPolicy(),
                _ids,
                _time,
                _context);
            Proposals = new(Stores, Stores, registry, Stores, new ActivityContractAuthoringValidator(new EmptyCapabilityCatalog()), Service, _context);
        }

        public InMemoryReusableActivityStores Stores { get; }
        public ReusableActivityAuthoringService Service { get; }
        public ActivityContractProposalService Proposals { get; }

        public CreateReusableActivityDefinition CreateDefinitionCommand() => new(
            "Orders",
            "Calculate",
            null,
            Manifest("{\"secret\":42}"),
            Contract().ToView(),
            [new("root", Json("{\"x\":1}"))]);

        public async Task<ActivityDefinitionAuthoringState> SeedDefinitionAsync(
            ActivityContentAuthorityKind authority,
            IReadOnlyDictionary<string, string>? options = null,
            string? displayName = "Seed",
            ActivityContract? contract = null,
            ActivityProviderManifest? manifest = null)
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
                State = new(contract ?? Contract(), manifest ?? Manifest("{}"), options ?? new Dictionary<string, string>()),
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

    private sealed class TestAuthoringContext(string? tenantId, bool canReadProviderPayload, bool canAuthorProvider) : IActivityAuthoringContext
    {
        public string? TenantId => tenantId;
        public string AuthorizationProfile => $"{tenantId ?? "global"}/{canReadProviderPayload}/{canAuthorProvider}";
        public bool CanAuthorProvider(string providerKey) => canAuthorProvider;
        public bool CanReadProviderPayload(string providerKey) => canReadProviderPayload;
    }

    private sealed class EmptyCapabilityCatalog : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types => [];
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

    private sealed class NonOverridableActivityTypeKeyPolicy : IActivityTypeKeyPolicy
    {
        public int NormalizeCalls { get; private set; }
        public ActivityTypeKeyRules Rules { get; } = new(
            ServerGenerated: true,
            AllowsPreCreationOverride: false,
            Immutable: true,
            "elsa.user",
            "^elsa\\.user\\.[a-z0-9]+\\.[a-z0-9]+$",
            160,
            "tenantId + activityTypeKey");

        public string Generate(string displayName, string definitionId) => $"elsa.user.generated.{definitionId}";

        public string NormalizeAndValidateOverride(string activityTypeKey)
        {
            NormalizeCalls++;
            throw new InvalidOperationException("Override normalization must not be called.");
        }
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
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new(
            key,
            [new("1", true, new HashSet<string> { "1" })],
            new([]));

        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityContractProposal([], []));

        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);

        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityManifestMigration(new(ProviderKey, request.TargetSchemaVersion, request.Source.Payload.Clone()), []));
    }

    private sealed class ProposalProvider(Func<ActivityProviderContractProposalRequest, ActivityContractProposal> propose) : IActivityProvider
    {
        public int ProposalCalls { get; private set; }
        public string ProviderKey => "elsa.activity-graph";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new(
            "Test proposal provider",
            [new("1", true, new HashSet<string> { "1" })],
            new([]));

        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default)
        {
            ProposalCalls++;
            return ValueTask.FromResult(propose(request));
        }

        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);

        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityManifestMigration(new(ProviderKey, request.TargetSchemaVersion, request.Source.Payload.Clone()), []));
    }
}
