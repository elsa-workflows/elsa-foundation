using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Samples.Dashboard;

[ShellFeature(
    name: "DashboardSample",
    DisplayName = "Dashboard sample module",
    Description = "Contributes the frontend-only dashboard sample Studio module manifest."
)]
public sealed class DashboardStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDashboardStudioSample();
    }
}
