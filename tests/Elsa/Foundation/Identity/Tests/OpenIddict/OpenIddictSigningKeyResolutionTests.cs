using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Elsa.Foundation.Identity.Tests.OpenIddict;

/// <summary>
/// Pins the signing-key resolution behavior behind issue #1163: the <c>IsDevelopmentOrDemo</c> flag is honored
/// at options-resolution time with no <c>IHostEnvironment</c> dependency whatsoever (the Development-environment
/// requirement is enforced separately by <c>DevelopmentOrDemoGuard</c> — see <c>DevelopmentOrDemoGuardTests</c>),
/// and the missing-key error tells operators the whole truth: enabling the flag alone does not work outside the
/// Development environment, and environment overlays (<c>shells.Production.json</c>) can silently reset it.
/// </summary>
public sealed class OpenIddictSigningKeyResolutionTests
{
    [Fact]
    public void DevDemo_Flag_Resolves_Ephemeral_Signing_Key_Without_Any_Host_Environment()
    {
        // No IHostEnvironment is registered anywhere in this container: options resolution must honor the
        // flag purely from configuration. (Environment gating is the guard's job, not the resolver's.)
        using var provider = BuildProvider(isDevelopmentOrDemo: true);

        var options = provider.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value;

        var signingKey = Assert.Single(options.SigningCredentials).Key;
        Assert.Equal("elsa-identity-dev-signing", signingKey.KeyId);
        Assert.NotEmpty(options.EncryptionCredentials);
    }

    [Fact]
    public void Missing_Signing_Key_Error_Explains_The_Development_Environment_Requirement()
    {
        using var provider = BuildProvider(isDevelopmentOrDemo: false);

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IOptions<OpenIddictServerOptions>>().Value);

        // Names the setting to configure for production.
        Assert.Contains("SigningKey", exception.Message, StringComparison.Ordinal);
        // Does not send operators down the dead end from issue #1163: the dev/demo flag alone does not
        // activate the ephemeral key — the environment must be Development, and overlays can reset the flag.
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Development", exception.Message, StringComparison.Ordinal);
        Assert.Contains("shells.Production.json", exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider(bool isDevelopmentOrDemo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenIddictVendorForTests(
            builder => OpenIddictIdentityFixture.ConfigureInMemoryStore(builder, $"openiddict-{Guid.NewGuid():n}"));
        services.AddFoundationIdentityOpenIddict(
            options => options.IsDevelopmentOrDemo = isDevelopmentOrDemo);
        return services.BuildServiceProvider();
    }
}
