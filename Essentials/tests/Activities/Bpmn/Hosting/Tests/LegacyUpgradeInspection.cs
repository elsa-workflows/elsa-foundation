using System.Security.Cryptography;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>Read-only checks shared by actual saved-store host qualifications.</summary>
internal static class LegacyUpgradeInspection
{
    internal static void AssertStandardHost(OwnedWorkbench host)
    {
        using var assembly = File.OpenRead(host.AssemblyPath); using var pe = new PEReader(assembly); var metadata = pe.GetMetadataReader();
        Assert.DoesNotContain(metadata.TypeDefinitions, type => metadata.GetString(metadata.GetTypeDefinition(type).Name) == "BpmnRecoveryProbeFeature");
    }
    internal static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static JsonNode Activity(JsonNode view, string node) =>
        Assert.Single(view["activities"]!.AsArray(), activity => activity!["executableNodeId"]!.GetValue<string>() == node)!;
    internal static void SamePin(JsonNode expected, JsonNode actual)
    {
        foreach (var field in new[] { "workflowExecutionId", "artifactId", "artifactHash", "artifactVersion", "definitionId", "definitionVersionId" })
            Assert.Equal(expected[field]!.GetValue<string>(), actual[field]!.GetValue<string>());
    }
    internal static void SameView(JsonNode expected, JsonNode actual)
    {
        var differences = new List<string>();
        Compare(expected, actual, "$", differences);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Take(20)));
        static void Compare(JsonNode? before, JsonNode? after, string path, List<string> differences)
        {
            if (JsonNode.DeepEquals(before, after)) return;
            // Rehydrated native timestamps may render UTC instead of the original offset; preserve the exact instant.
            var field = path[(path.LastIndexOf('.') + 1)..];
            if (field is "createdAt" or "startedAt" or "updatedAt" or "scheduledAt" or "lastCommittedAt" or "completedAt" or "appliedAt" &&
                before is JsonValue oldValue && after is JsonValue newValue && oldValue.TryGetValue<string>(out var oldText) &&
                newValue.TryGetValue<string>(out var newText) && DateTimeOffset.TryParse(oldText, out var oldTime) &&
                DateTimeOffset.TryParse(newText, out var newTime) && oldTime.UtcTicks == newTime.UtcTicks) return;
            if (before is JsonObject left && after is JsonObject right)
                foreach (var key in left.Select(pair => pair.Key).Union(right.Select(pair => pair.Key))) Compare(left[key], right[key], path + "." + key, differences);
            else if (before is JsonArray first && after is JsonArray second && first.Count == second.Count)
                for (var i = 0; i < first.Count; i++) Compare(first[i], second[i], path + "[" + i + "]", differences);
            else differences.Add(path + ": " + before?.ToJsonString() + " -> " + after?.ToJsonString());
        }
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

    internal static async Task BackupAsync(string source, string destination)
    {
        if (File.Exists(destination)) throw new IOException("An owned closed backup must not overwrite an existing input.");
        await using var input = Read(source); await using var output = new SqliteConnection($"Data Source={destination};Pooling=False");
        await input.OpenAsync(); await output.OpenAsync(); input.BackupDatabase(output);
        await using var command = output.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", (string)(await command.ExecuteScalarAsync())!);
    }

    internal static async Task<long> SourceStatusAsync(string database, string id)
    {
        await using var connection = Read(database); await connection.OpenAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId = $id";
        command.Parameters.AddWithValue("$id", Convert.ToBase64String(Encoding.Unicode.GetBytes(id))); return (long)(await command.ExecuteScalarAsync())!;
    }

    internal static async Task<JsonNode> CheckedSnapshotAsync(string reader, string database, string? command, params string[] arguments)
    {
        var hash = Hash(await File.ReadAllBytesAsync(database));
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add(reader);
        if (command is not null) info.ArgumentList.Add(command);
        foreach (var argument in new[] { database, hash, "default" }.Concat(arguments)) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The checked snapshot reader did not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(limit.Token); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        var text = await output; Assert.True(process.ExitCode == 0, "Actual checked provider refused: " + text + await error);
        var result = JsonNode.Parse(text)!; Assert.True(result["providerChecked"]!.GetValue<bool>()); Assert.True(result["readOnly"]!.GetValue<bool>());
        Assert.Equal(hash, result["snapshotSha256"]!.GetValue<string>()); Assert.Equal(hash, Hash(await File.ReadAllBytesAsync(database))); return result;
    }

    internal static async Task<object?> ExportSnapshotAsync(string database, string name, string directory)
    {
        if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is not { } root) return null;
        var output = Path.Combine(root, directory); Directory.CreateDirectory(output); var path = Path.Combine(output, name);
        var data = await File.ReadAllBytesAsync(database);
        await using (var file = File.Create(path)) await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize)) await gzip.WriteAsync(data);
        return new { file = name, databaseSha256 = Hash(data), gzipSha256 = Hash(await File.ReadAllBytesAsync(path)) };
    }
}
