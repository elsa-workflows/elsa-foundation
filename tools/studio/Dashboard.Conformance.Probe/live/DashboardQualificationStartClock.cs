using CShells.Features;
using CShells.AspNetCore.Features;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Modularity.Api.Authorization;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using MinimalEndpoints;

// Only the owned host template compiles this fixture. IAM, leases, retention and
// query evaluation retain the normal system clock. Runtime still executes and
// checkpoints every seeded workflow through its ordinary HTTP capabilities.
[ShellFeature(name: "DashboardQualificationStartClock", DependsOn = [typeof(WorkflowsRuntimeApiFeature)])]
public sealed class DashboardQualificationStartClockFeature : IShellFeature, IPostConfigureShellServices
{
    public void ConfigureServices(IServiceCollection services) { }

    public void PostConfigureServices(IServiceCollection services)
    {
        var original = services.Single(item => item.ServiceType == typeof(IWorkflowSchedulerWorkHandler)
            && item.ImplementationType == typeof(WorkflowStartSchedulerWorkHandler));
        if (original.Lifetime != ServiceLifetime.Scoped)
            throw new InvalidOperationException("The real runtime start handler must remain scoped.");
        services.Remove(original);
        services.AddScoped<IWorkflowSchedulerWorkHandler>(provider =>
            ActivatorUtilities.CreateInstance<WorkflowStartSchedulerWorkHandler>(provider,
                provider.GetRequiredService<DashboardQualificationStartClock>()));
    }
}

internal sealed class DashboardQualificationStartClock : TimeProvider
{
    private long ticks;
    public override DateTimeOffset GetUtcNow()
    {
        var value = Interlocked.Read(ref ticks);
        return value == 0 ? TimeProvider.System.GetUtcNow() : new DateTimeOffset(value, TimeSpan.Zero);
    }

    public void Set(DateTimeOffset? instant)
    {
        var now = TimeProvider.System.GetUtcNow();
        if (instant is { } value && (value < now.AddDays(-14) || value > now))
            throw new ArgumentException("Only bounded retained-cohort fixture instants are allowed.");
        Interlocked.Exchange(ref ticks, instant?.UtcTicks ?? 0);
    }

    public static void Map(IEndpointRouteBuilder endpoints, DashboardQualificationStartClock clock)
    {
        endpoints.MapPost("/__qualification/dashboard-live/start-clock", (StartClockRequest request) =>
            { clock.Set(request.Instant); return Results.NoContent(); })
            .WithHostOwner("Elsa.Workbench").WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(ModuleManagementPermissionKeys.Manage);
    }

    public sealed record StartClockRequest(DateTimeOffset? Instant);
}
