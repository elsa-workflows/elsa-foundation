using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Http.Core.Contracts;
using Elsa.Primitives.Extensions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Options;
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
    // the IWorkflowTriggerBindingStore the resolver reads and the IWorkflowTriggerIndexer the observer hooks; Tasks runs
    // the startup task that fills the table and the pump that keeps it converged with other nodes (#2190).
    DependsOn = new object[] { "Http", "WorkflowsRuntimeTriggers", "Tasks" }
)]
public class WorkflowsRuntimeHttpFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Fault handler type", Description = "CLR type name of the HTTP endpoint fault handler implementation.", Category = "Services", Advanced = true)]
    public string FaultHandlerType { get; set; } = typeof(HttpEndpointFaultHandler).GetSimpleAssemblyQualifiedName();

    [ManifestSetting(DisplayName = "Authorization handler type", Description = "CLR type name of the HTTP endpoint authorization handler implementation.", Category = "Services", Advanced = true)]
    public string AuthorizationHandlerType { get; set; } = typeof(AuthenticationBasedHttpEndpointAuthorizationHandler).GetSimpleAssemblyQualifiedName();

    [ManifestSetting(DisplayName = "Route resolver type", Description = "CLR type name of the HTTP endpoint route resolver implementation.", Category = "Services", Advanced = true)]
    public string RouteResolverType { get; set; } = typeof(HttpEndpointRoutesResolver).GetSimpleAssemblyQualifiedName();

    [ManifestSetting(DisplayName = "Route table convergence interval (seconds)", Description = "Seconds between checks that pick up HTTP endpoints published, and HTTP bookmarks created or consumed, on other nodes: the bound on how long such an endpoint can return 404 on this node. A check reads only stimulus identities; the route table is rebuilt only when they changed.", Category = "Runtime", DefaultValue = "5")]
    public double RouteTableConvergenceIntervalSeconds { get; set; } = 5;

    [ManifestSetting(DisplayName = "Route table convergence max backoff (seconds)", Description = "Upper bound the convergence interval widens to while checks keep failing.", Category = "Runtime", DefaultValue = "60")]
    public double RouteTableConvergenceMaxBackoffSeconds { get; set; } = 60;

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

        // Converge with changes made on other nodes (#2190): the observers below fire only where the change was made,
        // so every node checks the durable index's stimulus identities on an interval and rebuilds when they moved.
        // The settings are validated here so a bad value fails the shell, not the first tick.
        var interval = PositiveSeconds(RouteTableConvergenceIntervalSeconds, nameof(RouteTableConvergenceIntervalSeconds));
        var maxBackoffInterval = PositiveSeconds(RouteTableConvergenceMaxBackoffSeconds, nameof(RouteTableConvergenceMaxBackoffSeconds));
        services.Configure<HttpEndpointRouteTableConvergenceOptions>(options =>
        {
            options.Interval = interval;
            options.MaxBackoffInterval = maxBackoffInterval;
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecurringTask, HttpEndpointRouteTableConvergencePumpTask>());

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
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowTriggerIndexValidator, HttpEndpointRoutingUniquenessValidator>());
    }

    private static TimeSpan PositiveSeconds(double seconds, string setting) =>
        double.IsFinite(seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : throw new ArgumentOutOfRangeException(setting, seconds, $"{setting} must be a positive number of seconds.");

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
