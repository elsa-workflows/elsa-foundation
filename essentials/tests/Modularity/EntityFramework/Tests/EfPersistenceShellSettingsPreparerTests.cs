using CShells;
using CShells.Lifecycle;
using Elsa.Modularity.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Modularity.EntityFramework.Tests;

public sealed class EfPersistenceShellSettingsPreparerTests
{
    private const string FeatureId = "WorkflowsRuntimeEntityFrameworkCore";

    [Fact]
    public async Task Resource_selection_patches_only_the_target_fields_before_feature_construction()
    {
        var root = Configuration(
            ("Elsa:Persistence:DefaultResource", "primary"),
            ("Elsa:Persistence:Resources:primary:Provider", "Sqlite"),
            ("Elsa:Persistence:Resources:primary:ConnectionName", "Shared"),
            ("ConnectionStrings:Shared", "Data Source=shared.db"));
        var settings = new Dictionary<string, string?> { ["Other:Setting"] = "keep" };
        var context = Context(settings);

        var patch = await new EfPersistenceShellSettingsPreparer(root).PrepareAsync(context);

        Assert.Equal(2, patch.ConfigurationData.Count);
        Assert.Equal("Sqlite", patch.ConfigurationData[$"{FeatureId}:Provider"]);
        Assert.Equal("Shared", patch.ConfigurationData[$"{FeatureId}:ConnectionName"]);
        Assert.Equal("keep", context.ConfigurationData["Other:Setting"]);
        Assert.DoesNotContain("ConnectionStrings:Shared", patch.ConfigurationData.Keys);
    }

    [Fact]
    public async Task Missing_named_connection_refuses_without_disclosing_its_name_or_value()
    {
        var root = Configuration(
            ("Elsa:Persistence:DefaultResource", "primary"),
            ("Elsa:Persistence:Resources:primary:Provider", "Sqlite"),
            ("Elsa:Persistence:Resources:primary:ConnectionName", "SecretConnectionCanary"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfPersistenceShellSettingsPreparer(root).PrepareAsync(Context()));

        Assert.Contains("resource-definition-invalid", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretConnectionCanary", error.ToString(), StringComparison.Ordinal);
    }

    private static ShellSettingsPreparationContext Context(IReadOnlyDictionary<string, string?>? values = null) =>
        new(new ShellId("default"), values ?? new Dictionary<string, string?>(), [FeatureId], [], [],
            [new ShellFeaturePreparationDescriptor(FeatureId, [], typeof(RuntimeEntityFrameworkCoreFeature), false)],
            [FeatureId], [], []);

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(x =>
            new KeyValuePair<string, string?>(x.Key, x.Value))).Build();
}
