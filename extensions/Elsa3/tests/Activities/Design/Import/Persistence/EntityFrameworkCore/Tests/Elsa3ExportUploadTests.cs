using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;
using Elsa3.Activities.Design.Import.Services;
using Xunit;
using static Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support.ImportFixtures;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests;

public sealed class Elsa3ExportUploadTests
{
    private const string Boundary = "elsa-export";
    private static readonly ReusableActivityImportAccessScope Scope = new("tenant-a", "user-a");

    [Fact]
    public async Task Mixed_exports_share_one_handle_and_preserve_overlapping_versions_for_analysis()
    {
        await using var harness = await SqliteImportHarness.CreateAsync();
        var service = harness.Database.Service(MutableAccess.Tenant("tenant-a"));
        var approval = Workflow("approval", "Exact-A", 1, true, Leaf("root"));
        var caller = Workflow("caller", "Exact-C", 1, false, Reference("call", approval.Id));
        var other = Workflow("other", "Exact-O", 1, false, Leaf("root"));
        byte[] ObjectBytes(object value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.Web);

        using var selection = new MultipartFormDataContent(Boundary)
        {
            { new ByteArrayContent(Zip(("approval.json", ObjectBytes(approval)))), "files", "original.ZIP" },
            { new ByteArrayContent(ObjectBytes(caller)), "files", "caller.JSON" },
            { new ByteArrayContent(ObjectBytes(new[] { other })), "files", "more.json" }
        };
        var uploader = Assert.IsAssignableFrom<IReusableActivityExportFileUpload>(service);
        var upload = await uploader.UploadExportFilesAsync(await selection.ReadAsStreamAsync(), Boundary, null, Scope);
        Assert.Equal(3, upload.SourceVersionCount);
        var page = await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, Scope);
        Assert.Equal(new[] { "Exact-A", "Exact-C", "Exact-O" }, page.Items.Select(item => item.SourceVersionId));
        var closure = await service.ExpandSelectionAsync(upload.CollectionHandle, page.PlanId, [caller.Id], Scope);
        Assert.Equal([approval.Id], closure.AddedDependencySourceVersionIds);

        selection.Add(new ByteArrayContent(ObjectBytes(approval)), "files", "also-approval.json");
        using var bytes = new MemoryStream(await selection.ReadAsByteArrayAsync());
        var overlapping = await uploader.UploadExportFilesAsync(bytes, Boundary, bytes.Length, Scope);
        var diagnostics = (await service.AnalyzeAsync(overlapping.CollectionHandle, 0, 10, Scope)).Diagnostics;
        Assert.Contains(diagnostics, d => d.Code == ReusableActivityImportDiagnosticCodes.DuplicateSourceVersion);
    }

    [Theory]
    [InlineData("invalid JSON", "batch.zip / second.json")]
    [InlineData("invalid ZIP", "batch.zip")]
    [InlineData("unsupported member", "batch.zip / notes.txt")]
    [InlineData("unsupported file", "notes.txt")]
    [InlineData("null", "batch.zip / second.json")]
    [InlineData("empty archive", "batch.zip")]
    [InlineData("entry count", "batch.zip")]
    [InlineData("definition count", "batch.zip / second.json")]
    [InlineData("expanded bytes", "batch.zip / second.json")]
    [InlineData("uploaded bytes", "batch.zip")]
    public async Task Invalid_batch_names_source_and_writes_no_handle(string scenario, string source)
    {
        await using var harness = await SqliteImportHarness.CreateAsync();
        var options = new ReusableActivityImportOptions();
        var definition = Encoding.UTF8.GetBytes("""{"id":"v","definitionId":"d"}""");
        var memberName = scenario == "unsupported member" ? "notes.txt" : "second.json";
        var second = scenario switch
        {
            "invalid JSON" => "{"u8.ToArray(),
            "null" => "[null]"u8.ToArray(),
            _ => definition
        };
        var archive = scenario switch
        {
            "invalid ZIP" => "not a ZIP"u8.ToArray(),
            "empty archive" => Zip(),
            _ => Zip(("first.json", definition), (memberName, second))
        };
        if (scenario == "entry count") options.MaximumArchiveEntries = 1;
        if (scenario == "definition count") options.MaximumSourceVersions = 2;
        if (scenario == "expanded bytes") options.MaximumExpandedBytes = definition.Length * 2;
        if (scenario == "uploaded bytes") options.MaximumUploadBytes = archive.Length;

        var service = harness.Database.Service(MutableAccess.Tenant("tenant-a"), options: options);
        using var batch = new MultipartFormDataContent(Boundary)
        {
            { new ByteArrayContent(definition), "files", "standalone.json" },
            { new ByteArrayContent(archive), "files", scenario == "unsupported file" ? "notes.txt" : "batch.zip" }
        };
        var uploader = Assert.IsAssignableFrom<IReusableActivityExportFileUpload>(service);
        var error = await Assert.ThrowsAsync<ReusableActivityImportPayloadException>(async () =>
            await uploader.UploadExportFilesAsync(await batch.ReadAsStreamAsync(), Boundary, null, Scope));
        Assert.Contains(source, error.Message);
        Assert.Equal(0, (await harness.Database.CountAsync()).Collections);
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] members)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in members)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(content);
            }
        return bytes.ToArray();
    }
}
