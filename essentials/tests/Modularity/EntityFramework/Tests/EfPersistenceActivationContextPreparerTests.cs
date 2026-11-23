using System.Text.Json;
using CShells.Configuration;
using CShells.Features;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.EntityFramework;
using Elsa.Modularity.Nuplane.Services;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Testing;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Elsa.Modularity.EntityFramework.Tests;

public sealed class EfPersistenceActivationContextPreparerTests
{
    private const string FeatureId = "WorkflowsRuntimeEntityFrameworkCore";
    private static readonly JsonElement Empty = Json("{}");

    [Fact]
    public async Task Current_only_resource_selection_refuses_a_disable_request()
    {
        var context = Context([FeatureId], [new FeatureApplyItem(FeatureId, false, Empty)]);

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            Preparer(SelectedResource()).PrepareAsync(context));

        Assert.Equal(FeatureId, Assert.Single(error.Refusals).Feature);
        Assert.StartsWith("[resource-managed-configuration] Feature '",
            error.Refusals[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Candidate_only_resource_selection_refuses_an_enable_request()
    {
        var context = Context([], [new FeatureApplyItem(FeatureId, true, Empty)]);

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            Preparer(SelectedResource()).PrepareAsync(context));

        Assert.Equal(FeatureId, Assert.Single(error.Refusals).Feature);
    }

    [Fact]
    public async Task Dependency_enabled_resource_consumer_is_visible_to_preflight()
    {
        var context = Context([], [new FeatureApplyItem("AppFeature", true, Empty)]);

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            Preparer(SelectedResource()).PrepareAsync(context));

        Assert.Equal(FeatureId, Assert.Single(error.Refusals).Feature);
    }

    [Fact]
    public async Task Disabled_required_dependency_refuses_before_feature_construction_or_configuration()
    {
        var context = Context([], [
            new FeatureApplyItem("AppFeature", true, Empty),
            new FeatureApplyItem(FeatureId, false, Empty)
        ]);
        var preparer = new EfPersistenceActivationContextPreparer(
            SelectedResource(),
            new FakeRuntimeFeatureCatalog(
                new ShellFeatureDescriptor("WorkflowsRuntimeResumption"),
                new ShellFeatureDescriptor(FeatureId)
                {
                    StartupType = typeof(RuntimeEntityFrameworkCoreFeature),
                    Dependencies = ["WorkflowsRuntimeResumption"]
                },
                new ShellFeatureDescriptor("AppFeature") { Dependencies = [FeatureId] },
                new ShellFeatureDescriptor(nameof(EffectProbeFeature))
                {
                    StartupType = typeof(EffectProbeFeature)
                }),
            new DeferredEffectHostDefaults());

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            preparer.PrepareAsync(context));

        Assert.Equal(FeatureId, Assert.Single(error.Refusals).Feature);
    }

    [Fact]
    public async Task Opaque_configurator_on_resource_consumer_refuses_without_running_feature_code()
    {
        var context = Context([], [new FeatureApplyItem(FeatureId, true, Empty)]);

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            Preparer(SelectedResource(), new OpaqueRuntimeHostDefaults()).PrepareAsync(context));

        Assert.Equal(FeatureId, Assert.Single(error.Refusals).Feature);
    }

    [Fact]
    public async Task Root_default_and_binding_do_not_claim_an_unenrolled_private_store()
    {
        const string secretsId = "SecretsEntityFrameworkCore";
        var authored = Json("""{"Provider":"Sqlite","ConnectionString":"Data Source=private-store.db"}""");
        var context = Context([secretsId], [new FeatureApplyItem(secretsId, true, authored)]);
        var root = SelectedResource();
        root[$"CShells:Shells:default:Configuration:Elsa:Persistence:Bindings:{secretsId}"] = "primary";
        var preparer = new EfPersistenceActivationContextPreparer(
            root,
            new FakeRuntimeFeatureCatalog(new ShellFeatureDescriptor(secretsId)
            {
                StartupType = typeof(SecretsEntityFrameworkCoreFeature)
            }),
            new NoHostDefaults());

        Assert.Same(context, await preparer.PrepareAsync(context));
        Assert.Equal("Data Source=private-store.db",
            context.Request.Features[0].Configuration.GetProperty("ConnectionString").GetString());
    }

    [Fact]
    public async Task Cancellation_refuses_before_host_composition()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var defaults = new CountingHostDefaults();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Preparer(SelectedResource(), defaults).PrepareAsync(Context([], []), cancellation.Token));

        Assert.Equal(0, defaults.ConfigureCount);
    }

    [Fact]
    public async Task Shell_default_and_code_only_default_are_both_evaluated()
    {
        var fromShell = new FeatureActivationContext(
            new ShellFeatureConfigurationSnapshot("default", "revision",
                new Dictionary<string, JsonElement> { [FeatureId] = Empty },
                Json("""{"Elsa":{"Persistence":{"DefaultResource":"primary"}}}""")),
            new FeatureApplyRequest("revision", [new FeatureApplyItem(FeatureId, true, Empty)]));
        var fromCode = Context([FeatureId], [new FeatureApplyItem(FeatureId, true, Empty)]);

        var shellError = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            Preparer(ResourceDefinitionsOnly()).PrepareAsync(fromShell));
        var codeError = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            Preparer(ResourceDefinitionsOnly(), new ResourceDefaultHostDefaults()).PrepareAsync(fromCode));

        Assert.Equal(FeatureId, Assert.Single(shellError.Refusals).Feature);
        Assert.Equal(FeatureId, Assert.Single(codeError.Refusals).Feature);
    }

    [Fact]
    public async Task Unselected_definitions_and_wholly_legacy_edits_remain_available()
    {
        var context = Context([FeatureId], [new FeatureApplyItem(FeatureId, true, Empty)]);

        Assert.Same(context, await Preparer(ResourceDefinitionsOnly()).PrepareAsync(context));
        Assert.Same(context, await Preparer(new ConfigurationBuilder().Build()).PrepareAsync(context));
    }

    [Fact]
    public async Task Invalid_selected_intent_refuses_without_echoing_connection_values()
    {
        var root = new ConfigurationBuilder().AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("Elsa:Persistence:DefaultResource", "primary"),
            new KeyValuePair<string, string?>("Elsa:Persistence:Resources:primary:Provider", "unsupported"),
            new KeyValuePair<string, string?>("Elsa:Persistence:Resources:primary:ConnectionName", "Shared"),
            new KeyValuePair<string, string?>("ConnectionStrings:Shared", "connection-value-canary")
        ]).Build();
        var context = Context([], [new FeatureApplyItem(FeatureId, true, Empty)]);

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            Preparer(root).PrepareAsync(context));

        Assert.Equal(FeatureId, Assert.Single(error.Refusals).Feature);
        Assert.DoesNotContain("connection-value-canary", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Source_change_during_composition_refuses_before_editor_acceptance()
    {
        var root = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var context = Context([], []);

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            Preparer(root, new ReloadingHostDefaults()).PrepareAsync(context));

        Assert.Contains("changed during validation", Assert.Single(error.Refusals).Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_only_resource_selection_refuses_before_management_guards_or_side_effects()
    {
        var management = Management(
            SelectedResource(),
            new Dictionary<string, JsonElement> { [FeatureId] = Empty });
        var catalog = await management.Service.GetCatalogAsync();

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            management.Service.ApplyAsync(new FeatureApplyRequest(catalog.Revision,
                [new FeatureApplyItem(FeatureId, false, Empty)])));

        Assert.Equal(FeatureId, Assert.Single(error.Refusals).Feature);
        AssertNoManagementEffects(management);
    }

    [Fact]
    public async Task Candidate_only_resource_selection_refuses_before_management_guards_or_side_effects()
    {
        var management = Management(SelectedResource());
        var catalog = await management.Service.GetCatalogAsync();

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            management.Service.ApplyAsync(new FeatureApplyRequest(catalog.Revision,
                [new FeatureApplyItem(FeatureId, true, Empty)])));

        Assert.Equal(FeatureId, Assert.Single(error.Refusals).Feature);
        AssertNoManagementEffects(management);
    }

    [Fact]
    public async Task Invalid_candidate_resource_selection_refuses_before_management_guards_or_side_effects()
    {
        var root = Configuration(
            ("Elsa:Persistence:DefaultResource", "missing"));
        var management = Management(root);
        var catalog = await management.Service.GetCatalogAsync();

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            management.Service.ApplyAsync(new FeatureApplyRequest(catalog.Revision,
                [new FeatureApplyItem(FeatureId, true, Empty)])));

        Assert.Equal(FeatureId, Assert.Single(error.Refusals).Feature);
        AssertNoManagementEffects(management);
    }

    [Fact]
    public async Task Changed_configuration_source_refuses_before_management_guards_or_side_effects()
    {
        var root = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var management = Management(root, defaults: new ReloadingHostDefaults());
        var catalog = await management.Service.GetCatalogAsync();

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            management.Service.ApplyAsync(new FeatureApplyRequest(catalog.Revision, [])));

        Assert.Contains("changed during validation", Assert.Single(error.Refusals).Reason,
            StringComparison.Ordinal);
        AssertNoManagementEffects(management);
    }

    [Fact]
    public async Task Configuration_token_failure_is_redacted_before_management_guards_or_side_effects()
    {
        var root = new ThrowingReloadTokenConfiguration(new ConfigurationBuilder().Build());
        var management = Management(root);
        var catalog = await management.Service.GetCatalogAsync();

        var error = await Assert.ThrowsAsync<FeatureActivationRefusedException>(() =>
            management.Service.ApplyAsync(new FeatureApplyRequest(catalog.Revision, [])));

        Assert.StartsWith("[resource-managed-configuration]", Assert.Single(error.Refusals).Reason,
            StringComparison.Ordinal);
        Assert.DoesNotContain("connection-value-canary", error.ToString(), StringComparison.Ordinal);
        AssertNoManagementEffects(management);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Definitions_only_and_legacy_edits_still_reach_the_ordinary_management_pipeline(
        bool includeUnselectedResourceDefinitions)
    {
        var root = includeUnselectedResourceDefinitions
            ? ResourceDefinitionsOnly()
            : new ConfigurationBuilder().Build();
        var stored = LegacyFeatureConfiguration("Data Source=before.db");
        var candidate = LegacyFeatureConfiguration("Data Source=after.db");
        var management = Management(root, new Dictionary<string, JsonElement> { [FeatureId] = stored });
        var catalog = await management.Service.GetCatalogAsync();

        await management.Service.ApplyAsync(new FeatureApplyRequest(catalog.Revision,
            [new FeatureApplyItem(FeatureId, true, candidate)]));

        Assert.Equal(1, management.Guard.EvaluationCount);
        Assert.Equal(1, management.Store.SaveCount);
        Assert.Equal(1, management.Refresher.RefreshCount);
        Assert.Equal(1, management.Reloader.ReloadCount);
        Assert.Equal("Data Source=after.db",
            management.Guard.Seen!.Request.Features[0].Configuration.GetProperty("ConnectionString").GetString());
        Assert.Equal("Data Source=after.db",
            management.Store.Snapshot.Features[FeatureId].GetProperty("ConnectionString").GetString());
    }

    private static EfPersistenceActivationContextPreparer Preparer(
        IConfiguration root, IEfToolingShellDefaults? defaults = null) =>
        new(root, new FakeRuntimeFeatureCatalog(
                new ShellFeatureDescriptor("WorkflowsRuntimeResumption"),
                new ShellFeatureDescriptor(FeatureId)
                {
                    StartupType = typeof(RuntimeEntityFrameworkCoreFeature),
                    Dependencies = ["WorkflowsRuntimeResumption"]
                },
                new ShellFeatureDescriptor("AppFeature") { Dependencies = [FeatureId] }),
            defaults ?? new NoHostDefaults());

    private static FeatureActivationContext Context(
        IReadOnlyList<string> stored,
        IReadOnlyList<FeatureApplyItem> candidate) =>
        new(new ShellFeatureConfigurationSnapshot("default", "revision",
                stored.ToDictionary(x => x, _ => Empty, StringComparer.OrdinalIgnoreCase), Empty),
            new FeatureApplyRequest("revision", candidate));

    private static IConfiguration SelectedResource() => new ConfigurationBuilder().AddInMemoryCollection(
    [
        new KeyValuePair<string, string?>("Elsa:Persistence:DefaultResource", "primary"),
        new KeyValuePair<string, string?>("Elsa:Persistence:Resources:primary:Provider", "Sqlite"),
        new KeyValuePair<string, string?>("Elsa:Persistence:Resources:primary:ConnectionName", "Shared")
    ]).Build();

    private static IConfiguration ResourceDefinitionsOnly() => new ConfigurationBuilder().AddInMemoryCollection(
    [
        new KeyValuePair<string, string?>("Elsa:Persistence:Resources:primary:Provider", "Sqlite"),
        new KeyValuePair<string, string?>("Elsa:Persistence:Resources:primary:ConnectionName", "Shared")
    ]).Build();

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(x =>
            new KeyValuePair<string, string?>(x.Key, x.Value))).Build();

    private static JsonElement LegacyFeatureConfiguration(string connectionString) =>
        Json($$"""{"Provider":"Sqlite","ConnectionString":"{{connectionString}}"}""");

    private static ManagementProbe Management(
        IConfiguration root,
        IReadOnlyDictionary<string, JsonElement>? features = null,
        IEfToolingShellDefaults? defaults = null)
    {
        var store = new ManagementShellStore(new ShellFeatureConfigurationSnapshot(
            "default", "revision", features ?? new Dictionary<string, JsonElement>(), Empty));
        var guard = new CountingActivationGuard();
        var refresher = new CountingRefresher();
        var reloader = new CountingReloader();
        var service = new FeatureManagementService(
            store,
            Array.Empty<IFeatureCatalogContributor>(),
            [guard],
            Preparer(root, defaults),
            refresher,
            reloader);
        return new ManagementProbe(service, store, guard, refresher, reloader);
    }

    private static void AssertNoManagementEffects(ManagementProbe management)
    {
        Assert.Equal(0, management.Guard.EvaluationCount);
        Assert.Equal(0, management.Store.SaveCount);
        Assert.Equal(0, management.Refresher.RefreshCount);
        Assert.Equal(0, management.Reloader.ReloadCount);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class NoHostDefaults : IEfToolingShellDefaults
    {
        public void Configure(ShellBuilder builder, IConfiguration configuration) { }
    }

    private sealed class ResourceDefaultHostDefaults : IEfToolingShellDefaults
    {
        public void Configure(ShellBuilder builder, IConfiguration configuration) =>
            builder.WithConfiguration("Elsa:Persistence:DefaultResource", "primary");
    }

    private sealed class OpaqueRuntimeHostDefaults : IEfToolingShellDefaults
    {
        public void Configure(ShellBuilder builder, IConfiguration configuration) =>
            builder.WithFeature<RuntimeEntityFrameworkCoreFeature>(_ => throw new InvalidOperationException(
                "Feature configurators must not run during preflight."));
    }

    private sealed class ReloadingHostDefaults : IEfToolingShellDefaults
    {
        public void Configure(ShellBuilder builder, IConfiguration configuration) =>
            ((IConfigurationRoot)configuration).Reload();
    }

    private sealed class ThrowingReloadTokenConfiguration(IConfiguration inner) : IConfiguration
    {
        public string? this[string key]
        {
            get => inner[key];
            set => inner[key] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren() => inner.GetChildren();
        public IChangeToken GetReloadToken() =>
            throw new InvalidOperationException("connection-value-canary");
        public IConfigurationSection GetSection(string key) => inner.GetSection(key);
    }

    private sealed class DeferredEffectHostDefaults : IEfToolingShellDefaults
    {
        public void Configure(ShellBuilder builder, IConfiguration configuration) =>
            builder.WithFeature<EffectProbeFeature>(_ => throw new InvalidOperationException(
                "Feature configurators must not run during preflight."));
    }

    private sealed class CountingHostDefaults : IEfToolingShellDefaults
    {
        public int ConfigureCount { get; private set; }

        public void Configure(ShellBuilder builder, IConfiguration configuration) => ConfigureCount++;
    }

    private sealed class EffectProbeFeature : IShellFeature
    {
        public EffectProbeFeature() => throw new InvalidOperationException(
            "Feature constructors must not run during preflight.");

        public void ConfigureServices(IServiceCollection services) => throw new InvalidOperationException(
            "Feature services must not be configured during preflight.");
    }

    private sealed record ManagementProbe(
        FeatureManagementService Service,
        ManagementShellStore Store,
        CountingActivationGuard Guard,
        CountingRefresher Refresher,
        CountingReloader Reloader);

    private sealed class ManagementShellStore(ShellFeatureConfigurationSnapshot snapshot)
        : IShellFeatureConfigurationStore
    {
        public ShellFeatureConfigurationSnapshot Snapshot { get; private set; } = snapshot;

        public int SaveCount { get; private set; }

        public Task<ShellFeatureConfigurationSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot);

        public Task<ShellFeatureConfigurationSnapshot> SaveAsync(
            string expectedRevision,
            IReadOnlyList<FeatureConfigurationChange> features,
            CancellationToken cancellationToken = default)
        {
            if (expectedRevision != Snapshot.Revision)
                throw new FeatureCatalogRevisionConflictException(expectedRevision, Snapshot.Revision);

            SaveCount++;
            Snapshot = Snapshot with
            {
                Revision = $"revision-{SaveCount}",
                Features = features.Where(x => x.Enabled)
                    .ToDictionary(x => x.Id, x => x.Configuration.Clone(), StringComparer.OrdinalIgnoreCase)
            };
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class CountingActivationGuard : IFeatureActivationGuard
    {
        public int EvaluationCount { get; private set; }

        public FeatureActivationContext? Seen { get; private set; }

        public Task<FeatureActivationDecision> EvaluateAsync(
            FeatureActivationContext context, CancellationToken cancellationToken = default)
        {
            EvaluationCount++;
            Seen = context;
            return Task.FromResult(FeatureActivationDecision.Allowed);
        }
    }

    private sealed class CountingRefresher : IRuntimeFeatureCatalogRefresher
    {
        public int RefreshCount { get; private set; }

        public Task<int> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(++RefreshCount);
    }

    private sealed class CountingReloader : IShellReloader
    {
        public int ReloadCount { get; private set; }

        public Task<int> ReloadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(++ReloadCount);
    }
}
