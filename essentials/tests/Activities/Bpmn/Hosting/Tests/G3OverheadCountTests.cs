using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// G3 measurement of the already-proved Root/SQLite/Owned Workbench child-to-parent cancel journey (#4028).
/// Records EF round-trips, writes, transactions, lease/fence operations and step latency. Asserts the
/// deferred #4028 semantic count: exactly one Cancel command is issued and applied. Not a budget,
/// throughput or CI-threshold gate. SQLite, one in-process host only.
/// </summary>
public sealed class G3OverheadCountTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RunBpmn_child_cancel_records_overhead_and_exactly_one_Cancel_command()
    {
        var environment = new
        {
            os = RuntimeInformation.OSDescription,
            framework = RuntimeInformation.FrameworkDescription,
            provider = "SQLite",
            tool = "EF Core Database.Command/Transaction console logs parsed as CommandCaptureInterceptor-equivalent counts; Stopwatch per logical step; SQLite ContentJson corroboration after stop",
            host = "Owned Workbench, one process"
        };
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2, capturePersistence: true);
        await host.LoginAsync();
        host.Capture.Mark();

        var process = new BpmnProcessDefinition("g3-child-cancel-human", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("human", BpmnElementTypes.UserTask, bindingRef: "work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-work", "start", "human"), new("to-end", "human", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ["work"]);
        var steps = new List<MeasuredStep>();

        async Task<JsonNode> StepAsync(string name, Func<Task<JsonNode>> action)
        {
            host.Capture.Mark();
            var clock = Stopwatch.StartNew();
            var result = await action();
            clock.Stop();
            var counters = host.Capture.Delta();
            steps.Add(new MeasuredStep(name, clock.ElapsedMilliseconds, counters));
            return result;
        }

        async Task StepVoidAsync(string name, Func<Task> action)
        {
            await StepAsync(name, async () =>
            {
                await action();
                return new JsonObject();
            });
        }

        var published = await StepAsync("publish-bpmn-model", () =>
            host.PostAsync("/bpmn/artifacts", new { requestId = "g3-publish-child-cancel", definition }));
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Bpmn.Activities.RunBpmn");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn");
        var native = await StepAsync("submit-native-parent", () => host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "G3 RunBpmn child-to-parent cancel overhead",
            state = new
            {
                variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new
                {
                    nodeId = "run-bpmn", activityVersionId = activity!["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
                    inputs = new[] { new { referenceKey = "Model", value = new { value = published["sourceReferenceId"]!.GetValue<string>(), expressionType = "Literal" } } }
                }
            }
        }));
        var publication = await StepAsync("publish-native-parent", () =>
            host.PostAsync($"/publishing/workflows/{native["version"]!["id"]!.GetValue<string>()}/publish", new { }));

        var started = await StepAsync("execute-parent-admit-child", () =>
            host.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
                new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() }));
        var execution = started["workflowExecutionId"]!.GetValue<string>();
        var nativePath = "/runtime/workflows/instances/" + execution;

        string? modelId = null;
        await StepVoidAsync("wait-cross-model-human", async () =>
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            JsonNode view = null!;
            while (modelId is null)
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "The actual RunBpmn native wait did not publish its model invocation.");
                view = await host.GetAsync(nativePath);
                Assert.Empty(view["incidents"]!.AsArray());
                if (view["activities"]!.AsArray().SingleOrDefault(item => item!["activityType"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn") is { } root)
                {
                    var detail = await host.GetAsync(nativePath + "/activity-executions/" + root["activityExecutionId"]!.GetValue<string>());
                    if (detail["bookmarks"] is JsonArray waits && waits.Count == 1) modelId = waits[0]!["stimulusHash"]!.GetValue<string>();
                }

                if (modelId is null) await Task.Delay(100);
            }

            var modelPath = "/bpmn/executions/" + modelId;
            while (await host.FindAsync(modelPath) is null)
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "The real native source did not admit its model child.");
                await Task.Delay(100);
            }

            Assert.Equal("Running", (await host.GetAsync(modelPath))["status"]!.GetValue<string>());
            Assert.Equal("Running", (await host.GetAsync(nativePath))["instance"]!["status"]!.GetValue<string>());
        });

        var modelPath = "/bpmn/executions/" + modelId;
        var work = (await host.GetAsync(modelPath + "/work"))["items"]!.AsArray();
        var waiting = Assert.Single(work)!;
        var cancel = new { requestId = "g3-cancel-embedded-child", reason = "Operator cancelled the embedded model." };
        host.HoldNativeParentResume();
        var cancelling = host.PostAsync(modelPath + "/cancel", cancel);
        var httpCancelApplied = 1;
        await StepVoidAsync("operator-cancel-child-before-parent-consume", async () =>
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (true)
            {
                var pendingModel = await host.GetAsync(modelPath);
                var pendingNative = await host.GetAsync(nativePath);
                if (pendingModel["status"]!.GetValue<string>() == "TerminalPendingSettlement" &&
                    pendingModel["result"]!["status"]!.GetValue<string>() == "Cancelled" &&
                    pendingNative["instance"]!["status"]!.GetValue<string>() == "Running")
                    break;
                Assert.True(DateTimeOffset.UtcNow < deadline, "The child model cancel did not reach a terminal result while the native parent was still waiting.");
                await Task.Delay(100);
            }
        });

        await StepVoidAsync("kill-before-parent-consume", async () =>
        {
            await host.StopAsync();
            try { await cancelling; }
            catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException or IOException) { }
            var pendingPhysical = await host.ReadEmbeddingAsync(modelId!, execution);
            Assert.DoesNotContain(pendingPhysical["sources"]!.AsArray(),
                item => item!["intent"]!["kind"]!.GetValue<string>() == "Elsa.Bpmn.CancelEmbedded.v1");
        });

        host.ReleaseNativeParentResumeHold();
        await StepVoidAsync("restart-host", async () =>
        {
            await host.RestartAsync();
            await host.LoginAsync();
        });
        await StepVoidAsync("parent-consumes-Cancelled", async () =>
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            JsonNode nativeFinal = null!;
            JsonNode final = null!;
            while (true)
            {
                var settled = await host.ReadEmbeddingAsync(modelId!, execution);
                nativeFinal = await host.GetAsync(nativePath);
                final = await host.GetAsync(modelPath);
                if (final["status"]!.GetValue<string>() == "TerminalSettled" && nativeFinal["instance"]!["status"]!.GetValue<string>() == "Cancelled" &&
                    settled["sources"]!.AsArray().Count >= 2 && settled["sources"]!.AsArray().All(item => item!["status"]!.GetValue<int>() == 2))
                    break;
                Assert.True(DateTimeOffset.UtcNow < deadline, "The restarted host did not map the child Cancelled result to exactly one native parent Cancel.");
                await Task.Delay(100);
            }

            var cancelledRun = Assert.Single(nativeFinal["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!;
            Assert.Equal("Cancelled", cancelledRun["status"]!.GetValue<string>());
            Assert.DoesNotContain(cancelledRun["outcomeNames"]?.AsArray() ?? [], name => name!.GetValue<string>() == "Done");
        });

        var replayed = await host.PostAsync(modelPath + "/cancel", cancel);
        Assert.True(replayed["replay"]!.GetValue<bool>());
        var httpCancelReplayed = 1;
        await host.PostAsync(modelPath + "/work/" + waiting["invocationId"]!.GetValue<string>() + "/complete",
            new { requestId = "g3-late-embedded", attemptId = waiting["attemptId"]!.GetValue<string>() }, HttpStatusCode.Conflict);
        var lateCancel = await host.PostAsync(modelPath + "/cancel",
            new { requestId = "g3-late-child-cancel", reason = "too late" }, HttpStatusCode.Conflict);
        Assert.Equal("execution-terminal", lateCancel["result"]!["code"]!.GetValue<string>());
        var httpCancelConflicted = 1;

        await host.StopAsync();
        Assert.True(steps.Any(step => step.Counters.RoundTrips > 0),
            "EF command logs were not captured. The G3 harness expects Information Microsoft.EntityFrameworkCore.Database.Command lines containing 'Executed DbCommand'.");
        var logTotal = new PersistenceCounters(
            steps.Sum(step => step.Counters.RoundTrips),
            steps.Sum(step => step.Counters.RuntimeRoundTrips),
            steps.Sum(step => step.Counters.Writes),
            steps.Sum(step => step.Counters.RuntimeWrites),
            steps.Sum(step => step.Counters.TransactionsStarted),
            steps.Sum(step => step.Counters.TransactionsCommitted),
            steps.Sum(step => step.Counters.TransactionsRolledBack),
            steps.Sum(step => step.Counters.LeaseOps),
            steps.Sum(step => step.Counters.FenceOps),
            steps.Sum(step => step.Counters.SchedulerCancelCommands),
            steps.Sum(step => step.Counters.CancelActivityScopeCommands),
            steps.Sum(step => step.Counters.CancelEmbeddedMentions),
            steps.Sum(step => step.Counters.EfCommandDurationMs));
        var sqlite = await RuntimeSqliteCancelLedger.ReadAsync(host.RuntimeDatabase, httpCancelApplied, httpCancelReplayed,
            httpCancelConflicted, nativeRunBpmnCancelled: 1);
        var issuedFromLogs = logTotal.SchedulerCancelCommands;
        // Parameterized EF SQL does not include ContentJson, so Cancel-kind is counted from durable SQLite.
        var issued = sqlite.SchedulerCancelCommands;
        var settledSources = (await host.ReadEmbeddingAsync(modelId!, execution))["sources"]!.AsArray();
        var settledCancelEmbedded = settledSources.Count(item => item!["intent"]!["kind"]!.GetValue<string>() == "Elsa.Bpmn.CancelEmbedded.v1");

        // Agreed #4028 semantics: the operator cancel is applied once to the model; the native parent observes
        // exactly one Cancel mapping. CancelEmbedded is forbidden while the parent is still waiting (asserted at
        // kill). A later CancelEmbedded after the parent is already Cancelled is parent-owned teardown and is
        // recorded, not treated as the child commanding the parent.
        Assert.Equal(1, sqlite.HttpCancelApplied);
        Assert.Equal(1, sqlite.NativeRunBpmnCancelled);
        Assert.Equal(1, issued);

        var receipt = new
        {
            journey = "RunBpmnChildCancelTests / #4028 child-to-parent cancel",
            environment,
            steps = steps.Select(step => new
            {
                step.Step,
                latencyMs = step.LatencyMs,
                roundTrips = step.Counters.RoundTrips,
                runtimeRoundTrips = step.Counters.RuntimeRoundTrips,
                writes = step.Counters.Writes,
                runtimeWrites = step.Counters.RuntimeWrites,
                transactionsStarted = step.Counters.TransactionsStarted,
                transactionsCommitted = step.Counters.TransactionsCommitted,
                transactionsRolledBack = step.Counters.TransactionsRolledBack,
                leaseOps = step.Counters.LeaseOps,
                fenceOps = step.Counters.FenceOps,
                schedulerCancelCommands = step.Counters.SchedulerCancelCommands,
                cancelActivityScopeCommands = step.Counters.CancelActivityScopeCommands,
                efCommandDurationMs = Math.Round(step.Counters.EfCommandDurationMs, 1)
            }),
            totals = new
            {
                roundTrips = logTotal.RoundTrips,
                runtimeRoundTrips = logTotal.RuntimeRoundTrips,
                writes = logTotal.Writes,
                runtimeWrites = logTotal.RuntimeWrites,
                transactionsStarted = logTotal.TransactionsStarted,
                transactionsCommitted = logTotal.TransactionsCommitted,
                transactionsRolledBack = logTotal.TransactionsRolledBack,
                leaseOps = logTotal.LeaseOps,
                fenceOps = logTotal.FenceOps,
                efCommandDurationMs = Math.Round(logTotal.EfCommandDurationMs, 1)
            },
            cancelCommands = new
            {
                issued,
                issuedFromLogs,
                issuedFromSqlite = sqlite.SchedulerCancelCommands,
                cancelActivityScopeCommands = Math.Max(logTotal.CancelActivityScopeCommands, sqlite.CancelActivityScopeCommands),
                cancelEmbeddedIntentsAfterSettle = settledCancelEmbedded,
                cancelEmbeddedMentionsInEfLogs = logTotal.CancelEmbeddedMentions,
                httpCancelApplied = sqlite.HttpCancelApplied,
                httpCancelReplayed = sqlite.HttpCancelReplayed,
                httpCancelConflicted = sqlite.HttpCancelConflicted,
                nativeRunBpmnCancelled = sqlite.NativeRunBpmnCancelled
            },
            limits = new[]
            {
                "SQLite Owned Workbench, one host. No PostgreSQL/SQL Server/MySQL cell.",
                "Round-trips/writes include every EF command the Workbench process logged (runtime, design, identity). runtimeRoundTrips/runtimeWrites are elsa_runtime_* only.",
                "Cancel-kind work items are counted from durable SQLite ContentJson; parameterized EF SQL does not include the payload.",
                "recoverySweepSeconds=0.2 is part of the G2 journey, so sweep/timer commands can land in waiting steps.",
                "No budget, target, speedup or throughput claim. Retired benchmark gates (B01–T04 / #2892) are not restored.",
                "Third-restart retention is already proved by RunBpmnChildCancelTests and is not re-measured here."
            }
        };
        var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        output.WriteLine(json);
        var evidence = Environment.GetEnvironmentVariable("ELSA_G3_EVIDENCE_DIRECTORY");
        if (evidence is not null)
        {
            Directory.CreateDirectory(evidence);
            await File.WriteAllTextAsync(Path.Combine(evidence, "g3-child-cancel.json"), json + Environment.NewLine);
        }
    }
}
