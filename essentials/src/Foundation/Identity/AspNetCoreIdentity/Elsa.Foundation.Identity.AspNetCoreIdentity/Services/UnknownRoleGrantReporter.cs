using System.Security.Claims;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Services;

/// <summary>
/// Warns at startup about every permission grant a role holds that matches no registered permission: a pre-rename or
/// Elsa 3 name, a typo, a malformed pattern, or a grant for a feature the host does not enable. Such a grant authorizes
/// nothing, so without the warning a role loses access with no trace. One warning names each role and grant.
/// </summary>
/// <remarks>
/// A grant matches when the registered <see cref="IPermissionEvaluator"/> authorizes at least one catalog permission
/// for a principal that holds only that grant, so the report and the evaluator cannot disagree about what a grant
/// reaches. It reads the roles of the identity default tenant, the tenant that holds them while identity is host-wide.
/// Registered under both lifecycle hooks, like the administrator seeder: shell-scoped hosted services do not run, so a
/// shell host reaches it as an <see cref="IShellInitializer"/> and a plain host as an <see cref="IHostedService"/>.
/// Reporting never stops the host from starting: a role store it cannot read is itself reported as a warning.
/// </remarks>
internal sealed class UnknownRoleGrantReporter(
    IServiceScopeFactory scopes,
    IOptions<AspNetCoreIdentityOptions> options,
    ILogger<UnknownRoleGrantReporter> logger) : IHostedService, IShellInitializer
{
    private int _started;

    public Task InitializeAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // A host that runs both hooks against one container would otherwise report every grant twice.
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;

        var tenantId = options.Value.DefaultTenantId;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
            var catalog = services.GetRequiredService<IPermissionCatalog>().List();
            var evaluator = services.GetRequiredService<IPermissionEvaluator>();

            foreach (var role in await services.GetRequiredService<IRoleStore>().ListAsync(tenantId, cancellationToken))
            foreach (var grant in role.Permissions)
            {
                if (!await MatchesAnyAsync(evaluator, grant, catalog, cancellationToken))
                    logger.LogWarning(
                        "Role '{RoleName}' ({RoleId}) in tenant '{TenantId}' holds the permission grant '{Grant}', which matches no registered permission, so it authorizes nothing. Replace it with a '{{resource}}:{{verb}}' permission from the catalog.",
                        role.Name, role.Id, tenantId, grant);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning(exception, "Could not check the permission grants of the roles in tenant '{TenantId}'.", tenantId);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async ValueTask<bool> MatchesAnyAsync(
        IPermissionEvaluator evaluator,
        string grant,
        IEnumerable<Permission> catalog,
        CancellationToken cancellationToken)
    {
        var holder = new ClaimsPrincipal(new ClaimsIdentity([new Claim(IdentityClaimTypes.Permission, grant)]));
        foreach (var permission in catalog)
        {
            if ((await evaluator.EvaluateAsync(new PermissionEvaluationContext(holder, permission.Key), cancellationToken)).Succeeded)
                return true;
        }

        return false;
    }
}
