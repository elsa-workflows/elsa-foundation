using CShells.Features;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Events;
using Elsa.Activities.Design.Disclosure.Handlers;
using Elsa.Activities.Design.Disclosure.Services;
using Elsa.Activities.Design.Disclosure.Tasks;
using Elsa.Events.Core.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Design.Disclosure;

/// <summary>Composes trusted admission and current policy evaluation for the single activity catalog.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ShellFeature(name: "ActivityCatalogDisclosure", DisplayName = "Activity Catalog Disclosure",
    Description = "Retains catalog disclosure policy composition and trusted producer admission.",
    DependsOn = new object[] { "Events", "Tasks" })]
public class ActivityCatalogDisclosureFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<CatalogDisclosureRegistry>();
        services.TryAddScoped<CatalogDisclosureRequirementCanonicalizer>();
        services.TryAddScoped<IActivityCatalogDisclosureEvaluator, ActivityCatalogDisclosureEvaluator>();
        services.TryAddScoped<IActivityCatalogAdmission, ActivityCatalogAdmission>();
        services.TryAddScoped<ICatalogProducerScope, CatalogProducerScope>();
        services.TryAddScoped<IActivityCatalogDisclosureAuthority, UnavailableCatalogDisclosureAuthority>();
        services.TryAddScoped<IActivityCatalogOwnerScopeAccessor, UnavailableCatalogOwnerScopeAccessor>();
        // A child shell's final collection is its own composition, even if parent descriptors were copied.
        services.RemoveAll<CatalogDisclosureComposition>();
        services.AddSingleton(new CatalogDisclosureComposition(services));
        services.AddEventHandler<ActivityCatalogDisclosureInitializing, CollectActivityCatalogDisclosureRegistrations>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, InitializeCatalogDisclosure>());
    }
}
