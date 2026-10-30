using System.Text.Json.Nodes;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Activities.Design.Import.Reconciliation.Exceptions;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>
/// Spec 235 User Story 3: unconvertible input is skipped and reported with everything that uses it, the rest imports;
/// strict mode and refusals the import does not attribute to an item fail the start.
/// </summary>
public sealed class Elsa3JsonReconciliationRefusalTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("elsa3-folder-");

    [Fact]
    public async Task Unconvertible_items_and_their_callers_are_skipped_and_the_rest_imports()
    {
        WriteMixedFolder();
        await using var host = await Elsa3ImportHost.StartAsync(withHttp: true, reconcileFolder: _folder.FullName);

        foreach (var imported in new[] { "caller", "approval", "http-hello" })
            Assert.True(await host.ActivePublicationAsync(imported) is not null, $"'{imported}' was not published. Log: {string.Join(" | ", host.Warnings.Entries)}");
        foreach (var skipped in new[] { "broken", "broken-caller", "sets-variable", "no-root" })
        {
            Assert.True(await host.ActivePublicationAsync(skipped) is null, $"'{skipped}' was published. Log: {string.Join(" | ", host.Warnings.Entries)}");
            Assert.Contains(host.Warnings.Entries, entry => entry.Contains($"skipped {skipped} version 1", StringComparison.Ordinal));
        }

        var callerSkip = Assert.Single(host.Warnings.Entries, entry => entry.Contains("skipped broken-caller version 1", StringComparison.Ordinal));
        Assert.Contains("ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY", callerSkip, StringComparison.Ordinal);
        Assert.Contains("broken-caller.json", callerSkip, StringComparison.Ordinal);
        Assert.Contains("reusable workflow 'broken'", callerSkip, StringComparison.Ordinal);

        // A reusable that converts with a behaviour difference imports, and its warning is logged.
        Assert.Contains(host.Warnings.Entries, entry => entry.StartsWith("Information", StringComparison.Ordinal) && entry.Contains("ELS3-BEHAVIOUR-", StringComparison.Ordinal));
        // Reusables: approval and the behaviour-warning one. Workflows: caller, http-hello and both wrappers.
        Assert.Contains(host.Warnings.Entries, entry => entry.Contains("2 reusable activities and 4 workflows in place; 4 workflows published by this start; 4 skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Strict_mode_fails_the_start_and_commits_nothing()
    {
        WriteMixedFolder();
        var host = await Elsa3ImportHost.StartAsync(withHttp: true, reconcileFolder: EmptyFolder());
        var database = host.DesignDatabase;
        var rows = await host.CountDesignRowsAsync();

        try
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => host.RestartAsync(_folder.FullName, strict: true));

            Assert.Contains(Chain(failure), exception => exception is Elsa3ReconciliationFailedException && exception.Message.Contains("FailOnUnconvertible", StringComparison.Ordinal));
            Assert.Equal(rows, await CountRowsAsync(host, database));
        }
        finally
        {
            host.DeleteStores();
        }
    }

    [Fact]
    public async Task A_changed_workflow_under_an_imported_elsa3_version_fails_the_start_and_keeps_the_stored_version()
    {
        File.WriteAllText(Path.Join(_folder.FullName, "http.json"), HttpHello("Hello from Elsa 3").ToJsonString());
        var host = await Elsa3ImportHost.StartAsync(withHttp: true, reconcileFolder: _folder.FullName);
        var rows = await host.CountDesignRowsAsync();

        File.WriteAllText(Path.Join(_folder.FullName, "http.json"), HttpHello("Changed without a new version").ToJsonString());
        try
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => host.RestartAsync(_folder.FullName));

            Assert.Contains(Chain(failure), exception => exception is Elsa3ReconciliationFailedException && exception.Message.Contains("names no single workflow", StringComparison.Ordinal));
            Assert.Equal(rows, await CountRowsAsync(host, host.DesignDatabase));
        }
        finally
        {
            host.DeleteStores();
        }
    }

    /// <summary>
    /// The approval reusable, its caller and the HTTP workflow, which import; and four that do not: a reusable using an
    /// Elsa 3 activity with no Elsa 4 counterpart, its caller, a reusable that sets a variable (#3023), and a document
    /// with no root activity.
    /// </summary>
    private void WriteMixedFolder()
    {
        Copy("caller-routes-outcome", "caller.json");
        Copy("reusable-two-outcomes", "approval.json");
        Copy("http-endpoint", "http.json");
        Copy("reusable-sets-variable", "sets-variable.json");
        Copy("reusable-with-behaviour-warning", "behaviour-warning.json");

        var broken = Fixture("reusable-two-outcomes");
        broken["id"] = "broken-v1";
        broken["definitionId"] = "broken";
        broken["name"] = "broken";
        broken["root"]!["activities"]!.AsArray()[0]!["type"] = "Elsa.StateMachine";
        Write(broken, "broken.json");

        var brokenCaller = Fixture("caller-routes-outcome");
        brokenCaller["id"] = "broken-caller-v1";
        brokenCaller["definitionId"] = "broken-caller";
        brokenCaller["name"] = "broken-caller";
        // An Elsa 3 call node names its reusable by type and by exact version id.
        var call = brokenCaller["root"]!["activities"]!.AsArray().Single(activity => (string?)activity!["id"] == "call")!;
        call["type"] = "broken";
        call["workflowDefinitionVersionId"] = "broken-v1";
        Write(brokenCaller, "broken-caller.json");

        Write(new JsonObject
        {
            ["id"] = "no-root-v1",
            ["definitionId"] = "no-root",
            ["name"] = "no-root",
            ["version"] = 1,
            ["options"] = new JsonObject { ["usableAsActivity"] = false }
        }, "no-root.json");
    }

    private static JsonNode HttpHello(string body)
    {
        var definition = Fixture("http-endpoint");
        definition["root"]!["activities"]!.AsArray().Single(activity => (string?)activity!["id"] == "respond")!["content"]!["expression"]!["value"] = body;
        return definition;
    }

    private static JsonNode Fixture(string name) =>
        JsonNode.Parse(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "Fixtures", $"{name}.json")))!;

    private void Write(JsonNode definition, string fileName) => File.WriteAllText(Path.Join(_folder.FullName, fileName), definition.ToJsonString());

    private void Copy(string fixture, string fileName) =>
        File.Copy(Path.Join(AppContext.BaseDirectory, "Fixtures", $"{fixture}.json"), Path.Join(_folder.FullName, fileName));

    private string EmptyFolder() => Directory.CreateDirectory(Path.Join(_folder.FullName, "empty")).FullName;

    /// <summary>The design store's row counts, read from the file after the host that failed to start released it.</summary>
    private static Task<IReadOnlyDictionary<string, long>> CountRowsAsync(Elsa3ImportHost stopped, string database)
    {
        Assert.Equal(database, stopped.DesignDatabase);
        return stopped.CountDesignRowsAsync();
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
            if (current is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Chain))
                    yield return inner;
        }
    }

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
