using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// The spec 250 M1 journey on a real Workbench process: a compensation scope is published and run through REST, a
/// later step is rejected, and the process is killed after a compensation handler's external effect and before its
/// completion commits. The restarted process re-runs that handler with the same invocation id, so the participant
/// applies each effect once, and the scope finishes Compensated with its handlers newest first.
/// </summary>
/// <remarks>
/// The participant keeps its state in files under the content root, which outlive the killed process. The recovery
/// sweep waits for the dead process's ownership lease, so the shell shortens that lease for this test. Runs in its own
/// non-parallel collection because sibling Workbench hosts starve the recovery poller.
/// </remarks>
[Collection(Name)]
public sealed class WorkbenchCompensationRecoveryTests
{
    public const string Name = nameof(WorkbenchCompensationRecoveryTests);

    private const string PackageId = "Elsa.Workbench.Tests.Reservations";
    private const string ParticipantType = PackageId + ".ReservationParticipant";
    private const string ScopeType = "Elsa.Activities.Compensation.Activities.CompensationScope";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task A_killed_handler_runs_again_with_the_same_invocation_and_recovery_finishes_once()
    {
        await using var original = await WorkbenchProcess.StartAsync(WitnessShell(), PrepareContentRoot);
        await LoginAsync(original);
        var journal = Path.Combine(original.ContentRoot, "reservations");
        var published = await PublishAsync(original, journal);

        // Execute runs inline until the workflow yields, and the held handler never yields, so the request stays open
        // until the process is killed. The handler for "Reserve delivery" applies its effect, then holds.
        var execute = original.Client.PostAsJsonAsync($"/runtime/workflows/executables/{published.ArtifactId}/execute",
            new { sourceReferenceId = published.SourceReferenceId });
        await WaitForAsync(original, () => EffectLog(journal).Contains("delivery-cancel"),
            "the delivery cancellation effect to land before its completion commits", () => Describe(journal));
        var executionId = await ExecutionIdAsync(original, published.ArtifactId);
        var killedCall = Assert.Single(Calls(journal, "delivery-cancel"));
        Assert.DoesNotContain("inventory-release", EffectLog(journal));

        await using var restarted = await original.CrashAndRestartAsync(directory =>
            File.WriteAllText(Path.Combine(directory, "reservations", "delivery-cancel.release"), "release"));
        await Assert.ThrowsAnyAsync<Exception>(() => execute);
        await LoginAsync(restarted);
        var completed = await WaitForStateAsync(restarted, executionId, instance => instance["instance"]?["status"]?.GetValue<string>() == "Completed");

        // Each effect landed once, handlers ran newest first, and the killed handler ran again with its invocation id.
        Assert.Equal(["inventory", "delivery", "delivery-cancel", "inventory-release"], EffectLog(journal));
        var calls = Calls(journal, "delivery-cancel");
        Assert.Equal(2, calls.Length);
        Assert.NotEqual(killedCall.ProcessId, calls[1].ProcessId);
        Assert.Equal(killedCall.InvocationId, calls[1].InvocationId);
        Assert.Single(Calls(journal, "inventory-release"));

        var scope = Activity(completed, "scope");
        Assert.Contains("Compensated", scope["outcomeNames"]!.AsArray().Select(outcome => outcome!.GetValue<string>()));
        var incident = Assert.Single(completed["incidents"]!.AsArray());
        Assert.Equal("confirm", incident!["executableNodeId"]!.GetValue<string>());
        Assert.Equal("Resolved", incident["status"]!.GetValue<string>());
        Assert.Equal("compensation", Activity(completed, "delivery-cancel")["provenance"]!["schedulingCause"]!.GetValue<string>());
        Assert.Single(completed["activities"]!.AsArray(), activity => activity?["executableNodeId"]?.GetValue<string>() == "delivery-cancel" &&
                                                                       activity["status"]?.GetValue<string>() == "Completed");
    }

    private static WorkbenchShell WitnessShell() => WorkbenchShell.Development with
    {
        Settings = new Dictionary<string, string>
        {
            ["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false",
            ["Nuplane:Setup:PollInterval"] = "1.00:00:00"
        }
    };

    private static async Task<PublishedWorkflow> PublishAsync(WorkbenchProcess host, string journal)
    {
        var participant = await ActivityVersionAsync(host, ParticipantType);
        var scope = await ActivityVersionAsync(host, ScopeType);
        JsonObject Participant(string nodeId, string key, bool reject = false, bool hold = false) => Node(nodeId, participant,
            [Input("DirectoryPath", journal), Input("Key", key), Input("Reject", reject), Input("HoldAfterEffect", hold)]);
        JsonObject Step(string name, JsonObject activity, JsonObject? compensation) => new()
        {
            ["name"] = name, ["activity"] = activity, ["compensation"] = compensation
        };

        var root = Node("scope", scope, [Input("faultAfterCompensation", false)], new JsonObject
        {
            ["kind"] = "elsa.compensation-scope.structure", ["schemaVersion"] = "1.0.0",
            ["payload"] = new JsonObject
            {
                ["steps"] = new JsonArray(
                    Step("Reserve inventory", Participant("inventory", "inventory"), Participant("inventory-release", "inventory-release")),
                    Step("Reserve delivery", Participant("delivery", "delivery"), Participant("delivery-cancel", "delivery-cancel", hold: true)),
                    Step("Confirm order", Participant("confirm", "confirm", reject: true), null))
            }
        });
        var definition = await PostAsync(host, "/design/workflows/definitions/submit", new
        {
            name = "Compensation recovery witness", description = "Reservations undone after a rejected order",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = root }
        });
        var published = await PostAsync(host, $"/publishing/workflows/{definition["version"]!["id"]!.GetValue<string>()}/publish", new { });
        return new(published["artifactId"]!.GetValue<string>(), published["sourceReferenceId"]!.GetValue<string>());
    }

    private static async Task<string> ExecutionIdAsync(WorkbenchProcess host, string artifactId)
    {
        var instances = await host.Client.GetFromJsonAsync<JsonArray>("/runtime/workflows/instances");
        var instance = Assert.Single(instances!, item => item?["artifactId"]?.GetValue<string>() == artifactId);
        return instance!["workflowExecutionId"]!.GetValue<string>();
    }

    private static JsonObject Node(string id, string version, JsonNode[]? inputs = null, JsonNode? structure = null) => new()
    {
        ["nodeId"] = id, ["activityVersionId"] = version, ["inputs"] = new JsonArray(inputs ?? []),
        ["outputs"] = new JsonArray(), ["structure"] = structure
    };

    private static JsonObject Input(string key, object value) => new()
    {
        ["referenceKey"] = key, ["value"] = new JsonObject { ["expressionType"] = "Literal", ["value"] = JsonSerializer.SerializeToNode(value) }
    };

    private static JsonNode Activity(JsonNode instance, string nodeId) => instance["activities"]!.AsArray()
        .Last(activity => activity?["executableNodeId"]?.GetValue<string>() == nodeId)!;

    private static string[] EffectLog(string journal)
    {
        var path = Path.Combine(journal, "effects.log");
        return File.Exists(path) ? File.ReadAllLines(path).Where(line => line.Length > 0).ToArray() : [];
    }

    private static ParticipantCall[] Calls(string journal, string key)
    {
        var path = Path.Combine(journal, key + ".calls");
        return File.Exists(path)
            ? File.ReadAllLines(path).Select(line => line.Split('|')).Where(parts => parts.Length == 3)
                .Select(parts => new ParticipantCall(parts[0], parts[1])).ToArray()
            : [];
    }

    private static string Describe(string journal) => Directory.Exists(journal)
        ? string.Join(Environment.NewLine, Directory.EnumerateFiles(journal).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => $"{Path.GetFileName(path)}: {File.ReadAllText(path).Trim()}"))
        : $"Participant journal is missing: {journal}";

    private static async Task LoginAsync(WorkbenchProcess host) =>
        _ = await PostAsync(host, "/_elsa/identity/login", new { username = "admin", password = "Password123!" });

    private static async Task<string> ActivityVersionAsync(WorkbenchProcess host, string type)
    {
        var catalog = await host.Client.GetFromJsonAsync<JsonNode>($"/design/activities/definitions?search={Uri.EscapeDataString(type)}");
        Assert.True(catalog?["items"]?.AsArray().Count > 0, $"Activity {type} was not reconciled: {catalog}\n{host.Output}");
        var match = Assert.Single(catalog!["items"]!.AsArray(), item => item?["definition"]?["activityTypeKey"]?.GetValue<string>() == type);
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

    private static async Task WaitForAsync(WorkbenchProcess host, Func<bool> predicate, string expected, Func<string>? evidence = null)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                Assert.Fail($"Timed out waiting for {expected}.\n{evidence?.Invoke()}\n{host.Output}");
            await Task.Delay(100);
        }
    }

    private static void PrepareContentRoot(string directory)
    {
        var path = Path.Combine(directory, "shells.json");
        var shells = JsonNode.Parse(File.ReadAllText(path))!;
        var features = shells["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        features["ReservationWitness"] = new JsonObject();
        // A restarted process reclaims the killed process's execution once its ownership lease expires.
        features["WorkflowsRuntimeResumption"] = new JsonObject
        {
            ["SweepIntervalSeconds"] = 1,
            ["LeaseTimeoutMinutes"] = 0.1,
            ["HeartbeatTimeoutMinutes"] = 0.1
        };
        File.WriteAllText(path, shells.ToJsonString());

        var configuration = typeof(WorkbenchBuild).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var project = Path.Combine(WorkbenchBuild.RepositoryRoot, "Essentials/tests/Workbench/Fixtures/Reservations");
        var framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        // Nuplane's acquisition cache can outlive the host's state directory, so each run packs a unique version.
        var packageVersion = "1.0.0-witness." + Guid.NewGuid().ToString("N");
        using var archive = ZipFile.Open(Path.Combine(directory, "packages", PackageId + "." + packageVersion + ".nupkg"), ZipArchiveMode.Create);
        AddText(archive, "[Content_Types].xml", """<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="nuspec" ContentType="application/octet"/><Default Extension="dll" ContentType="application/octet"/><Default Extension="json" ContentType="application/octet"/></Types>""");
        AddText(archive, "_rels/.rels", $"""<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{PackageId}.nuspec" Id="R1"/></Relationships>""");
        AddText(archive, PackageId + ".nuspec", $"""<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{PackageId}</id><version>{packageVersion}</version><authors>tests</authors><description>Owned process compensation witness.</description></metadata></package>""");
        archive.CreateEntryFromFile(Path.Combine(project, "bin", configuration, framework, PackageId + ".dll"), "lib/" + framework + "/" + PackageId + ".dll");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(project, "elsa-package.json")))!;
        manifest["package"]!["version"] = packageVersion;
        AddText(archive, "elsa-package.json", manifest.ToJsonString());
    }

    private static void AddText(ZipArchive archive, string path, string value)
    {
        using var stream = archive.CreateEntry(path).Open();
        stream.Write(Encoding.UTF8.GetBytes(value));
    }

    private sealed record ParticipantCall(string ProcessId, string InvocationId);

    private sealed record PublishedWorkflow(string ArtifactId, string SourceReferenceId);
}

[CollectionDefinition(WorkbenchCompensationRecoveryTests.Name, DisableParallelization = true)]
public sealed class WorkbenchCompensationRecoveryTestCollection;
