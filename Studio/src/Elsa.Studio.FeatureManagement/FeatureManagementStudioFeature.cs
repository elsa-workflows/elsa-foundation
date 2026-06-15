using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.FeatureManagement;

[ShellFeature(
    name: "FeatureManagement",
    DisplayName = "Feature management module",
    Description = "Contributes the Studio feature-management UI module."
)]
public sealed class FeatureManagementStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddFeatureManagementStudio();
    }
}
