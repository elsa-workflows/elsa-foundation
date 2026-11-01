using System.Net;
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
}
