using CShells;
using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class SharedPersistenceLayoutTests
{
    private static readonly (string Id, Type StartupType)[] SharedConsumers =
    [
        ("WorkflowsRuntimeEntityFrameworkCore", typeof(RuntimeEntityFrameworkCoreFeature)),
        ("WorkflowsRuntimeWorkflowExecutionEntityFrameworkCorePersistence", typeof(RuntimeWorkflowExecutionEntityFrameworkCoreFeature)),
        ("WorkflowsRuntimeActivityExecutionEntityFrameworkCorePersistence", typeof(RuntimeActivityExecutionEntityFrameworkCoreFeature)),
        ("WorkflowsRuntimeOperationalStateEntityFrameworkCorePersistence", typeof(RuntimeOperationalStateEntityFrameworkCoreFeature)),
        ("WorkflowsRuntimeAlterationEntityFrameworkCorePersistence", typeof(RuntimeWorkflowAlterationEntityFrameworkCoreFeature)),
        ("WorkflowsRuntimeTestScopeEntityFrameworkCorePersistence", typeof(RuntimeWorkflowTestScopeEntityFrameworkCoreFeature)),
        ("WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence", typeof(RuntimeBookmarksEntityFrameworkCoreFeature)),
        ("WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence", typeof(RuntimeArtifactsEntityFrameworkCoreFeature)),
        ("WorkflowsDesignEntityFrameworkCore", typeof(WorkflowsDesignEntityFrameworkCoreFeature)),
        ("ActivitiesDesignEntityFrameworkCore", typeof(ActivitiesDesignEntityFrameworkCoreFeature)),
        ("WorkflowsPublishingEntityFrameworkCore", typeof(PublishingEntityFrameworkCoreFeature))
    ];

    [Fact]
    public void One_root_resource_materializes_an_atomic_target_for_every_shared_consumer()
    {
        var result = EfPersistencePreparation.Prepare(Context(), Configuration(includeProvider: true));

        Assert.True(result.HasApplicableResource);
        Assert.Empty(result.RefusalCodes);
        Assert.Equal(SharedConsumers.Length * 2, result.Patch.ConfigurationData.Count);
        Assert.Equal(SharedConsumers.Select(x => x.Id).Order(StringComparer.Ordinal),
            result.Participants.Select(x => x.FeatureId).Order(StringComparer.Ordinal));
        foreach (var (id, _) in SharedConsumers)
        {
            Assert.Equal("Sqlite", result.Patch.ConfigurationData[$"{id}:Provider"]);
            Assert.Equal("Shared", result.Patch.ConfigurationData[$"{id}:ConnectionName"]);
        }
        Assert.DoesNotContain("secret-canary", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Missing_resource_provider_refuses_the_whole_layout_before_sqlite_defaults_can_apply()
    {
        var result = EfPersistencePreparation.Prepare(Context(), Configuration(includeProvider: false));

        Assert.True(result.HasApplicableResource);
        Assert.Contains("resource-definition-invalid", result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
        Assert.DoesNotContain("secret-canary", System.Text.Json.JsonSerializer.Serialize(result));
    }

    private static ShellSettingsPreparationContext Context()
    {
        var ids = SharedConsumers.Select(x => x.Id).ToArray();
        var descriptors = SharedConsumers.Select(x =>
            new ShellFeaturePreparationDescriptor(x.Id, [], x.StartupType, false)).ToArray();
        return new ShellSettingsPreparationContext(new ShellId("default"),
            new Dictionary<string, string?>(), ids, [], [], descriptors, ids, [], []);
    }

    private static IConfiguration Configuration(bool includeProvider)
    {
        var values = new Dictionary<string, string?>
        {
            ["Elsa:Persistence:DefaultResource"] = "primary",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
            ["ConnectionStrings:Shared"] = "Data Source=shared.db;Password=secret-canary"
        };
        if (includeProvider)
            values["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
