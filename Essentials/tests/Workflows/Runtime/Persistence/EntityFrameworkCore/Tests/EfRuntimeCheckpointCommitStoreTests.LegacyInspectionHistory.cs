using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Actual_prefeature_raw_inspection_checkpoint_replays_its_old_marker_with_history_enabled()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacyInspection");
        using var proof = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture, "provenance.json")));
        Assert.Empty(proof.RootElement.GetProperty("oldWorktreeTrackedDiff").EnumerateArray());
        Assert.Equal("074259b3565e046701acbce69613120d7bed5f64", proof.RootElement.GetProperty("oldSourceCommit").GetString());
        foreach (var file in proof.RootElement.GetProperty("files").EnumerateObject())
            Assert.Equal(file.Value.GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(fixture, file.Name)))));
        var oldFingerprint = (await File.ReadAllTextAsync(Path.Combine(fixture, "fingerprint.txt"))).Trim();
        var raw = ReadLegacyInspectionCheckpoint(await File.ReadAllTextAsync(Path.Combine(fixture, "checkpoint.json")));
        Assert.Null(raw.StateChanges.WorkflowExecution!.State.HistoryCapturePolicy); Assert.Null(raw.HistoryBatches);
        var value = Assert.Single(Assert.Single(raw.StateChanges.ActivityExecutionInspections).State.ValueSnapshots);
        Assert.Equal(42, value.Payload!.Value.GetInt32()); Assert.Equal("old-evidence", value.EvidenceId);
        // This is the actual hash retained by the old writer, not two hashes computed with the current serializer.
        Assert.Equal(oldFingerprint, RuntimeCheckpointCommitFingerprint.Compute(raw));
        var directory = Directory.CreateTempSubdirectory("elsa-old-inspection-replay-");
        try
        {
            var path = Path.Combine(directory.FullName, "runtime.sqlite");
            await using (var input = new GZipStream(File.OpenRead(Path.Combine(fixture, "runtime.sqlite.gz")), CompressionMode.Decompress))
            await using (var output = File.Create(path)) await input.CopyToAsync(output);
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=" + path + ";Pooling=False").Options);
            await context.Database.MigrateAsync();
            var markerBefore = await context.RuntimeCheckpointCommits.AsNoTracking().SingleAsync();
            var inspectionBefore = (await context.ActivityExecutionInspections.AsNoTracking().SingleAsync()).ContentJson;
            var workflowBefore = (await context.WorkflowExecutionStates.AsNoTracking().SingleAsync()).ContentJson;
            Assert.Equal((await File.ReadAllTextAsync(Path.Combine(fixture, "marker.json"))).TrimEnd(), markerBefore.ContentJson);
            Assert.Equal(oldFingerprint, markerBefore.Fingerprint);
            var access = new FixedAccessor("old-tenant"); var services = new ServiceCollection();
            services.AddSingleton<IWorkflowExecutionStateStore>(new EfWorkflowExecutionStateStore(context, access, new NoopContinuationCodec()));
            services.AddRuntimeHistory();
            await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
            var capture = Assert.Single(scope.ServiceProvider.GetServices<IRuntimeCheckpointCommitEnricher>());
            var enriched = await capture.EnrichAsync(raw); await capture.ValidateFinalAsync(enriched);
            Assert.Null(enriched.HistoryBatches); Assert.Equal(oldFingerprint, RuntimeCheckpointCommitFingerprint.Compute(enriched));
            await new EfRuntimeCheckpointCommitStore(context, access).CommitAsync(enriched, Decision());
            var markerAfter = await context.RuntimeCheckpointCommits.AsNoTracking().SingleAsync();
            Assert.Equal(markerBefore.ContentJson, markerAfter.ContentJson); Assert.Equal(markerBefore.Revision, markerAfter.Revision);
            Assert.Equal(inspectionBefore, (await context.ActivityExecutionInspections.AsNoTracking().SingleAsync()).ContentJson);
            Assert.Equal(workflowBefore, (await context.WorkflowExecutionStates.AsNoTracking().SingleAsync()).ContentJson);
            Assert.Empty(await context.RuntimeHistoryHeads.ToArrayAsync()); Assert.Empty(await context.RuntimeHistoryEvents.ToArrayAsync());
        }
        finally { directory.Delete(recursive: true); }
    }

    private static RuntimeCheckpointCommit ReadLegacyInspectionCheckpoint(string json)
    {
        // StateChangeSet has several public constructors and is an in-process envelope, not a general JSON
        // protocol. Decode this deliberately narrow exported source with current typed member deserializers.
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        var changes = root.GetProperty("stateChanges"); var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var name in new[] { "activityExecutions", "bookmarks", "durableValues", "incidents", "operational", "activityScopeCleanups", "workflowDispatches", "workflowDispatchCancellations", "consumedSchedulerWorkItems", "postCommitOutbox" })
            Assert.Empty(changes.GetProperty(name).EnumerateArray());
        foreach (var name in new[] { "admissions", "acceptances", "cancellations" }) Assert.Empty(changes.GetProperty("backgroundActivityWork").GetProperty(name).EnumerateArray());
        Assert.Equal(JsonValueKind.Null, changes.GetProperty("scheduler").ValueKind); Assert.Equal(JsonValueKind.Null, changes.GetProperty("alterationJobTerminalChange").ValueKind);
        return new(root.GetProperty("commitId").GetString()!, root.GetProperty("checkpoint").Deserialize<RuntimeCheckpoint>(options)!,
            new(changes.GetProperty("workflowExecution").Deserialize<RuntimeStateChange<WorkflowExecutionState>>(options), null, [], [], [], [], [], null,
                changes.GetProperty("activityExecutionInspections").Deserialize<RuntimeStateChange<ActivityExecutionInspectionProjection>[]>(options), null, null, null),
            root.GetProperty("postCommitIntents").Deserialize<RuntimePostCommitIntent[]>(options)!, root.GetProperty("metadata").Deserialize<Dictionary<string, string>>(options)!);
    }
}
