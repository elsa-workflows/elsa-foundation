using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Api.FastEndpoints;
using Elsa.Mediator.Core.Extensions;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Api;

/// <summary>
/// The publishing surface — today a single bridge over the activity-construction seam, tomorrow the
/// seed of the compile-and-publish domain. Its endpoints read a persisted activity definition (the
/// Design seam) and invoke <c>IActivityFactory</c> (the Runtime seam) to materialise a live
/// <c>IActivity</c>. The feature depends only on the two seams' <c>.Core</c> contracts; it is neither
/// Design nor Runtime, which is why it may bridge them without breaking §E2.2.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Publishing")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "WorkflowsPublishingApi",
    DisplayName = "Workflows Publishing API",
    Description = "Bridge endpoints that construct a live activity from a persisted catalog row (the construction seam).",
    DependsOn = new object[] { "WorkflowsRuntimeTriggers" }
)]
public class WorkflowsPublishingApiFeature : FastEndpointsFeatureBase
{
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var assembly = GetType().Assembly;

        services.TryAddSingleton<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        services.TryAddScoped<IActivityStructureService, DefaultActivityStructureService>();
        // W30b (#418): WorkflowExecutableCompiler decomposition collaborators. Registered at the compiler's own
        // scoped lifetime so each is independently resolvable, replaceable, and unit-testable.
        services.TryAddScoped<RuntimeInputBindingCompiler>();
        services.TryAddScoped<IWorkflowExecutableCompiler, WorkflowExecutableCompiler>();
        services.TryAddSingleton<IWorkflowTestRunStore, InMemoryWorkflowTestRunStore>();
        services.TryAddSingleton<ITransientWorkflowExecutableStore, InMemoryTransientWorkflowExecutableStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddRequestHandlersFrom(assembly);
    }
}
