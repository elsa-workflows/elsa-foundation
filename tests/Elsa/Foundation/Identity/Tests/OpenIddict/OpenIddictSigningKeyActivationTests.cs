using System.Security.Cryptography;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.OpenIddict;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.Tests.OpenIddict;

/// <summary>
/// The OpenIddict server options, and with them the signing credentials, are built lazily. Without a startup check a
/// Production shell that composes <c>FoundationIdentityOpenIddict</c> with no signing key activated, readiness reported
/// it ready, and every token issuance and every request OpenIddict authenticated returned 500. These tests pin the
/// failure to activation.
/// </summary>
public sealed class OpenIddictSigningKeyActivationTests
{
    private const string MissingKeyError = "No signing key is configured for the OpenIddict identity module";

    [Theory]
    [InlineData(null, MissingKeyError)]
    [InlineData("not-a-pkcs8-key", "must be a base64-encoded PKCS#8 RSA private key")]
    public async Task Unusable_Signing_Key_Fails_Shell_Activation(string? signingKey, string expectedError)
    {
        await using var host = await StartShellHostAsync(signingKey);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(host.ActivateShellAsync);

        Assert.Contains(expectedError, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configured_Signing_Key_Activates_The_Shell()
    {
        await using var host = await StartShellHostAsync(GenerateSigningKey());

        var shell = await host.ActivateShellAsync();

        Assert.Equal(ShellLifecycleState.Active, shell.State);
    }

    // Plain (non-shell) hosts do not run shell initializers, so the same check rides the hosted-service hook.
    [Fact]
    public async Task Missing_Signing_Key_Fails_Plain_Host_Startup()
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

        Assert.Contains(MissingKeyError, exception.Message, StringComparison.Ordinal);
    }

    // Like Workbench, the host owns the vendor token store; CShells copies root registrations into the shell.
    private static Task<WebApplication> StartShellHostAsync(string? signingKey) =>
        ShellActivationHost.StartAsync<OpenIddictIdentityFeature>(
            Environments.Production,
            feature =>
            {
                feature.IsDevelopmentOrDemo = false;
                feature.SigningKey = signingKey;
            },
            AddVendorStore);

    private static void AddVendorStore(IServiceCollection services)
    {
        var databaseName = $"openiddict-{Guid.NewGuid():n}";
        services.AddOpenIddictVendorForTests(store => OpenIddictIdentityFixture.ConfigureInMemoryStore(store, databaseName));
    }

    private static string GenerateSigningKey()
    {
        using var rsa = RSA.Create(2048);
        return Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
    }
}
