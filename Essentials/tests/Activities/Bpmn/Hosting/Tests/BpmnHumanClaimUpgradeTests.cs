using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// Claim-required work in the production Workbench: one owner survives an actual process restart, a second
/// seeded subject cannot claim or complete it, and the owner's completion replays across another restart.
/// </summary>
public sealed class BpmnHumanClaimUpgradeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_claim_owner_survives_restart_and_a_second_subject_cannot_take_or_complete_the_work()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true);
        await host.LoginAsync();
        var process = new BpmnProcessDefinition("claimed-review", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("review", BpmnElementTypes.UserTask, bindingRef: "review-work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-review", "start", "review"), new("to-end", "review", "end")]);
        var definition = new BpmnModelDefinition(6, JsonSerializer.SerializeToElement(process, Web), ["review-work"], null, null, null, ["review-work"]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-claimed", definition });
        Assert.Equal(BpmnModelProtocol.ClaimsProfile, published["profile"]!.GetValue<string>());
        var started = await host.PostAsync("/bpmn/executions", new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>(), requestId = "start-claimed" });
        var executionId = started["executionId"]!.GetValue<string>();
        var path = "/bpmn/executions/" + Uri.EscapeDataString(executionId);
        var work = Assert.Single((await host.GetAsync(path + "/work"))["items"]!.AsArray())!;
        Assert.True(work["claimRequired"]!.GetValue<bool>()); Assert.Null(work["claim"]);
        var invocation = path + "/work/" + Uri.EscapeDataString(work["invocationId"]!.GetValue<string>());
        var attemptId = work["attemptId"]!.GetValue<string>();

        var legacy = await host.PostAsync(invocation + "/complete", new { requestId = "legacy", attemptId }, HttpStatusCode.Conflict);
        Assert.Equal("claim-required", legacy["result"]!["code"]!.GetValue<string>());
        var claimed = await host.PostAsync(invocation + "/claim", new { requestId = "claim-admin", attemptId });
        var claimId = claimed["result"]!["claimId"]!.GetValue<string>();
        var owner = (await host.GetAsync(path + "/work"))["items"]![0]!["claim"]!;
        Assert.Equal(claimId, owner["claimId"]!.GetValue<string>());
        var ownerSubject = owner["claimantId"]!.GetValue<string>();
        var claimedView = await host.GetAsync(path);

        // An actual process restart, seeding a second administrator with its own subject.
        var firstProcess = host.ProcessId;
        await host.StopAsync();
        var claimedSnapshot = await host.SnapshotAsync("claimed-runtime.sqlite.gz", "claims");
        var claimedDesign = await host.SnapshotAsync("claimed-design.sqlite.gz", "claims", design: true);
        await host.SeedAdministratorAsync("operator");
        await host.RestartAsync();
        Assert.NotEqual(firstProcess, host.ProcessId);
        await host.LoginAsync("operator");
        Assert.True(JsonNode.DeepEquals(claimedView, await host.GetAsync(path)));
        var recovered = Assert.Single((await host.GetAsync(path + "/work"))["items"]!.AsArray())!;
        Assert.Equal(claimId, recovered["claim"]!["claimId"]!.GetValue<string>());
        Assert.Equal(ownerSubject, recovered["claim"]!["claimantId"]!.GetValue<string>());
        var contender = await host.PostAsync(invocation + "/claim", new { requestId = "claim-operator", attemptId }, HttpStatusCode.Conflict);
        Assert.Equal("already-claimed", contender["result"]!["code"]!.GetValue<string>());
        var foreign = await host.PostAsync(invocation + "/complete-claimed", new { requestId = "operator-completes", attemptId, claimId }, HttpStatusCode.Conflict);
        Assert.Equal("foreign-claim", foreign["result"]!["code"]!.GetValue<string>());
        // The owner's request id cannot be reused by another subject with different content.
        await host.PostAsync(invocation + "/claim", new { requestId = "claim-admin", attemptId }, HttpStatusCode.Conflict);
        Assert.True(JsonNode.DeepEquals(claimedView, await host.GetAsync(path)));

        await host.LoginAsync();
        var completion = new { requestId = "admin-completes", attemptId, claimId };
        var accepted = await host.PostAsync(invocation + "/complete-claimed", completion);
        Assert.Equal("TerminalSettled", accepted["result"]!["lifecycle"]!.GetValue<string>());
        var completed = await host.GetAsync(path);
        Assert.Equal("Completed", completed["result"]!["status"]!.GetValue<string>());
        await host.RestartAsync();
        await host.LoginAsync();
        var replayed = await host.PostAsync(invocation + "/complete-claimed", completion);
        Assert.True(replayed["replay"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(accepted["result"], replayed["result"]));
        Assert.True(JsonNode.DeepEquals(completed, await host.GetAsync(path)));
        await host.StopAsync();
        await using (var database = new SqliteConnection($"Data Source={host.RuntimeDatabase};Mode=ReadOnly;Pooling=False"))
        {
            await database.OpenAsync();
            foreach (var table in new[] { "elsa_runtime_workflow_execution_state", "elsa_runtime_activity_execution_state", "elsa_runtime_bookmark_state" })
            {
                await using var command = database.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
                Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
            }
        }
        var completedSnapshot = await host.SnapshotAsync("completed-runtime.sqlite.gz", "claims");
        if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
        {
            var record = new { formatVersion = 1, sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"),
                binaries = host.DescribeBinaries(Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION")),
                executionId, invocationId = work["invocationId"]!.GetValue<string>(), attemptId, claimId, ownerSubject,
                legacy, claimed, contender, foreign, accepted, replayed, claimedView, completed,
                snapshots = new[] { claimedSnapshot, claimedDesign, completedSnapshot } };
            await File.WriteAllTextAsync(Path.Combine(evidence, "claims", "claims-host.json"),
                JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
        }
    }

    [Fact]
    public async Task Two_signed_in_subjects_racing_to_claim_produce_exactly_one_owner()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true);
        await host.StopAsync(); await host.SeedAdministratorAsync("operator"); await host.RestartAsync();
        await host.LoginAsync();
        var source = await PublishAsync(host, "race", timer: null);
        using var admin = await host.SignInSessionAsync("admin");
        using var operatorSession = await host.SignInSessionAsync("operator");
        for (var round = 0; round < 3; round++)
        {
            var (path, invocation, attemptId) = await StartAsync(host, source, "race-start-" + round);
            var results = await Task.WhenAll(
                admin.PostAsJsonAsync(invocation + "/claim", new { requestId = "admin-" + round, attemptId }),
                operatorSession.PostAsJsonAsync(invocation + "/claim", new { requestId = "operator-" + round, attemptId }));
            var outcomes = await Task.WhenAll(results.Select(async response => (response.StatusCode, Body: (await response.Content.ReadFromJsonAsync<JsonNode>())!)));
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], outcomes.Select(outcome => outcome.StatusCode).Order());
            var winner = Assert.Single(outcomes, outcome => outcome.StatusCode == HttpStatusCode.OK);
            Assert.Equal("already-claimed", Assert.Single(outcomes, outcome => outcome.StatusCode == HttpStatusCode.Conflict).Body["result"]!["code"]!.GetValue<string>());
            var claim = (await host.GetAsync(path + "/work"))["items"]![0]!["claim"]!;
            Assert.Equal(winner.Body["result"]!["claimId"]!.GetValue<string>(), claim["claimId"]!.GetValue<string>());
            Assert.Equal(2, (await host.GetAsync(path))["revision"]!.GetValue<long>());
            foreach (var response in results) response.Dispose();
        }
    }

    [Fact]
    public async Task Admission_off_keeps_claim_work_operable_and_interruptions_keep_their_first_winner()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true);
        await host.LoginAsync();
        var plain = await PublishAsync(host, "orders", timer: null);
        var timed = await PublishAsync(host, "timed", timer: "PT4S");
        var cancelFirst = await StartAsync(host, plain, "cancel-first");
        var claimFirst = await StartAsync(host, plain, "claim-first");
        // Accepted work stays operable after a real restart with new admission turned off.
        await host.StopAsync(); await host.SetAdmissionAsync(false); await host.RestartAsync(); await host.LoginAsync();
        Assert.False((await host.GetAsync("/bpmn/admission"))["admitNewExecutions"]!.GetValue<bool>());
        await host.PostAsync("/bpmn/executions", new { sourceReferenceId = plain, requestId = "refused-start" }, HttpStatusCode.ServiceUnavailable);

        // Cancellation before a claim: the claim finds the execution terminal.
        var cancelled = await host.PostAsync(cancelFirst.Path + "/cancel", new { requestId = "cancel", reason = "withdrawn" });
        Assert.Equal("Accepted", cancelled["result"]!["status"]!.GetValue<string>());
        var late = await host.PostAsync(cancelFirst.Invocation + "/claim", new { requestId = "late-claim", cancelFirst.AttemptId }, HttpStatusCode.Conflict);
        Assert.Equal("execution-terminal", late["result"]!["code"]!.GetValue<string>());

        // A claim and completion before cancellation: the cancellation finds the execution terminal.
        var claimId = (await host.PostAsync(claimFirst.Invocation + "/claim", new { requestId = "claim", claimFirst.AttemptId }))["result"]!["claimId"]!.GetValue<string>();
        await host.PostAsync(claimFirst.Invocation + "/complete-claimed", new { requestId = "complete", claimFirst.AttemptId, claimId });
        var lateCancel = await host.PostAsync(claimFirst.Path + "/cancel", new { requestId = "late-cancel", reason = "too late" }, HttpStatusCode.Conflict);
        Assert.Equal("execution-terminal", lateCancel["result"]!["code"]!.GetValue<string>());
        Assert.Equal("Completed", (await host.GetAsync(claimFirst.Path))["result"]!["status"]!.GetValue<string>());

        // Claimed completion before the duration timer: the timer arrives late and changes nothing.
        await host.StopAsync(); await host.SetAdmissionAsync(true); await host.RestartAsync(); await host.LoginAsync();
        var beatsTimer = await StartAsync(host, timed, "beats-timer");
        var beatsClaim = (await host.PostAsync(beatsTimer.Invocation + "/claim", new { requestId = "claim", beatsTimer.AttemptId }))["result"]!["claimId"]!.GetValue<string>();
        await host.PostAsync(beatsTimer.Invocation + "/complete-claimed", new { requestId = "complete", beatsTimer.AttemptId, claimId = beatsClaim });
        var completedBeforeTimer = await host.GetAsync(beatsTimer.Path);
        // The timer before claimed completion: the late completion finds the execution terminal.
        var losesToTimer = await StartAsync(host, timed, "loses-to-timer");
        var loserClaim = (await host.PostAsync(losesToTimer.Invocation + "/claim", new { requestId = "claim", losesToTimer.AttemptId }))["result"]!["claimId"]!.GetValue<string>();
        await Task.Delay(TimeSpan.FromSeconds(6));
        Assert.True(JsonNode.DeepEquals(completedBeforeTimer, await host.GetAsync(beatsTimer.Path)));
        var timedOut = await WaitTerminalAsync(host, losesToTimer.Path);
        Assert.Equal("Completed", timedOut["result"]!["status"]!.GetValue<string>());
        var lateComplete = await host.PostAsync(losesToTimer.Invocation + "/complete-claimed", new { requestId = "late-complete", losesToTimer.AttemptId, claimId = loserClaim }, HttpStatusCode.Conflict);
        Assert.Equal("execution-terminal", lateComplete["result"]!["code"]!.GetValue<string>());

        // Cancellation replay after a real restart; the same request id with other content conflicts.
        await host.RestartAsync(); await host.LoginAsync();
        var replayed = await host.PostAsync(cancelFirst.Path + "/cancel", new { requestId = "cancel", reason = "withdrawn" });
        Assert.True(replayed["replay"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(cancelled["result"], replayed["result"]));
        await host.PostAsync(cancelFirst.Path + "/cancel", new { requestId = "cancel", reason = "another reason" }, HttpStatusCode.Conflict);
    }

    private static async Task<string> PublishAsync(OwnedWorkbench host, string name, string? timer)
    {
        var elements = new List<BpmnElement> { new("start", BpmnElementTypes.StartEvent), new("review", BpmnElementTypes.UserTask, bindingRef: "review-work"), new("end", BpmnElementTypes.EndEvent) };
        var flows = new List<BpmnSequenceFlow> { new("to-review", "start", "review"), new("to-end", "review", "end") };
        if (timer is not null)
        {
            elements.AddRange([new("timeout", BpmnElementTypes.BoundaryEvent, bindingRef: "timeout-work", attachedToRef: "review", cancelActivity: true,
                eventDefinitions: [new(BpmnEventDefinitionTypes.Timer, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Interval] = timer })]),
                new("timed-out", BpmnElementTypes.EndEvent)]);
            flows.Add(new("timeout-end", "timeout", "timed-out"));
        }
        var definition = new BpmnModelDefinition(6, JsonSerializer.SerializeToElement(new BpmnProcessDefinition(name, Elements: elements, SequenceFlows: flows), Web),
            ["review-work"], null, timer is null ? null : ["timeout-work"], null, ["review-work"]);
        return (await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-" + name, definition }))["sourceReferenceId"]!.GetValue<string>();
    }

    private static async Task<(string Path, string Invocation, string AttemptId)> StartAsync(OwnedWorkbench host, string source, string requestId)
    {
        var started = await host.PostAsync("/bpmn/executions", new { sourceReferenceId = source, requestId });
        var path = "/bpmn/executions/" + Uri.EscapeDataString(started["executionId"]!.GetValue<string>());
        var work = Assert.Single((await host.GetAsync(path + "/work"))["items"]!.AsArray())!;
        return (path, path + "/work/" + Uri.EscapeDataString(work["invocationId"]!.GetValue<string>()), work["attemptId"]!.GetValue<string>());
    }

    private static async Task<JsonNode> WaitTerminalAsync(OwnedWorkbench host, string path)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            var view = await host.GetAsync(path);
            if (view["status"]!.GetValue<string>() == "TerminalSettled") return view;
            Assert.True(DateTimeOffset.UtcNow < deadline, "The duration timer did not settle the execution.");
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
