using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Runtime.Api;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class ActivitiesFlowchartFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersFlowchartStructureHandler()
    {
        var services = new ServiceCollection();

        new ActivitiesFlowchartFeature().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var handler = Assert.Single(provider.GetServices<IActivityStructureHandler>());
        Assert.Equal(global::Elsa.Activities.Flowchart.Activities.Flowchart.StructureKind, handler.Kind);
        Assert.Equal(global::Elsa.Activities.Flowchart.Activities.Flowchart.StructureSchemaVersion, handler.SchemaVersion);
    }

    [Fact]
    public void ConfigureServices_RegistersFlowchartExecutionServicesAndBuiltInPolicies()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        new ActivitiesFlowchartFeature().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<FlowchartReachabilityAnalyzer>());
        Assert.NotNull(provider.GetRequiredService<FlowchartExecutionEngine>());

        var registry = provider.GetRequiredService<IFlowchartPolicyRegistry>();
        var policyKinds = registry.Policies.Select(policy => policy.PolicyKind).ToHashSet(StringComparer.Ordinal);
        Assert.True(policyKinds.IsSupersetOf([
            FlowchartPolicyKinds.Decision,
            FlowchartPolicyKinds.ParallelFork,
            FlowchartPolicyKinds.ParallelJoin,
            FlowchartPolicyKinds.InclusiveFork,
            FlowchartPolicyKinds.InclusiveJoin,
            FlowchartPolicyKinds.FirstWins,
            FlowchartPolicyKinds.Merge,
            FlowchartPolicyKinds.ImplicitActivationJoin,
            FlowchartPolicyKinds.DirectContinuation
        ]));
    }
}
