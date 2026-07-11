using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Http.Core.Contracts;
using Elsa.Primitives.Extensions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Services;
using Elsa.Workflows.Runtime.Http.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Http;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("HTTP")]
[ShellFeature(
    name: "WorkflowsRuntimeHttp",
    DisplayName = "Workflows Runtime HTTP",
    Description = "Provides HTTP endpoint routing, authorization, and fault handling for workflow runtime endpoints.",
    // Http contributes the IRouteTable implementation this feature refreshes; WorkflowsRuntimeTriggers contributes
    // the IWorkflowTriggerBindingStore the resolver reads and the IWorkflowTriggerIndexer the observer hooks.
    DependsOn = new object[] { "Http", "WorkflowsRuntimeTriggers" }
)]
public class WorkflowsRuntimeHttpFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Fault handler type", Description = "CLR type name of the HTTP endpoint fault handler implementation.", Category = "Services", Advanced = true)]
    public string FaultHandlerType { get; set; } = typeof(HttpEndpointFaultHandler).GetSimpleAssemblyQualifiedName();

    [ManifestSetting(DisplayName = "Authorization handler type", Description = "CLR type name of the HTTP endpoint authorization handler implementation.", Category = "Services", Advanced = true)]
    public string AuthorizationHandlerType { get; set; } = typeof(AuthenticationBasedHttpEndpointAuthorizationHandler).GetSimpleAssemblyQualifiedName();

    [ManifestSetting(DisplayName = "Route resolver type", Description = "CLR type name of the HTTP endpoint route resolver implementation.", Category = "Services", Advanced = true)]
    public string RouteResolverType { get; set; } = typeof(HttpEndpointRoutesResolver).GetSimpleAssemblyQualifiedName();

    public void ConfigureServices(IServiceCollection services)
    {
        RegisterFaultHandler(services);
        RegisterAuthorizationHandler(services);
        RegisterRouteResolver(services);

        // The single serialization point for every route-table refresh (startup + publish observer + bookmark
        // observer): a singleton owning a SemaphoreSlim(1,1) that opens a fresh scope per refresh, so a stale read
        // can never clobber a newer swap and drop a live route (spec 089 D review fix). TryAdd — a host may override.
        services.TryAddSingleton<IHttpEndpointRouteTableSynchronizer, HttpEndpointRouteTableSynchronizer>();

        // Rebuild the route table from the durable trigger index at startup, so a fresh host (or a restart)
        // has every published HTTP endpoint's route before the middleware runs.
        services.AddScoped<IStartupTask, UpdateRouteTableStartupTask>();

        // Keep the route table fresh on every publish: the trigger indexer notifies this observer after it
        // rewrites an artifact's bindings. Contribution seam (fan-in), so TryAddEnumerable.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowTriggerIndexObserver, RouteTableTriggerIndexObserver>());

        // Keep the route table fresh as instances suspend on / resume from mid-flow endpoints (spec 089 D): the
        // bookmark lifecycle notifier calls this observer after a bookmark create/consume commits, and it re-projects
        // the whole table (trigger bindings ∪ waiting bookmarks). Contribution seam (fan-in), so TryAddEnumerable.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBookmarkLifecycleObserver, RouteTableBookmarkObserver>());

        // Publish-time (template, method) uniqueness (issue #592 item 2): validates the extracted binding set on
        // the indexer's PRE-write seam so a cross-definition conflict fails the second publish with the durable
        // index untouched. HTTP-specific by design — shared stimulus identity is legitimate fan-out for other
        // stimulus types. Contribution seam (fan-in), so TryAddEnumerable.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWorkflowTriggerIndexValidator, HttpEndpointRoutingUniquenessValidator>());
    }

    private void RegisterFaultHandler(IServiceCollection services)
    {
        var type = FaultHandlerType.GetLoadedType();
        services.AddScoped(typeof(IHttpEndpointFaultHandler), type);
    }

    private void RegisterAuthorizationHandler(IServiceCollection services)
    {
        var type = AuthorizationHandlerType.GetLoadedType();
        services.AddScoped(typeof(IHttpEndpointAuthorizationHandler), type);
    }

    private void RegisterRouteResolver(IServiceCollection services)
    {
        var type = RouteResolverType.GetLoadedType();
        services.AddScoped(typeof(IHttpEndpointRoutesResolver), type);
    }
}
