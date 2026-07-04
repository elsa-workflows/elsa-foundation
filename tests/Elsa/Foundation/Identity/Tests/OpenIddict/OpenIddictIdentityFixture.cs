using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.OpenIddict;

/// <summary>
/// Shared setup for the OpenIddict module tests: builds a DI container with the full real pipeline (server
/// options, EF token store over a private in-memory database, validation, token service) in development/demo
/// mode and ensures the schema exists. Disposed via <see cref="IAsyncDisposable"/> so each test class tears
/// its provider down.
/// </summary>
public sealed class OpenIddictIdentityFixture : IAsyncDisposable
{
    public ServiceProvider Services { get; }

    public OpenIddictIdentityFixture(Action<OpenIddictIdentityOptions>? configure = null)
    {
        var databaseName = $"openiddict-{Guid.NewGuid():n}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFoundationIdentityOpenIddict(
            options =>
            {
                options.IsDevelopmentOrDemo = true;
                configure?.Invoke(options);
            },
            configureDbContext: builder => builder.UseInMemoryDatabase(databaseName));

        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database.EnsureCreated();
    }

    /// <summary>The token service, resolved from the root scope (sufficient for tests).</summary>
    public ITokenService TokenService => Services.GetRequiredService<ITokenService>();

    public ValueTask<TokenIssueResult> IssueAsync(
        string subject = "user-1", string tenant = "tenant-a", params string[] scopes) =>
        TokenService.IssueAsync(new TokenIssueRequest(subject, tenant, scopes.Length is 0 ? ["identity.users.read"] : scopes));

    public async ValueTask DisposeAsync() => await Services.DisposeAsync();
}
