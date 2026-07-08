using CShells.AspNetCore.Features;
using CShells.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Web;

internal static class ThemeStoreFeatureNames
{
    public const string Core = "ThemeStoreCore";
    public const string Picker = "ThemePicker";
    public const string Management = "ThemeManagement";
}

[ShellFeature(
    name: ThemeStoreFeatureNames.Core,
    DisplayName = "Theme Store core",
    Description = "Hosts the Studio Theme Store API and shared theme services."
)]
public sealed class ThemeStoreCoreStudioFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapElsaThemeStoreCoreApi(ModuleManagementAuth.PolicyName);
    }
}

[ShellFeature(
    name: ThemeStoreFeatureNames.Picker,
    DisplayName = "Theme picker",
    Description = "Enables the Studio top-bar theme picker.",
    DependsOn = [ThemeStoreFeatureNames.Core]
)]
public sealed class ThemePickerStudioFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapElsaThemePickerApi(ModuleManagementAuth.PolicyName);
    }
}

[ShellFeature(
    name: ThemeStoreFeatureNames.Management,
    DisplayName = "Theme management",
    Description = "Enables the Studio Theme Builder management surface.",
    DependsOn = [ThemeStoreFeatureNames.Core]
)]
public sealed class ThemeManagementStudioFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapElsaThemeManagementApi(ModuleManagementAuth.PolicyName);
    }
}
