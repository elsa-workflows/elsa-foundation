using System.Buffers.Binary;
using System.Security.Cryptography;
using Elsa.Foundation.Identity.Core.Authentication;

namespace Elsa.Foundation.Identity.OpenIddict;

/// <summary>Bounded, domain-separated keys independent of database collation and delimiter characters.</summary>
internal static class TokenSubjectKey
{
    public static string Create(TokenSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var kind = subject.CanonicalUserId is null ? "external" : "user";
        Append(hash, "elsa.token-subject.v1");
        Append(hash, kind);
        Append(hash, subject.TenantId);
        Append(hash, subject.SubjectId);
        if (subject.Provider is { } provider)
        {
            Append(hash, provider);
            Append(hash, subject.Issuer!);
        }
        return $"elsa:v1:{kind}:{Convert.ToHexStringLower(hash.GetHashAndReset())}";
    }

    private static void Append(IncrementalHash hash, string value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
        hash.AppendData(length);
        Span<byte> codeUnit = stackalloc byte[sizeof(char)];
        foreach (var character in value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(codeUnit, character);
            hash.AppendData(codeUnit);
        }
    }
}
