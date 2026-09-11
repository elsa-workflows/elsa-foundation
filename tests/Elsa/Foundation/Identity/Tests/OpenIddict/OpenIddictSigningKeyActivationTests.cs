using System.Security.Cryptography;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.Identity.Tests.OpenIddict;

/// <summary>
/// The OpenIddict server options, and with them the signing credentials, are built lazily. Without a startup check a
/// Production shell that composes <c>FoundationIdentityOpenIddict</c> with no signing key activated, readiness reported
/// it ready, and every request that ran authentication returned 500. These tests pin the failure to activation.
/// </summary>
public sealed class OpenIddictSigningKeyActivationTests
{
    private const string ShellName = "openiddict-signing-key-probe";

    [Theory]
    [InlineData(null, "No signing key is configured for the OpenIddict identity module")]
    [InlineData("not-a-pkcs8-key", "must be a base64-encoded PKCS#8 RSA private key")]
    public async Task Unusable_signing_key_fails_shell_activation(string? signingKey, string expectedMessage)
    {
        await using var host = await StartShellHostAsync(signingKey);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configured_signing_key_activates_the_shell()
    {
        await using var host = await StartShellHostAsync(GenerateSigningKey());

        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);

        Assert.Equal(ShellLifecycleState.Active, shell.State);
    }

    // Plain (non-shell) hosts do not run shell initializers, so the same check rides the hosted-service hook.
    [Fact]
    public async Task Missing_signing_key_fails_plain_host_startup()
    {
        using var host = new HostBuilder()
            .UseEnvironment(Environments.Production)
            .ConfigureServices(services =>
            {
                services.AddLogging();
                AddVendorStore(services);
                services.AddFoundationIdentityOpenIddict(options => options.IsDevelopmentOrDemo = false);
            })
            .Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Contains("No signing key is configured for the OpenIddict identity module", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<WebApplication> StartShellHostAsync(string? signingKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        // Like Workbench, the host owns the vendor token store; CShells copies root descriptors into the shell.
        AddVendorStore(builder.Services);
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(typeof(OpenIddictIdentityFeature).Assembly)
            .AddShell(ShellName, shell => shell.WithFeature<OpenIddictIdentityFeature>(feature =>
            {
                feature.IsDevelopmentOrDemo = false;
                feature.SigningKey = signingKey;
            })));

        var app = builder.Build();
        app.MapShells();
        await app.StartAsync();
        return app;
    }

    private static void AddVendorStore(IServiceCollection services)
    {
        var databaseName = $"openiddict-{Guid.NewGuid():n}";
        services.AddOpenIddictVendorForTests(store => store.UseInMemoryDatabase(databaseName));
    }

    private static string GenerateSigningKey()
    {
        using var rsa = RSA.Create(2048);
        return Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
    }
}
