using Elsa.Studio.Api.Authorization;
using CShells.AspNetCore.Features;
using CShells.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Hosting.Themes;

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
public class ThemeStoreCoreStudioFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapElsaThemeStoreCoreApi(StudioBridgeAuthorization.PolicyName);
    }
}

[ShellFeature(
    name: ThemeStoreFeatureNames.Picker,
    DisplayName = "Theme picker",
    Description = "Enables the Studio top-bar theme picker.",
    DependsOn = [ThemeStoreFeatureNames.Core]
)]
public class ThemePickerStudioFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapElsaThemePickerApi(StudioBridgeAuthorization.PolicyName);
    }
}

[ShellFeature(
    name: ThemeStoreFeatureNames.Management,
    DisplayName = "Theme management",
    Description = "Enables the Studio Theme Builder management surface.",
    DependsOn = [ThemeStoreFeatureNames.Core]
)]
public class ThemeManagementStudioFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapElsaThemeManagementApi(StudioBridgeAuthorization.PolicyName);
    }
}
