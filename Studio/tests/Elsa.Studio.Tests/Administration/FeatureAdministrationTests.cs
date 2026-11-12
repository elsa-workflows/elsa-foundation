using CShells.DependencyInjection;
using CShells.Features;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using CShells.Lifecycle.Blueprints;
using Elsa.Studio.Hosting.Nuplane;
using System.Text.Json.Nodes;
using CShells.Lifecycle;
using CShells.Lifecycle.Providers;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Tests.Administration;

public sealed class FeatureAdministrationTests
{
    [Fact]
    public async Task Masked_round_trip_preserves_secret_and_unrelated_shell_and_reloads_only_selected_shell()
    {
        await using var host = await AdministrationHost.StartAsync(managedFeatures: true);
        var registry = host.App.Services.GetRequiredService<IShellRegistry>();
        var decoy = await registry.GetOrActivateAsync("Default");
        var catalog = await Catalog(host);
        Assert.DoesNotContain(AdministrationHost.Credential, catalog.ToJsonString());
        using var response = await host.Client.PostAsJsonAsync("/modularity/features/apply", Request(catalog));
        response.EnsureSuccessStatusCode();
        Assert.DoesNotContain(AdministrationHost.Credential, await response.Content.ReadAsStringAsync());
        var stored = JsonNode.Parse(await File.ReadAllTextAsync(host.ShellsFile))!;
        Assert.Equal("unrelated", (string?)stored["preserve"]);
        Assert.Equal([host.ShellsFile], Directory.GetFiles(Path.GetDirectoryName(host.ShellsFile)!));
        Assert.Equal(AdministrationHost.Credential, (string?)stored["CShells"]!["Shells"]!["Authoring"]!["Features"]!["Configured"]!["Credential"]);
        Assert.NotNull(stored["CShells"]!["Shells"]!["Default"]!["Features"]!["StudioApi"]);
        Assert.Same(decoy, registry.GetActive("Default"));
        Assert.Equal(AdministrationHost.Credential, host.App.Configuration["CShells:Shells:Authoring:Features:Configured:Credential"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Catalog_round_trip_preserves_implicitly_enabled_Studio_infrastructure(bool explicitlyDisabled)
    {
        await using var host = await AdministrationHost.StartAsync(managedFeatures: true, beforeStudio: builder =>
        {
            if (!explicitlyDisabled) return;
            var path = Path.Combine(builder.Environment.ContentRootPath, "config", "studio-shells.json");
            var document = JsonNode.Parse(File.ReadAllText(path))!;
            document["CShells"]!["Shells"]!["Authoring"]!["Features"]!["Events"] = false;
            File.WriteAllText(path, document.ToJsonString());
        });
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/studio-runtime.js")).StatusCode);
        var before = await File.ReadAllBytesAsync(host.ShellsFile);
        var catalog = await Catalog(host);
        Assert.True((bool)catalog["features"]!.AsArray().Single(feature => (string?)feature!["id"] == "Events")!["enabled"]!);
        Assert.Equal(before, await File.ReadAllBytesAsync(host.ShellsFile));
        using var response = await host.Client.PostAsJsonAsync("/modularity/features/apply", Request(catalog));
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/studio-runtime.js")).StatusCode);
    }

    [Fact]
    public async Task Public_store_reads_boolean_and_object_feature_configuration_from_the_selected_blueprint()
    {
        await using var host = await AdministrationHost.StartAsync(managedFeatures: true, beforeStudio: builder =>
        {
            var path = Path.Combine(builder.Environment.ContentRootPath, "config", "studio-shells.json");
            var document = JsonNode.Parse(File.ReadAllText(path))!;
            var features = document["CShells"]!["Shells"]!["Authoring"]!["Features"]!;
            features["On"] = true;
            features["Off"] = false;
            features["Configured"]!["Size"] = 10;
            File.WriteAllText(path, document.ToJsonString());
        });
        var store = host.App.Services.GetRequiredService<IShellFeatureConfigurationStore>();
        var snapshot = await store.LoadAsync();
        Assert.Contains("On", snapshot.Features.Keys);
        Assert.DoesNotContain("Off", snapshot.Features.Keys);
        Assert.Equal(10, snapshot.Features["Configured"].GetProperty("Size").GetInt32());
    }

    [Fact]
    public async Task Activation_guards_receive_the_shell_configuration_separately_from_feature_secrets()
    {
        var guard = new ConfigurationGuard();
        await using var host = await AdministrationHost.StartAsync(managedFeatures: true,
            afterStudio: builder => builder.Services.AddSingleton<IFeatureActivationGuard>(guard));
        using var response = await host.Client.PostAsJsonAsync("/modularity/features/apply", Request(await Catalog(host)));
        response.EnsureSuccessStatusCode();
        Assert.Equal(host.App.Configuration["CShells:Shells:Authoring:Configuration:Widget:Limit"], guard.Configuration.GetProperty("Widget").GetProperty("Limit").ToString());
        Assert.DoesNotContain(AdministrationHost.Credential, guard.Configuration.GetRawText());
    }

    [Fact]
    public async Task Activation_guard_rejects_the_effective_shell_configuration_override_before_writing()
    {
        var guard = new ConfigurationGuard(refuseLimit99: true);
        await using var host = await AdministrationHost.StartAsync(managedFeatures: true, afterStudio: builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["CShells:Shells:Authoring:Configuration:Widget:Limit"] = "99" });
            builder.Services.AddSingleton<IFeatureActivationGuard>(guard);
        });
        var before = await File.ReadAllBytesAsync(host.ShellsFile);
        using var response = await host.Client.PostAsJsonAsync("/modularity/features/apply", Request(await Catalog(host)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("effective-limit-99", await response.Content.ReadAsStringAsync());
        Assert.Equal("99", guard.Configuration.GetProperty("Widget").GetProperty("Limit").ToString());
        Assert.DoesNotContain(AdministrationHost.Credential, guard.Configuration.GetRawText());
        Assert.Equal(before, await File.ReadAllBytesAsync(host.ShellsFile));
    }

    [Fact]
    public async Task Secret_only_edit_changes_revision_and_rejects_stale_state()
    {
        await using var host = await AdministrationHost.StartAsync(managedFeatures: true);
        var catalog = await Catalog(host);
        var request = Request(catalog);
        Configuration(request)["Credential"] = "updated-private-credential";
        using var saved = await host.Client.PostAsJsonAsync("/modularity/features/apply", request);
        saved.EnsureSuccessStatusCode();
        var next = await Catalog(host);
        Assert.NotEqual((string?)catalog["revision"], (string?)next["revision"]);
        Assert.DoesNotContain("updated-private-credential", next.ToJsonString());
        using var stale = await host.Client.PostAsJsonAsync("/modularity/features/apply", Request(catalog));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("updated-private-credential", host.App.Configuration["CShells:Shells:Authoring:Features:Configured:Credential"]);
    }

    [Fact]
    public async Task Concurrent_edits_have_one_winner_and_one_conflict()
    {
        await using var host = await AdministrationHost.StartAsync(managedFeatures: true);
        var catalog = await Catalog(host);
        var first = Request(catalog);
        var second = Request(catalog);
        Configuration(first)["Credential"] = "first-edit";
        Configuration(second)["Credential"] = "second-edit";
        var responses = await Task.WhenAll(host.Client.PostAsJsonAsync("/modularity/features/apply", first), host.Client.PostAsJsonAsync("/modularity/features/apply", second));
        try
        {
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order().ToArray());
            Assert.Contains(host.App.Configuration["CShells:Shells:Authoring:Features:Configured:Credential"], new[] { "first-edit", "second-edit" });
            Assert.NotNull(JsonNode.Parse(await File.ReadAllTextAsync(host.ShellsFile)));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task A_stale_edit_whose_load_meets_a_configuration_reload_is_a_conflict()
    {
        // #4092: the losing edit of two concurrent applies loads the catalog while the winner's save reloads configuration.
        // The tripwire reproduces that interleaving deterministically: it fires one reload from inside the store's compose.
        var tripwire = new CompositionTripwire();
        await using var host = await AdministrationHost.StartAsync(managedFeatures: true, afterStudio: builder => builder.Configuration.Add(tripwire));
        var catalog = await Catalog(host);
        var winner = Request(catalog);
        var loser = Request(catalog);
        Configuration(winner)["Credential"] = "first-edit";
        Configuration(loser)["Credential"] = "second-edit";
        using var won = await host.Client.PostAsJsonAsync("/modularity/features/apply", winner);
        Assert.Equal(HttpStatusCode.OK, won.StatusCode);

        tripwire.Arm();
        using var lost = await host.Client.PostAsJsonAsync("/modularity/features/apply", loser);

        Assert.Equal(1, tripwire.Fired);
        Assert.Equal(HttpStatusCode.Conflict, lost.StatusCode);
        Assert.Equal("first-edit", host.App.Configuration["CShells:Shells:Authoring:Features:Configured:Credential"]);
    }

    [Theory]
    [InlineData("incomplete", HttpStatusCode.BadRequest)]
    [InlineData("duplicate", HttpStatusCode.BadRequest)]
    [InlineData("mandatory", HttpStatusCode.Conflict)]
    public async Task Invalid_full_states_leave_the_file_unchanged(string kind, HttpStatusCode expected)
    {
        await using var host = await AdministrationHost.StartAsync(managedFeatures: true);
        var before = await File.ReadAllBytesAsync(host.ShellsFile);
        var request = Request(await Catalog(host));
        var features = request["features"]!.AsArray();
        if (kind == "incomplete") features.Remove(features.Single(feature => (string?)feature!["id"] == "Configured"));
        if (kind == "duplicate") features.Add(features[0]!.DeepClone());
        if (kind == "mandatory") features.Single(feature => (string?)feature!["id"] == "StudioApi")!["enabled"] = false;
        using var response = await host.Client.PostAsJsonAsync("/modularity/features/apply", request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(before, await File.ReadAllBytesAsync(host.ShellsFile));
    }

    [Theory]
    [InlineData("host-owned")]
    [InlineData("lower")]
    [InlineData("upper")]
    [InlineData("equal")]
    [InlineData("provider")]
    public async Task Unowned_or_overlaid_sources_refuse_without_writing(string kind)
    {
        void Overlay(WebApplicationBuilder builder) => builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CShells:Shells:Authoring:Features:Configured:Credential"] = kind == "equal" ? AdministrationHost.Credential : "overlay-credential"
        });
        await using var host = await AdministrationHost.StartAsync(
            managedFeatures: kind != "host-owned",
            beforeStudio: kind == "lower" ? Overlay : null,
            afterStudio: builder =>
            {
                if (kind is "upper" or "equal") Overlay(builder);
                if (kind == "provider") builder.Services.Replace(ServiceDescriptor.Singleton<IShellBlueprintProvider>(new ConfigurationShellBlueprintProvider(builder.Configuration.GetSection("CShells:Shells"))));
            });
        var before = File.Exists(host.ShellsFile) ? await File.ReadAllBytesAsync(host.ShellsFile) : null;
        using var response = await host.Client.PostAsJsonAsync("/modularity/features/apply", Request(await Catalog(host)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("configuration-read-only", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, File.Exists(host.ShellsFile) ? await File.ReadAllBytesAsync(host.ShellsFile) : null);
    }

    [Theory]
    [InlineData("MissingDependency")]
    [InlineData("CycleA")]
    public async Task Catalog_keeps_invalid_dependency_roots_readable_and_allows_disabling_them(string featureId)
    {
        await using var host = await AdministrationHost.StartAsync(managedFeatures: true,
            configureStudio: studio => studio.ConfigureShells(shells => shells.WithAssemblies(typeof(MissingDependencyFeature).Assembly)),
            beforeStudio: builder =>
            {
                var path = Path.Combine(builder.Environment.ContentRootPath, "config", "studio-shells.json");
                var document = JsonNode.Parse(File.ReadAllText(path))!;
                document["CShells"]!["Shells"]!["Authoring"]!["Features"]![featureId] = true;
                File.WriteAllText(path, document.ToJsonString());
            });
        var catalog = await Catalog(host);
        Assert.True((bool)catalog["features"]!.AsArray().Single(feature => (string?)feature!["id"] == featureId)!["enabled"]!);
        var request = Request(catalog);
        var before = await File.ReadAllBytesAsync(host.ShellsFile);
        using var refused = await host.Client.PostAsJsonAsync("/modularity/features/apply", request);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(before, await File.ReadAllBytesAsync(host.ShellsFile));
        request["features"]!.AsArray().Single(feature => (string?)feature!["id"] == featureId)!["enabled"] = false;
        using var response = await host.Client.PostAsJsonAsync("/modularity/features/apply", request);
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/studio-runtime.js")).StatusCode);
    }

    [ShellFeature("MissingDependency", DependsOn = new object[] { "UnavailableInfrastructure" })]
    public class MissingDependencyFeature : IShellFeature { public virtual void ConfigureServices(IServiceCollection services) { } }
    [ShellFeature("CycleA", DependsOn = new object[] { typeof(CycleBFeature) })]
    public class CycleAFeature : IShellFeature { public virtual void ConfigureServices(IServiceCollection services) { } }
    [ShellFeature("CycleB", DependsOn = new object[] { typeof(CycleAFeature) })]
    public class CycleBFeature : IShellFeature { public virtual void ConfigureServices(IServiceCollection services) { } }

    private static async Task<JsonObject> Catalog(AdministrationHost host)
    {
        using var response = await host.Client.GetAsync("/modularity/features");
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }

    private static JsonObject Request(JsonObject catalog) => new()
    {
        ["revision"] = catalog["revision"]!.DeepClone(),
        ["features"] = new JsonArray(catalog["features"]!.AsArray().Select(feature => (JsonNode)new JsonObject
        {
            ["id"] = feature!["id"]!.DeepClone(),
            ["enabled"] = feature["enabled"]!.DeepClone(),
            ["configuration"] = feature["configuration"]!.DeepClone()
        }).ToArray())
    };

    private static JsonObject Configuration(JsonObject request) => request["features"]!.AsArray().Single(feature => (string?)feature!["id"] == "Configured")!["configuration"]!.AsObject();
    /// <summary>
    /// A configuration source with no keys that, once armed, raises one reload from inside the Studio feature store's
    /// blueprint compose — after CShells captured its reload token and before it checks it.
    /// </summary>
    private sealed class CompositionTripwire : ConfigurationProvider, IConfigurationSource
    {
        private int _armed;
        private int _fired;
        public int Fired => Volatile.Read(ref _fired);
        public void Arm() => Volatile.Write(ref _armed, 1);
        public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

        public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
        {
            if (parentPath?.StartsWith("CShells:Shells:Authoring", StringComparison.OrdinalIgnoreCase) == true
                && Volatile.Read(ref _armed) == 1
                && OnStack(typeof(ConfigurationShellBlueprint)) && OnStack(typeof(ShellFeatureStore))
                && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Interlocked.Increment(ref _fired);
                OnReload();
            }
            return base.GetChildKeys(earlierKeys, parentPath);
        }

        // Async state machines are nested types, so match the declaring type or its parent.
        private static bool OnStack(Type type) => new StackTrace().GetFrames().Any(frame =>
            frame.GetMethod()?.DeclaringType is { } declaring && (declaring == type || declaring.DeclaringType == type));
    }

    private sealed class ConfigurationGuard(bool refuseLimit99 = false) : IFeatureActivationGuard
    {
        public JsonElement Configuration { get; private set; }
        public Task<FeatureActivationDecision> EvaluateAsync(FeatureActivationContext context, CancellationToken cancellationToken = default)
        {
            Configuration = context.Shell.Configuration.Clone();
            return Task.FromResult(refuseLimit99 && Configuration.GetProperty("Widget").GetProperty("Limit").ToString() == "99"
                ? FeatureActivationDecision.Refused(new FeatureActivationRefusal("Configured", "effective-limit-99")) : FeatureActivationDecision.Allowed);
        }
    }

}
