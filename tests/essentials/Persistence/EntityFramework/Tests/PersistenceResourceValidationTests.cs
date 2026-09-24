using CShells;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class PersistenceResourceValidationTests
{
    private const string Aggregate = "WorkflowsRuntimeEntityFrameworkCore";
    private const string Execution = "WorkflowsRuntimeWorkflowExecutionEntityFrameworkCorePersistence";

    [Fact]
    public void Mixed_shared_context_with_opaque_legacy_configurator_refuses_before_materialization()
    {
        var context = RuntimeContext(new Dictionary<string, string?>
        {
            [$"Elsa:Persistence:Bindings:{Aggregate}"] = "primary"
        }, opaqueExecution: true);
        var root = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Elsa",
            ["ConnectionStrings:Elsa"] = "Data Source=shared.db;Password=connection-canary"
        }).Build();

        var result = EfPersistencePreparation.Prepare(context, root);

        Assert.True(result.HasApplicableResource);
        Assert.Contains("resource-ownership-unresolved", result.RefusalCodes);
        Assert.Empty(result.Patch.ConfigurationData);
        Assert.DoesNotContain("connection-canary", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Distinct_runtime_references_are_unverified_offline_but_valid_when_live_values_match()
    {
        var context = RuntimeContext(new Dictionary<string, string?>
        {
            [$"Elsa:Persistence:Bindings:{Aggregate}"] = "primary",
            [$"Elsa:Persistence:Bindings:{Execution}"] = "alias"
        });
        var root = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "SharedA",
            ["Elsa:Persistence:Resources:alias:Provider"] = "Sqlite",
            ["Elsa:Persistence:Resources:alias:ConnectionName"] = "SharedB",
            ["ConnectionStrings:SharedA"] = "Data Source=shared.db",
            ["ConnectionStrings:SharedB"] = "Data Source=shared.db"
        }).Build();

        var offline = EfPersistencePreparation.Prepare(context, root, verifyConnectionValues: false);
        var live = EfPersistencePreparation.Prepare(context, root);

        Assert.Empty(offline.RefusalCodes);
        Assert.Contains("target-affinity-unverified", offline.UnresolvedCodes);
        Assert.Equal(4, offline.Patch.ConfigurationData.Count);
        Assert.Empty(live.RefusalCodes);
        Assert.DoesNotContain("target-affinity-unverified", live.UnresolvedCodes);
        Assert.DoesNotContain("Data Source=", System.Text.Json.JsonSerializer.Serialize(live));
    }

    private static ShellSettingsPreparationContext RuntimeContext(
        IReadOnlyDictionary<string, string?> configuration,
        bool opaqueExecution = false) =>
        new(new ShellId("default"), configuration, [Aggregate, Execution], [], [],
            [
                new ShellFeaturePreparationDescriptor(Aggregate, [], typeof(RuntimeEntityFrameworkCoreFeature), false),
                new ShellFeaturePreparationDescriptor(Execution, [], typeof(RuntimeWorkflowExecutionEntityFrameworkCoreFeature), opaqueExecution)
            ],
            [Aggregate, Execution], [], []);
}
