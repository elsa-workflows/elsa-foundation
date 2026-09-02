using System.Security.Cryptography;
using System.Text;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>Mirrors Groundwork's stable logical-to-physical index-name composition for retained plans.</summary>
internal static class GroundworkPhysicalIndexNames
{
    public static string For(string provider, string tableName, string indexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        var composed = $"__groundwork_ix_{tableName.Length}_{tableName}_{indexName.Length}_{indexName}";
        return provider switch
        {
            "mongodb" => indexName,
            "sqlite" => composed,
            "postgresql" => Truncate(composed, 63, 10),
            "sqlserver" => Truncate(composed, 128, 12),
            _ => throw new PerformanceContractException(
                $"Provider-native plan admission does not support provider '{provider}'.")
        };
    }

    private static string Truncate(string composed, int maximumLength, int digestLength) =>
        composed.Length <= maximumLength
            ? composed
            : composed[..(maximumLength - digestLength - 1)] + "_" +
              Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(composed)))[..digestLength].ToLowerInvariant();
}
