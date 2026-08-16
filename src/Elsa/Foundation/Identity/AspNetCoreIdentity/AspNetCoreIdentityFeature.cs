using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityAspNetCoreIdentity",
    DisplayName = "Foundation Identity ASP.NET Core Identity",
    Description = "Registers the ASP.NET Core Identity-backed IAM substrate, cookie sign-in surface, and principal factory."
)]
public class AspNetCoreIdentityFeature : IWebShellFeature
{
    [ManifestSetting(DisplayName = "Is default provider", Description = "Advertises this password/login-page provider as the default in bootstrap, so Studio challenges it (redirecting to the login page) when a session is required.", Category = "Identity", DefaultValue = "false")]
    public bool IsDefault { get; set; }

    [ManifestSetting(DisplayName = "Allowed return URL origins", Description = "Absolute origins (e.g. https://localhost:7030) a post-login returnUrl may redirect back to, in addition to same-origin paths. Set to the Studio origin when Studio is hosted separately from the backend.", Category = "Identity")]
    public string[]? AllowedReturnUrlOrigins { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationAspNetCoreIdentity(options =>
        {
            options.IsDefault = IsDefault;
            if (AllowedReturnUrlOrigins is { Length: > 0 })
                options.AllowedReturnUrlOrigins = AllowedReturnUrlOrigins;
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        AspNetCoreIdentityApi.MapAspNetCoreIdentityApi(endpoints);
}
