using CShells.Features;
using Elsa.Activities.Graph.Runtime.Activation;
using Elsa.Activities.Graph.Runtime.Services;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Graph.Runtime;

/// <summary>Provides graph activity capabilities and descriptors for hosts with their own workflow runner.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Composition")]
[ShellFeature(
    name: "ActivitiesGraphRuntimeHosting",
    DisplayName = "Activities Graph Hosting",
    Description = "Runtime construction and activity-execution scoping for reusable activity graphs.",
    DependsOn = new object[] { "ActivitiesRuntimeHosting" })]
public sealed class GraphActivitiesRuntimeHostingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityActivationStrategy, GraphActivityActivationStrategy>());
        // Distinct type per capability -- see ClrActivityConsumerCapability's remarks.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeActivityConsumerCapability, GraphActivityConsumerCapability>());
        services.AddSingleton<GraphActivityScopeFactory>();
        services.AddSingleton<GraphActivityRecovery>();
    }
}
