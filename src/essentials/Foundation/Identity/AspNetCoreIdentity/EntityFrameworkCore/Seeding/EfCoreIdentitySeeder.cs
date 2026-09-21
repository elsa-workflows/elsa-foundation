using CShells.Lifecycle;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;

/// <summary>
/// Runs the provider-neutral administrator convergence against the EF-backed stores. Database schema
/// creation and migrations remain host-owned; enabling this seeder never initializes a database implicitly.
/// </summary>
public sealed class EfCoreIdentitySeeder(
    IServiceProvider services,
    IOptions<IdentitySeedOptions> seedOptions,
    ILogger<EfCoreIdentitySeeder> logger,
    IHostEnvironment? hostEnvironment = null) : IHostedService, IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var seed = seedOptions.Value;
        if (string.IsNullOrWhiteSpace(seed.UserName) || string.IsNullOrWhiteSpace(seed.Password))
            throw new InvalidOperationException("EfCoreIdentitySeeder requires both an administrator username and password, or the seed options must be omitted.");
        if (seed.IsDevelopmentSeed && hostEnvironment?.IsDevelopment() != true)
            throw new InvalidOperationException("EfCoreIdentitySeeder refuses development credentials unless the host environment is explicitly Development.");

        await using var scope = services.CreateAsyncScope();
        var identityOptions = scope.ServiceProvider.GetRequiredService<IOptions<AspNetCoreIdentityOptions>>().Value;
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
            PersistenceAccessContext.Scoped(new PersistenceScope(identityOptions.DefaultTenantId)));
        var coordinator = scope.ServiceProvider.GetRequiredService<IdentitySeedCoordinator>();
        var result = await coordinator.EnsureSeededAsync(seed, cancellationToken);
        if (result is IdentitySeedCoordinator.PasswordPolicyRejected rejected)
        {
            var errors = string.Join("; ", rejected.Errors.Select(error => RedactSecret(error, seed.Password)));
            throw new InvalidOperationException("Failed to seed the administrator account because the configured password policy rejected it: " + errors);
        }

        // The provider-neutral convergence records the desired role on the user aggregate. The
        // ASP.NET Core adapter additionally materializes the framework UserRole relationship so
        // UserManager role queries and authorization observe the same converged membership.
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();
        var user = await userManager.FindByNameAsync(seed.UserName);
        if (user is null)
            throw new InvalidOperationException("The EF Identity administrator was not available after seeding.");
        if (!await userManager.IsInRoleAsync(user, seed.RoleName))
        {
            var membershipResult = await userManager.AddToRoleAsync(user, seed.RoleName);
            if (!membershipResult.Succeeded)
                throw new InvalidOperationException("Failed to materialize the EF Identity administrator role membership: " + string.Join("; ", membershipResult.Errors.Select(x => x.Code)));
        }

        if (seed.IsDevelopmentSeed)
            logger.LogInformation("Seeded EF Core ASP.NET Core Identity administrator account. Sign in at /{LoginRoute} with username '{Username}' (development/demo only).", AspNetCoreIdentityDefaults.LoginRoute, seed.UserName);
        else
            logger.LogInformation("Ensured the EF Core ASP.NET Core Identity administrator account. Sign in at /{LoginRoute}.", AspNetCoreIdentityDefaults.LoginRoute);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string RedactSecret(string value, string secret) =>
        string.IsNullOrEmpty(secret) ? value : value.Replace(secret, "[redacted]", StringComparison.Ordinal);
}
