using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Jobs.Api.Authorization;
using Elsa.Jobs.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Jobs.UxAcceptance;

// The Workbench project references this assembly only with JobsUxAcceptance=true.
// These controls do not replace Jobs/identity APIs or seed any model/runtime state.
[ShellFeature(name: "JobsUxAcceptance", DisplayName = "Owned Jobs UX fixture", DependsOn = new object[] { "Tasks" })]
public sealed class JobsUxAcceptanceFeature : IWebShellFeature
{
    public const string Partition = "default";
    public const string PrimaryTenant = "default";
    public const string SecondaryTenant = "ux-jobs-secondary";
    public void ConfigureServices(IServiceCollection services)
    {
        if (Environment.GetEnvironmentVariable("UX_ACCEPTANCE_OWNED") != "1")
            throw new InvalidOperationException("The Jobs UX fixture requires the owned acceptance runner.");
        services.AddSingleton<ReportControl>();
        services.AddJobHandler<ReportHandler, ReportInput, ReportOutput>(ReportHandler.Support);
        services.AddJobHandler<ReportHandler, ReportInput, ReportOutput>(ReportHandler.UnrelatedSupport);
        services.AddElsaJobs(options =>
        {
            options.EnableWorker = true; options.MaterializeSchedules = true; options.MaximumConcurrentAttempts = 4;
            // Exactly the tenants whose real first-party users this fixture creates; no HTTP/input scope.
            options.WorkerScopes.Add(new(Partition, PrimaryTenant)); options.WorkerScopes.Add(new(Partition, SecondaryTenant));
            options.Queues.Clear(); foreach (var project in new[] { "desktop-light", "desktop-dark", "narrow-light", "narrow-dark" }) options.Queues.Add("reports-" + project);
            options.Queues.Add("reports"); options.PollInterval = TimeSpan.FromMilliseconds(50);
            options.LeaseDuration = TimeSpan.FromSeconds(5); options.RenewalInterval = TimeSpan.FromMilliseconds(500);
            options.MaterializationInterval = TimeSpan.FromMilliseconds(100); options.ShutdownGracePeriod = TimeSpan.FromMilliseconds(200);
        });
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        if (environment?.IsDevelopment() != true) throw new InvalidOperationException("The owned Jobs UX fixture requires Development.");
        endpoints.MapPost("/_ux/jobs/setup", async Task<IResult> (HttpContext context) =>
        {
            if (!await AdminAsync(context)) return Results.StatusCode(403);
            var factory = context.RequestServices.GetRequiredService<IServiceScopeFactory>();
            foreach (var (tenant, name, permissions) in Users)
            {
                await using var scope = factory.CreateAsyncScope(); var services = scope.ServiceProvider;
                services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
                var manager = services.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
                var user = await manager.FindByNameAsync(name);
                if (user is null)
                {
                    user = new() { Id = "owned-" + name, UserName = name, TenantId = tenant, DisplayName = name, LockoutEnabled = true };
                    if (!(await manager.CreateAsync(user, "Password123!")).Succeeded) throw new InvalidOperationException("Owned identity fixture creation failed.");
                }
                var store = services.GetRequiredService<Elsa.Foundation.Identity.Core.Iam.IUserStore>();
                var record = await store.FindAsync(tenant, user.Id, context.RequestAborted) ?? throw new InvalidOperationException("Owned identity fixture is unavailable.");
                await store.SaveAsync(record with { DirectPermissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase) }, context.RequestAborted);
                await services.GetRequiredService<ITenantMembershipStore>().SaveAsync(new(tenant, user.Id, TenantMembershipStatus.Active, new HashSet<string>(), new HashSet<string>()), context.RequestAborted);
            }
            return Results.Ok(new { fixture = "actual-first-party-identity-jobs-sqlite-worker", handler = ReportHandler.Support, unrelatedHandler = ReportHandler.UnrelatedSupport });
        }).WithOwner("Elsa.Jobs.UxAcceptance").RequireAnyPermission("*");
        endpoints.MapPost("/_ux/jobs/recover/{tag}", async Task<IResult> (HttpContext context, string tag) =>
        {
            if (!await AdminAsync(context)) return Results.StatusCode(403);
            if (string.IsNullOrWhiteSpace(tag) || tag.Length > 128) return Results.BadRequest();
            context.RequestServices.GetRequiredService<ReportControl>().Recover(tag); return Results.NoContent();
        }).WithOwner("Elsa.Jobs.UxAcceptance").RequireAnyPermission("*");
        endpoints.MapPost("/_ux/jobs/revoke-reader", async Task<IResult> (HttpContext context) =>
        {
            if (!await AdminAsync(context)) return Results.StatusCode(403);
            await using var scope = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(PrimaryTenant)));
            var store = scope.ServiceProvider.GetRequiredService<Elsa.Foundation.Identity.Core.Iam.IUserStore>();
            var user = await store.FindAsync(PrimaryTenant, "owned-jobs-reader", context.RequestAborted) ?? throw new InvalidOperationException("The owned reader is unavailable.");
            await store.SaveAsync(user with { DirectPermissions = new HashSet<string>() }, context.RequestAborted);
            return Results.NoContent();
        }).WithOwner("Elsa.Jobs.UxAcceptance").RequireAnyPermission("*");
    }
    private static readonly (string Tenant, string Name, string[] Permissions)[] Users =
    [
        (PrimaryTenant, "jobs-reader", [JobsPermissions.Read]),
        (PrimaryTenant, "jobs-submitter", [JobsPermissions.Submit]),
        (PrimaryTenant, "jobs-auditor", [JobsPermissions.AuditRead]),
        (PrimaryTenant, "jobs-denied", []),
        (SecondaryTenant, "jobs-other", [JobsPermissions.Read, JobsPermissions.Submit, JobsPermissions.InputsRead, JobsPermissions.ResultsRead, JobsPermissions.Pause, JobsPermissions.Cancel, JobsPermissions.Retry, JobsPermissions.SchedulesManage, JobsPermissions.CapacityManage, JobsPermissions.AuditRead])
    ];
    private static async Task<bool> AdminAsync(HttpContext context)
    {
        var services = context.RequestServices;
        if (!services.GetRequiredService<NormalizedPrincipalValidator>().TryGetNormalizedPrincipal(context.User, out var principal)) return false;
        var users = principal.FindAll(IdentityClaimTypes.UserId).Select(x => x.Value).ToArray();
        var tenants = principal.FindAll(IdentityClaimTypes.TenantId).Select(x => x.Value).ToArray();
        if (users.Length != 1 || tenants.Length != 1 || tenants[0] != PrimaryTenant) return false;
        return (await services.GetRequiredService<ICurrentSubjectPermissionAuthorizer>().AuthorizeAsync(new(tenants[0], users[0], "*"), context.RequestAborted)).Status == CurrentSubjectPermissionStatus.Granted;
    }
}
