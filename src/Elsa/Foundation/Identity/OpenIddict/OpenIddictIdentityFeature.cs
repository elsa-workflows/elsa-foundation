using CShells.Features;
using Elsa.Foundation.Identity.OpenIddict.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.OpenIddict;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityOpenIddict",
    DisplayName = "Foundation Identity OpenIddict",
    Description = "OpenIddict-backed first-party token issuance (JWT access tokens + rotating refresh tokens) and bearer validation for the API surface."
)]
public sealed class OpenIddictIdentityFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Development or demo", Description = "Uses an in-memory token store and ephemeral per-process keys so a fresh checkout can issue tokens without configuration.", Category = "Identity", DefaultValue = "false")]
    public bool IsDevelopmentOrDemo { get; set; }

    [ManifestSetting(DisplayName = "Issuer", Description = "Logical issuer URI written into first-party access tokens.", Category = "Identity")]
    public string? Issuer { get; set; }

    [ManifestSetting(DisplayName = "Signing key", Description = "Key material used to derive the token signing key. Required outside development/demo.", Category = "Security", Secret = true)]
    public string? SigningKey { get; set; }

    [ManifestSetting(DisplayName = "Encryption key", Description = "Key material used to derive OpenIddict's encryption key. Defaults to a key derived from the signing key.", Category = "Security", Secret = true)]
    public string? EncryptionKey { get; set; }

    [ManifestSetting(DisplayName = "Connection string", Description = "Sqlite connection string for the token store. Defaults to the shared identity database.", Category = "Persistence", Secret = true)]
    public string? ConnectionString { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        // Capture setting values locally: the feature instance is configuration-bound before this runs.
        var isDevelopmentOrDemo = IsDevelopmentOrDemo;
        var issuer = Issuer;
        var signingKey = SigningKey;
        var encryptionKey = EncryptionKey;
        var connectionString = ConnectionString;

        services.AddFoundationIdentityOpenIddict(options =>
        {
            options.IsDevelopmentOrDemo = isDevelopmentOrDemo;

            if (!string.IsNullOrWhiteSpace(issuer))
                options.Issuer = issuer;

            if (!string.IsNullOrWhiteSpace(signingKey))
                options.SigningKey = signingKey;

            if (!string.IsNullOrWhiteSpace(encryptionKey))
                options.EncryptionKey = encryptionKey;

            if (!string.IsNullOrWhiteSpace(connectionString))
                options.ConnectionString = connectionString;
        });
    }
}
