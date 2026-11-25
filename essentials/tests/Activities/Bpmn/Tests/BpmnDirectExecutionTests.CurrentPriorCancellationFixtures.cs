using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    private async Task<(string Outer, string Middle, string Inner, string Source, PriorCancellationCustody Custody)> RestoreCurrentPriorCancellationAsync()
    {
        const string stage = "pending-outer-result";
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CurrentPriorCancellation");
        using var manifestDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(folder, stage + ".json")));
        var manifest = manifestDocument.RootElement;
        Assert.Equal("current-writer-prior-scope-cancellation", manifest.GetProperty("kind").GetString());
        var compressed = await File.ReadAllBytesAsync(Path.Combine(folder, stage + ".sqlite.gz"));
        Assert.Equal(manifest.GetProperty("compressedByteLength").GetInt64(), compressed.LongLength);
        Assert.Equal(manifest.GetProperty("compressedSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(compressed)));
        await using (var input = new MemoryStream(compressed))
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await gzip.CopyToAsync(output);
        var raw = await File.ReadAllBytesAsync(database);
        Assert.Equal(manifest.GetProperty("byteLength").GetInt64(), raw.LongLength);
        Assert.Equal(manifest.GetProperty("databaseSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(raw)));
        var reportBytes = await File.ReadAllBytesAsync(Path.Combine(folder, stage + ".capture.json"));
        Assert.Equal(manifest.GetProperty("captureReportSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(reportBytes)));
        using var reportDocument = JsonDocument.Parse(reportBytes);
        var report = reportDocument.RootElement;
        var provenance = report.GetProperty("captureProvenance");
        Assert.False(provenance.GetProperty("sourceDirty").GetBoolean());
        var writerSource = manifest.GetProperty("writerSourceSha").GetString();
        Assert.Equal(writerSource, provenance.GetProperty("sourceSha").GetString());
        Assert.Equal(manifest.GetProperty("databaseSha256").GetString(), provenance.GetProperty("databaseSha256").GetString());
        string[] writers = ["Elsa.Activities.Bpmn.Tests.dll", "Elsa.Activities.Bpmn.dll", "Elsa.Workflows.Runtime.Core.dll",
            "Elsa.Workflows.Runtime.dll", "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll", "Elsa.Serialization.Core.dll",
            "Elsa.Activities.Runtime.Core.dll", "Bpmn.Model.dll", "Bpmn.Semantics.dll", "Elsa.Activities.Bpmn.Core.dll",
            "Elsa.Activities.Scheduling.dll", "Elsa.Workflows.Runtime.Scheduling.dll", "Elsa.Activities.Testing.dll", "Elsa.Serialization.SystemText.dll"];
        Assert.Equal(writers.Order(StringComparer.Ordinal), provenance.GetProperty("binaryHashes").EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal));
        Assert.Equal(writers.Order(StringComparer.Ordinal), manifest.GetProperty("binaries").EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal));
        foreach (var writer in writers)
        {
            Assert.Equal(manifest.GetProperty("binaries").GetProperty(writer).GetString(), provenance.GetProperty("binaryHashes").GetProperty(writer).GetString());
            var version = provenance.GetProperty("binaryVersions").GetProperty(writer).GetString();
            Assert.Equal(manifest.GetProperty("binaryVersions").GetProperty(writer).GetString(), version);
            if (writer.StartsWith("Elsa.", StringComparison.Ordinal)) Assert.EndsWith("+" + writerSource, version);
        }
        var observations = report.GetProperty("observations");
        var state = report.GetProperty("state").Deserialize<RuntimeModelExecutionState>(Web)!;
        var innerState = observations.GetProperty("innerState").Deserialize<RuntimeModelExecutionState>(Web)!;
        var native = report.GetProperty("native").Deserialize<WorkflowExecutionState[]>(Web)!.OrderBy(item => item.WorkflowExecutionId, StringComparer.Ordinal);
        var queue = Assert.Single(report.GetProperty("modelQueue").Deserialize<RuntimeSchedulerWorkItem[]>(Web)!);
        var cancellation = observations.GetProperty("cancellation").Deserialize<RuntimePostCommitOutboxItem>(Web)!;
        var custody = new PriorCancellationCustody(state, JsonSerializer.Serialize(innerState, Web), JsonSerializer.Serialize(native, Web),
            JsonSerializer.Serialize(queue, Web), JsonSerializer.Serialize(cancellation, Web));
        return (observations.GetProperty("outer").GetString()!, observations.GetProperty("middle").GetString()!,
            observations.GetProperty("inner").GetString()!, observations.GetProperty("source").GetString()!, custody);
    }
}
