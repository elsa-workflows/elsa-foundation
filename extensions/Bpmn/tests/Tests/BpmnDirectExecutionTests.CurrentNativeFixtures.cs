using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    private async Task<RuntimeModelExecutionState> RestoreCurrentNativeAsync(string name)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "CurrentNative", name);
        using var receipt = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(folder, "receipt.json")));
        var manifestBytes = await File.ReadAllBytesAsync(Path.Combine(folder, "manifest.json"));
        Assert.Equal(receipt.RootElement.GetProperty("manifestSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(manifestBytes)));
        using var manifest = JsonDocument.Parse(manifestBytes);
        Assert.Equal(receipt.RootElement.GetProperty("writerSourceSha").GetString(), manifest.RootElement.GetProperty("sourceRevision").GetString());
        var assets = manifest.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        async Task<byte[]> ReadAsync(string fileName)
        {
            var asset = Assert.Single(assets, item => Path.GetFileName(item.GetProperty("path").GetString()) == fileName);
            var bytes = await File.ReadAllBytesAsync(Path.Combine(folder, asset.GetProperty("path").GetString()!));
            Assert.Equal(asset.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            return bytes;
        }
        using var process = JsonDocument.Parse(await ReadAsync("process.json"));
        Assert.Equal(manifest.RootElement.GetProperty("sourceRevision").GetString(), process.RootElement.GetProperty("sourceRevision").GetString());
        var products = process.RootElement.GetProperty("products").GetProperty("actualProductVersions");
        Assert.True(JsonCanonicalForm.Equals(products, receipt.RootElement.GetProperty("writers")));
        Assert.Equal(14, products.GetArrayLength());
        foreach (var writer in new[] { "Elsa.Serialization.Core.dll", "Elsa.Activities.Runtime.Core.dll", "Elsa.Workflows.Runtime.dll" })
        {
            var product = Assert.Single(products.EnumerateArray(), item => item.GetProperty("name").GetString() == writer);
            Assert.EndsWith("+" + manifest.RootElement.GetProperty("sourceRevision").GetString(), product.GetProperty("productVersion").GetString());
        }
        var compressed = await ReadAsync("pending-elsa.sqlite.gz");
        await using (var input = new MemoryStream(compressed))
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await gzip.CopyToAsync(output);
        var raw = await File.ReadAllBytesAsync(database);
        Assert.Equal(process.RootElement.GetProperty("pendingStores").GetProperty("elsa").GetProperty("databaseSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(raw)));
        using var checkedState = JsonDocument.Parse(await ReadAsync("pending-checked.json"));
        return checkedState.RootElement.GetProperty("model").Deserialize<RuntimeModelExecutionState>(Web)!;
    }

    [Theory]
    [InlineData("ActualNativeStartRetirement.sqlite.gz", "bd5e9a8d447bb63a85affd1402fa5b1cfe98dbf58b344cd1ed2bb733d859160d")]
    [InlineData("ActualIndependentNativeCancellation.sqlite.gz", "1380bc749db55812d0e2f6aaff2428bc4bdc06aa962ffca7ed7f52ef79595dc1")]
    [InlineData("ActualPriorScopeCancellation.sqlite.gz", "f19abce8444b42284483f511018039a48e39dd4d9cd6a53ab348a55191f4ecb4")]
    public async Task Archived_native_material_is_refused_without_rewriting_its_original_payloads(string fixture, string expectedHash)
    {
        await using (var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)))
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await gzip.CopyToAsync(output);
        Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(database))));
        var originalPayloads = await FaultFixtureRowsAsync(payloadsOnly: true);
        await using (var host = await OpenAsync(admitNewExecutions: false))
        await using (var scope = await ScopeAsync(host))
        {
            var before = await FaultFixtureRowsAsync();
            var states = (await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().ListPageAsync(new())).Items;
            var state = fixture == "ActualPriorScopeCancellation.sqlite.gz"
                ? Assert.Single(states, item => item.ExecutionId == "bpmn:723068f9ada40fbde52746a0678a2f5c65e61ef6831c683f85b0098a3daaa044")
                : Assert.Single(states);
            var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(state.Executable.ArtifactId));
            Assert.Equal("The persisted model material payload is corrupt.", error.Message);
            var cause = Assert.IsType<ArgumentException>(error.InnerException);
            Assert.Equal("material", cause.ParamName);
            Assert.StartsWith("Model material requires its complete behavioral content hash and a supported artifact ID.", cause.Message);
            Assert.Equal(before, await FaultFixtureRowsAsync());
        }
        Assert.Equal(originalPayloads, await FaultFixtureRowsAsync(payloadsOnly: true));
    }
}
