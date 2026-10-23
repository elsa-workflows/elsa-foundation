using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// #2159: every module-management route of the built Workbench that triggers a Nuplane reconcile, run as an operator runs
/// it. The routes are mapped on the host, but the path-less shell resolves the request, and CShells copies every root
/// registration into every shell, so an <c>INuplaneAdminOperations</c> taken from the request's services enqueues on a
/// second trigger queue that no dispatcher reads: the request never returns, and the background reconcile never runs.
/// </summary>
public sealed class WorkbenchModuleManagementReconcileTests
{
    private const string Api = "/_elsa/module-management";
    private const string PackageId = "Elsa.Workbench.Tests.ModuleManagementMarker";

    /// <summary>The reconcile itself takes seconds; a request that outlasts this has hung, as the sibling suites bound theirs.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The stock Development composition with the periodic poll pushed out of reach and the drop folder's watcher off, so the
    /// only reconcile that runs is the one the test asks for: a poll cycle or a watcher pass in flight would answer the request
    /// under test with a skipped outcome.
    /// </summary>
    private static readonly WorkbenchShell OnDemandReconcile = WorkbenchShell.Development with
    {
        Settings = new Dictionary<string, string>
        {
            ["Nuplane:Setup:PollInterval"] = "1.00:00:00",
            ["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false"
        }
    };

    [Fact]
    public async Task Reconciling_on_demand_answers_with_a_completed_outcome()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(OnDemandReconcile);

        using var response = await AnsweredAsync(workbench, "POST", $"{Api}/reconcile", workbench.ManagementClient.PostAsync($"{Api}/reconcile", content: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Completed", await OutcomeAsync(response));
    }

    [Fact]
    public async Task Uploading_a_package_reconciles_it_in_before_answering()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(OnDemandReconcile);
        using var form = new MultipartFormDataContent { { PackageContent(PackageId), "package", $"{PackageId}.1.0.0.nupkg" } };

        using var response = await AnsweredAsync(workbench, "POST", $"{Api}/packages/upload", workbench.ManagementClient.PostAsync($"{Api}/packages/upload", form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Completed", await OutcomeAsync(response));
        var registry = await workbench.ManagementClient.GetFromJsonAsync<JsonNode>($"{Api}/registry");
        Assert.Contains(PackageId, registry!["modules"]!.AsArray().Select(module => module!["packageId"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Deleting_a_dropped_package_runs_its_background_reconcile()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(OnDemandReconcile);
        var file = $"{PackageId}.1.0.0.nupkg";
        await File.WriteAllBytesAsync(Path.Join(workbench.ContentRoot, "packages", file), await PackageContent(PackageId).ReadAsByteArrayAsync());

        var path = $"{Api}/packages/drop-folder/{file}";

        using var response = await AnsweredAsync(workbench, "DELETE", path, workbench.ManagementClient.DeleteAsync(path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await WaitForOutputAsync(workbench, "Background Nuplane reconciliation completed after package deletion. Outcome=Completed");
    }

    /// <summary>The request, bounded by <see cref="Patience"/>: a host that never answers fails the test with what it logged, instead of hanging it.</summary>
    private static async Task<HttpResponseMessage> AnsweredAsync(WorkbenchProcess workbench, string method, string path, Task<HttpResponseMessage> request)
    {
        try
        {
            return await request.WaitAsync(Patience);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException($"{method} {path} was not answered within {Patience}. Host output:{Environment.NewLine}{workbench.Output}", exception);
        }
    }

    private static async Task<string?> OutcomeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonNode>())!["reconcile"]!["outcome"]!.GetValue<string>();

    private static async Task WaitForOutputAsync(WorkbenchProcess workbench, string expected)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!workbench.Output.Contains(expected, StringComparison.Ordinal))
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, $"The host never logged '{expected}' within {Patience}. Host output:{Environment.NewLine}{workbench.Output}");
            await Task.Delay(250);
        }
    }

    /// <summary>A package with nothing in it but its identity: enough for a directory feed to list it and Nuplane to install it.</summary>
    private static ByteArrayContent PackageContent(string id)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", """<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" /><Default Extension="nuspec" ContentType="application/octet" /><Default Extension="_" ContentType="application/octet" /></Types>""");
            Add(archive, "_rels/.rels", $"""<?xml version="1.0" encoding="utf-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{id}.nuspec" Id="R1" /></Relationships>""");
            Add(archive, $"{id}.nuspec", $"""<?xml version="1.0" encoding="utf-8"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{id}</id><version>1.0.0</version><authors>tests</authors><description>Marks a package dropped in by a module-management test.</description></metadata></package>""");
            Add(archive, "lib/net10.0/_._", "");
        }

        var content = new ByteArrayContent(stream.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private static void Add(ZipArchive archive, string name, string text)
    {
        using var entry = archive.CreateEntry(name).Open();
        entry.Write(Encoding.UTF8.GetBytes(text));
    }
}
