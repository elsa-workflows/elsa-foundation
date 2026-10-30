using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// Spec 235 User Story 2: restarting with the same input changes nothing, a newer Elsa 3 version is published, what an
/// earlier start left unpublished is published, and an operator's choice of version is kept.
/// </summary>
public sealed class Elsa3JsonReconciliationRestartTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("elsa3-folder-");

    [Fact]
    public async Task A_second_start_with_the_same_folder_writes_and_publishes_nothing()
    {
        Copy("caller-routes-outcome", "caller.json");
        Copy("reusable-two-outcomes", "approval.json");
        WriteHttpHello(1, "http.json");
        var host = await Elsa3ImportHost.StartAsync(withHttp: true, reconcileFolder: _folder.FullName);
        try
        {
            var rows = await host.CountDesignRowsAsync();
            var records = await PublicationRecordsAsync(host, "caller", "approval", "http-hello");

            host = await host.RestartAsync(_folder.FullName);

            Assert.Equal(rows, await host.CountDesignRowsAsync());
            Assert.Equal(records, await PublicationRecordsAsync(host, "caller", "approval", "http-hello"));
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_file_replaced_by_a_newer_elsa3_version_publishes_that_version()
    {
        WriteHttpHello(1, "http.json");
        var host = await Elsa3ImportHost.StartAsync(withHttp: true, reconcileFolder: _folder.FullName);
        try
        {
            WriteHttpHello(2, "http.json");
            host = await host.RestartAsync(_folder.FullName);

            Assert.Equal("Hello from Elsa 3 v2", await CallHttpHelloAsync(host));
            var records = await host.PublicationRecordsAsync("http-hello");
            Assert.Equal(2, records.Count);
            Assert.Single(records, record => record.Status == PublicationStatus.Active);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task What_an_earlier_start_committed_but_did_not_publish_is_published_on_the_next_start()
    {
        Copy("caller-routes-outcome", "caller.json");
        Copy("reusable-two-outcomes", "approval.json");
        var host = await Elsa3ImportHost.StartAsync(reconcileFolder: EmptyFolder());
        try
        {
            // The commit of an earlier start whose workflow publish step never ran (its reusable was published).
            await ApplyWithoutPublishingAsync(host, Fixture("caller-routes-outcome"), Fixture("reusable-two-outcomes"));
            Assert.Null(await host.ActivePublicationAsync("caller"));

            host = await host.RestartAsync(_folder.FullName);

            var caller = await host.ActivePublicationAsync("caller");
            Assert.True(caller is not null, string.Join(" | ", host.Warnings.Entries));
            var run = await host.RunAsync(caller.Value.Workflow);
            Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_operators_rollback_to_an_older_imported_version_is_kept()
    {
        WriteHttpHello(1, "http-v1.json");
        WriteHttpHello(2, "http-v2.json");
        var host = await Elsa3ImportHost.StartAsync(withHttp: true, reconcileFolder: _folder.FullName);
        try
        {
            var v1 = await host.ReadAsync(services => WorkflowVersionIdAsync(services, "http-hello", "1.0.0"));
            await host.PublishWorkflowAsync(v1!);

            host = await host.RestartAsync(_folder.FullName);

            Assert.Equal(v1, (await host.ActivePublicationAsync("http-hello"))!.Value.Workflow.VersionId);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_reusable_refused_on_one_start_publishes_with_its_caller_on_the_next()
    {
        Copy("caller-routes-outcome", "caller.json");
        Copy("reusable-two-outcomes", "approval.json");
        var host = await Elsa3ImportHost.StartAsync(withPublishSeam: true, reconcileFolder: _folder.FullName, refuseAllPublications: true);
        try
        {
            Assert.Null(await host.ActivePublicationAsync("caller"));
            Assert.Contains(host.Warnings.Entries, entry => entry.Contains("ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY", StringComparison.Ordinal));

            host = await host.RestartAsync(_folder.FullName);

            var caller = await host.ActivePublicationAsync("caller");
            Assert.True(caller is not null, string.Join(" | ", host.Warnings.Entries));
            var run = await host.RunAsync(caller.Value.Workflow);
            Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_publications_of_one_version_at_once_leave_one_publication(bool slotHasAnActivePublication)
    {
        if (slotHasAnActivePublication)
            WriteHttpHello(1, "http.json");
        var host = await Elsa3ImportHost.StartAsync(withHttp: true, reconcileFolder: slotHasAnActivePublication ? _folder.FullName : EmptyFolder());
        try
        {
            var before = (await host.PublicationRecordsAsync("http-hello")).Count;
            var applied = await ApplyWithoutPublishingAsync(host, slotHasAnActivePublication ? HttpHello(2) : HttpHello(1));
            var version = Assert.Single(applied.Workflows!);

            // Two nodes publish the same version at the same moment.
            var outcomes = await Task.WhenAll(PublishInScopeAsync(host, version), PublishInScopeAsync(host, version));

            Assert.DoesNotContain(ImportedWorkflowPublication.Failed, outcomes);
            var records = await host.PublicationRecordsAsync("http-hello");
            Assert.Single(records, record => record.WorkflowDefinitionVersionId == version.WorkflowVersionId && record.Status != PublicationStatus.Failed);
            Assert.Equal(before + 1, records.Count(record => record.Status != PublicationStatus.Failed));
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private static async Task<ImportedWorkflowPublication> PublishInScopeAsync(Elsa3ImportHost host, ReusableActivityImportedWorkflowVersion version)
    {
        await using var scope = await host.Shell.ServiceProvider.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(Elsa3ImportHost.TenantId));
        var outcomes = await scope.ServiceProvider.GetRequiredService<ImportedWorkflowPublisher>()
            .PublishAsync([version], Elsa3ImportHost.TenantId, CancellationToken.None);
        return outcomes[version.SourceVersionId];
    }

    /// <summary>
    /// Commits <paramref name="definitions"/> as the folder import does, without its workflow publish step: the import
    /// still publishes reusables, as the folder import's commit does.
    /// </summary>
    private static async Task<ReusableActivityImportApplyResult> ApplyWithoutPublishingAsync(Elsa3ImportHost host, params JsonNode[] definitions)
    {
        await using var scope = await host.Shell.ServiceProvider.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(Elsa3ImportHost.TenantId));
        scope.ServiceProvider.GetRequiredService<Elsa3StartupPublishingGrant>().Grant(Elsa3ImportHost.TenantId);
        var importer = scope.ServiceProvider.GetRequiredService<IReusableActivityCollectionImporter>();
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var collection = new ReusableActivityImportCollection(
            "restart-test",
            definitions.Select(definition => definition.Deserialize<Elsa3WorkflowDefinition>(web)!).ToArray());
        var plan = await importer.AnalyzeAsync(collection);
        return await importer.ApplyAsync(new(plan.PlanId, collection, plan.Items.Select(item => item.SourceVersionId).ToArray(), TenantId: Elsa3ImportHost.TenantId));
    }

    private static async Task<Dictionary<string, int>> PublicationRecordsAsync(Elsa3ImportHost host, params string[] definitionIds)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var definitionId in definitionIds)
            counts[definitionId] = (await host.PublicationRecordsAsync(definitionId)).Count;
        return counts;
    }

    private static async Task<string> CallHttpHelloAsync(Elsa3ImportHost host)
    {
        using var response = await host.Client.PostAsync("workflows/http/elsa3/hello", new StringContent("{}", Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        return body;
    }

    private static async Task<string?> WorkflowVersionIdAsync(IServiceProvider services, string definitionId, string version)
    {
        var versions = await services.GetRequiredService<Elsa.Workflows.Design.Persistence.Core.Stores.IWorkflowDefinitionVersionStore>()
            .ListByDefinitionAsync(definitionId);
        return versions.SingleOrDefault(candidate => candidate.Version == version)?.Id;
    }

    /// <summary>The <c>http-endpoint</c> fixture as Elsa 3 version <paramref name="version"/>, answering with its version.</summary>
    private static JsonNode HttpHello(int version)
    {
        var definition = JsonNode.Parse(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Fixtures", "http-endpoint.json")))!;
        definition["id"] = $"http-hello-v{version}";
        definition["version"] = version;
        var respond = definition["root"]!["activities"]!.AsArray().Single(activity => (string?)activity!["id"] == "respond")!;
        respond["content"]!["expression"]!["value"] = $"Hello from Elsa 3 v{version}";
        return definition;
    }

    private void WriteHttpHello(int version, string fileName) =>
        File.WriteAllText(Path.Join(_folder.FullName, fileName), HttpHello(version).ToJsonString());

    private static JsonNode Fixture(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Fixtures", $"{name}.json")))!;

    private void Copy(string fixture, string fileName) =>
        File.Copy(Path.Join(AppContext.BaseDirectory, "Fixtures", $"{fixture}.json"), Path.Join(_folder.FullName, fileName));

    private string EmptyFolder() => Directory.CreateDirectory(Path.Join(_folder.FullName, "empty")).FullName;

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
