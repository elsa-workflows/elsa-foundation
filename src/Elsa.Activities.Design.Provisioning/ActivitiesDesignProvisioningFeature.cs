using CShells.Features;
using Elsa.Activities.Design.Provisioning.Core;
using Elsa.Activities.Design.Provisioning.Options;
using Elsa.Activities.Design.Provisioning.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Provisioning;

[ShellFeature(
    name: "ActivitiesDesignProvisioning"
)]
public class ActivitiesDesignProvisioningFeature : IShellFeature
{
    public ActivityVersionProvisionerOptions ProvisionerOptions { get; set; } = new();

    public ActivityVersionProvisionerStartupTaskOptions StartupTaskOptions { get; set; } = new();

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(ProvisionerOptions));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(StartupTaskOptions));
        services.AddScoped<IActivityVersionProvisioner, ActivityVersionProvisioner>();
    }
}
