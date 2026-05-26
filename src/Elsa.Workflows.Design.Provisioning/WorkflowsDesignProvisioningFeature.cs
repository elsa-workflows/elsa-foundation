using CShells.Features;
using Elsa.Workflows.Design.Provisioning.Core.Contracts;
using Elsa.Workflows.Design.Provisioning.Options;
using Elsa.Workflows.Design.Provisioning.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Provisioning;

[ShellFeature(
    name: "WorkflowsDesignProvisioning"
)]
public class WorkflowsDesignProvisioningFeature : IShellFeature
{
    public WorkflowVersionProvisionerOptions ProvisionerOptions { get; set; } = new();

    public WorkflowVersionProvisionerStartupTaskOptions StartupTaskOptions { get; set; } = new();

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(ProvisionerOptions));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(StartupTaskOptions));
        services.AddScoped<IWorkflowVersionProvisioner, WorkflowsVersionProvisioner>();
    }
}
