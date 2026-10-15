using System.Security.Cryptography;
using System.Text;

namespace Elsa.Integrations.Sdk.OAuth;

/// <summary>RFC 7636 S256 PKCE helpers for authorization-code.</summary>
internal static class Pkce
{
    public static string CreateVerifier()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url(bytes);
    }

    public static string ChallengeS256(string verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifier);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    public static string CreateState()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url(bytes);
    }

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
