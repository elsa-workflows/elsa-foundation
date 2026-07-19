using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Testing;

public sealed record GroundworkProviderSchemaStateSnapshot
{
    private GroundworkProviderSchemaStateSnapshot(
        string providerKey,
        string fingerprint,
        int canonicalItemCount,
        GroundworkSanitizedEvidence evidence)
    {
        ProviderKey = providerKey;
        Fingerprint = fingerprint;
        CanonicalItemCount = canonicalItemCount;
        Evidence = evidence;
    }

    public string ProviderKey { get; }
    public string Fingerprint { get; }
    public int CanonicalItemCount { get; }
    public GroundworkSanitizedEvidence Evidence { get; }

    public static GroundworkProviderSchemaStateSnapshot Create(
        string providerKey,
        IEnumerable<string> canonicalItems)
    {
        EvidenceCatalog.EnsureProviderKey(providerKey);
        ArgumentNullException.ThrowIfNull(canonicalItems);
        var items = canonicalItems.Order(StringComparer.Ordinal).ToArray();
        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', items))));
        return new GroundworkProviderSchemaStateSnapshot(
            providerKey,
            fingerprint,
            items.Length,
            GroundworkSanitizedEvidence.Create(
                "provider-schema-state",
                $"provider={providerKey}\nfingerprint={fingerprint}\ncanonical-item-count={items.Length}"));
    }
}

public sealed record GroundworkSchemaToolResult(
    int ExitCode,
    JsonElement Report,
    string StandardError);

internal static class GroundworkRelationalSchemaStateReader
{
    public static async Task<GroundworkProviderSchemaStateSnapshot> CaptureAsync(
        string providerKey,
        DbConnection connection,
        IEnumerable<string> catalogItems,
        IReadOnlyCollection<string> tableNames,
        Func<string, string> quoteTable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(catalogItems);
        ArgumentNullException.ThrowIfNull(tableNames);
        ArgumentNullException.ThrowIfNull(quoteTable);
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        var items = new List<string>(catalogItems);
        foreach (var tableName in tableNames.Order(StringComparer.Ordinal))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {quoteTable(tableName)};";
            var rows = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var values = new string[reader.FieldCount];
                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                    values[ordinal] = CanonicalValue(reader.GetValue(ordinal));
                rows.Add(string.Join('\u001f', values));
            }
            rows.Sort(StringComparer.Ordinal);
            items.AddRange(rows.Select(row => $"row\u001f{tableName}\u001f{row}"));
        }

        return GroundworkProviderSchemaStateSnapshot.Create(providerKey, items);
    }

    private static string CanonicalValue(object value) => value switch
    {
        DBNull => "null",
        byte[] bytes => Convert.ToHexString(bytes),
        DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
}
