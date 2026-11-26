using System.Text.Json.Nodes;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Activities.Design.Import.Reconciliation.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// The shared import sequence's result (spec 1237 FR-013, research R-2): skips with their cause and blocker, the
/// superseded older workflow version, and a refusal it cannot pin on one definition returned instead of thrown.
/// </summary>
public sealed class Elsa3ImportRunTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("elsa3-run-");

    [Fact]
    public async Task An_older_selected_workflow_version_is_imported_but_superseded_by_the_newest()
    {
        await using var host = await StartAsync();
        var v2 = HttpHello("Hello from version 2");
        v2["id"] = "http-hello-v2";
        v2["version"] = 2;

        var result = await RunAsync(host, HttpHello("Hello from version 1"), v2);

        Assert.Null(result.Refusal);
        Assert.Empty(result.Skipped);
        var newest = Assert.Single(result.WorkflowPublications, publication => publication.Workflow.SourceVersionId == "http-hello-v2");
        Assert.Equal(WorkflowPublicationStatus.Published, newest.Status);
        Assert.Null(newest.SupersededBySourceVersionId);
        var older = Assert.Single(result.WorkflowPublications, publication => publication.Workflow.SourceVersionId == "http-hello-v1");
        Assert.Null(older.Status);
        Assert.Equal("http-hello-v2", older.SupersededBySourceVersionId);
        Assert.Equal(1, result.WorkflowsPublished);
        Assert.Equal(newest.Workflow.WorkflowVersionId, (await host.ActivePublicationAsync("http-hello"))!.Value.Workflow.VersionId);
    }

    [Fact]
    public async Task Skips_carry_their_code_cause_and_the_item_that_blocks_them()
    {
        await using var host = await StartAsync();
        var broken = Fixture("reusable-two-outcomes");
        broken["id"] = "broken-v1";
        broken["definitionId"] = "broken";
        broken["name"] = "broken";
        broken["root"]!["activities"]!.AsArray()[0]!["type"] = "Elsa.StateMachine";
        var caller = Fixture("caller-routes-outcome");
        caller["id"] = "broken-caller-v1";
        caller["definitionId"] = "broken-caller";
        caller["name"] = "broken-caller";
        var call = caller["root"]!["activities"]!.AsArray().Single(activity => (string?)activity!["id"] == "call")!;
        call["type"] = "broken";
        call["workflowDefinitionVersionId"] = "broken-v1";
        var duplicate = HttpHello("Twice");

        var result = await RunAsync(host, broken, caller, duplicate, duplicate.DeepClone());

        Assert.Null(result.Refusal);
        var root = Assert.Single(result.Skipped, skip => skip.SourceVersionId == "broken-v1");
        Assert.Null(root.BlockedBy);
        Assert.False(string.IsNullOrWhiteSpace(root.Cause));
        var dependent = Assert.Single(result.Skipped, skip => skip.SourceVersionId == "broken-caller-v1");
        Assert.Equal(Elsa3ReconciliationDiagnosticCodes.BlockedByDependency, dependent.Code);
        Assert.Equal("broken-v1", dependent.BlockedBy);
        Assert.Equal(2, result.Skipped.Count(skip => skip.Code == Elsa3ReconciliationDiagnosticCodes.DuplicateInput && skip.SourceVersionId == "http-hello-v1"));
        // The unknown activity type passes the analysis; the apply refuses it by name and the retry leaves it out.
        Assert.Contains(result.PlanItems, item => item.SourceVersionId == "broken-v1");
        Assert.DoesNotContain(result.WorkflowPublications, publication => publication.Workflow.SourceVersionId is "broken-v1" or "broken-caller-v1");
    }

    [Fact]
    public async Task A_refusal_that_names_no_single_workflow_is_returned_not_thrown()
    {
        await using var host = await StartAsync();
        Assert.Null((await RunAsync(host, HttpHello("Hello from Elsa 3"))).Refusal);

        // The same Elsa 3 version id with other content: the apply refuses the whole selection.
        var result = await RunAsync(host, HttpHello("Changed without a new version"));

        var refusal = Assert.IsType<Elsa3ImportRunRefusal>(result.Refusal);
        Assert.Equal(Elsa3ImportRunStage.Apply, refusal.Stage);
        Assert.NotNull(refusal.Exception);
        Assert.Equal(["http-hello-v1"], refusal.UnappliedSourceVersionIds);
        Assert.Empty(result.WorkflowPublications);
    }

    private async Task<Elsa3ImportHost> StartAsync() =>
        await Elsa3ImportHost.StartAsync(withHttp: true, reconcileFolder: Directory.CreateDirectory(Path.Join(_folder.FullName, "empty")).FullName);

    /// <summary>Writes the definitions to one file, reads it as the startup task does, and runs the import once.</summary>
    private async Task<Elsa3ImportRunResult> RunAsync(Elsa3ImportHost host, params JsonNode[] definitions)
    {
        var file = Path.Join(_folder.FullName, $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, new JsonArray(definitions.Select(definition => definition.DeepClone()).ToArray()).ToJsonString());
        await using var scope = host.Shell.ServiceProvider.CreateAsyncScope();
        var inputs = await scope.ServiceProvider.GetRequiredService<Elsa3JsonFileReader>()
            .ReadAsync(new() { SourceId = "elsa3-run", FilePath = file }, CancellationToken.None);
        var results = await scope.ServiceProvider.GetRequiredService<Elsa3ImportRun>()
            .RunAsync(new("elsa3-run", inputs), CancellationToken.None);
        return Assert.Single(results);
    }

    private static JsonNode HttpHello(string body)
    {
        var definition = Fixture("http-endpoint");
        definition["root"]!["activities"]!.AsArray().Single(activity => (string?)activity!["id"] == "respond")!["content"]!["expression"]!["value"] = body;
        return definition;
    }

    private static JsonNode Fixture(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Fixtures", $"{name}.json")))!;

    public void Dispose()
    {
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
