using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The frozen mechanical identity rule shared by scheduler owners and cross-model contributors.</summary>
public static class RuntimeChainIdentity
{
    /// <summary>Number of leading lowercase SHA-256 hex characters retained in a successor identity.</summary>
    public const int FingerprintLength = 16;

    /// <summary>Derives the existing bounded successor identity without changing persisted identifier bytes.</summary>
    public static string Derive(string parentId, string step)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        return $"{Fingerprint(parentId)}:{step}";
    }

    /// <summary>Returns the stable leading lowercase SHA-256 hex characters of the exact UTF-8 value.</summary>
    public static string Fingerprint(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..FingerprintLength];
    }
}
