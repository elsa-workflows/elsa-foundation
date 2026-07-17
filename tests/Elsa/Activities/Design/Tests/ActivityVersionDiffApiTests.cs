using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Tests.Fixtures;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityVersionDiffApiTests
{
    [Fact]
    public async Task Draft_preview_uses_the_exact_head_and_safe_compiled_candidate_facts()
    {
        var stores = new InMemoryReusableActivityStores();
        var compiler = new StubCandidateCompiler(new(
            "sha256-candidate",
            "provider/2",
            12,
            3,
            [new("elsa.graph-activity", "1")],
            [],
            [],
            []));
        var draft = await SeedDraftAsync(stores, "definition-1", "draft-1", "version-1");
        SeedVersion(stores, "definition-1", "version-1", Contract(), [new("root", Json("{\"x\":1}"))]);
        var service = Service(stores, compiler);

        var result = await service.PreviewDraftAsync(draft.Id, 1, null, default);

        Assert.Equal("version-1", result.From.VersionId);
        Assert.Equal("ActivityDraft", result.To.Kind);
        Assert.Equal("sha256-candidate", result.To.TemplateHash);
        Assert.Equal("version-1", compiler.Request!.BaseVersionId);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task Failed_candidate_compilation_returns_contract_changes_and_safe_diagnostics()
    {
        var stores = new InMemoryReusableActivityStores();
        var diagnostic = new ActivityDiagnostic(
            "activity.provider.compilation-failed",
            ActivityDiagnosticSeverity.Error,
            "Candidate compilation failed.",
            new("ActivityDraft", "draft-1", "definition-1", Revision: 1),
            Metadata: new Dictionary<string, string>());
        var compiler = new StubCandidateCompiler(new(null, null, null, null, [], [], [], [diagnostic]));
        var draft = await SeedDraftAsync(stores, "definition-1", "draft-1", null, Contract(inputs: [Input("added")]));

        var result = await Service(stores, compiler).PreviewDraftAsync(draft.Id, 1, null, default);

        Assert.Equal("ActivityDefinitionBaseline", result.From.Kind);
        Assert.Null(result.From.VersionId);
        Assert.Null(result.To.TemplateHash);
        Assert.Contains(result.Changes, x => x.ChangeId == "contract:input:added:member-added");
        Assert.Equal("activity.provider.compilation-failed", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task Immutable_comparison_rejects_cross_definition_versions()
    {
        var stores = new InMemoryReusableActivityStores();
        SeedVersion(stores, "definition-1", "version-1", Contract(), []);
        SeedVersion(stores, "definition-2", "version-2", Contract(), []);

        var exception = await Assert.ThrowsAsync<Elsa.Activities.Design.Api.Models.ActivityAuthoringException>(() =>
            Service(stores).CompareVersionsAsync("version-1", "version-2", default));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("activity.request.invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Layout_hash_is_stable_across_record_order_and_json_property_order()
    {
        var stores = new InMemoryReusableActivityStores();
        SeedVersion(stores, "definition-1", "version-1", Contract(),
            [new("b", Json("{\"z\":2,\"a\":1}")), new("a", Json("[1,2]"))]);
        SeedVersion(stores, "definition-1", "version-2", Contract(),
            [new("a", Json("[1,2]")), new("b", Json("{\"a\":1,\"z\":2}"))]);

        var result = await Service(stores).CompareVersionsAsync("version-1", "version-2", default);

        Assert.DoesNotContain(result.Changes, x => x.Kind == "LayoutChanged");
    }

    [Fact]
    public async Task Draft_preview_rejects_a_stale_revision_with_safe_metadata()
    {
        var stores = new InMemoryReusableActivityStores();
        var draft = await SeedDraftAsync(stores, "definition-1", "draft-1", null);

        var exception = await Assert.ThrowsAsync<Elsa.Activities.Design.Api.Models.ActivityAuthoringException>(() =>
            Service(stores).PreviewDraftAsync(draft.Id, 0, null, default));

        Assert.Equal("activity.draft.stale-revision", exception.ErrorCode);
        var metadata = Assert.Single(exception.Diagnostics).Metadata!;
        Assert.Equal("0", metadata["expectedRevision"]);
        Assert.Equal("1", metadata["actualRevision"]);
    }

    private static ActivityVersionDiffService Service(
        InMemoryReusableActivityStores stores,
        IActivityDraftDiffCandidateCompiler? compiler = null) => new(
        stores,
        stores,
        stores,
        stores,
        stores,
        new ActivityVersionDiffer(),
        compiler ?? new StubCandidateCompiler(new("sha256-candidate", "provider/1", 1, 0, [], [], [], [])),
        new TestAuthoringContext());

    private static async Task<ActivityDefinitionDraft> SeedDraftAsync(
        InMemoryReusableActivityStores stores,
        string definitionId,
        string draftId,
        string? headVersionId,
        ActivityContract? contract = null)
    {
        var now = DateTimeOffset.Parse("2026-07-15T12:00:00Z");
        var definition = new Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinition
        {
            Id = definitionId,
            TenantId = "tenant-a",
            ActivityTypeKey = $"acme.{definitionId}",
            Category = "Tests",
            DisplayName = definitionId,
            CreatedAt = now,
            LastModifiedAt = now
        };
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = $"authoring-{definitionId}",
            TenantId = "tenant-a",
            DefinitionId = definitionId,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design),
            HeadVersionId = headVersionId,
            CreatedAt = now,
            LastModifiedAt = now
        };
        var draft = new ActivityDefinitionDraft
        {
            Id = draftId,
            TenantId = "tenant-a",
            DefinitionId = definitionId,
            Revision = 1,
            State = new(contract ?? Contract(), Manifest(), new Dictionary<string, string>()),
            CreatedAt = now,
            LastModifiedAt = now
        };
        var layout = new ActivityDefinitionDraftLayout
        {
            Id = $"layout-{draftId}",
            TenantId = "tenant-a",
            DraftId = draftId,
            Revision = 1,
            Records = [new("root", Json("{\"x\":1}"))],
            CreatedAt = now,
            LastModifiedAt = now
        };
        await stores.ExecuteAsync(new CreateActivityDefinitionRequest(definition, authoring, draft, layout));
        return draft;
    }

    private static void SeedVersion(
        InMemoryReusableActivityStores stores,
        string definitionId,
        string versionId,
        ActivityContract contract,
        IReadOnlyList<ActivityLayoutRecord> layout)
    {
        var now = DateTimeOffset.Parse("2026-07-15T12:00:00Z");
        stores.SeedPublication(new()
        {
            Id = $"publication-{versionId}",
            TenantId = "tenant-a",
            DefinitionVersionId = versionId,
            DefinitionId = definitionId,
            Version = versionId == "version-1" ? "1.0.0" : "2.0.0",
            Contract = contract,
            Provider = Manifest(),
            TemplateId = "template-same",
            TemplateHash = "sha256-same",
            SourceReferenceId = $"reference-{versionId}",
            ProviderFingerprint = "provider/1",
            DirectDependencyCount = 0,
            ClosedTemplateCount = 1,
            RuntimeRequirements = [],
            ResourceMeasurements = new(2, 2, 0, 1, 10, 10, 1),
            ResumeTargetCount = 1,
            PublishedAt = now,
            CreatedAt = now,
            LastModifiedAt = now
        }, new()
        {
            Id = $"layout-{versionId}",
            TenantId = "tenant-a",
            DefinitionVersionId = versionId,
            Records = layout.ToList(),
            CreatedAt = now,
            LastModifiedAt = now
        });
    }

    private static ActivityContract Contract(IReadOnlyList<ActivityInputContract>? inputs = null) =>
        new("1", inputs ?? [], [], []);

    private static ActivityInputContract Input(string key) =>
        new(key, key, new("string", Elsa.Primitives.Models.CollectionKind.Single), false, null, "elsa.json");

    private static ActivityProviderManifest Manifest() => new("elsa.activity-graph", "1", Json("{}"));

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class StubCandidateCompiler(ActivityDraftDiffCandidate result) : IActivityDraftDiffCandidateCompiler
    {
        public ActivityDraftDiffCandidateRequest? Request { get; private set; }

        public ValueTask<ActivityDraftDiffCandidate> CompileAsync(ActivityDraftDiffCandidateRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestAuthoringContext : IActivityAuthoringContext
    {
        public string? TenantId => "tenant-a";
        public string AuthorizationProfile => "tenant-a/read";
        public bool CanAuthorProvider(string providerKey) => true;
        public bool CanReadProviderPayload(string providerKey) => false;
    }
}
