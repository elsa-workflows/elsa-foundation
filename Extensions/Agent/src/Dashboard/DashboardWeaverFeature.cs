using CShells.Features;
using CShells.Lifecycle;
using Elsa.Agent;
using Elsa.Agent.Core.Contracts;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Dashboard.Core.Authoring;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Weaver")]
[ShellFeature(name: "StudioDashboardWeaver", DisplayName = "Dashboard authoring with Weaver",
    Description = "Authors validated dashboard questions through the configured Weaver harness.",
    DependsOn = [typeof(FoundationAgentAbstractionsFeature), "FoundationAgentApi", typeof(ApiCapabilitiesFeature), "StudioDashboardApi"])]
public class DashboardWeaverFeature : FoundationAgentAbstractionsFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.studio-dashboard-authoring", 1,
            [new("agent-bootstrap", "_elsa/agent/bootstrap"), new("agent-sessions", "_elsa/agent/sessions")], "StudioDashboardWeaver"));
        services.TryAddScoped<IDashboardAuthoringService, WeaverDashboardAuthoringService>();
        services.TryAddScoped<DashboardWeaverAccess>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentContextProvider, DashboardAuthoringContextProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, DashboardDatasetTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, DashboardQuestionTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, DashboardClarificationTool>());
        var validation = new DashboardWeaverRegistrationCheck(services);
        services.AddSingleton<IShellInitializer>(validation);
    }
}

internal sealed class DashboardWeaverRegistrationCheck(IServiceCollection services) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var type in new[] { typeof(IDashboardAuthoringService), typeof(IDashboardActorContext),
                     typeof(IDashboardQueryService), typeof(IDashboardResourceService) })
            if (services.Count(x => x.ServiceType == type) != 1)
                throw new InvalidOperationException($"Dashboard Weaver requires exactly one {type.Name} registration.");
        return Task.CompletedTask;
    }
}
