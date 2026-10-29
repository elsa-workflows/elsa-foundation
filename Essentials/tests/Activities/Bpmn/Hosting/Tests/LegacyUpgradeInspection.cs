using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>Read-only checks shared by actual saved-store host qualifications.</summary>
internal static class LegacyUpgradeInspection
{
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static JsonNode Activity(JsonNode view, string node) =>
        Assert.Single(view["activities"]!.AsArray(), activity => activity!["executableNodeId"]!.GetValue<string>() == node)!;
    internal static void SamePin(JsonNode expected, JsonNode actual)
    {
        foreach (var field in new[] { "workflowExecutionId", "artifactId", "artifactHash", "artifactVersion", "definitionId", "definitionVersionId" })
            Assert.Equal(expected[field]!.GetValue<string>(), actual[field]!.GetValue<string>());
    }
    internal static async Task<List<string>> MigrationsAsync(string database, bool immutable = false)
    {
        await using var connection = Read(database, immutable);
        await connection.OpenAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory_ElsaRuntime ORDER BY MigrationId";
        await using var reader = await command.ExecuteReaderAsync(); var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0) + "|" + reader.GetString(1));
        return rows;
    }
    internal static async Task<SortedDictionary<string, string>> ArtifactsAsync(string database, bool immutable = false)
    {
        await using var connection = Read(database, immutable);
        await connection.OpenAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, IncarnationId, ContentJson FROM elsa_runtime_workflow_executable ORDER BY Id";
        await using var reader = await command.ExecuteReaderAsync(); var rows = new SortedDictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0), reader.GetString(1) + ":" + Hash(Encoding.UTF8.GetBytes(reader.GetString(2))));
        return rows;
    }

    internal static SqliteConnection Read(string database, bool immutable = false) => new(new SqliteConnectionStringBuilder
    {
        DataSource = immutable ? new Uri(Path.GetFullPath(database)).AbsoluteUri + "?immutable=1" : database,
        Mode = SqliteOpenMode.ReadOnly,
        Pooling = false
    }.ToString());
}
