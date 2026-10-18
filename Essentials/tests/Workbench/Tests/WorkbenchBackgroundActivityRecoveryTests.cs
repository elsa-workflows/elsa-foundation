using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>Publishes through REST and kills the real Workbench while two ordinary background bodies are live.</summary>
public sealed class WorkbenchBackgroundActivityRecoveryTests
{
    private const string PackageId = "Elsa.Workbench.Tests.BackgroundActivities";
    private const string ProbeType = PackageId + ".BackgroundProbe";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    [Theory]
    [InlineData("left", "right")]
    [InlineData("right", "left")]
    public async Task Killed_workers_are_reclaimed_with_stable_invocations_and_one_native_continuation(string first, string second)
    {
        await using var original = await WorkbenchProcess.StartAsync(WitnessShell(), PrepareContentRoot);
        await LoginAsync(original);
        var journal = Path.Combine(original.ContentRoot, "background-witness");
        var executionId = await PublishAndStartAsync(original, journal);
        await WaitForStateAsync(original, executionId, state => ActivityStatus(state, "left") == "Running" && ActivityStatus(state, "right") == "Running");
        Assert.Equal(0, StartedCount(journal, "left"));
        Assert.Equal(0, StartedCount(journal, "right"));

        // Admission is durable even when this process never runs a worker. Enable workers only after it dies.
        await using var admitted = await original.CrashAndRestartAsync(EnableWorkers);
        await WaitForAsync(admitted, () => StartedCount(journal, "left") >= 1 && StartedCount(journal, "right") >= 1,
            "both bodies to enter before either is released");
        Assert.False(File.Exists(Path.Combine(journal, "final.started")));
        var initialLeft = File.ReadAllLines(Path.Combine(journal, "left.started"))[0];
        var initialRight = File.ReadAllLines(Path.Combine(journal, "right.started"))[0];

        await using var restarted = await admitted.CrashAndRestartAsync();
        Assert.Equal(original.ContentRoot, restarted.ContentRoot);
        await LoginAsync(restarted);
        await WaitForAsync(restarted, () => StartedCount(journal, "left") >= 2 && StartedCount(journal, "right") >= 2,
            "a new process to reclaim both expired worker leases");
        AssertRestartedWithSameLogicalIdentity(initialLeft, File.ReadAllLines(Path.Combine(journal, "left.started"))[1]);
        AssertRestartedWithSameLogicalIdentity(initialRight, File.ReadAllLines(Path.Combine(journal, "right.started"))[1]);

        // Persist the first receipt while its notification and repair sweep are held, then lose the process.
        File.WriteAllText(Path.Combine(journal, "hold-receipts"), "hold");
        File.WriteAllText(Path.Combine(journal, first + ".release"), "release");
        await WaitForAsync(restarted, () => File.Exists(Path.Combine(journal, first + ".receipt")),
            "the real EF receipt to commit before notification", () => DescribeJournal(journal));
        Assert.False(File.Exists(Path.Combine(journal, "final.started")));
        var completedBodyCount = StartedCount(journal, first);
        await using var receiptRecovered = await restarted.CrashAndRestartAsync(_ => File.Delete(Path.Combine(journal, "hold-receipts")));
        await LoginAsync(receiptRecovered);
        await WaitForStateAsync(receiptRecovered, executionId, instance => ActivityStatus(instance, first) == "Completed");
        Assert.Equal(completedBodyCount, StartedCount(journal, first));
        Assert.False(File.Exists(Path.Combine(journal, "final.started")));
        File.WriteAllText(Path.Combine(journal, second + ".release"), "release");
        var completed = await WaitForStateAsync(receiptRecovered, executionId, instance => instance["instance"]?["status"]?.GetValue<string>() == "Completed");
        Assert.Empty(completed["incidents"]!.AsArray());
        Assert.Equal(1, StartedCount(journal, "final"));
        Assert.Single(completed["activities"]!.AsArray(), activity => activity?["executableNodeId"]?.GetValue<string>() == "final");
        Assert.Equal("Completed", ActivityStatus(completed, "left"));
        Assert.Equal("Completed", ActivityStatus(completed, "right"));

        // Accepted native history survives another process loss without a second logical final execution.
        await using var terminal = await receiptRecovered.CrashAndRestartAsync();
        await LoginAsync(terminal);
        await WaitForStateAsync(terminal, executionId, instance => instance["instance"]?["status"]?.GetValue<string>() == "Completed");
        Assert.Equal(1, StartedCount(journal, "final"));
        Assert.Equal(completedBodyCount, StartedCount(journal, first));
    }

    [Fact]
    public async Task Explicit_cancellation_stops_held_bodies_and_survives_process_restart()
    {
        await using var host = await WorkbenchProcess.StartAsync(WitnessShell(), directory =>
        {
            PrepareContentRoot(directory);
            EnableWorkers(directory);
        });
        await LoginAsync(host);
        var journal = Path.Combine(host.ContentRoot, "background-witness");
        var executionId = await PublishAndStartAsync(host, journal);
        await WaitForAsync(host, () => StartedCount(journal, "left") == 1 && StartedCount(journal, "right") == 1,
            "both background bodies to enter before cancellation");
        await WaitForAsync(host, () => File.Exists(Path.Combine(journal, "left.claim.json")) && File.Exists(Path.Combine(journal, "right.claim.json")),
            "the real pre-cancellation worker claims to be captured");
        var originalProcessId = int.Parse(File.ReadAllLines(Path.Combine(journal, "left.started"))[0].Split('|')[0]);
        await PostAsync(host, "/runtime/workflows/alteration-plans", new
        {
            target = new { executionIds = new[] { executionId } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
        }, Guid.NewGuid().ToString("N"));
        await WaitForStateAsync(host, executionId, state => state["instance"]?["status"]?.GetValue<string>() == "Cancelled");
        await WaitForAsync(host, () => File.Exists(Path.Combine(journal, "left.cancelled")) && File.Exists(Path.Combine(journal, "right.cancelled")),
            "both held bodies to observe committed business cancellation through claim loss");
        Assert.Equal(0, StartedCount(journal, "final"));

        File.WriteAllText(Path.Combine(journal, "left.release"), "release");
        File.WriteAllText(Path.Combine(journal, "right.release"), "release");
        await using var restarted = await host.CrashAndRestartAsync(directory =>
            File.WriteAllText(Path.Combine(directory, "background-witness", "attempt-late-receipt-after-restart"), "submit"));
        await LoginAsync(restarted);
        await WaitForAsync(restarted, () => File.Exists(Path.Combine(journal, "left.late-receipt.json")) &&
                                                  File.Exists(Path.Combine(journal, "right.late-receipt.json")),
            "the replacement process to reject both captured late receipts");
        foreach (var key in new[] { "left", "right" })
        {
            var capturedClaim = JsonNode.Parse(File.ReadAllText(Path.Combine(journal, key + ".claim.json")))!;
            var lateReceipt = JsonNode.Parse(File.ReadAllText(Path.Combine(journal, key + ".late-receipt.json")))!;
            Assert.NotEqual(originalProcessId, lateReceipt["ProcessId"]!.GetValue<int>());
            Assert.Equal(executionId, lateReceipt["WorkflowExecutionId"]!.GetValue<string>());
            Assert.Equal(capturedClaim["WorkId"]!.GetValue<string>(), lateReceipt["WorkId"]!.GetValue<string>());
            Assert.Equal(capturedClaim["ClaimOwnerId"]!.GetValue<string>(), lateReceipt["ClaimOwnerId"]!.GetValue<string>());
            Assert.Equal(capturedClaim["ClaimFence"]!.GetValue<long>(), lateReceipt["ClaimFence"]!.GetValue<long>());
            Assert.Equal(capturedClaim["Revision"]!.GetValue<long>(), lateReceipt["Revision"]!.GetValue<long>());
            Assert.Contains("cancellation tombstone fenced this receipt", lateReceipt["Message"]!.GetValue<string>());
            Assert.Equal("Cancelled", lateReceipt["Status"]!.GetValue<string>());
            Assert.False(lateReceipt["HasResult"]!.GetValue<bool>());
        }

        var state = await WaitForStateAsync(restarted, executionId, instance => instance["instance"]?["status"]?.GetValue<string>() == "Cancelled");
        Assert.Equal("Cancelled", ActivityStatus(state, "left"));
        Assert.Equal("Cancelled", ActivityStatus(state, "right"));
        Assert.Equal(1, StartedCount(journal, "left"));
        Assert.Equal(1, StartedCount(journal, "right"));
        Assert.Equal(0, StartedCount(journal, "final"));
    }

    private static WorkbenchShell WitnessShell() => WorkbenchShell.Development with
    {
        Settings = new Dictionary<string, string>
        {
            ["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false",
            ["Nuplane:Setup:PollInterval"] = "1.00:00:00"
        }
    };

    private static void EnableWorkers(string directory)
    {
        var path = Path.Combine(directory, "shells.json");
        var shells = JsonNode.Parse(File.ReadAllText(path))!;
        shells["CShells"]!["Shells"]!["default"]!["Features"]!["BackgroundActivities"]!["RunWorkers"] = true;
        File.WriteAllText(path, shells.ToJsonString());
    }

    private static void AssertRestartedWithSameLogicalIdentity(string before, string after)
    {
        var original = before.Split('|');
        var replacement = after.Split('|');
        Assert.NotEqual(original[0], replacement[0]);
        Assert.Equal(original[1..], replacement[1..]);
    }

    private static int StartedCount(string directory, string key)
    {
        var path = Path.Combine(directory, key + ".started");
        return File.Exists(path) ? File.ReadAllLines(path).Count(line => line.Split('|').Length == 3) : 0;
    }

    private static async Task LoginAsync(WorkbenchProcess host) =>
        _ = await PostAsync(host, "/_elsa/identity/login", new { username = "admin", password = "Password123!" });

    private static async Task<string> PublishAndStartAsync(WorkbenchProcess host, string journal)
    {
        var probe = await ActivityVersionAsync(host, ProbeType);
        var sequence = await ActivityVersionAsync(host, "Elsa.Activities.Sequence.Activities.Sequence");
        var flowchart = await ActivityVersionAsync(host, "Elsa.Activities.Flowchart.Activities.Flowchart");
        var writeLine = await ActivityVersionAsync(host, "Elsa.Activities.Primitives.Activities.WriteLine");
        JsonObject Probe(string key, bool background) => Node(key, probe,
            [Input("DirectoryPath", journal), Input("Key", key), Input("WaitForRelease", background)],
            placement: background ? 1 : 0);
        var graph = Node("flowchart", flowchart, structure: new JsonObject
        {
            ["kind"] = "elsa.flowchart.structure", ["schemaVersion"] = "1.0.0",
            ["payload"] = new JsonObject
            {
                ["activities"] = new JsonArray(Node("fork", writeLine, [Input("text", "background witness fork")]), Probe("left", true), Probe("right", true)),
                ["connections"] = new JsonArray(Connection("fork", "left"), Connection("fork", "right")),
                ["startNodeId"] = "fork", ["nodeMetadata"] = new JsonObject(), ["connectionMetadata"] = new JsonObject()
            }
        });
        var root = Node("sequence", sequence, structure: new JsonObject
        {
            ["kind"] = "elsa.sequence.structure", ["schemaVersion"] = "1.0.0",
            ["payload"] = new JsonObject { ["activities"] = new JsonArray(graph, Probe("final", false)) }
        });
        var definition = await PostAsync(host, "/design/workflows/definitions/submit", new
        {
            name = "Native background crash witness", description = "Two gated bodies and one native parent continuation",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = root }
        });
        var published = await PostAsync(host, $"/publishing/workflows/{definition["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var started = await PostAsync(host, $"/runtime/workflows/executables/{published["artifactId"]!.GetValue<string>()}/execute",
            new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>() });
        return started["workflowExecutionId"]!.GetValue<string>();
    }

    private static JsonObject Node(string id, string version, JsonNode[]? inputs = null, JsonNode? structure = null, int placement = 0) => new()
    {
        ["nodeId"] = id, ["activityVersionId"] = version, ["inputs"] = new JsonArray(inputs ?? []),
        ["outputs"] = new JsonArray(), ["structure"] = structure, ["executionPlacement"] = placement
    };

    private static JsonObject Input(string key, object value) => new()
    {
        ["referenceKey"] = key, ["value"] = new JsonObject { ["expressionType"] = "Literal", ["value"] = JsonSerializer.SerializeToNode(value) }
    };

    private static JsonObject Connection(string from, string to) => new()
    {
        ["id"] = from + "-" + to, ["source"] = new JsonObject { ["nodeId"] = from, ["port"] = "Done" },
        ["target"] = new JsonObject { ["nodeId"] = to }
    };

    private static async Task<string> ActivityVersionAsync(WorkbenchProcess host, string type)
    {
        var catalog = await host.Client.GetFromJsonAsync<JsonNode>($"/design/activities/definitions?search={Uri.EscapeDataString(type)}");
        Assert.True(catalog?["items"]?.AsArray().Count > 0, $"Activity {type} was not reconciled: {catalog}\n{host.Output}");
        var match = Assert.Single(catalog!["items"]!.AsArray(), item => item?["definition"]?["activityTypeKey"]?.GetValue<string>() == type);
        return match!["definition"]!["headVersionId"]!.GetValue<string>();
    }

    private static async Task<JsonNode> PostAsync(WorkbenchProcess host, string path, object body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await host.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path}: {(int)response.StatusCode} {text}\n{host.Output}");
        return JsonNode.Parse(text)!;
    }

    private static string? ActivityStatus(JsonNode instance, string nodeId) => instance["activities"]!.AsArray()
        .SingleOrDefault(activity => activity?["executableNodeId"]?.GetValue<string>() == nodeId)?["status"]?.GetValue<string>();

    private static async Task<JsonNode> WaitForStateAsync(WorkbenchProcess host, string executionId, Func<JsonNode, bool> predicate)
    {
        JsonNode? last = null;
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await host.Client.GetFromJsonAsync<JsonNode>($"/runtime/workflows/instances/{executionId}");
            if (last is not null && predicate(last)) return last;
            if (last?["instance"]?["status"]?.GetValue<string>() is "Faulted" or "Cancelled") break;
            await Task.Delay(100);
        }
        throw new InvalidOperationException($"Workflow did not reach the expected state: {last}\n{host.Output}");
    }

    private static async Task WaitForAsync(WorkbenchProcess host, Func<bool> predicate, string expected,
        Func<string>? timeoutEvidence = null)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                var evidence = timeoutEvidence?.Invoke();
                Assert.Fail($"Timed out waiting for {expected}.\n{evidence}\n{host.Output}");
            }
            await Task.Delay(100);
        }
    }

    private static string DescribeJournal(string journal)
    {
        const int maximumFiles = 32;
        const int maximumFileCharacters = 768;
        const int maximumTotalCharacters = 12000;
        if (!Directory.Exists(journal))
            return $"Background witness journal is missing: {journal}";

        var output = new StringBuilder();
        string[] files;
        try
        {
            files = Directory.EnumerateFiles(journal)
                .Where(path => !Path.GetFileName(path).EndsWith(".tmp", StringComparison.Ordinal))
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .Take(maximumFiles).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"Background witness journal could not be enumerated: {exception.GetType().Name}";
        }

        foreach (var path in files)
        {
            if (output.Length >= maximumTotalCharacters)
                break;

            output.Append(Path.GetFileName(path)).Append(": ");
            try
            {
                using var reader = new StreamReader(path);
                var buffer = new char[maximumFileCharacters];
                var count = reader.ReadBlock(buffer, 0, buffer.Length);
                output.Append(buffer, 0, count);
                if (reader.Peek() >= 0)
                    output.Append("…");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                output.Append("<unreadable: ").Append(exception.GetType().Name).Append('>');
            }
            output.AppendLine();
        }

        if (output.Length > maximumTotalCharacters)
        {
            output.Length = maximumTotalCharacters;
            output.Append("… journal output truncated");
        }
        return output.ToString();
    }

    private static void PrepareContentRoot(string directory)
    {
        var path = Path.Combine(directory, "shells.json");
        var shells = JsonNode.Parse(File.ReadAllText(path))!;
        var features = shells["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        features["BackgroundActivities"] = new JsonObject
        {
            ["RunWorkers"] = false, ["MaxConcurrency"] = 2, ["PollIntervalSeconds"] = .1, ["LeaseDurationSeconds"] = 20, ["RenewalIntervalSeconds"] = 1
        };
        features["BackgroundActivityWitness"] = new JsonObject();
        File.WriteAllText(path, shells.ToJsonString());
        var configuration = typeof(WorkbenchBuild).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var project = Path.Combine(WorkbenchBuild.RepositoryRoot, "Essentials/tests/Workbench/Fixtures/BackgroundActivities");
        var framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        // Nuplane's immutable acquisition cache may outlive the host's state directory. Give each owned package
        // a unique version so a rebuilt fixture never reuses an older binary from a previous test run.
        var packageVersion = "1.0.0-witness." + Guid.NewGuid().ToString("N");
        using var archive = ZipFile.Open(Path.Combine(directory, "packages", PackageId + "." + packageVersion + ".nupkg"), ZipArchiveMode.Create);
        AddText(archive, "[Content_Types].xml", """<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="nuspec" ContentType="application/octet"/><Default Extension="dll" ContentType="application/octet"/><Default Extension="json" ContentType="application/octet"/></Types>""");
        AddText(archive, "_rels/.rels", $"""<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{PackageId}.nuspec" Id="R1"/></Relationships>""");
        AddText(archive, PackageId + ".nuspec", $"""<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{PackageId}</id><version>{packageVersion}</version><authors>tests</authors><description>Owned process background activity witness.</description></metadata></package>""");
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
}
