using System.Security.Cryptography;
using System.Text;
using Elsa.Foundation.Identity.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>
/// Configures the OpenIddict server from Elsa options at options-resolution time (so keys configured by the
/// host after feature registration are honored): issuer, token lifetimes, readable (signed, non-encrypted)
/// JWT access tokens, and signing/encryption credentials.
/// </summary>
/// <remarks>
/// OpenIddict requires an asymmetric signing key, so access tokens are RS256-signed:
/// <list type="bullet">
/// <item><b>Development/demo</b> (no key configured + <see cref="OpenIddictIdentityOptions.IsDevelopmentOrDemo"/>):
/// a stable per-process ephemeral RSA key — tokens simply do not survive a restart.</item>
/// <item><b>Production</b>: <see cref="OpenIddictIdentityOptions.SigningKey"/> (falling back to
/// <see cref="FoundationIdentityOptions.SigningKey"/>) must hold a base64-encoded PKCS#8 RSA private key.</item>
/// </list>
/// The encryption credential (required by OpenIddict even though access-token encryption is disabled) is
/// symmetric, derived (SHA-256, domain-separated) from <see cref="OpenIddictIdentityOptions.EncryptionKey"/>
/// or the signing key material. Tokens are only validated in-process against these same server options
/// (<c>UseLocalServer</c>), so no key distribution is needed.
/// </remarks>
internal sealed class ConfigureOpenIddictServerOptions(
    IOptions<OpenIddictIdentityOptions> identityOptions,
    IOptions<FoundationIdentityOptions> foundationOptions) : IConfigureOptions<OpenIddictServerOptions>
{
    private static readonly Lazy<RsaSecurityKey> DevelopmentSigningKey = new(() =>
        new RsaSecurityKey(RSA.Create(2048)) { KeyId = "elsa-identity-dev-signing" });

    private static readonly Lazy<byte[]> DevelopmentEncryptionKey = new(() => RandomNumberGenerator.GetBytes(32));

    public void Configure(OpenIddictServerOptions target)
    {
        var options = identityOptions.Value;

        target.Issuer = new Uri(options.Issuer, UriKind.Absolute);
        target.AccessTokenLifetime = options.AccessTokenLifetime;
        target.RefreshTokenLifetime = options.RefreshTokenLifetime;

        // First-party JWTs stay readable (signed-only); integrity is signature + token-entry validation.
        target.DisableAccessTokenEncryption = true;

        var signingKeyMaterial = options.SigningKey ?? foundationOptions.Value.SigningKey;
        var encryptionKeyMaterial = options.EncryptionKey ?? signingKeyMaterial;

        target.SigningCredentials.Add(new SigningCredentials(
            ResolveSigningKey(signingKeyMaterial, options.IsDevelopmentOrDemo),
            SecurityAlgorithms.RsaSha256));

        target.EncryptionCredentials.Add(new EncryptingCredentials(
            new SymmetricSecurityKey(ResolveEncryptionKey(encryptionKeyMaterial, options.IsDevelopmentOrDemo)) { KeyId = "elsa-identity-encryption" },
            SecurityAlgorithms.Aes256KW,
            SecurityAlgorithms.Aes256CbcHmacSha512));
    }

    private static RsaSecurityKey ResolveSigningKey(string? material, bool isDevelopmentOrDemo)
    {
        if (!string.IsNullOrWhiteSpace(material))
        {
            var rsa = RSA.Create();
            try
            {
                rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(material), out _);
            }
            catch (Exception exception)
            {
                rsa.Dispose();
                throw new InvalidOperationException(
                    "The configured OpenIddict signing key must be a base64-encoded PKCS#8 RSA private key. Generate one with: " +
                    "openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -outform DER | base64", exception);
            }

            return new RsaSecurityKey(rsa) { KeyId = "elsa-identity-signing" };
        }

        if (isDevelopmentOrDemo)
            return DevelopmentSigningKey.Value;

        throw new InvalidOperationException(
            "No signing key is configured for the OpenIddict identity module. Set OpenIddictIdentityOptions.SigningKey " +
            "(or FoundationIdentityOptions.SigningKey) to a base64-encoded PKCS#8 RSA private key, or enable " +
            "IsDevelopmentOrDemo to use an ephemeral development key.");
    }

    private static byte[] ResolveEncryptionKey(string? material, bool isDevelopmentOrDemo)
    {
        if (!string.IsNullOrWhiteSpace(material))
            return SHA256.HashData(Encoding.UTF8.GetBytes($"elsa-openiddict:encryption:{material}"));

        if (isDevelopmentOrDemo)
            return DevelopmentEncryptionKey.Value;

        throw new InvalidOperationException(
            "No encryption key is configured for the OpenIddict identity module. Set OpenIddictIdentityOptions.EncryptionKey " +
            "or a signing key to derive it from, or enable IsDevelopmentOrDemo to use an ephemeral development key.");
    }
}
