using System.Net;
using System.Text;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// One host that started with <c>Elsa3JsonReconciliation</c> on a folder of Elsa 3 exports, on an empty store: the
/// approval reusable and its caller, the nested <c>inner</c>/<c>outer</c> reusables, and an HTTP endpoint workflow. File
/// names put callers before what they call, so the import cannot rely on file order. No request carries a tenant claim
/// while the host starts, so the reusables publish through the startup grant.
/// </summary>
public sealed class FolderImportFixture : IAsyncLifetime
{
    private DirectoryInfo _folder = null!;

    public Elsa3ImportHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _folder = Directory.CreateTempSubdirectory("elsa3-folder-");
        Copy("caller-routes-outcome", "a-caller.json");
        Copy("http-endpoint", "b-http.json");
        Copy("reusable-calls-reusable", "c-nested.json");
        Copy("reusable-two-outcomes", "z-approval.json");
        Host = await Elsa3ImportHost.StartAsync(withHttp: true, reconcileFolder: _folder.FullName);
    }

    private void Copy(string fixture, string fileName) =>
        File.Copy(Path.Join(AppContext.BaseDirectory, "Fixtures", $"{fixture}.json"), Path.Join(_folder.FullName, fileName));

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        try
        {
            _folder.Delete(recursive: true);
        }
        catch (IOException)
        {
            // Cleaned with the temp folder later.
        }
    }
}

/// <summary>Spec 235 User Story 1: an Elsa 3 folder becomes runnable Elsa 4 workflows at start.</summary>
public sealed class Elsa3JsonReconciliationTests(FolderImportFixture fixture) : IClassFixture<FolderImportFixture>
{
    private Elsa3ImportHost Host => fixture.Host;

    [Theory]
    [InlineData("caller")]
    [InlineData("http-hello")]
    [InlineData("approval")]
    [InlineData("outer")]
    [InlineData("inner")]
    public async Task Each_workflow_and_wrapper_is_published_under_its_elsa3_id_in_the_scope_tenant(string definitionId)
    {
        var publication = await Host.ActivePublicationAsync(definitionId);

        Assert.True(publication is not null, $"No active publication of '{definitionId}'. Log: {string.Join(" | ", Host.Warnings.Entries)}");
        Assert.Equal(definitionId, publication.Value.Reference.DefinitionId);
        Assert.Equal(Elsa3ImportHost.TenantId, publication.Value.Reference.TenantId);
    }

    [Fact]
    public async Task Only_the_newest_version_of_a_workflow_is_published()
    {
        // The folder holds inner v1 and v2: only v2's wrapper is published, and v1's never was.
        var publication = await Host.ActivePublicationAsync("inner");
        var records = await Host.PublicationRecordsAsync("inner");

        Assert.NotNull(publication);
        var record = Assert.Single(records);
        Assert.Equal(publication.Value.Workflow.VersionId, record.WorkflowDefinitionVersionId);
        Assert.NotEqual(
            await Host.ReadAsync(services => WorkflowVersionIdAsync(services, "inner", "1.0.0")),
            record.WorkflowDefinitionVersionId);
    }

    private static async Task<string?> WorkflowVersionIdAsync(IServiceProvider services, string definitionId, string version)
    {
        var versions = await services.GetRequiredService<Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore>()
            .ListByDefinitionAsync(definitionId);
        return versions.SingleOrDefault(candidate => candidate.Version == version)?.Id;
    }

    [Fact]
    public async Task Caller_runs_the_imported_reusable_and_follows_its_outcome()
    {
        var caller = (await Host.ActivePublicationAsync("caller"))!.Value.Workflow;

        var run = await Host.RunAsync(caller);

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Contains("on-approved", run.ExecutedNodeIds);
        Assert.DoesNotContain("on-rejected", run.ExecutedNodeIds);
    }

    [Fact]
    public async Task Nested_reusables_run_through_the_outer_wrapper()
    {
        var outer = (await Host.ActivePublicationAsync("outer"))!.Value.Workflow;

        var run = await Host.RunAsync(outer);

        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
    }

    [Fact]
    public async Task Imported_http_endpoint_answers()
    {
        using var response = await Host.Client.PostAsync("workflows/http/elsa3/hello", new StringContent("{}", Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var started = System.Text.Json.Nodes.JsonNode.Parse(body)?["started"]?.AsArray().FirstOrDefault()?.GetValue<string>();
            var run = started is null ? null : await Host.WaitForSettledAsync(started);
            Assert.Fail($"{(int)response.StatusCode}: {body}\n{run?.Describe()}");
        }
        Assert.Equal("Hello from Elsa 3", body);
    }
}
