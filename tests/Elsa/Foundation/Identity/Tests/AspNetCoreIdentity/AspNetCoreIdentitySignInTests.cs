using Elsa.Foundation.Identity.AspNetCoreIdentity;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.AspNetCoreIdentity;

/// <summary>
/// Password sign-in and seeding behaviour over the EF-backed substrate. Shared fixture builds the container;
/// <see cref="CreateUserAsync"/> removes repeated user-creation arrange blocks.
/// </summary>
public sealed class AspNetCoreIdentitySignInTests : IAsyncDisposable
{
    private readonly AspNetCoreIdentityFixture _fixture = new();

    [Fact]
    public async Task PasswordSignIn_Succeeds_With_Valid_Credentials()
    {
        await CreateUserAsync("dave", "S3cret!pass", tenantId: AspNetCoreIdentityDefaults.DefaultTenantId);

        await using var scope = _fixture.CreateScope();
        var signIn = scope.ServiceProvider.GetRequiredService<IIdentitySignInService>();

        var outcome = await signIn.PasswordSignInAsync("dave", "S3cret!pass", tenantId: null);

        Assert.True(outcome.Succeeded);
        Assert.Equal("authenticated", outcome.Session!.Status);
        Assert.Equal(AspNetCoreIdentityDefaults.DefaultTenantId, outcome.Session.TenantId);
    }

    [Fact]
    public async Task PasswordSignIn_Fails_With_Bad_Password()
    {
        await CreateUserAsync("erin", "GoodPass!1", tenantId: AspNetCoreIdentityDefaults.DefaultTenantId);

        await using var scope = _fixture.CreateScope();
        var signIn = scope.ServiceProvider.GetRequiredService<IIdentitySignInService>();

        var outcome = await signIn.PasswordSignInAsync("erin", "wrong-password", tenantId: null);

        Assert.False(outcome.Succeeded);
        Assert.Null(outcome.Session);
    }

    [Fact]
    public async Task PasswordSignIn_Fails_For_Unknown_User()
    {
        await using var scope = _fixture.CreateScope();
        var signIn = scope.ServiceProvider.GetRequiredService<IIdentitySignInService>();

        // An unknown username still returns the same generic Failure. (The service also runs a dummy password
        // verification on this path — see AspNetCoreIdentitySignInService — to equalize timing and close the
        // username-enumeration timing oracle; a timing assertion would be inherently flaky, so this asserts the
        // observable contract: unknown user → Failure, indistinguishable from a wrong password.)
        var outcome = await signIn.PasswordSignInAsync("nobody", "whatever", tenantId: null);

        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public async Task Seeder_Creates_Admin_That_Can_Sign_In_With_Admin_Permissions()
    {
        var seeder = ActivatorUtilities.CreateInstance<IdentitySeeder>(_fixture.Services);
        await seeder.StartAsync(CancellationToken.None);

        await using var scope = _fixture.CreateScope();
        var signIn = scope.ServiceProvider.GetRequiredService<IIdentitySignInService>();

        var outcome = await signIn.PasswordSignInAsync(IdentitySeeder.AdminUserName, IdentitySeeder.AdminPassword, tenantId: null);

        Assert.True(outcome.Succeeded);
        // The admin role is granted every catalog permission; a representative one should be projected.
        Assert.Contains("identity.users.manage", outcome.Session!.Permissions);
    }

    private async Task CreateUserAsync(string username, string password, string tenantId)
    {
        await using var scope = _fixture.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AspNetCoreIdentityUser>>();

        var user = new AspNetCoreIdentityUser
        {
            Id = Guid.NewGuid().ToString("n"),
            UserName = username,
            Email = $"{username}@example.com",
            TenantId = tenantId
        };

        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();
}
