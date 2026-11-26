using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
            ["Nuplane:Reconciliation:PollInterval"] = "1.00:00:00",
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
        var stagingDirectory = Path.Combine(workbench.ContentRoot, "upload-fixture");
        Directory.CreateDirectory(Path.Combine(stagingDirectory, "packages"));
        var package = NativeHostingScenario.CreatePackage(stagingDirectory);
        using var content = new StreamContent(File.OpenRead(package));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var form = new MultipartFormDataContent { { content, "package", Path.GetFileName(package) } };

        using var response = await AnsweredAsync(workbench, "POST", $"{Api}/packages/upload", workbench.ManagementClient.PostAsync($"{Api}/packages/upload", form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reconcile = (await response.Content.ReadFromJsonAsync<JsonNode>())!["reconcile"]!;
        Assert.Equal("Completed", reconcile["outcome"]!.GetValue<string>());
        Assert.False(reconcile["isDegraded"]!.GetValue<bool>(), reconcile.ToJsonString());
        var registry = await workbench.ManagementClient.GetFromJsonAsync<JsonNode>($"{Api}/registry");
        Assert.Contains(NativeHostingScenario.PackageId, registry!["modules"]!.AsArray().Select(module => module!["packageId"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Deleting_a_dropped_package_runs_its_background_reconcile()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(OnDemandReconcile);
        var file = Path.GetFileName(NativeHostingScenario.CreatePackage(workbench.ContentRoot));

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
}
