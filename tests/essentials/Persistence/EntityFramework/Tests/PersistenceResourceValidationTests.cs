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
        var context = new ShellSettingsPreparationContext(
            new ShellId("default"),
            new Dictionary<string, string?>
            {
                [$"Elsa:Persistence:Bindings:{Aggregate}"] = "primary"
            },
            [Aggregate, Execution], [], [],
            [
                new ShellFeaturePreparationDescriptor(Aggregate, [], typeof(RuntimeEntityFrameworkCoreFeature), false),
                new ShellFeaturePreparationDescriptor(Execution, [], typeof(RuntimeWorkflowExecutionEntityFrameworkCoreFeature), true)
            ],
            [Aggregate, Execution], [], []);
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
}
