using System.Text.Json;
using CShells.Configuration;
using CShells.Features;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Testing;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    private sealed class ReloadingHostDefaults : IEfToolingShellDefaults
    {
        public void Configure(ShellBuilder builder, IConfiguration configuration) =>
            ((IConfigurationRoot)configuration).Reload();
    }
}
