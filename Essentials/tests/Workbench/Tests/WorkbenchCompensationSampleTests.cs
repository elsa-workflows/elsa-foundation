using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// The compensation sample (<c>samples/Elsa.Samples.Compensation</c>, spec 261 OD19 A) on a real Workbench process: the
/// package is dropped into the Workbench's feed, its feature is enabled, and the three workflow files are imported as
/// the README says, published and run through REST. S1 and S2 run to the end; S3 waits for an operator after two
/// scheduled retries and is resolved as effect absent. Each run's outcome and the participant's ledger files are what
/// the README tells a reader to expect, so the sample cannot drift from what compensation does.
/// </summary>
/// <remarks>
/// One process runs all three, and S3's retry waits overlap S1 and S2. Shares the compensation witness collection, so
/// no sibling Workbench host starves its sweeps.
/// </remarks>
[Collection(WorkbenchCompensationRecoveryTests.Name)]
public sealed class WorkbenchCompensationSampleTests
{
    private const string PackageId = "Elsa.Samples.Compensation";
    private const string ReserveType = PackageId + ".Activities.Reserve";
    private const string ReleaseType = PackageId + ".Activities.Release";
    private const string ScopeType = "Elsa.Activities.Compensation.Activities.CompensationScope";
    private const string ConfirmType = "Elsa.Activities.Compensation.Activities.Confirm";
    private static readonly string SampleDirectory = Path.Combine(WorkbenchBuild.RepositoryRoot, "samples", PackageId);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task The_sample_workflows_end_as_the_readme_says()
    {
        await using var host = await WorkbenchProcess.StartAsync(SampleShell(), PrepareContentRoot);
        await PostAsync(host, "/_elsa/identity/login", new { username = "admin", password = "Password123!" });
        var ledger = Path.Combine(host.ContentRoot, "compensation-sample");
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in new[] { ScopeType, ConfirmType, ReserveType, ReleaseType })
            versions[type] = await ActivityVersionAsync(host, type);

        // S3 starts first: its two retry waits (5 s, then 10 s) pass while S1 and S2 run.
        var recovery = await ExecuteAsync(host, await ImportAndPublishAsync(host, "operator-recovery.json", versions));
        var saga = await ExecuteAsync(host, await ImportAndPublishAsync(host, "reservation-saga.json", versions));
        var nested = await ExecuteAsync(host, await ImportAndPublishAsync(host, "nested-confirm.json", versions));

        // S1: the order is rejected; the delivery, then the inventory, are released with the reservations the steps made.
        var sagaRun = await WaitForStateAsync(host, saga, IsCompleted);
        Assert.Contains("Compensated", Outcomes(sagaRun, "order"));
        var inventory = Reservation(ledger, "inventory", saga);
        var delivery = Reservation(ledger, "delivery", saga);
        Assert.Equal(("Released", "Released"), (Status(inventory), Status(delivery)));
        var inventoryRelease = Assert.Single(Releases(ledger, "inventory", saga));
        var deliveryRelease = Assert.Single(Releases(ledger, "delivery", saga));
        Assert.Equal(Id(inventory), inventoryRelease["reservationId"]!.GetValue<string>());
        Assert.Equal(Id(delivery), deliveryRelease["reservationId"]!.GetValue<string>());
        Assert.True(At(deliveryRelease) < At(inventoryRelease), "The delivery is released before the inventory.");
        Assert.Empty(Reservations(ledger, "order", saga));

        // S2: the charge is confirmed and kept; the inner scope's reservations are handed over and released, B then A.
        var nestedRun = await WaitForStateAsync(host, nested, IsCompleted);
        Assert.Contains("Compensated", Outcomes(nestedRun, "order"));
        Assert.Contains("Done", Outcomes(nestedRun, "stock"));
        Assert.Equal("Reserved", Status(Reservation(ledger, "payment", nested)));
        Assert.Empty(Releases(ledger, "payment", nested));
        var stockA = Reservation(ledger, "stock-a", nested);
        var stockB = Reservation(ledger, "stock-b", nested);
        Assert.Equal(("Released", "Released"), (Status(stockA), Status(stockB)));
        var releaseA = Assert.Single(Releases(ledger, "stock-a", nested));
        var releaseB = Assert.Single(Releases(ledger, "stock-b", nested));
        Assert.Equal((Id(stockA), Id(stockB)), (releaseA["reservationId"]!.GetValue<string>(), releaseB["reservationId"]!.GetValue<string>()));
        Assert.True(At(releaseB) < At(releaseA), "B is released before A.");
        Assert.Empty(Reservations(ledger, "shipping", nested));

        // S3: releasing the delivery fails on all three attempts, the last two after a scheduled wait, so the scope
        // waits for an operator with both reservations still held.
        var waiting = await WaitForStateAsync(host, recovery, instance => WaitingIncident(instance) is not null);
        Assert.Equal("Running", waiting["instance"]!["status"]!.GetValue<string>());
        var journal = await JournalAsync(host, waiting);
        Assert.Equal("AwaitingIntervention", journal["phase"]!.GetValue<string>());
        var entry = Assert.Single(journal["entries"]!.AsArray(), entry => entry?["step"]?.GetValue<string>() == "Reserve delivery")!;
        Assert.Equal(["Faulted", "Faulted", "Faulted"], entry["attempts"]!.AsArray().Select(attempt => attempt!["result"]!.GetValue<string>()));
        // The participant saw each attempt no sooner than its wait allowed: 5 seconds, then 10 (the multiplier is 2).
        var failures = Assert.Single(LedgerFile(ledger, "delivery")["failedReleaseAttempts"]!.AsObject()).Value!.AsArray()
            .Select(at => at!.GetValue<DateTimeOffset>()).ToArray();
        Assert.Equal(3, failures.Length);
        Assert.True(failures[1] - failures[0] >= TimeSpan.FromSeconds(5) && failures[2] - failures[1] >= TimeSpan.FromSeconds(10),
            $"The release attempts came at {string.Join(", ", failures.Select(at => at.ToString("O")))}.");
        Assert.Equal(("Reserved", "Reserved"), (Status(Reservation(ledger, "inventory", recovery)), Status(Reservation(ledger, "delivery", recovery))));

        // The operator says the delivery was cancelled by hand: recovery carries on and releases the inventory.
        await ResolveEffectAbsentAsync(host, recovery, WaitingIncident(waiting)!["incidentId"]!.GetValue<string>());
        var resolved = await WaitForStateAsync(host, recovery, IsCompleted);
        Assert.Contains("Compensated", Outcomes(resolved, "order"));
        Assert.Equal("Released", Status(Reservation(ledger, "inventory", recovery)));
        Assert.Equal("Reserved", Status(Reservation(ledger, "delivery", recovery)));
        Assert.Empty(Releases(ledger, "delivery", recovery));
    }

    // The README's import step: each node names its activity by type key, which becomes that catalog's version id.
    private static async Task<JsonNode> ImportAndPublishAsync(WorkbenchProcess host, string file, IReadOnlyDictionary<string, string> versions)
    {
        var body = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(SampleDirectory, "workflows", file)))!;
        foreach (var node in Descendants(body).OfType<JsonObject>().Where(node => node.ContainsKey("activityVersionId")))
            node["activityVersionId"] = versions[node["activityVersionId"]!.GetValue<string>()];

        var submitted = await PostAsync(host, "/design/workflows/definitions/submit", body);
        return await PostAsync(host, $"/publishing/workflows/{submitted["version"]!["id"]!.GetValue<string>()}/publish", new { });
    }

    private static async Task<string> ExecuteAsync(WorkbenchProcess host, JsonNode published)
    {
        var executed = await PostAsync(host, $"/runtime/workflows/executables/{published["artifactId"]!.GetValue<string>()}/execute",
            new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>() });
        return executed["workflowExecutionId"]!.GetValue<string>();
    }

    private static async Task ResolveEffectAbsentAsync(WorkbenchProcess host, string executionId, string incidentId)
    {
        var body = new
        {
            target = new { executionIds = new[] { executionId } },
            alterations = new[]
            {
                new { kind = "DecideIntervention", schemaVersion = 1, payload = new { incidentId, decision = "compensation.resolve.effect-absent", reason = "Delivery cancelled by phone" } }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/runtime/workflows/alteration-plans") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", "sample-s3-" + executionId);
        using var response = await host.Client.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, $"Decision submission: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}\n{host.Output}");
    }

    private static async Task<JsonNode> JournalAsync(WorkbenchProcess host, JsonNode instance)
    {
        var scope = instance["activities"]!.AsArray().Last(activity => activity?["executableNodeId"]?.GetValue<string>() == "order")!;
        var view = await host.Client.GetFromJsonAsync<JsonNode>(
            $"/runtime/workflows/instances/{instance["instance"]!["workflowExecutionId"]!.GetValue<string>()}/activity-executions/{scope["activityExecutionId"]!.GetValue<string>()}/state-view");
        Assert.True(view!["available"]!.GetValue<bool>(), view.ToJsonString());
        return view["view"]!;
    }

    private static JsonNode? WaitingIncident(JsonNode instance) => instance["incidents"]?.AsArray().LastOrDefault(item =>
        item?["executableNodeId"]?.GetValue<string>() == "release-delivery" && item["isBlocking"]?.GetValue<bool>() == true &&
        item["resolutionOutcome"]?["actionKind"]?.GetValue<string>() == "WaitForIntervention");

    private static bool IsCompleted(JsonNode instance) => instance["instance"]?["status"]?.GetValue<string>() == "Completed";

    private static IEnumerable<string> Outcomes(JsonNode instance, string nodeId) => instance["activities"]!.AsArray()
        .Last(activity => activity?["executableNodeId"]?.GetValue<string>() == nodeId)!["outcomeNames"]!.AsArray()
        .Select(outcome => outcome!.GetValue<string>());

    // The participant's ledger: one JSON file per resource, as the README shows it.
    private static JsonNode LedgerFile(string ledger, string resource)
    {
        var path = Path.Combine(ledger, resource + ".json");
        return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))! : new JsonObject { ["reservations"] = new JsonArray(), ["releases"] = new JsonArray() };
    }

    private static JsonNode[] Reservations(string ledger, string resource, string executionId) =>
        [.. LedgerFile(ledger, resource)["reservations"]!.AsArray().Where(item => item!["workflowExecutionId"]!.GetValue<string>() == executionId).Select(item => item!)];

    private static JsonNode Reservation(string ledger, string resource, string executionId) => Assert.Single(Reservations(ledger, resource, executionId));

    private static JsonNode[] Releases(string ledger, string resource, string executionId) =>
        [.. LedgerFile(ledger, resource)["releases"]!.AsArray().Where(item => item!["workflowExecutionId"]!.GetValue<string>() == executionId).Select(item => item!)];

    private static string Status(JsonNode reservation) => reservation["status"]!.GetValue<string>();

    private static string Id(JsonNode reservation) => reservation["reservationId"]!.GetValue<string>();

    private static DateTimeOffset At(JsonNode release) => release["at"]!.GetValue<DateTimeOffset>();

    private static IEnumerable<JsonNode> Descendants(JsonNode node) => node switch
    {
        JsonObject value => [value, .. value.Select(property => property.Value).OfType<JsonNode>().SelectMany(Descendants)],
        JsonArray items => items.OfType<JsonNode>().SelectMany(Descendants),
        _ => []
    };

    private static async Task<string> ActivityVersionAsync(WorkbenchProcess host, string type)
    {
        var catalog = await host.Client.GetFromJsonAsync<JsonNode>($"/design/activities/definitions?search={Uri.EscapeDataString(type)}");
        var match = catalog?["items"]?.AsArray().SingleOrDefault(item => item?["definition"]?["activityTypeKey"]?.GetValue<string>() == type);
        Assert.True(match is not null, $"Activity {type} was not reconciled: {catalog}\n{host.Output}");
        return match!["definition"]!["headVersionId"]!.GetValue<string>();
    }

    private static async Task<JsonNode> PostAsync(WorkbenchProcess host, string path, object body)
    {
        using var response = await host.Client.PostAsJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path}: {(int)response.StatusCode} {text}\n{host.Output}");
        return JsonNode.Parse(text)!;
    }

    private static async Task<JsonNode> WaitForStateAsync(WorkbenchProcess host, string executionId, Func<JsonNode, bool> predicate)
    {
        JsonNode? last = null;
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await host.Client.GetFromJsonAsync<JsonNode>($"/runtime/workflows/instances/{executionId}");
            if (last is not null && predicate(last)) return last;
            if (last?["instance"]?["status"]?.GetValue<string>() is "Faulted" or "Cancelled") break;
            await Task.Delay(200);
        }
        throw new InvalidOperationException($"Workflow did not reach the expected state: {last}\n{host.Output}");
    }

    private static WorkbenchShell SampleShell() => WorkbenchShell.Development with
    {
        Settings = new Dictionary<string, string>
        {
            ["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false",
            ["Nuplane:Setup:PollInterval"] = "1.00:00:00"
        }
    };

    // The README's "build and drop" and "enable the feature" steps. The package is the sample's build output and the
    // manifest its build generated, as dotnet pack would assemble them; each run packs a unique version because Nuplane's
    // acquisition cache can outlive the host's state directory.
    private static void PrepareContentRoot(string directory)
    {
        var path = Path.Combine(directory, "shells.json");
        var shells = JsonNode.Parse(File.ReadAllText(path))!;
        var features = shells["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        features["SampleCompensation"] = new JsonObject { ["Directory"] = Path.Combine(directory, "compensation-sample") };
        // Scheduled retries run at the first outbox sweep after they are due; sweep every second, not every ten.
        features["WorkflowsRuntimeResumption"] = new JsonObject { ["SweepIntervalSeconds"] = 1 };
        File.WriteAllText(path, shells.ToJsonString());

        var configuration = typeof(WorkbenchBuild).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var assembly = Path.Combine(SampleDirectory, "bin", configuration, framework, PackageId + ".dll");
        var generatedManifest = Path.Combine(SampleDirectory, "obj", configuration, framework, "elsa-package.json");
        if (!File.Exists(assembly) || !File.Exists(generatedManifest))
            throw new FileNotFoundException($"Build samples/{PackageId} ({configuration}) before running this test.", File.Exists(assembly) ? generatedManifest : assembly);

        var packageVersion = "1.0.0-sample." + Guid.NewGuid().ToString("N");
        using var archive = ZipFile.Open(Path.Combine(directory, "packages", PackageId + "." + packageVersion + ".nupkg"), ZipArchiveMode.Create);
        AddText(archive, "[Content_Types].xml", """<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="nuspec" ContentType="application/octet"/><Default Extension="dll" ContentType="application/octet"/><Default Extension="json" ContentType="application/octet"/></Types>""");
        AddText(archive, "_rels/.rels", $"""<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{PackageId}.nuspec" Id="R1"/></Relationships>""");
        AddText(archive, PackageId + ".nuspec", $"""<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{PackageId}</id><version>{packageVersion}</version><authors>tests</authors><description>Compensation sample.</description></metadata></package>""");
        archive.CreateEntryFromFile(assembly, "lib/" + framework + "/" + PackageId + ".dll");
        var manifest = JsonNode.Parse(File.ReadAllText(generatedManifest))!;
        manifest["package"]!["version"] = packageVersion;
        AddText(archive, "elsa-package.json", manifest.ToJsonString());
    }

    private static void AddText(ZipArchive archive, string path, string value)
    {
        using var stream = archive.CreateEntry(path).Open();
        stream.Write(Encoding.UTF8.GetBytes(value));
    }
}
