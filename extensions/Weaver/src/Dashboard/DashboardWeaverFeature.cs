using CShells.Features;
using CShells.Lifecycle;
using Elsa.Weaver;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Dashboard.Core.Authoring;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Dashboard;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Weaver")]
[ShellFeature(name: "StudioDashboardWeaver", DisplayName = "Dashboard authoring with Weaver",
    Description = "Authors validated dashboard questions through the configured Weaver harness.",
    DependsOn = [typeof(WeaverFeature), "WeaverApi", typeof(ApiCapabilitiesFeature), "StudioDashboardApi"])]
public class DashboardWeaverFeature : WeaverFeature
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.studio-dashboard-authoring", 1,
            [new("weaver-bootstrap", "_elsa/weaver/bootstrap"), new("weaver-sessions", "_elsa/weaver/sessions")], "StudioDashboardWeaver"));
        services.TryAddScoped<IDashboardAuthoringService, WeaverDashboardAuthoringService>();
        services.TryAddScoped<DashboardWeaverAccess>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverContextProvider, DashboardAuthoringContextProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWeaverCapabilityProvider, DashboardWeaverCapabilityProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, DashboardDatasetTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, DashboardQuestionTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWeaverTool, DashboardClarificationTool>());
        var validation = new DashboardWeaverRegistrationCheck(services);
        services.AddSingleton<IShellInitializer>(validation);
    }
}

internal sealed class DashboardWeaverCapabilityProvider : IWeaverCapabilityProvider
{
    public ValueTask<IReadOnlyCollection<WeaverCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyCollection<WeaverCapability>>([
            new("dashboards.authoring", DashboardWeaverProtocol.ModuleId, "Author dashboard questions",
                "Clarifies and proposes supported declarative questions. Native Preview, Use and Save remain explicit viewer-authorized actions.",
                WeaverCapabilityKind.Answer, WeaverRisk.ReadOnly, ["/questions", "/dashboards"],
                ["studio/questions:read"], [DashboardWeaverProtocol.ContextKind]) { Modes = ["build"] }
        ]);
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
