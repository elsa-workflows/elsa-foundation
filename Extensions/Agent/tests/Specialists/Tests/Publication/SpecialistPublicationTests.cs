using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Design;
using Elsa.Locking.Core;
using Elsa.Locking.FileSystem;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Models;
using Elsa.Secrets.Features;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Publication;

public sealed class SpecialistPublicationTests
{
    internal static async ValueTask<(ActivityTemplatePlacement Placement, IReadOnlyCollection<RuntimeStorageDriverRequirement> Storage)> PublishAndPlaceAsync(string outputName, string outputType, bool outputNullable = false, bool optionalBrief = false, RuntimeInputBinding? briefBinding = null, IReadOnlyDictionary<string, RuntimeOutputCapture>? outputCaptures = null, bool allowContinuity = false, bool invocationControls = false, string originKey = "workflow-v1", RuntimeInputBinding? optionsBinding = null, SpecialistTurnLimits? limits = null)
    {
        await using var fixture = await Fixture.CreateAsync(outputName, outputType, outputNullable, optionalBrief, allowContinuity, invocationControls, limits);
        Assert.Equal(ActivityPublicationReceiptStatus.Applied, (await fixture.PublishAsync()).Status);
        var publication = (await fixture.Publications.FindAsync("copywriter-v1"))!;
        var template = (await fixture.Templates.FindAsync(publication.TemplateId))!;
        var source = (await fixture.Sources.FindAsync(publication.SourceReferenceId))!;
        var types = fixture.Services.GetRequiredService<IWellKnownTypeRegistry>();
        types.RegisterType(typeof(string), "String");
        types.RegisterType(typeof(JsonElement), "System.Text.Json.JsonElement");
        var placer = new ActivityTemplatePlacer(fixture.Publications, fixture.Templates, fixture.Sources, new Sha256ActivityPlacementHasher(),
            new DefaultActivityStructureService([]), new RuntimeInputBindingCompiler(types), new RuntimeOutputCaptureCompiler(fixture.Drivers));
        var placement = await placer.PlaceAsync(new(publication, template, source,
            new([new(ActivityInvocationOriginSegmentKind.WorkflowRoot, originKey)]), publication.ActivityTypeKey,
            new Dictionary<string, RuntimeInputBinding>
            {
                ["brief-key"] = briefBinding ?? new("brief-key", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                    literal: optionalBrief ? ValueEnvelope.Absent(new("String"), ValueProtectionPolicy.InstanceInline) :
                        ValueEnvelope.Inline(new("String"), Json("\"moss gardens\""), ValueProtectionPolicy.InstanceInline))
            }.Concat(!invocationControls || !allowContinuity ? [] : new Dictionary<string, RuntimeInputBinding> { ["$specialist.options"] = optionsBinding ??
                new("$specialist.options", new("System.Text.Json.JsonElement"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Absent(new("System.Text.Json.JsonElement"), ValueProtectionPolicy.InstanceInline)) }).ToDictionary(x => x.Key, x => x.Value), outputCaptures ?? new Dictionary<string, RuntimeOutputCapture>()));
        return (placement, template.StorageDriverRequirements);
    }

    [Fact]
    public async Task Reviewed_publication_is_idempotent_and_real_placement_stamps_the_exact_specialist_version()
    {
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.PublishAsync();
        Assert.Equal(ActivityPublicationReceiptStatus.Applied, receipt.Status);
        var publication = await fixture.Publications.FindAsync("copywriter-v1");
        Assert.NotNull(publication);
        var template = await fixture.Templates.FindAsync(publication.TemplateId);
        Assert.NotNull(template);
        var source = await fixture.Sources.FindAsync(publication.SourceReferenceId);
        Assert.NotNull(source);
        var types = fixture.Services.GetRequiredService<IWellKnownTypeRegistry>();
        types.RegisterType(typeof(string), "String");
        var placer = new ActivityTemplatePlacer(fixture.Publications, fixture.Templates, fixture.Sources, new Sha256ActivityPlacementHasher(),
            new DefaultActivityStructureService([]), new RuntimeInputBindingCompiler(types), new RuntimeOutputCaptureCompiler(fixture.Drivers));
        var placed = await placer.PlaceAsync(new(publication, template, source,
            new([new(ActivityInvocationOriginSegmentKind.WorkflowRoot, "workflow-v1")]), publication.ActivityTypeKey,
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>()));
        Assert.Equal(publication.DefinitionVersionId, placed.Root.DescriptorPayload.GetProperty("definitionVersionId").GetString());
        Assert.Equal(template.TemplateHash, placed.Root.DescriptorPayload.GetProperty("templateHash").GetString());
        Assert.Equal(publication.SourceReferenceId, placed.Root.Metadata["activity.sourceReferenceId"]);
        var worker = Assert.Single(Assert.Single(placed.Root.ChildSlots).Activities);
        Assert.Equal(ActivityExecutionPlacement.Background, worker.ExecutionPlacement);
        Assert.NotEqual("model-turn", worker.ExecutableNodeId);
        Assert.Equal(SpecialistRuntimeIds.TurnCapability, worker.ActivityContract!.Activation.Capability);
        Assert.Equal(JsonValueKind.Null, template.Root.DescriptorPayload.GetProperty("definitionVersionId").ValueKind);
        var replay = await fixture.Publisher.PublishReviewedAsync(fixture.LastPublish!);
        Assert.Equal(receipt.Status, replay.Status);
        Assert.Equal(receipt.RequestFingerprint, replay.RequestFingerprint);
        Assert.Equal(receipt.Outcome, replay.Outcome);
        Assert.Single(await fixture.Design.ActivityDefinitionVersionPublications.ToListAsync());
    }

    [Fact]
    public async Task Constraint_changes_strengthen_the_actual_publication_version_floor()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(ActivityPublicationReceiptStatus.Applied, (await fixture.PublishAsync()).Status);
        var changed = fixture.Manifest with { ResultConstraints = new Dictionary<string, JsonElement> { ["article-key"] = Json("{\"maxLength\":10}") } };
        await fixture.CreateDraftAsync("second", changed, "copywriter-v1");
        var review = await fixture.Publisher.PreflightAsync(new("second", 1, "copywriter-v1"));
        Assert.True(review.HasBaseline);
        Assert.True(review.IsPublishable, JsonSerializer.Serialize(review.Diagnostics));
        Assert.Equal("2.0.0", review.MinimumVersion);
        Assert.Equal(ActivityVersionBump.Major, review.Diff!.RequiredBump);
        Assert.Contains(review.ImpactFirstChanges, x => x.Kind == "SpecialistJsonConstraintsChanged" && x.RequiredBump == ActivityVersionBump.Major);
        var insufficient = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => fixture.Publisher.PreflightAsync(new("second", 1, "copywriter-v1") { Version = "1.1.0" }));
        Assert.Contains(insufficient.Diagnostics, x => x.Code == "activity.version.bump-insufficient");
        Assert.Single(await fixture.Design.ActivityDefinitionVersionPublications.ToListAsync());
    }

    [Fact]
    public async Task Removing_native_conversation_metadata_requires_a_major_publication_version()
    {
        await using var fixture = await Fixture.CreateAsync(allowContinuity: true, invocationControls: true);
        Assert.Equal(ActivityPublicationReceiptStatus.Applied, (await fixture.PublishAsync()).Status);
        await fixture.CreateDraftAsync("second", fixture.Manifest with { AllowContinuity = false }, "copywriter-v1");
        var review = await fixture.Publisher.PreflightAsync(new("second", 1, "copywriter-v1"));
        Assert.True(review.IsPublishable, JsonSerializer.Serialize(review.Diagnostics));
        Assert.Equal("2.0.0", review.MinimumVersion);
        Assert.Contains(review.ImpactFirstChanges, x => x.Kind == "SpecialistInvocationContractChanged" && x.RequiredBump == ActivityVersionBump.Major);
    }

    [Fact]
    public async Task Prompt_changes_affect_behavior_hash_without_rewriting_the_published_artifact_or_forcing_a_schema_major()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(ActivityPublicationReceiptStatus.Applied, (await fixture.PublishAsync()).Status);
        var publication = (await fixture.Publications.FindAsync("copywriter-v1"))!;
        var original = (await fixture.Templates.FindAsync(publication.TemplateId))!;
        var changed = fixture.Manifest with { Instructions = "A changed private instruction." };
        await fixture.CreateDraftAsync("second", changed, "copywriter-v1");
        var secondDraft = (await ((IActivityDefinitionDraftStore)fixture.Stores).FindAsync("second"))!;
        var compiled = await fixture.Compiler.CompileAsync(new(await ((IActivityDefinitionStore)fixture.Stores).GetAsync("copywriter"), secondDraft, "copywriter-v1", "1.0.0", 0));
        Assert.True(compiled.IsSuccessful, JsonSerializer.Serialize(compiled.Diagnostics));
        Assert.NotEqual(original.TemplateHash, compiled.Template!.TemplateHash);
        Assert.Empty(compiled.ProviderCompatibilityChanges);
        var review = await fixture.Publisher.PreflightAsync(new("second", 1, "copywriter-v1") { Version = "1.1.0" });
        Assert.True(review.IsPublishable, JsonSerializer.Serialize(review.Diagnostics));
        Assert.Equal(ActivityVersionBump.Minor, review.Diff!.RequiredBump);
        Assert.Equal("Write the supplied brief.", (await fixture.Templates.FindAsync(publication.TemplateId))!.Root.DescriptorPayload.GetProperty("instructions").GetString());
    }

    [Theory]
    [InlineData("model")]
    [InlineData("schema")]
    public async Task Model_and_schema_changes_have_causal_template_hash_controls(string axis)
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(ActivityPublicationReceiptStatus.Applied, (await fixture.PublishAsync()).Status);
        var publication = (await fixture.Publications.FindAsync("copywriter-v1"))!;
        var original = (await fixture.Templates.FindAsync(publication.TemplateId))!;
        await fixture.CreateDraftAsync("unchanged", fixture.Manifest, "copywriter-v1");
        var baselineDraft = (await ((IActivityDefinitionDraftStore)fixture.Stores).FindAsync("unchanged"))!;
        var definition = await ((IActivityDefinitionStore)fixture.Stores).GetAsync("copywriter");
        var baseline = await fixture.Compiler.CompileAsync(new(definition, baselineDraft, "copywriter-v1", "1.0.0", 0));
        Assert.True(baseline.IsSuccessful, JsonSerializer.Serialize(baseline.Diagnostics));
        Assert.Equal(original.TemplateHash, baseline.Template!.TemplateHash);
        var changed = axis == "model"
            ? fixture.Manifest with { Model = fixture.Manifest.Model with { ModelId = "another-model" } }
            : fixture.Manifest with { ResultConstraints = new Dictionary<string, JsonElement> { ["article-key"] = Json("{\"minLength\":1}") } };
        await fixture.CreateDraftAsync("changed", changed, "copywriter-v1");
        var changedDraft = (await ((IActivityDefinitionDraftStore)fixture.Stores).FindAsync("changed"))!;
        var compiled = await fixture.Compiler.CompileAsync(new(definition, changedDraft, "copywriter-v1", "1.0.0", 0));
        Assert.True(compiled.IsSuccessful, JsonSerializer.Serialize(compiled.Diagnostics));
        Assert.NotEqual(baseline.Template.TemplateHash, compiled.Template!.TemplateHash);
        var retained = (await fixture.Templates.FindAsync(publication.TemplateId))!;
        Assert.Equal(original.TemplateHash, retained.TemplateHash);
        Assert.True(JsonElement.DeepEquals(original.Root.DescriptorPayload, retained.Root.DescriptorPayload));
    }

    [Fact]
    public async Task Saving_after_review_invalidates_the_reviewed_request_and_publishes_nothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var review = await fixture.Publisher.PreflightAsync(new("draft", 1, null) { Version = "1.0.0" });
        Assert.True(review.IsPublishable, JsonSerializer.Serialize(review.Diagnostics));
        await fixture.Stores.ExecuteAsync(new ReplaceActivityDraftRequest("draft", 1, await fixture.StateAsync(fixture.Manifest with { Instructions = "Changed after review." }), []));
        var stale = await Assert.ThrowsAsync<ActivityPublicationRejectedException>(() => fixture.Publisher.PublishReviewedAsync(new("draft", 1, null, "1.0.0", review.ReviewToken, "stale-publish", "copywriter-v1")));
        Assert.Equal("activity.draft.stale-revision", stale.ErrorCode);
        Assert.False(await fixture.Design.ActivityDefinitionVersionPublications.AnyAsync());
        Assert.False(await fixture.Runtime.ExecutableActivityTemplates.AnyAsync());
    }

    [Theory]
    [InlineData(false, true, "activity.runtime.consumer-missing")]
    [InlineData(true, false, "activity.runtime.storage-driver-missing")]
    public async Task Missing_runtime_or_storage_is_refused_without_partial_publication(bool runtime, bool storage, string code)
    {
        await using var fixture = await Fixture.CreateAsync();
        var publisher = fixture.NewPublisher(runtime, storage);
        var review = await publisher.PreflightAsync(new("draft", 1, null) { Version = "1.0.0" });
        Assert.False(review.IsPublishable);
        Assert.Contains(review.Diagnostics, x => x.Code == code);
        Assert.False(await fixture.Design.ActivityDefinitionVersionPublications.AnyAsync());
        Assert.False(await fixture.Runtime.ExecutableActivityTemplates.AnyAsync());
    }

    private sealed class Fixture(string directory, ActivitiesDesignSqliteDbContext design, RuntimeSqliteDbContext runtime,
        PublishingSnapshotReviewSqliteDbContext publishing, ServiceProvider root, AsyncServiceScope scope,
        EfActivityDesignStores stores, EfExecutableActivityTemplateStore templates, EfWorkflowExecutableSourceReferenceStore sources,
        EfActivityPublicationReceiptStore receipts, EfActivityPublicationCommand commit, Access access, MetadataModel model, string outputName, string outputType, bool outputNullable, bool optionalBrief, bool allowContinuity, bool invocationControls, SpecialistTurnLimits? limits) : IAsyncDisposable
    {
        public ActivitiesDesignSqliteDbContext Design => design;
        public RuntimeSqliteDbContext Runtime => runtime;
        public IServiceProvider Services => scope.ServiceProvider;
        public EfActivityDesignStores Stores => stores;
        public IActivityDefinitionVersionPublicationStore Publications => stores;
        public EfExecutableActivityTemplateStore Templates => templates;
        public EfWorkflowExecutableSourceReferenceStore Sources => sources;
        public MetadataModel Model => model;
        public PublishActivityDefinitionRequest? LastPublish { get; private set; }
        public SpecialistManifest Manifest { get; } = new("Write the supplied brief.", new("fixture", "test-connection", "fixture-model"), Json("{}"),
            new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), limits ?? new(), allowContinuity);
        public RuntimeDurableValueStorageDriverRegistry Drivers { get; } = new([new JsonRuntimeDurableValueStorageDriver()]);
        public ActivityTemplateCompiler Compiler => new(new ActivityTemplateProviderCompilerRegistry(Services.GetServices<IActivityTemplateProviderCompiler>()),
            new ActivityTemplateDependencyDiscovererRegistry(Services.GetServices<IActivityTemplateDependencyDiscoverer>()), stores, stores, templates,
            new AcceptAllActivityTemplateAdmissionPolicy(), TimeProvider.System);
        public ActivityDefinitionPublisher Publisher => NewPublisher(true, true);
        public ActivityDefinitionPublisher NewPublisher(bool runtimeInstalled, bool storageInstalled) => new(stores, stores, stores, stores, stores, stores,
            new ActivityDraftValidator(new ActivityProviderRegistry(Services.GetServices<IActivityProvider>()), new ActivityContractAuthoringValidator(new Capabilities()), TimeProvider.System),
            new ActivityVersionDiffer(), Compiler, access, commit, Services.GetRequiredService<IDistributedLockProvider>(), new Ids(), TimeProvider.System, receipts,
            runtimeInstalled ? Services.GetServices<IActivityActivationStrategy>() : [], storageInstalled ? Drivers : new RuntimeDurableValueStorageDriverRegistry([]));

        public async ValueTask<ActivityDefinitionDraftState> StateAsync(SpecialistManifest manifest)
        {
            var contract = new Elsa.Activities.Design.Core.Models.ActivityContract("1", [new("brief-key", "Brief", new("String"), !optionalBrief, false, null, "elsa.json")],
                [new("article-key", outputName, new(outputType), true, outputNullable, "elsa.json")], [new("done", "Done", true)]);
            var source = new ActivityProviderManifest(SpecialistRuntimeIds.ProviderKey, invocationControls ? "2" : "1", JsonSerializer.SerializeToElement(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            if (invocationControls)
            {
                var proposal = await Services.GetRequiredService<IActivityProvider>().ProposeContractAsync(new(source, contract));
                Assert.Empty(proposal.Diagnostics);
                contract = contract with { Inputs = [.. contract.Inputs, .. proposal.Changes.Where(x => x.Input is not null).Select(x => x.Input!)],
                    Outputs = [.. contract.Outputs, .. proposal.Changes.Where(x => x.Output is not null).Select(x => x.Output!)] };
            }
            return new(contract, source, new Dictionary<string, string>());
        }

        public async Task<ActivityPublicationReceipt> PublishAsync()
        {
            var publisher = Publisher;
            var review = await publisher.PreflightAsync(new("draft", 1, null) { Version = "1.0.0" });
            Assert.True(review.IsPublishable, JsonSerializer.Serialize(review.Diagnostics));
            LastPublish = new("draft", 1, null, "1.0.0", review.ReviewToken, "publish-copywriter", "copywriter-v1");
            return await publisher.PublishReviewedAsync(LastPublish);
        }

        public async Task CreateDraftAsync(string id, SpecialistManifest manifest, string head) => await stores.ExecuteAsync(new CreateActivityDraftRequest(
            new() { Id = id, DefinitionId = "copywriter", Revision = 1, State = await StateAsync(manifest), CreatedAt = DateTimeOffset.UtcNow, LastModifiedAt = DateTimeOffset.UtcNow },
            new() { Id = id + "-layout", DraftId = id, Revision = 1, CreatedAt = DateTimeOffset.UtcNow, LastModifiedAt = DateTimeOffset.UtcNow }, head));

        public static async Task<Fixture> CreateAsync(string outputName = "Article", string outputType = "String", bool outputNullable = false, bool optionalBrief = false, bool allowContinuity = false, bool invocationControls = false, SpecialistTurnLimits? limits = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "elsa-specialist-publication-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, "locks"));
            var design = new ActivitiesDesignSqliteDbContext(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite("Data Source=" + Path.Combine(directory, "design.db")).Options);
            var runtime = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=" + Path.Combine(directory, "runtime.db")).Options);
            var publishing = new PublishingSnapshotReviewSqliteDbContext(new DbContextOptionsBuilder<PublishingSnapshotReviewSqliteDbContext>().UseSqlite("Data Source=" + Path.Combine(directory, "publishing.db")).Options);
            await design.Database.EnsureCreatedAsync();
            await runtime.Database.EnsureCreatedAsync();
            await publishing.Database.EnsureCreatedAsync();
            var access = new Access();
            var stores = new EfActivityDesignStores(design, access, null, new EfActivityManagementProjectionWriter(design, access));
            var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), AllowEphemeralDevelopmentKey = false }));
            var templates = new EfExecutableActivityTemplateStore(runtime, access, codec);
            var sources = new EfWorkflowExecutableSourceReferenceStore(runtime, access, codec);
            var receipts = new EfActivityPublicationReceiptStore(publishing, access);
            var commit = new EfActivityPublicationCommand(receipts, templates, sources, stores, access);
            var model = new MetadataModel();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddWorkflowRuntime();
            new ActivitiesRuntimeFeature().ConfigureServices(services);
            new SerializationFeature().ConfigureServices(services);
            new SecretsFeature { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }.ConfigureServices(services);
            new SpecialistsDesignFeature().ConfigureServices(services);
            new SpecialistsRuntimeFeature().ConfigureServices(services);
            new SpecialistsInMemoryJournalFeature().ConfigureServices(services);
            new FileSystemLockingFeature { LocksFolderPath = Path.Combine(directory, "locks") }.ConfigureServices(services);
            services.AddSingleton<ISpecialistModelCapabilitiesSource>(model);
            services.AddSingleton<IActivityDefinitionAuthoringStore>(stores);
            services.AddSingleton<IActivityDefinitionVersionPublicationStore>(stores);
            var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var scope = root.CreateAsyncScope();
            var fixture = new Fixture(directory, design, runtime, publishing, root, scope, stores, templates, sources, receipts, commit, access, model, outputName, outputType, outputNullable, optionalBrief, allowContinuity, invocationControls, limits);
            var now = DateTimeOffset.UtcNow;
            await stores.ExecuteAsync(new CreateActivityDefinitionRequest(
                new() { Id = "copywriter", ActivityTypeKey = "acme.copywriter", Category = "Specialists", DisplayName = "Copywriter", CreatedAt = now, LastModifiedAt = now },
                new() { Id = "authoring", DefinitionId = "copywriter", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design), CreatedAt = now, LastModifiedAt = now },
                new() { Id = "draft", DefinitionId = "copywriter", Revision = 1, State = await fixture.StateAsync(fixture.Manifest), CreatedAt = now, LastModifiedAt = now },
                new() { Id = "layout", DraftId = "draft", Revision = 1, CreatedAt = now, LastModifiedAt = now }));
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await root.DisposeAsync();
            await publishing.DisposeAsync();
            await runtime.DisposeAsync();
            await design.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    private sealed class Access : IPersistenceAccessContextAccessor, IActivityPublishingAuthorizationContext
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("default"));
        public string? TenantId => null;
        public bool CanAccessTenant(string? tenantId) => tenantId is null;
    }
    private sealed class Ids : IIdentityGenerator { public string Generate() => Guid.NewGuid().ToString("N"); }
    private sealed class Capabilities : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types => new[] { "String", "Guid", "System.Text.Json.JsonElement" }.Select(alias =>
            new ActivityContractTypeCapability(alias, alias, "Primitives", "text", new HashSet<CollectionKind> { CollectionKind.Single }, true, true, new HashSet<string> { "elsa.json" })).ToArray();
    }
    private sealed class MetadataModel : ISpecialistModelCapabilitiesSource
    {
        public string ProviderKey => "fixture";
        public ValueTask<SpecialistModelCapabilities> GetCapabilitiesAsync(SpecialistModelReference model, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new SpecialistModelCapabilities("fixture/1", Json("{\"type\":\"object\",\"additionalProperties\":false}"), true, 4096));
    }
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}
