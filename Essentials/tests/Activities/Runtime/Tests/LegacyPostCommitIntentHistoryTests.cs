using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class LegacyPostCommitIntentHistoryTests
{
    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Actual_old_nonempty_intent_and_native_marker_keep_their_original_hashes_when_history_is_composed()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacyPostCommitIntent");
        using var provenance = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "provenance.json")));
        Assert.Empty(provenance.RootElement.GetProperty("oldWorktreeTrackedDiff").EnumerateArray());
        Assert.Equal("074259b3565e046701acbce69613120d7bed5f64", provenance.RootElement.GetProperty("oldSourceCommit").GetString());
        foreach (var file in provenance.RootElement.GetProperty("files").EnumerateObject())
            Assert.Equal(file.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(fixture, file.Name)))));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var intent = JsonSerializer.Deserialize<RuntimePostCommitIntent>(await File.ReadAllTextAsync(Path.Combine(fixture, "intent.json")), options)!;
        Assert.Null(intent.HistoryCausation);
        Assert.Equal((await File.ReadAllTextAsync(Path.Combine(fixture, "intent-fingerprint.txt"))).Trim(), RuntimeCheckpointCommitFingerprint.ComputeIntent(intent));
        Assert.False(JsonDocument.Parse(JsonSerializer.Serialize(intent, options)).RootElement.TryGetProperty("historyCausation", out _));
        var commit = DecodeFixture(await File.ReadAllTextAsync(Path.Combine(fixture, "checkpoint.json")), options);
        var nativeFingerprint = (await File.ReadAllTextAsync(Path.Combine(fixture, "fingerprint.txt"))).Trim();
        using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "marker.json")));
        Assert.Equal(nativeFingerprint, marker.RootElement.GetProperty("fingerprint").GetString());
        Assert.Equal(nativeFingerprint, RuntimeCheckpointCommitFingerprint.Compute(commit));
        var storedIntent = Assert.Single(commit.StateChanges.PostCommitOutbox).State.Intent;
        Assert.Null(storedIntent.HistoryCausation); Assert.True(intent.IsEquivalentTo(storedIntent));
        var native = new InMemoryWorkflowExecutionStateStore(); await native.SaveAsync(commit.StateChanges.WorkflowExecution!.State);
        var services = new ServiceCollection(); services.AddSingleton<IWorkflowExecutionStateStore>(native); services.AddRuntimeHistory();
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var capture = Assert.Single(scope.ServiceProvider.GetServices<IRuntimeCheckpointCommitEnricher>(), item => item is IRuntimeHistoryCaptureSupport);
        var enriched = await capture.EnrichAsync(commit); await capture.ValidateFinalAsync(enriched);
        Assert.Same(commit, enriched); Assert.Null(enriched.HistoryBatches);
        Assert.Null(Assert.Single(enriched.StateChanges.PostCommitOutbox).State.Intent.HistoryCausation);
        Assert.Equal(nativeFingerprint, RuntimeCheckpointCommitFingerprint.Compute(enriched));
    }

    private static RuntimeCheckpointCommit DecodeFixture(string json, JsonSerializerOptions options)
    {
        // The in-process StateChangeSet has multiple constructors. Decode the actual old raw member values;
        // assert the remaining source channels empty instead of inventing an old serializer golden.
        using var document = JsonDocument.Parse(json); var root = document.RootElement; var changes = root.GetProperty("stateChanges");
        foreach (var name in new[] { "activityExecutions", "bookmarks", "durableValues", "incidents", "operational", "activityScopeCleanups", "workflowDispatches", "workflowDispatchCancellations", "consumedSchedulerWorkItems" })
            Assert.Empty(changes.GetProperty(name).EnumerateArray());
        foreach (var name in new[] { "admissions", "acceptances", "cancellations" }) Assert.Empty(changes.GetProperty("backgroundActivityWork").GetProperty(name).EnumerateArray());
        Assert.Equal(JsonValueKind.Null, changes.GetProperty("scheduler").ValueKind); Assert.Equal(JsonValueKind.Null, changes.GetProperty("alterationJobTerminalChange").ValueKind);
        return new(root.GetProperty("commitId").GetString()!, root.GetProperty("checkpoint").Deserialize<RuntimeCheckpoint>(options)!,
            new(changes.GetProperty("workflowExecution").Deserialize<RuntimeStateChange<WorkflowExecutionState>>(options), null, [], [], [], [], [], null,
                changes.GetProperty("activityExecutionInspections").Deserialize<RuntimeStateChange<ActivityExecutionInspectionProjection>[]>(options),
                changes.GetProperty("postCommitOutbox").Deserialize<RuntimeStateChange<RuntimePostCommitOutboxItem>[]>(options), null, null),
            root.GetProperty("postCommitIntents").Deserialize<RuntimePostCommitIntent[]>(options)!, root.GetProperty("metadata").Deserialize<Dictionary<string, string>>(options)!);
    }
}
