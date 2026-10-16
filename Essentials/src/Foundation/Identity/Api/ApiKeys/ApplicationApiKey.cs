using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Elsa.Foundation.Identity.Core.Iam;

namespace Elsa.Foundation.Identity.Api.ApiKeys;

/// <summary>
/// The format, generation and verification of an application's API key:
/// <c>elsa_ak.{base64url(UTF-8 application id)}.{base64url(32 random bytes)}</c>.
/// </summary>
/// <remarks>
/// The middle segment is a non-secret lookup identifier: it names the application, whose one API-key credential has
/// the id <see cref="CredentialId"/>. The last segment is 256 bits from <see cref="RandomNumberGenerator"/>. Only a
/// SHA-256 hash of the whole key is stored. A fast hash is enough for a uniformly random 256-bit secret, because
/// guessing a preimage is as hard as guessing the key itself; a slow key-derivation function only adds cost for
/// low-entropy secrets such as passwords. Hashing the whole key also binds the identifier into the hash. Verification
/// compares hashes with <see cref="CryptographicOperations.FixedTimeEquals"/>.
/// </remarks>
internal static class ApplicationApiKey
{
    /// <summary>The <see cref="CredentialRecord.HashAlgorithm"/> of a key issued here. A credential with any other value never verifies.</summary>
    public const string HashAlgorithm = "sha256";

    private const string Prefix = "elsa_ak";
    private const char Separator = '.';
    private const int SecretLength = 32;
    private const int HashLength = 32;
    private const int MaximumKeyLength = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The id of the one API-key credential an application owns.</summary>
    public static string CredentialId(string applicationId) => $"application-api-key:{applicationId}";

    /// <summary>A new key for <paramref name="applicationId"/>. It exists only in the response that shows it once.</summary>
    public static string Generate(string applicationId)
    {
        Span<byte> secret = stackalloc byte[SecretLength];
        RandomNumberGenerator.Fill(secret);
        try
        {
            return string.Join(
                Separator,
                Prefix,
                Base64Url.EncodeToString(StrictUtf8.GetBytes(applicationId)),
                Base64Url.EncodeToString(secret));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>The lowercase hexadecimal SHA-256 of the whole key, which is what the credential stores.</summary>
    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    /// <summary>Reads the application a well-formed key names. Anything else is not a key and names nothing.</summary>
    public static bool TryParse(string key, [NotNullWhen(true)] out string? applicationId)
    {
        applicationId = null;
        if (key.Length > MaximumKeyLength)
            return false;

        var parts = key.Split(Separator);
        if (parts is not [Prefix, var identifier, var secret] ||
            !Base64Url.IsValid(identifier, out var identifierLength) || identifierLength == 0 ||
            !Base64Url.IsValid(secret, out var secretLength) || secretLength != SecretLength)
            return false;

        try
        {
            applicationId = StrictUtf8.GetString(Base64Url.DecodeFromChars(identifier));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(applicationId);
    }

    /// <summary>
    /// Whether <paramref name="key"/> hashes to the hash <paramref name="credential"/> stores, compared in constant
    /// time. A credential with another hash algorithm or a malformed hash never matches.
    /// </summary>
    public static bool Matches(string key, CredentialRecord credential)
    {
        if (!string.Equals(credential.HashAlgorithm, HashAlgorithm, StringComparison.Ordinal) ||
            credential.HashedSecret.Length != HashLength * 2)
            return false;

        Span<byte> stored = stackalloc byte[HashLength];
        if (Convert.FromHexString(credential.HashedSecret, stored, out _, out var written) != System.Buffers.OperationStatus.Done ||
            written != HashLength)
            return false;

        Span<byte> presented = stackalloc byte[HashLength];
        SHA256.HashData(Encoding.UTF8.GetBytes(key), presented);
        return CryptographicOperations.FixedTimeEquals(presented, stored);
    }
}
