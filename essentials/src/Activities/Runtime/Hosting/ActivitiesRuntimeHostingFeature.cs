using CShells.Features;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Runtime.Tasks;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Runtime;

/// <summary>Registers activity CLR aliases and validates the workflow secret contracts in a shell.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "ActivitiesRuntimeHosting",
    DisplayName = "Activities Runtime Hosting",
    Description = "Registers activity CLR aliases and validates workflow secret registrations.",
    DependsOn = new object[] { "Events", "Tasks", "Serialization" }
)]
public sealed class ActivitiesRuntimeHostingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        RuntimeSecretResolverCompositionValidator.Register(services);
        RuntimeSecretMaskCompositionValidator.Register(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, RegisterActivityTypesStartupTask>());
    }
}
