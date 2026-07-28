using CShells.Lifecycle;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Seeding;

/// <summary>
/// Groundwork startup seeder for the initial ASP.NET Core Identity administrator. Schema creation remains
/// owned by Groundwork storage initialization; this type only invokes the provider-neutral seed coordinator.
/// </summary>
public sealed class GroundworkIdentitySeeder(
    IServiceProvider services,
    IOptions<IdentitySeedOptions> seedOptions,
    ILogger<GroundworkIdentitySeeder> logger,
    IHostEnvironment? hostEnvironment = null) : IHostedService, IShellInitializer
{
    private IdentitySeedOptions Seed => seedOptions.Value;

    public Task InitializeAsync(CancellationToken cancellationToken) => StartAsync(cancellationToken);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Seed.UserName) || string.IsNullOrWhiteSpace(Seed.Password))
            throw new InvalidOperationException(
                "GroundworkIdentitySeeder was registered without a configured admin username and password. " +
                "Supply both values or omit the seed options to disable administrator seeding.");
        if (Seed.IsDevelopmentSeed && hostEnvironment?.IsDevelopment() != true)
            throw new InvalidOperationException(
                "GroundworkIdentitySeeder refuses development credentials unless the host environment is explicitly Development.");

        await using var scope = services.CreateAsyncScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<IdentitySeedCoordinator>();
        var result = await coordinator.EnsureSeededAsync(Seed, cancellationToken);
        if (result is IdentitySeedCoordinator.PasswordPolicyRejected rejected)
        {
            var errors = string.Join("; ", rejected.Errors.Select(error => RedactSecret(error, Seed.Password)));
            throw new InvalidOperationException($"Failed to seed the administrator account because the configured password policy rejected it: {errors}");
        }

        if (Seed.IsDevelopmentSeed)
        {
            _ = new DevelopmentPasswordLoggedWithCaveat();
            logger.LogInformation(
                "Seeded Groundwork ASP.NET Core Identity admin account. Sign in at /{LoginRoute} with username '{Username}' and password '{Password}' (development/demo only).",
                AspNetCoreIdentityDefaults.LoginRoute, Seed.UserName, Seed.Password);
        }
        else
        {
            _ = new ProductionPasswordRedacted();
            logger.LogInformation(
                "Ensured the Groundwork ASP.NET Core Identity administrator account. Sign in at /{LoginRoute}.",
                AspNetCoreIdentityDefaults.LoginRoute);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string RedactSecret(string value, string secret) =>
        string.IsNullOrEmpty(secret)
            ? value
            : value.Replace(secret, "[redacted]", StringComparison.Ordinal);

    public sealed record ProductionPasswordRedacted;

    public sealed record DevelopmentPasswordLoggedWithCaveat;
}
