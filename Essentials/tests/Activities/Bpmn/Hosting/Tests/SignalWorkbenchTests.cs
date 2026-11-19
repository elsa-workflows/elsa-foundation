using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>Real model-owned signal catches, published Event workers and the durable PublishEvent sender.</summary>
public sealed class SignalWorkbenchTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Theory]
    [InlineData("Direct")]
    [InlineData("Root")]
    [InlineData("Sequence")]
    [InlineData("Flowchart")]
    [InlineData("CompensationScope")]
    public async Task Two_model_signal_catches_restart_and_complete_through_real_Event_workers(string parent)
    {
        var directory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION");
        const string eventName = "actual-model-release";
        var name = parent.ToLowerInvariant(); string? output = null;
        if (directory is not null)
        {
            Assert.False(string.IsNullOrWhiteSpace(source)); output = Path.Combine(directory, name);
            Assert.False(Directory.Exists(output)); Directory.CreateDirectory(output);
        }
        async Task Save(string file, object value)
        { if (output is not null) await File.WriteAllTextAsync(Path.Combine(output, file), JsonSerializer.Serialize(value, Web) + Environment.NewLine); }
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        var processes = new List<object>(); var exits = new List<int?>(); var stores = new List<object?>();
        void Record() => processes.Add(new { processId = host.ProcessId, controllerProcessId = Environment.ProcessId,
            startedAtUtc = Process.GetProcessById(host.ProcessId).StartTime.ToUniversalTime() });
        async Task Cut(string label)
        {
            await host.StopAsync(); exits.Add(host.LastStoppedExitCode);
            stores.Add(await host.SnapshotAsync(label + "-runtime.sqlite.gz", name));
            stores.Add(await host.SnapshotAsync(label + "-design.sqlite.gz", name, design: true));
        }
        Record(); var stage = "publish";
        try
        {
            await host.LoginAsync();
            var worker = await Publish("Signal catch worker", await Node("event", "Elsa.Activities.Primitives.Activities.Event",
                [Literal("EventName", eventName), Literal("CanStartWorkflow", false)]));
            var pin = new WorkflowExecutableIdentity(worker["artifactId"]!.GetValue<string>(), worker["definitionId"]!.GetValue<string>(),
                worker["definitionVersionId"]!.GetValue<string>(), worker["artifactVersion"]!.GetValue<string>(), worker["artifactHash"]!.GetValue<string>());
            var process = new BpmnProcessDefinition("actual-signals", Elements:
                [new("start", BpmnElementTypes.StartEvent), new("fork", BpmnElementTypes.ParallelGateway),
                    Catch("first", "first-event"), Catch("second", "second-event"),
                    new("join", BpmnElementTypes.ParallelGateway), new("end", BpmnElementTypes.EndEvent)],
                SequenceFlows: [new("to-fork", "start", "fork"), new("to-first", "fork", "first"), new("to-second", "fork", "second"),
                    new("first-join", "first", "join"), new("second-join", "second", "join"), new("to-end", "join", "end")]);
            var definition = new BpmnModelDefinition(5, JsonSerializer.SerializeToElement(process, Web), [], null, null,
                [new("first-event", pin), new("second-event", pin)]);
            var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-signals", definition });
            JsonNode? outer = null; string? nativeId = null; string model;
            if (parent == "Direct") model = (await host.PostAsync("/bpmn/executions", new
                { requestId = "start-signals", sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>() }))["executionId"]!.GetValue<string>();
            else
            {
                var run = await Node("run-bpmn", "Elsa.Activities.Bpmn.Activities.RunBpmn", [Literal("Model", published["sourceReferenceId"]!.GetValue<string>())]);
                object root = run;
                if (parent != "Root")
                {
                    var after = await Node("after", "Elsa.Activities.Primitives.Activities.WriteLine", [Literal("text", "Both model signal catches completed.")]);
                    object structure = parent switch
                    {
                        "Sequence" => new { kind = "elsa.sequence.structure", schemaVersion = "1.0.0", payload = new { activities = new[] { run, after } } },
                        "Flowchart" => new { kind = "elsa.flowchart.structure", schemaVersion = "1.0.0", payload = new { startNodeId = "run-bpmn", activities = new[] { run, after },
                            connections = new[] { new { id = "after", source = new { nodeId = "run-bpmn", port = "Done" }, target = new { nodeId = "after" } } } } },
                        "CompensationScope" => new { kind = "elsa.compensation-scope.structure", schemaVersion = "1.0.0", payload = new { steps = new[] { new { name = "process", activity = run }, new { name = "after", activity = after } } } },
                        _ => throw new InvalidOperationException("Unknown actual signal parent.")
                    };
                    root = await Node("native-parent", "Elsa.Activities." + (parent == "CompensationScope" ? "Compensation" : parent) + ".Activities." + parent, [], structure);
                }
                outer = await Publish("Signal native parent " + parent, root);
                nativeId = await Execute(outer);
                var parentWait = await Wait(async () =>
                {
                    var view = await host.GetAsync("/runtime/workflows/instances/" + nativeId);
                    Assert.Empty(view["incidents"]!.AsArray());
                    var activity = view["activities"]!.AsArray().SingleOrDefault(item => item!["activityType"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn");
                    return activity is null ? new JsonObject() : await host.GetAsync("/runtime/workflows/instances/" + nativeId + "/activity-executions/" + activity["activityExecutionId"]!.GetValue<string>());
                }, value => value["bookmarks"] is JsonArray waits && waits.Count == 1);
                model = parentWait["bookmarks"]![0]!["stimulusHash"]!.GetValue<string>();
                await Wait(async () => await host.FindAsync("/bpmn/executions/" + model) ?? new JsonObject(), value => value["executionId"] is not null);
            }
            var route = "/bpmn/executions/" + model; stage = "waiting";
            var before = await Read("--read-bpmn-direct", host.RuntimeDatabase, model);
            var work = before["model"]!["payload"]!["work"]!.AsArray(); Assert.Equal(2, work.Count);
            var workers = work.Select(item => item!["nativeWork"]!["workerExecutionId"]!.GetValue<string>()).ToArray();
            Assert.Equal(2, workers.Distinct(StringComparer.Ordinal).Count());
            foreach (var workerId in workers)
                await Wait(async () =>
                {
                    var view = await host.FindAsync("/runtime/workflows/instances/" + workerId);
                    if (view is null || view["activities"]!.AsArray().Count == 0) return new JsonObject();
                    Assert.Empty(view["incidents"]!.AsArray());
                    return await host.GetAsync("/runtime/workflows/instances/" + workerId + "/activity-executions/" + view["activities"]![0]!["activityExecutionId"]!.GetValue<string>());
                }, value => value["bookmarks"] is JsonArray waits && waits.Count == 1);
            Assert.Empty((await host.GetAsync(route + "/work"))["items"]!.AsArray());
            await Save("waiting-checked.json", before); await Save("waiting-api.json", await host.GetAsync(route));
            var waitingWorkers = new List<JsonNode>();
            foreach (var workerId in workers) waitingWorkers.Add(await Read("--read-bpmn-native", host.RuntimeDatabase, model, workerId));
            await Save("waiting-workers.json", waitingWorkers);
            await Cut("waiting"); await host.RestartAsync(); Record(); await host.LoginAsync(); stage = "recovered";
            var recovered = await Read("--read-bpmn-direct", host.RuntimeDatabase, model);
            Assert.True(JsonNode.DeepEquals(before["model"], recovered["model"]));
            await Save("recovered-checked.json", recovered);
            var publisher = await Publish("Signal sender", await Node("send", "Elsa.Activities.Primitives.Activities.PublishEvent",
                [Literal("EventName", eventName), Literal("IsLocalEvent", false), Literal("Payload", new { release = "after-restart" })]));
            var publisherId = await Execute(publisher);
            var completed = await Wait(() => Read("--read-bpmn-direct", host.RuntimeDatabase, model), value =>
                value["model"]!["lifecycle"]!.GetValue<int>() == (int)RuntimeModelLifecycle.TerminalSettled &&
                value["queuedModelInputs"]!.AsArray().Count == 0 && value["sources"]!.AsArray().All(item => item!["status"]!.GetValue<int>() == 2));
            Assert.Empty(completed["model"]!["payload"]!["work"]!.AsArray());
            Assert.Equal(2, completed["model"]!["payload"]!["nativeResults"]!.AsArray().Count);
            Assert.Equal(2, completed["inputs"]!.AsArray().Count(item => item!["inputKind"]!.GetValue<string>() == "native-result" && item["result"]!["status"]!.GetValue<string>() == "Accepted"));
            var finishedWorkers = new List<JsonNode>();
            foreach (var workerId in workers)
            {
                var physical = await Wait(() => Read("--read-bpmn-native", host.RuntimeDatabase, model, workerId), value => value["resultSource"]?["status"]?.GetValue<int>() == 2);
                Assert.True(JsonNode.DeepEquals(work.Single(item => item!["nativeWork"]!["workerExecutionId"]!.GetValue<string>() == workerId)!["nativeWork"], physical["worker"]!["modelWorkOrigin"]));
                Assert.Equal("Completed", (await host.GetAsync("/runtime/workflows/instances/" + workerId))["instance"]!["status"]!.GetValue<string>());
                finishedWorkers.Add(physical);
            }
            if (nativeId is not null)
            {
                var finishedParent = await Wait(() => host.GetAsync("/runtime/workflows/instances/" + nativeId), value => value["instance"]!["status"]!.GetValue<string>() == "Completed");
                Assert.Empty(finishedParent["incidents"]!.AsArray());
                if (parent != "Root") Assert.Single(finishedParent["activities"]!.AsArray(), item => item!["executableNodeId"]!.GetValue<string>() == "after" && item["status"]!.GetValue<string>() == "Completed");
                await Save("finished-parent.json", await host.ReadEmbeddingAsync(model, nativeId));
            }
            await Save("finished-checked.json", completed); await Save("finished-workers.json", finishedWorkers);
            await Cut("finished"); stage = "retained";
            var backup = Path.Combine(host.ContentRoot, "source-snapshot.sqlite");
            await Read("--backup-sqlite", host.RuntimeDatabase, backup);
            var broadcast = await Read(new[] { "--find-publisher-source", backup, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(backup))), "default", publisherId }.Concat(workers).ToArray());
            Assert.Equal("Delivered", broadcast["source"]!["status"]!.GetValue<string>());
            var cohort = broadcast["source"]!["snapshot"]!;
            Assert.Empty(cohort["starts"]!.AsArray());
            Assert.Equal(workers.Order(StringComparer.Ordinal), cohort["resumes"]!.AsArray().Select(item => item!["workflowExecutionId"]!.GetValue<string>()).Order(StringComparer.Ordinal));
            await Save("broadcast-checked.json", broadcast);
            await host.RestartAsync(); Record(); await host.LoginAsync();
            var retained = await Read("--read-bpmn-direct", host.RuntimeDatabase, model);
            Assert.True(JsonNode.DeepEquals(completed, retained)); await Save("retained-checked.json", retained);
            await Cut("retained"); Assert.All(exits, code => Assert.NotEqual(0, code));
            await Save("process.json", new { schemaVersion = 1, kind = "actual-model-signal-recovery", sourceRevision = source, parent,
                modelExecutionId = model, nativeParentExecutionId = nativeId, worker, published, outer, publisher, publisherId,
                workers, processes, exits, stores, binaries = host.DescribeBinaries(source, includePublishing: true), stateSeeded = false,
                limits = new[] { "Two actual model-owned signal catches and real Event workers across process loss. Delivery-time later-listener, rearm/pre-ACK retry and empty-cohort cuts remain separate gates.", "No final upgraded-host or Studio acceptance is inferred." } });
        }
        catch (Exception exception)
        { await Cut("failed"); await Save("failure.json", new { stage, sourceRevision = source, type = exception.GetType().FullName, processes, exits }); throw; }

        BpmnElement Catch(string id, string binding) => new(id, BpmnElementTypes.IntermediateCatchEvent, bindingRef: binding,
            eventDefinitions: [new(BpmnEventDefinitionTypes.Signal, new Dictionary<string,string> { [BpmnEventDefinitionProperties.Name] = eventName })]);
        async Task<object> Node(string id, string type, object[] inputs, object? structure = null)
        {
            var catalog = await host.GetAsync("/design/activities/definitions?search=" + Uri.EscapeDataString(type));
            var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == type)!;
            return new { nodeId = id, activityVersionId = activity["definition"]!["headVersionId"]!.GetValue<string>(), inputs, outputs = Array.Empty<object>(), structure };
        }
        async Task<JsonNode> Publish(string label, object root)
        {
            var draft = await host.PostAsync("/design/workflows/definitions/submit", new { name = label,
                state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = root } });
            return await host.PostAsync("/publishing/workflows/" + draft["version"]!["id"]!.GetValue<string>() + "/publish", new { });
        }
        async Task<string> Execute(JsonNode publication) => (await host.PostAsync("/runtime/workflows/executables/" + publication["artifactId"]!.GetValue<string>() + "/execute",
            new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() }))["workflowExecutionId"]!.GetValue<string>();
        async Task<JsonNode> Read(params string[] arguments)
        {
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add(host.ReaderAssemblyPath); foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var child = Process.Start(info) ?? throw new InvalidOperationException("The actual signal observer did not start.");
            var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await child.WaitForExitAsync(limit.Token); }
            catch { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } throw; }
            if (child.ExitCode != 0) throw new InvalidOperationException("The checked signal observation failed: " + await stdout + await stderr);
            return JsonNode.Parse(await stdout)!;
        }
    }

    private static object Literal(string key, object value) => new { referenceKey = key, value = new { value, expressionType = "Literal" } };
    private static async Task<JsonNode> Wait(Func<Task<JsonNode>> observe, Func<JsonNode, bool> complete)
    {
        var limit = Stopwatch.StartNew();
        while (limit.Elapsed < TimeSpan.FromSeconds(90)) { var value = await observe(); if (complete(value)) return value; await Task.Delay(200); }
        throw new TimeoutException("The actual signal journey did not finish its owed work.");
    }
}
