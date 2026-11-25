using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    private async Task<RuntimeModelExecutionState> RestoreCurrentFaultAsync(bool admitted, string stage = "pending-settlement")
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CurrentFault", admitted ? "concurrent-admitted" : "concurrent-unadmitted");
        var compressed = await File.ReadAllBytesAsync(Path.Combine(folder, stage + ".sqlite.gz"));
        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, stage + ".json")));
        var manifest = receipt.RootElement;
        Assert.Equal(manifest.GetProperty("compressedSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(compressed)));
        await using (var input = new MemoryStream(compressed))
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await gzip.CopyToAsync(output);
        var raw = await File.ReadAllBytesAsync(database);
        Assert.Equal(manifest.GetProperty("byteLength").GetInt64(), raw.LongLength);
        Assert.Equal(manifest.GetProperty("databaseSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(raw)));
        var reportBytes = await File.ReadAllBytesAsync(Path.Combine(folder, stage + ".capture.json"));
        Assert.Equal(manifest.GetProperty("captureReportSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reportBytes)));
        using var report = JsonDocument.Parse(reportBytes);
        var provenance = report.RootElement.GetProperty("captureProvenance");
        Assert.False(provenance.GetProperty("sourceDirty").GetBoolean());
        Assert.Equal(manifest.GetProperty("writerSourceSha").GetString(), provenance.GetProperty("sourceSha").GetString());
        Assert.Equal(manifest.GetProperty("databaseSha256").GetString(), provenance.GetProperty("databaseSha256").GetString());
        foreach (var writer in new[] { "Elsa.Serialization.Core.dll", "Elsa.Activities.Runtime.Core.dll", "Elsa.Activities.Bpmn.Tests.dll" })
        {
            Assert.Equal(manifest.GetProperty("binaries").GetProperty(writer).GetString(), provenance.GetProperty("binaryHashes").GetProperty(writer).GetString());
            Assert.EndsWith("+" + manifest.GetProperty("writerSourceSha").GetString(), provenance.GetProperty("binaryVersions").GetProperty(writer).GetString());
        }
        return report.RootElement.GetProperty("state").Deserialize<RuntimeModelExecutionState>(Web)!;
    }

    [Fact]
    public async Task Inspection_of_the_current_initial_fault_fixture_preserves_both_requested_attempts()
    {
        var captured = await RestoreCurrentFaultAsync(true, "initial-requested");
        await using var host = await OpenAsync(admitNewExecutions: false);
        await using var scope = await ScopeAsync(host);
        var inspection = await scope.ServiceProvider.GetRequiredService<Elsa.Activities.Bpmn.Core.IBpmnModelRuntime>()
            .InspectAsync(captured.ExecutionId, new("operator", captured.TenantId));
        Assert.Equal(JsonSerializer.Serialize(captured, Web), JsonSerializer.Serialize(inspection.State, Web));
        Assert.Equal(2, inspection.Attempts.Count);
        Assert.All(inspection.Attempts, attempt => Assert.Equal("Requested", attempt.Status));
        Assert.Equal(2, inspection.Attempts.Select(attempt => attempt.AttemptId).Distinct(StringComparer.Ordinal).Count());
        await AssertNativeEmptyAsync(scope.ServiceProvider);
    }

    [Theory]
    [InlineData(false, "bd663a394a582994c07059aa2e9efdeb74984b4a140ec2051d5908e65a3be5fe")]
    [InlineData(true, "d148588618d8f7ac06bc980caf561b35945de511115d0f6dd1a9dfe6774e1bc0")]
    public async Task Archived_concurrent_fault_material_is_refused_without_rewriting_its_historical_state(bool admitted, string expectedHash)
    {
        await using (var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", admitted ? "ActualConcurrentAdmittedFault.sqlite.gz" : "ActualConcurrentFault.sqlite.gz")))
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await gzip.CopyToAsync(output);
        Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(database))));
        var originalPayloads = await FaultFixtureRowsAsync(payloadsOnly: true);
        await using (var host = await OpenAsync(admitNewExecutions: false))
        await using (var scope = await ScopeAsync(host))
        {
            // Opening applies the initiative's existing schema migrations. Compare the refusal itself
            // against that complete physical state, and compare original retained payload bytes below.
            var before = await FaultFixtureRowsAsync();
            var state = Assert.Single((await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().ListPageAsync(new())).Items);
            var recoveryInput = "bpmn-fault-recovery:" + BpmnModelIdentity.Create(state.Scope.Value, state.ExecutionId,
                state.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var queued = Assert.Single((await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(state.ExecutionId))).Items);
            Assert.Equal(recoveryInput, queued.CommandId);
            var recovery = queued.Payload!.Value.Deserialize<RuntimeModelExecutionCommand>(Web)!;
            Assert.Equal(state.Executable, recovery.Executable);
            Assert.Equal(state.MaterialIncarnationId, recovery.MaterialIncarnationId);
            var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(state.Executable.ArtifactId));
            Assert.Equal("The persisted model material payload is corrupt.", error.Message);
            var cause = Assert.IsType<ArgumentException>(error.InnerException);
            Assert.Equal("material", cause.ParamName);
            Assert.StartsWith("Model material requires its complete behavioral content hash and a supported artifact ID.", cause.Message);
            Assert.Equal(before, await FaultFixtureRowsAsync());
        }
        var afterPayloads = await FaultFixtureRowsAsync(payloadsOnly: true);
        Assert.Equal(originalPayloads.Length + 1, afterPayloads.Length);
        foreach (var original in originalPayloads.GroupBy(payload => payload, StringComparer.Ordinal))
            Assert.True(afterPayloads.Count(payload => payload == original.Key) >= original.Count(), "An original historical payload was rewritten or removed.");
        // Existing BPMN startup schedules its documented old-fault debt; it does not rewrite the archive.
        var added = Assert.Single(afterPayloads.Except(originalPayloads, StringComparer.Ordinal));
        Assert.StartsWith("elsa_runtime_scheduler_work_item:", added);
    }

    private async Task<string[]> FaultFixtureRowsAsync(bool payloadsOnly = false)
    {
        await using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            await using var rows = await command.ExecuteReaderAsync();
            while (await rows.ReadAsync()) tables.Add(rows.GetString(0));
        }
        var result = new List<string>();
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            await using var rows = await command.ExecuteReaderAsync();
            var contentColumn = Enumerable.Range(0, rows.FieldCount).FirstOrDefault(index => rows.GetName(index) == "ContentJson", -1);
            if (payloadsOnly && contentColumn < 0) continue;
            var records = new List<string>();
            while (await rows.ReadAsync())
            {
                if (payloadsOnly) { records.Add(rows.GetString(contentColumn)); continue; }
                var values = new object[rows.FieldCount];
                rows.GetValues(values);
                records.Add(JsonSerializer.Serialize(values, Web));
            }
            result.AddRange(records.Order(StringComparer.Ordinal).Select(record => table + ":" + record));
        }
        return result.ToArray();
    }
}
