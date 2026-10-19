using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>Shared REST, package, and observation helpers for native background-activity hosting scenarios.</summary>
internal static class NativeHostingScenario
{
    internal const string PackageId = "Elsa.Workbench.Tests.BackgroundActivities";
    internal const string ProbeType = PackageId + ".BackgroundProbe";
    internal static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);
    internal static readonly TimeSpan WorkerLeaseDuration = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan WorkerPollInterval = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan WorkerRenewalInterval = TimeSpan.FromSeconds(1);

    internal static WorkbenchShell WitnessShell() => WorkbenchShell.Development with
    {
        Settings = new Dictionary<string, string>
        {
            ["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false",
            ["Nuplane:Setup:PollInterval"] = "1.00:00:00"
        }
    };

    internal static void EnableWorkers(string directory)
    {
        var path = Path.Combine(directory, "shells.json");
        var shells = JsonNode.Parse(File.ReadAllText(path))!;
        shells["CShells"]!["Shells"]!["default"]!["Features"]!["BackgroundActivities"]!["RunWorkers"] = true;
        File.WriteAllText(path, shells.ToJsonString());
    }

    internal static void AssertRestartedWithSameLogicalIdentity(string before, string after)
    {
        var original = before.Split('|');
        var replacement = after.Split('|');
        Assert.NotEqual(original[0], replacement[0]);
        Assert.Equal(original[1..], replacement[1..]);
    }

    internal static int StartedCount(string directory, string key) =>
        Directory.Exists(directory) ? Directory.EnumerateFiles(directory, key + ".started.*.json").Count() : 0;

    internal static string[] StartedLines(string directory, string key) =>
        Directory.EnumerateFiles(directory, key + ".started.*.json")
            .Select(path => JsonSerializer.Deserialize<BodyStart>(File.ReadAllText(path))!)
            .OrderBy(record => record.RecordedAt)
            .Select(record => $"{record.ProcessId}|{record.InvocationId}|{record.AttemptId}")
            .ToArray();

    private sealed record BodyStart(int ProcessId, DateTimeOffset RecordedAt, string InvocationId, string AttemptId);

    internal static async Task LoginAsync(WorkbenchProcess host) =>
        _ = await PostAsync(host, "/_elsa/identity/login", new { username = "admin", password = "Password123!" });

    internal static async Task<string> PublishAndStartAsync(WorkbenchProcess host, string journal)
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

    internal static JsonObject Node(string id, string version, JsonNode[]? inputs = null, JsonNode? structure = null, int placement = 0) => new()
    {
        ["nodeId"] = id, ["activityVersionId"] = version, ["inputs"] = new JsonArray(inputs ?? []),
        ["outputs"] = new JsonArray(), ["structure"] = structure, ["executionPlacement"] = placement
    };

    internal static JsonObject Input(string key, object value) => new()
    {
        ["referenceKey"] = key, ["value"] = new JsonObject { ["expressionType"] = "Literal", ["value"] = JsonSerializer.SerializeToNode(value) }
    };

    internal static JsonObject Connection(string from, string to) => new()
    {
        ["id"] = from + "-" + to, ["source"] = new JsonObject { ["nodeId"] = from, ["port"] = "Done" },
        ["target"] = new JsonObject { ["nodeId"] = to }
    };

    internal static async Task<string> ActivityVersionAsync(WorkbenchProcess host, string type)
    {
        var catalog = await host.Client.GetFromJsonAsync<JsonNode>($"/design/activities/definitions?search={Uri.EscapeDataString(type)}");
        Assert.True(catalog?["items"]?.AsArray().Count > 0, $"Activity {type} was not reconciled: {catalog}\n{host.Output}");
        var match = Assert.Single(catalog!["items"]!.AsArray(), item => item?["definition"]?["activityTypeKey"]?.GetValue<string>() == type);
        return match!["definition"]!["headVersionId"]!.GetValue<string>();
    }

    internal static async Task<JsonNode> PostAsync(WorkbenchProcess host, string path, object body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await host.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path}: {(int)response.StatusCode} {text}\n{host.Output}");
        return JsonNode.Parse(text)!;
    }

    internal static string? ActivityStatus(JsonNode instance, string nodeId) => instance["activities"]!.AsArray()
        .SingleOrDefault(activity => activity?["executableNodeId"]?.GetValue<string>() == nodeId)?["status"]?.GetValue<string>();

    internal static async Task<JsonNode> WaitForStateAsync(WorkbenchProcess host, string executionId, Func<JsonNode, bool> predicate)
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

    internal static async Task WaitForAsync(WorkbenchProcess host, Func<bool> predicate, string expected,
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

    /// <summary>
    /// Wall time still owed after a kill before the replacement process can treat the dead worker's fence as
    /// expired. Restart time already spent is subtracted; a slow restart does not wait again.
    /// </summary>
    internal static TimeSpan RemainingWaitAfterKill(
        DateTimeOffset killedAt,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        TimeSpan pollInterval)
    {
        var remaining = leaseDuration + pollInterval - (now - killedAt);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    internal static async Task WaitUntilWorkerLeasesMustHaveExpiredAsync(DateTimeOffset killedAt)
    {
        var remaining = RemainingWaitAfterKill(killedAt, DateTimeOffset.UtcNow, WorkerLeaseDuration, WorkerPollInterval);
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining);
        Assert.True(
            DateTimeOffset.UtcNow >= killedAt + WorkerLeaseDuration,
            "The reclaim poll started before the authored worker lease could have expired.");
    }

    internal static string DescribeJournal(string journal)
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

    internal static void PrepareContentRoot(string directory)
    {
        ConfigureContentRoot(directory);
        CreatePackage(directory);
    }

    internal static void ConfigureContentRoot(string directory)
    {
        var path = Path.Combine(directory, "shells.json");
        var shells = JsonNode.Parse(File.ReadAllText(path))!;
        var features = shells["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        features["BackgroundActivities"] = new JsonObject
        {
            ["RunWorkers"] = false,
            ["MaxConcurrency"] = 2,
            ["PollIntervalSeconds"] = WorkerPollInterval.TotalSeconds,
            ["LeaseDurationSeconds"] = WorkerLeaseDuration.TotalSeconds,
            ["RenewalIntervalSeconds"] = WorkerRenewalInterval.TotalSeconds
        };
        features["BackgroundActivityWitness"] = new JsonObject();
        File.WriteAllText(path, shells.ToJsonString());
    }

    /// <summary>Creates one uniquely versioned witness package and returns its path for exact-byte reuse.</summary>
    internal static string CreatePackage(string directory)
    {
        var configuration = typeof(WorkbenchBuild).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var project = Path.Combine(WorkbenchBuild.RepositoryRoot, "Essentials/tests/Workbench/Fixtures/BackgroundActivities");
        var framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        // Nuplane's immutable acquisition cache may outlive the host's state directory. Give each owned package
        // a unique version so a rebuilt fixture never reuses an older binary from a previous test run.
        var packageVersion = "1.0.0-witness." + Guid.NewGuid().ToString("N");
        var packagePath = Path.Combine(directory, "packages", PackageId + "." + packageVersion + ".nupkg");
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        AddText(archive, "[Content_Types].xml", """<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="nuspec" ContentType="application/octet"/><Default Extension="dll" ContentType="application/octet"/><Default Extension="json" ContentType="application/octet"/></Types>""");
        AddText(archive, "_rels/.rels", $"""<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{PackageId}.nuspec" Id="R1"/></Relationships>""");
        AddText(archive, PackageId + ".nuspec", $"""<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{PackageId}</id><version>{packageVersion}</version><authors>tests</authors><description>Owned process background activity witness.</description></metadata></package>""");
        archive.CreateEntryFromFile(Path.Combine(project, "bin", configuration, framework, PackageId + ".dll"), "lib/" + framework + "/" + PackageId + ".dll");
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(project, "elsa-package.json")))!;
        manifest["package"]!["version"] = packageVersion;
        AddText(archive, "elsa-package.json", manifest.ToJsonString());
        return packagePath;
    }

    private static void AddText(ZipArchive archive, string path, string value)
    {
        using var stream = archive.CreateEntry(path).Open();
        stream.Write(Encoding.UTF8.GetBytes(value));
    }
}
