using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Extensions;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Seeding;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.Tests.Api;

/// <summary>
/// Proves the identity <c>IsDevelopmentOrDemo</c> flag is safe by construction (security remediation Fix 2):
/// the default <c>shells.json</c> ships it <c>true</c>, and it must never boot the insecure posture (ephemeral
/// signing keys + a seeded well-known <c>admin</c>/<c>Password123!</c> account) outside the Development
/// environment. A host that combines the flag with a non-Development environment must hard-fail at startup;
/// with Development it boots and seeds as before; a production host with the flag <c>false</c> boots normally.
/// </summary>
public sealed class DevelopmentOrDemoGuardTests
{
    [Fact]
    public async Task DevDemo_Flag_In_Production_Hard_Fails_Startup_With_An_Actionable_Message()
    {
        var suffix = Guid.NewGuid().ToString("n");

        // IsDevelopmentOrDemo = true but the host is Production (the unedited-default-in-prod scenario).
        var host = BuildHost(Environments.Production, isDevelopmentOrDemo: true, suffix);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        // The message must name the setting and how to fix it (mirrors the SecurityDefaultGuard/kill-switch style).
        Assert.Contains("IsDevelopmentOrDemo", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Development", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_ENVIRONMENT", exception.Message, StringComparison.Ordinal);

        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task DevDemo_Flag_In_Development_Boots_And_Seeds()
    {
        var suffix = Guid.NewGuid().ToString("n");

        using var host = BuildHost(Environments.Development, isDevelopmentOrDemo: true, suffix);

        await EnsureSchemaAsync(host);
        await host.StartAsync(); // Does not throw; the dev seeder + store init run.

        // The seeded admin exists — proof the dev/demo posture booted as before.
        await using var scope = host.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Elsa.Foundation.Identity.AspNetCoreIdentity.Models.AspNetCoreIdentityUser>>();
        Assert.NotNull(await userManager.FindByNameAsync(IdentitySeeder.AdminUserName));

        await host.StopAsync();
    }

    [Fact]
    public async Task Production_With_Real_Keys_And_Flag_False_Boots()
    {
        var suffix = Guid.NewGuid().ToString("n");

        // Flag false + a real (test) signing key + Production: the guard is not registered and startup succeeds.
        using var host = new HostBuilder()
            .UseEnvironment(Environments.Production)
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
                        isDevelopmentOrDemo: false,
                        configureDbContext: builder => builder.UseInMemoryDatabase($"identity-{suffix}"));
                    services.AddFoundationIdentityOpenIddict(
                        options =>
                        {
                            options.IsDevelopmentOrDemo = false;
                            options.SigningKey = TestSigningKey;
                        },
                        configureDbContext: builder => builder.UseInMemoryDatabase($"openiddict-{suffix}"));
                });
                webHost.Configure(_ => { });
            })
            .Build();

        await EnsureSchemaAsync(host);

        // Must not throw — a correctly configured production host boots.
        await host.StartAsync();
        await host.StopAsync();
    }

    private static IHost BuildHost(string environment, bool isDevelopmentOrDemo, string suffix) =>
        new HostBuilder()
            .UseEnvironment(environment)
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddFoundationAspNetCoreIdentityEntityFrameworkCore(
                        isDevelopmentOrDemo: isDevelopmentOrDemo,
                        configureDbContext: builder => builder.UseInMemoryDatabase($"identity-{suffix}"));
                    services.AddFoundationIdentityOpenIddict(
                        options => options.IsDevelopmentOrDemo = isDevelopmentOrDemo,
                        configureDbContext: builder => builder.UseInMemoryDatabase($"openiddict-{suffix}"));
                });
                webHost.Configure(_ => { });
            })
            .Build();

    private static async Task EnsureSchemaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<OpenIddictIdentityDbContext>().Database.EnsureCreatedAsync();
    }

    // A throwaway base64 PKCS#8 RSA-2048 private key, generated for this test only. Never a production secret.
    private const string TestSigningKey = "MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQCQYxBzmbKE8p1iqXvLyBwfQQ97jEhfj62WrF3bLVBMPznl9LEBtLusaHHflwgG6roiVnOMQteVDuHbKCP8fc2Ks+zl6mgfTQngDM9nol59NOOMKlTi4pMalmJ3lj9rJuZqciCcBH3jQ7YXuKFxehyE9bbyQnzVcWJ1SboQwrK7iBxb7H6l5s+QHpllSRCwRjHtxBVDhuiyPgyxuwseVd+jjefthiltn3xIhe/RqQT4uir7hUSVE7gPUw7c9Ae8mQjIDldmhW2oYGQXvMedAawq88996XzgfZMun8ZbJmXYbFcYgox7YyvcqlRtyxT86aNBKspwrmMI3UiGeJ42ap9hAgMBAAECggEALNkggI/ClCoZ+c3kHpGbLpgSW5Fg35Hs3OrQQmaqVOykqslc+8csLirJCCbM/v0E8OqCfJQ8i1eyjtTCjMh0wjsOAAJV8jcHNLk16R5VlDWL4ns5n7m58J26myOnsjxEgNbPSzbX9XIQSwD14J4J4sDB4TEGvnO4He9XJKKdSsNOaMGEBrPYR2a2xQoOj2c/zh19ayofv4LYxuNtoC+FkhgcytRk9Kcm2pzyKpXFJinCM2nqlXTVKxiNqIzkq6tJb49L8NrRyvNNxoTXDTq3oWc8a/Of0iqnNrj55nOd/NvFJSryySudExMqQZMPxrUWtr39MvxuwwPLu5HyXy51EwKBgQDH5m0qXmo3IjyYIhM22oxpukFOcaKABy8Y5n4B57r6g844kuhCG6UcU9o6Lr7Hk7tHXjerc+zJDkN9UWxh5wIYv8hRFccECRbz1ZnFEhDBVyp9Sm3E6MymajumjeEK0j8E113Ypc5+xTQsg7Bvn7oHK4rhL8+u7ESUFGssKM3ZJwKBgQC46F2yqHxZtd+QJWBZhixiDKXWb+g1NmQkyVFqOmhK/KDhw9Lrc2c7LkBwvt2XhSQCJxhW61jwmigADC+yJs4MxrzpCxIFa7y/8oYjWI8UPO2pQuG+MflhzkRcxtT1tQwNSHe1ubhjcFtr/AVJq5Km1SiKw+IiNrwSQBe+8LJINwKBgBc/n1acIHJFExMGc4GfrVDmjo3W/XUyYLHerK0ADGjyR1uhCk4V9P/0aJ73rMx9S/yi4oqYMuO3OvYqMBa6zS2fsGZtx/WaMQcuwnF/Bl/WCWUE8PtDr/lyxXk7av7dMOMZQRrfPcLUoJrEvhJdWz9WOxKxgs/vPpKSRVyfakFlAoGAbY/BdQrAI6fQP+jlniYSRkaYPOcx/9WqoOFojDjvcv8dlKgjYb+Pe1F8fVGamx0YqO3hTh9FI8szyFNwL28uyAM6DBuzIeMkg9eAA5GFtcgkShaHC9swmPNPLmnh5XTRH03BILxatRDuGp3JxE0VKCVFUHOgmgU0itvPNiQIfyECgYEAjmRBiXnqcKu1gckJ5cCMMOrvwtuMm7KyjDXTmO0hMtUSaXaOcCFeybtxnSr34rhj0h4x5LmLC8aQ2OPUvex1GJ3cPByAq19wFNBzVtH+5i/AjwhgW+ceMJbavhzbSYXEO2mr4LHmSlxzkA1XAAR+XPk62tSt4eaBam6Oj7cFWGA=";
}
