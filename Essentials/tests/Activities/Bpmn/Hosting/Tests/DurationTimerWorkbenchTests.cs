using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>Real authenticated runtime-only hosts, elapsed wall time, process loss and the normal delayed outbox.</summary>
public sealed class DurationTimerWorkbenchTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_duration_boundary_keeps_the_first_decision_through_process_loss_and_late_input(bool timerFirst)
    {
        var name = timerFirst ? "timer-first" : "human-first";
        var directory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION");
        string? output = null;
        if (directory is not null)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("Exact-source evidence needs its committed revision.");
            output = Path.Combine(directory, name);
            if (Directory.Exists(output)) throw new InvalidOperationException("Use a fresh owned duration evidence directory.");
            Directory.CreateDirectory(output);
        }
        async Task Save(string file, object value)
        { if (output is not null) await File.WriteAllTextAsync(Path.Combine(output, file), JsonSerializer.Serialize(value, Web) + Environment.NewLine); }
        var processes = new List<object>(); var exits = new List<int?>(); var snapshots = new List<object?>();
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, runtimeOnly: true, recoverySweepSeconds: 0.2);
        void RecordProcess() => processes.Add(new { processId = host.ProcessId, controllerProcessId = Environment.ProcessId,
            startedAtUtc = Process.GetProcessById(host.ProcessId).StartTime.ToUniversalTime() });
        RecordProcess();
        var stage = "start";
        try
        {
            await host.LoginAsync();
            var process = new BpmnProcessDefinition("actual-duration-human", Elements:
                [new("start", BpmnElementTypes.StartEvent), new("review", BpmnElementTypes.UserTask, bindingRef: "review-work"),
                    new("timeout", BpmnElementTypes.BoundaryEvent, bindingRef: "timeout-work", attachedToRef: "review", cancelActivity: true,
                        eventDefinitions: [new(BpmnEventDefinitionTypes.Timer, new Dictionary<string,string> { [BpmnEventDefinitionProperties.Interval] = "PT30S" })]),
                    new("done", BpmnElementTypes.EndEvent), new("timed-out", BpmnElementTypes.EndEvent)],
                SequenceFlows: [new("start-review", "start", "review"), new("review-done", "review", "done"), new("timeout-end", "timeout", "timed-out")]);
            var definition = new BpmnModelDefinition(4, JsonSerializer.SerializeToElement(process, Web), ["review-work"], null, ["timeout-work"]);
            var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-duration", definition });
            var started = await host.PostAsync("/bpmn/executions", new { requestId = "start-duration", sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>() });
            var execution = started["executionId"]!.GetValue<string>(); var route = "/bpmn/executions/" + execution;
            var before = await ReadAsync(host, execution); await Save("before-checked.json", before);
            var beforeApi = await host.GetAsync(route); await Save("before-api.json", beforeApi);
            var human = Assert.Single((await host.GetAsync(route + "/work"))["items"]!.AsArray())!;
            var work = before["model"]!["payload"]!["work"]!.AsArray();
            Assert.Equal(2, work.Count);
            var timer = Assert.Single(work, item => item!["timer"] is not null)!["timer"]!;
            var dueAt = timer["dueAt"]!.GetValue<DateTimeOffset>();
            Assert.True(DateTimeOffset.UtcNow < dueAt); Assert.Equal("Running", beforeApi["status"]!.GetValue<string>());
            AssertNativeFree(before);
            await host.StopAsync(); exits.Add(host.LastStoppedExitCode); snapshots.Add(await host.SnapshotAsync("before-runtime.sqlite.gz", name));
            await host.RestartAsync(); RecordProcess(); await host.LoginAsync(); stage = "recovered-wait";
            var recovered = await ReadAsync(host, execution); await Save("recovered-checked.json", recovered);
            Assert.True(JsonNode.DeepEquals(before["model"], recovered["model"]));
            Assert.True(JsonNode.DeepEquals(timer, Assert.Single(recovered["model"]!["payload"]!["work"]!.AsArray(), item => item!["timer"] is not null)!["timer"]));
            Assert.True(DateTimeOffset.UtcNow < dueAt, "The owned restart exceeded the real timer interval.");
            var completionRoute = route + "/work/" + human["invocationId"]!.GetValue<string>() + "/complete";
            var completion = new { requestId = "actual-human-decision", attemptId = human["attemptId"]!.GetValue<string>() };
            JsonNode? humanResponse = null;
            if (!timerFirst) humanResponse = await host.PostAsync(completionRoute, completion);
            var decidedApi = await WaitAsync(() => host.GetAsync(route), value => value["status"]!.GetValue<string>() == "TerminalSettled");
            var decided = await ReadAsync(host, execution); await Save("decided-api.json", decidedApi); await Save("decided-checked.json", decided);
            Assert.Equal(2, decided["model"]!["revision"]!.GetValue<long>()); AssertNativeFree(decided);
            var diagnostics = decided["model"]!["payload"]!["state"]!["diagnostics"]!.AsArray();
            Assert.Contains(diagnostics, item => item!["elementId"]?.GetValue<string>() == (timerFirst ? "timed-out" : "done"));
            Assert.DoesNotContain(diagnostics, item => item!["elementId"]?.GetValue<string>() == (timerFirst ? "done" : "timed-out"));
            await host.StopAsync(); exits.Add(host.LastStoppedExitCode); snapshots.Add(await host.SnapshotAsync("decided-runtime.sqlite.gz", name));
            await host.RestartAsync(); RecordProcess(); await host.LoginAsync(); stage = "recover-winner";
            Assert.True(JsonNode.DeepEquals(decidedApi, await host.GetAsync(route)));
            var replay = await host.PostAsync(completionRoute, completion, timerFirst ? HttpStatusCode.Conflict : HttpStatusCode.OK);
            if (timerFirst) Assert.Equal("Rejected", replay["result"]!["status"]!.GetValue<string>());
            else Assert.True(JsonNode.DeepEquals(humanResponse!["result"], replay["result"]));
            var finished = await WaitAsync(() => ReadAsync(host, execution), value =>
                value["sources"]!.AsArray().All(item => item!["status"]!.GetValue<int>() == 2) && value["queuedModelInputs"]!.AsArray().Count == 0);
            Assert.True(JsonNode.DeepEquals(decided["model"], finished["model"])); AssertNativeFree(finished);
            var timerReceipt = Assert.Single(finished["inputs"]!.AsArray(), item => item!["inputKind"]!.GetValue<string>() == "timer-elapsed")!;
            Assert.Equal(timerFirst ? "Accepted" : "Rejected", timerReceipt["result"]!["status"]!.GetValue<string>());
            Assert.True(timerReceipt["acceptedAt"]!.GetValue<DateTimeOffset>() >= dueAt);
            var finishedApi = await host.GetAsync(route); await Save("finished-checked.json", finished); await Save("finished-api.json", finishedApi);
            await host.StopAsync(); exits.Add(host.LastStoppedExitCode); snapshots.Add(await host.SnapshotAsync("finished-runtime.sqlite.gz", name));
            await host.RestartAsync(); RecordProcess(); await host.LoginAsync(); stage = "retained";
            var retained = await ReadAsync(host, execution); await Save("retained-checked.json", retained); await Save("retained-api.json", await host.GetAsync(route));
            Assert.True(JsonNode.DeepEquals(finished, retained)); Assert.True(JsonNode.DeepEquals(finishedApi, await host.GetAsync(route)));
            Assert.False(File.Exists(host.DesignDatabase));
            await host.StopAsync(); exits.Add(host.LastStoppedExitCode); snapshots.Add(await host.SnapshotAsync("retained-runtime.sqlite.gz", name));
            Assert.Equal(4, processes.Count); Assert.All(exits, exit => Assert.NotEqual(0, exit));
            var observerVersion = FileVersionInfo.GetVersionInfo(host.ReaderAssemblyPath).ProductVersion;
            if (source is not null) Assert.Contains(source, observerVersion!);
            await Save("process.json", new { schemaVersion = 1, kind = "actual-runtime-only-human-duration-race", sourceRevision = source,
                order = name, durationSeconds = 30, dueAt, capturedAtUtc = DateTimeOffset.UtcNow, published, started, human, humanResponse, replay,
                processes, exits, snapshots, binaries = host.DescribeBinaries(source), observer = new { file = Path.GetFileName(host.ReaderAssemblyPath),
                    productVersion = observerVersion, sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(host.ReaderAssemblyPath))) },
                noDesignStorage = true, stateSeeded = false, clockEdited = false,
                limits = new[] { "Same-source direct human/timer journey. Worker/held timer interruption and simultaneous commit orders remain separate.",
                    "No upgraded-host or final Studio acceptance is inferred." } });
        }
        catch (Exception exception)
        {
            await host.StopAsync();
            await host.SnapshotAsync("failed-runtime.sqlite.gz", name);
            await Save("failure.json", new { stage, sourceRevision = source, failureType = exception.GetType().FullName, processes,
                failedExitCode = host.LastStoppedExitCode, snapshots, binaries = host.DescribeBinaries(source) });
            throw;
        }
    }

    private static void AssertNativeFree(JsonNode value)
    {
        Assert.True(value["readOnly"]!.GetValue<bool>());
        foreach (var field in new[] { "nativeWorkflowRows", "nativeActivityRows", "nativeBookmarkRows" }) Assert.Equal(0, value[field]!.GetValue<int>());
    }

    private static async Task<JsonNode> ReadAsync(OwnedWorkbench host, string model)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { host.ReaderAssemblyPath, "--read-bpmn-direct", host.RuntimeDatabase, model }) info.ArgumentList.Add(argument);
        using var child = Process.Start(info) ?? throw new InvalidOperationException("The owned direct reader did not start.");
        var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(limit.Token); }
        catch { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } throw; }
        if (child.ExitCode != 0) throw new InvalidOperationException("The actual direct reader failed: " + await stderr);
        return JsonNode.Parse(await stdout)!;
    }

    private static async Task<JsonNode> WaitAsync(Func<Task<JsonNode>> observe, Func<JsonNode,bool> complete)
    {
        var limit = Stopwatch.StartNew();
        while (limit.Elapsed < TimeSpan.FromSeconds(90))
        { var value = await observe(); if (complete(value)) return value; await Task.Delay(200); }
        throw new TimeoutException("The actual duration journey did not finish its owed work.");
    }
}
