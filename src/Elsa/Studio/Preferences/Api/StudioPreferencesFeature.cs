using CShells.Features;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Preferences.Core;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Studio")]
[ShellFeature(
    name: "StudioPreferences",
    DisplayName = "Studio Preferences",
    Description = "Provides governed, user and tenant scoped Studio preference documents.")]
public sealed class StudioPreferencesFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStudioPreferenceNamespace, DashboardPreferenceNamespace>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStudioPreferenceNamespace, AttentionPreferenceNamespace>());
        services.TryAddSingleton<IStudioPreferenceNamespaceRegistry, StudioPreferenceNamespaceRegistry>();
        services.TryAddSingleton<IStudioPreferenceStore, InMemoryStudioPreferenceStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IStudioPreferenceService, StudioPreferenceService>();
        services.AddPermissionContributor<StudioPreferencesPermissionContributor>();
    }
}
