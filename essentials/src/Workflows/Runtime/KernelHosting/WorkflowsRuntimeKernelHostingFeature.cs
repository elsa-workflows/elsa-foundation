using CShells;
using CShells.AspNetCore.Features;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Http.Options;
using Elsa.ActorFoundation.Hosting;
using Elsa.Api.AspNetCore;
using Elsa.Modularity.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.KernelApi;
using Elsa.Workflows.Runtime.KernelHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Runs the default Foundation shell's published workflows on the built-in Actor Foundation.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: FeatureName,
    DisplayName = "Workflows Runtime Kernel Hosting",
    Description = "Runs published Native and Flowchart workflows on the built-in Actor Foundation. Enable on the default shell at host startup.",
    DependsOn = new object[] { WorkflowsRuntimeKernelApiFeature.FeatureName, "ActivitiesHttp", "JavaScriptJintEngine", "Tasks" })]
public sealed class WorkflowsRuntimeKernelHostingFeature : IWebShellFeature
{
    public const string FeatureName = "WorkflowsRuntimeKernelHosting";

    [ManifestSetting(DisplayName = "Provider", Description = "Use SQLite or PostgreSQL.", Category = "Persistence", DefaultValue = "SQLite")]
    public string Provider { get; set; } = "SQLite";

    [ManifestSetting(DisplayName = "SQLite file", Description = "Absolute path to a dedicated local SQLite kernel database; its directory must already exist.", Category = "Persistence")]
    public string? SqlitePath { get; set; }

    [ManifestSetting(DisplayName = "PostgreSQL connection string", Description = "Connection string to a dedicated PostgreSQL kernel database.", Category = "Persistence", Secret = true)]
    public string? PostgresConnectionString { get; set; }

    [ManifestSetting(DisplayName = "Tenant", Description = "The tenant bound to this shell's kernel ingress and reads.", Category = "Runtime", DefaultValue = "default")]
    public string Tenant { get; set; } = "default";

    public void ConfigureServices(IServiceCollection services)
    {
        var storage = KernelShellStorage.Create(Provider, SqlitePath, PostgresConnectionString);
        var tenant = EmbeddedTenant.Bind(Tenant);
        var persistenceScope = new PersistenceScope(tenant.Tenant);
        services.AddSingleton(storage);
        services.AddSingleton(new ShellRestartRequirement(FeatureName));
        services.AddSingleton(new KernelServing(capabilities: new KernelHostCapabilities(
            Schedules: false, Timers: false, Children: false, SelfSends: storage.TakesSelfSends, HttpWaits: false),
            publicationScope: persistenceScope));
        services.AddSingleton<IScheduleActors>(new UnavailableScheduleActors(KernelHostCapabilities.SchedulesUnavailable));
        services.AddSingleton(sp => new KernelShellRuntime(storage, sp.GetRequiredService<KernelServing>(),
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILoggerFactory>(),
            persistenceScope));
        services.AddSingleton(sp => new KernelRuns(tenant, sp.GetRequiredService<KernelShellRuntime>().ReadAsync));
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<KernelShellRuntime>();
            var http = sp.GetRequiredService<IOptions<HttpEndpointOptions>>().Value;
            var shellPath = sp.GetRequiredService<ShellSettings>().GetConfigurationRoot()["WebRouting:Path"]?.Trim().Trim('/');
            var basePath = HttpBasePath(http);
            var requestBasePath = string.IsNullOrEmpty(shellPath) ? basePath : "/" + shellPath + basePath;
            return new KernelHttpRequestHandler(sp.GetRequiredService<KernelServing>(), runtime.IngressAsync,
                storage.ReadClockAsync, tenant, requestBasePath, http.SyncRequestTimeout, http.MaxRequestBodyBytes,
                enforceRequestTimeout: true);
        });
        KernelToolbox.Register(services);

        services.AddShellInitializer<PrepareKernelShell>(LifecyclePhase.Prepare, order: -100);
        // Task startup populates the activity catalog and makes the publishing stores ready first.
        services.AddShellInitializer<StartKernelShell>(LifecyclePhase.Start, order: 100);
        services.AddShellTerminator<StopKernelShell>(LifecyclePhase.Start, order: 100);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        var http = endpoints.ServiceProvider.GetRequiredService<IOptions<HttpEndpointOptions>>().Value;
        endpoints.Map(HttpBasePath(http) + "/{**path}", static (HttpContext context) =>
                context.RequestServices.GetRequiredService<KernelHttpRequestHandler>().HandleAsync(context))
            .WithOwner(FeatureName)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .AllowPublic("kernel workflow HTTP endpoints", "Starts a published workflow on its configured HTTP route and returns its response.");
    }

    internal static string HttpBasePath(HttpEndpointOptions options)
    {
        var basePath = "/" + options.BasePath.Trim().Trim('/');
        if (basePath == "/" || basePath.IndexOfAny(['?', '#', '%', '{', '}']) >= 0)
            throw new InvalidOperationException("Kernel workflow HTTP endpoints require a dedicated decoded base path below the root.");
        if (options.MaxRequestBodyBytes <= 0 || options.MaxRequestBodyBytes == long.MaxValue)
            throw new InvalidOperationException("Kernel workflow HTTP endpoints require a positive request body limit below Int64.MaxValue.");
        return basePath;
    }
}
