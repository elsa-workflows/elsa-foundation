using System.Data.Common;
using CShells;
using CShells.AspNetCore.Features;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.ActorFoundation.Hosting;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Modularity.Core.Models;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.KernelApi;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Durable;
using Kernel.Foundation.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    [ManifestSetting(DisplayName = "Finished retention (seconds)", Description = "Minimum age for automatic deletion of finished runs. Leave empty to keep them.", Category = "Retention")]
    public int? FinishedRetentionSeconds { get; set; }

    [ManifestSetting(DisplayName = "Cancelled retention (seconds)", Description = "Minimum age for automatic deletion of cancelled runs. Leave empty to keep them.", Category = "Retention")]
    public int? CancelledRetentionSeconds { get; set; }

    [ManifestSetting(DisplayName = "Faulted retention (seconds)", Description = "Minimum age for automatic deletion of resolved faults. Leave empty to keep them.", Category = "Retention")]
    public int? FaultedRetentionSeconds { get; set; }

    [ManifestSetting(DisplayName = "Retention confirmation", Description = "Receipt from the retention policy preview for this tenant and store.", Category = "Retention")]
    public string? RetentionConfirmation { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        var storage = KernelShellStorage.Create(Provider, SqlitePath, PostgresConnectionString);
        var tenant = EmbeddedTenant.Bind(Tenant);
        var persistenceScope = new PersistenceScope(tenant.Tenant);
        var proposedRetention = new ActorRetentionWindows(RetentionWindow(FinishedRetentionSeconds, nameof(FinishedRetentionSeconds)),
            RetentionWindow(CancelledRetentionSeconds, nameof(CancelledRetentionSeconds)),
            RetentionWindow(FaultedRetentionSeconds, nameof(FaultedRetentionSeconds)));
        var retention = new KernelRetentionActivation(tenant, proposedRetention, RetentionConfirmation);
        services.AddSingleton(storage);
        services.AddSingleton(retention);
        services.AddSingleton(new ShellRestartRequirement(FeatureName));
        services.AddSingleton(new KernelServing(capabilities: new KernelHostCapabilities(
            Schedules: false, Timers: storage.TakesTimers, Children: storage.TakesChildren, SelfSends: storage.TakesSelfSends,
            HttpWaits: storage.TakesHttpWaits),
            publicationScope: persistenceScope));
        services.AddSingleton<IScheduleActors>(new UnavailableScheduleActors(KernelHostCapabilities.SchedulesUnavailable));
        services.AddSingleton(sp => new KernelShellRuntime(storage, sp.GetRequiredService<KernelServing>(),
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILoggerFactory>(),
            persistenceScope, tenant, retention));
        services.AddSingleton(sp => new KernelRuns(tenant, sp.GetRequiredService<KernelShellRuntime>().ReadAsync));
        services.AddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<KernelShellRuntime>();
            var http = sp.GetRequiredService<WorkflowHttpEndpointSettings>();
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

    private static TimeSpan? RetentionWindow(int? seconds, string setting) => seconds switch
    {
        null => null,
        >= 0 => TimeSpan.FromSeconds(seconds.Value),
        _ => throw new InvalidOperationException($"{setting} must be nonnegative.")
    };

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapGet("/runtime/workflows/retention", static async (HttpContext context, KernelRetentionActivation retention,
                KernelShellRuntime runtime, NormalizedPrincipalValidator principalValidator,
                IPermissionAuthorizationService authorization, CancellationToken token) =>
            {
                if (!principalValidator.TryGetNormalizedPrincipal(context.User, out var principal)) return Results.NotFound();
                var tenants = principal.FindAll(IdentityClaimTypes.TenantId).Take(2).Select(claim => claim.Value).ToArray();
                var tenant = tenants switch { [] => "default", [var single] => single, _ => null };
                if (!StringComparer.Ordinal.Equals(tenant, retention.Tenant)) return Results.NotFound();
                var permission = await authorization.AuthorizeAsync(
                    new PermissionEvaluationContext(principal, WorkflowRuntimePermissions.WorkflowRuntimeManage, retention.Tenant, context), token);
                if (!permission.Succeeded) return Results.NotFound();

                context.Response.Headers.CacheControl = "no-store";
                try { return Results.Ok(await runtime.ReadRetentionStatusAsync(token)); }
                catch (Exception error) when (error is StoreFormatException or DbException or TimeoutException)
                {
                    token.ThrowIfCancellationRequested();
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "The durable retention policy is unavailable.");
                }
            })
            .WithOwner(FeatureName)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeManage)
            .WithName("GetKernelWorkflowRetention");

        var http = endpoints.ServiceProvider.GetRequiredService<WorkflowHttpEndpointSettings>();
        endpoints.Map(HttpBasePath(http) + "/{**path}", static (HttpContext context) =>
                context.RequestServices.GetRequiredService<KernelHttpRequestHandler>().HandleAsync(context))
            .WithOwner(FeatureName)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .AllowPublic("kernel workflow HTTP endpoints", "Starts or resumes a published workflow on its configured HTTP route and returns its response.");
    }

    internal static string HttpBasePath(WorkflowHttpEndpointSettings options)
    {
        var basePath = "/" + options.BasePath.Trim().Trim('/');
        if (basePath == "/" || basePath.IndexOfAny(['?', '#', '%', '{', '}']) >= 0)
            throw new InvalidOperationException("Kernel workflow HTTP endpoints require a dedicated decoded base path below the root.");
        if (options.MaxRequestBodyBytes <= 0 || options.MaxRequestBodyBytes == long.MaxValue)
            throw new InvalidOperationException("Kernel workflow HTTP endpoints require a positive request body limit below Int64.MaxValue.");
        return basePath;
    }
}
