using CShells.Features;
using Elsa.Events.Core.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Primitives.Extensions;
using Elsa.Tasks.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Options;
using Elsa.Workflows.Runtime.Http.Services;
using Elsa.Workflows.Runtime.Http.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Http;

/// <summary>Projects durable standalone HTTP reservations into the shell route table.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("HTTP")]
[ShellFeature(
    name: "WorkflowsRuntimeStandaloneHttp",
    DisplayName = "Workflow Standalone HTTP",
    Description = "Provides standalone HTTP route projection, authorization and fault handling without the workflow runner.",
    DependsOn = new object[] { "Http", "Tasks" }
)]
public sealed class WorkflowsRuntimeStandaloneHttpFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Fault handler type", Description = "CLR type name of the HTTP endpoint fault handler implementation.", Category = "Services", Advanced = true)]
    public string FaultHandlerType { get; set; } = typeof(HttpEndpointFaultHandler).GetSimpleAssemblyQualifiedName();

    [ManifestSetting(DisplayName = "Authorization handler type", Description = "CLR type name of the HTTP endpoint authorization handler implementation.", Category = "Services", Advanced = true)]
    public string AuthorizationHandlerType { get; set; } = typeof(AuthenticationBasedHttpEndpointAuthorizationHandler).GetSimpleAssemblyQualifiedName();

    [ManifestSetting(DisplayName = "Route table convergence interval (seconds)", Description = "Seconds between checks for changed reservations on other nodes.", Category = "Runtime", DefaultValue = "5")]
    public double RouteTableConvergenceIntervalSeconds { get; set; } = 5;

    [ManifestSetting(DisplayName = "Route table convergence max backoff (seconds)", Description = "Maximum interval while checks keep failing.", Category = "Runtime", DefaultValue = "60")]
    public double RouteTableConvergenceMaxBackoffSeconds { get; set; } = 60;

    public void ConfigureServices(IServiceCollection services)
    {
        // AuthenticationBasedHttpEndpointAuthorizationHandler needs IAuthorizationService. Hosts that enable
        // this feature without FoundationIdentityAbstractions still have to resolve that handler when the
        // standalone HTTP trigger middleware is constructed.
        services.AddAuthorization();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIngressReservationPolicy, HttpIngressReservationPolicy>());
        services.AddScoped(typeof(IHttpEndpointFaultHandler), FaultHandlerType.GetLoadedType());
        services.AddScoped(typeof(IHttpEndpointAuthorizationHandler), AuthorizationHandlerType.GetLoadedType());

        // Startup and reservation commits serialize route-table refreshes. Each refresh opens a fresh scope.
        services.TryAddSingleton<IHttpEndpointRouteTableSynchronizer, HttpEndpointRouteTableSynchronizer>();
        services.AddEventHandler<StandaloneTriggerResourceCommitted, StandaloneTriggerRouteObserver>();

        // Restore durable standalone routes before request intake.
        services.AddScoped<IStartupTask, UpdateRouteTableStartupTask>();

        // The pump picks up durable reservation changes committed on other nodes.
        var interval = PositiveSeconds(RouteTableConvergenceIntervalSeconds, nameof(RouteTableConvergenceIntervalSeconds));
        var maxBackoffInterval = PositiveSeconds(RouteTableConvergenceMaxBackoffSeconds, nameof(RouteTableConvergenceMaxBackoffSeconds));
        services.Configure<HttpEndpointRouteTableConvergenceOptions>(options =>
        {
            options.Interval = interval;
            options.MaxBackoffInterval = maxBackoffInterval;
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecurringTask, HttpEndpointRouteTableConvergencePumpTask>());

        services.AddScoped<IHttpEndpointRoutesResolver>(provider => new HttpEndpointRoutesResolver(null, null,
            provider.GetRequiredService<ILogger<HttpEndpointRoutesResolver>>(),
            provider.GetService<IRuntimeIngressReservationReader>(),
            provider.GetService<IPersistenceAccessContextAccessor>()));
    }

    // The one validation of the convergence settings: positive, finite and representable as a TimeSpan. NaN and the
    // infinities fail both comparisons.
    private static TimeSpan PositiveSeconds(double seconds, string setting) =>
        seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(seconds)
            : throw new ArgumentOutOfRangeException(setting, seconds, $"{setting} must be a positive, finite number of seconds.");

}
