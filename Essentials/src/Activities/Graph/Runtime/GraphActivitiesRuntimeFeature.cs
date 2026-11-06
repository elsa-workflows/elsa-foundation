using CShells.Features;
using Elsa.Activities.Graph.Runtime.Activation;
using Elsa.Activities.Graph.Runtime.Services;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Graph.Runtime;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesGraphRuntime",
    DisplayName = "Activities Graph (Runtime)",
    Description = "Runtime construction and activity-execution scoping for reusable activity graphs.",
    DependsOn = new object[] { "ActivitiesRuntime" })]
public sealed class GraphActivitiesRuntimeFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityActivationStrategy, GraphActivityActivationStrategy>());
        // Distinct type per capability -- see ClrActivityConsumerCapability's remarks.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeActivityConsumerCapability, GraphActivityConsumerCapability>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeTestScopeContractSource, GraphTestScopeContractSource>());
        services.AddSingleton<GraphActivityScopeFactory>();
        services.AddSingleton<GraphActivityRecovery>();
    }
}
