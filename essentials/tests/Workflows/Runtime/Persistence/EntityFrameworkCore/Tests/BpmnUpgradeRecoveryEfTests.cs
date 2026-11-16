using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells.Lifecycle;
using Elsa.Activities.Bpmn.Tests;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Consumes frozen databases from the preview host; this test never invokes their old writer.</summary>
public sealed partial class BpmnUpgradeRecoveryEfTests
{
    [Theory]
    [InlineData("resolved-race")]
    [InlineData("queued-loser")]
    [InlineData("queued-late-fault")]
    [InlineData("parallel-mi")]
    [InlineData("listener-rearm")]
    [InlineData("deferred-fault")]
    public Task Released_library_host_recovers_actual_preview_host_checkpoint(string scenario) => RecoverAsync(scenario);

    [Theory]
    [InlineData("missing-history")]
    [InlineData("iteration-mismatch")]
    [InlineData("element-mismatch")]
    [InlineData("missing-graph-element")]
    public Task Incomplete_or_conflicting_durable_identity_refuses_dispatch_and_preserves_original_state(string defect) =>
        RecoverAsync("resolved-race", defect);

    [Fact]
    public Task Active_terminal_callback_refuses_conflicting_element_identity_from_actual_legacy_mi_checkpoint() =>
        RecoverAsync("parallel-mi", "active-element-mismatch");

    private static async Task RecoverAsync(string scenario, string? defect = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa-bpmn-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = Path.Combine(directory, "upgraded.db");
            await CopyCheckpointAsync(scenario, database);

            await using var host = await OpenHostAsync(database, Enumerable.Range(0, 100).Select(index => $"upgraded-activity-{index}"));
            var store = host.Provider.GetRequiredService<IActivityExecutionStateStore>();
            var before = await store.ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
            var parent = Assert.Single(before, state => state.Execution.ExecutableNodeId == BpmnRuntimeFixture.ProcessNodeId);
            Assert.Equal(1, parent.PrivateState!.StateVersion);
            var original = parent.PrivateState.Value.InlineValue!.Value;
            Assert.True(original.TryGetProperty("activeChildren", out _));
            Assert.False(original.TryGetProperty("activeWork", out _));
            var directSubstitution = original.Deserialize<global::Bpmn.Model.State.BpmnExecutionState>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Empty(directSubstitution.ActiveWork);
            if (scenario is "parallel-mi" or "listener-rearm")
                Assert.NotEmpty(original.GetProperty("activeChildren").EnumerateArray());

            string? damagedHandle = null;
            if (defect is not null)
            {
                if (defect == "missing-graph-element")
                {
                    var payload = JsonNode.Parse(original.GetRawText())!.AsObject();
                    payload["tokens"]!.AsArray().Select(token => token!.AsObject())
                        .Single(token => token["status"]!.GetValue<int>() == 4)["atElementId"] = "missing-element";
                    original = JsonSerializer.SerializeToElement(payload);
                    parent = parent with { PrivateState = new ActivityPrivateState(parent.InvocationId, parent.PrivateState.StateVersion,
                        ValueEnvelope.Inline(parent.PrivateState.Value.Type, original, parent.PrivateState.Value.Policy),
                        parent.PrivateState.ProducedByAttemptId, parent.PrivateState.CommittedAt) };
                    await store.SaveAsync(parent);
                }
                else
                {
                    var loser = defect == "active-element-mismatch"
                        ? before.Where(state => state.Execution.ExecutableNodeId == "node-work").OrderBy(state => state.InvocationId, StringComparer.Ordinal).First()
                        : Assert.Single(before, state => state.Execution.ExecutableNodeId == "node-loser");
                    damagedHandle = loser.InvocationId;
                    var damaged = defect switch
                    {
                        "missing-history" => loser with { ParentActivityExecutionId = "missing-parent-history" },
                        "iteration-mismatch" => loser with { Provenance = loser.Provenance with { IterationId = "wrong-iteration" } },
                        _ => loser with { Provenance = loser.Provenance with { Metadata = new Dictionary<string, string>(loser.Provenance.Metadata)
                            { ["bpmn.elementId"] = "wrong-element" } } }
                    };
                    await store.SaveAsync(damaged);
                }
            }

            await host.Provider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new RuntimeResumptionSweepRequest());
            if (scenario == "listener-rearm")
                await ResumeAsync(host, "node-listener");
            if (scenario != "deferred-fault")
            {
                var continuing = await store.FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, parent.InvocationId);
                Assert.Equal(1, continuing!.PrivateState!.StateVersion);
                Assert.True(continuing.PrivateState.Value.InlineValue!.Value.TryGetProperty("activeChildren", out _));
                Assert.False(continuing.PrivateState.Value.InlineValue.Value.TryGetProperty("activeWork", out _));
                var waitingNode = scenario == "parallel-mi" ? "node-work" : scenario == "listener-rearm" ? "node-hold" : "node-final";
                var bookmarks = (await host.BookmarksAsync()).Where(bookmark => bookmark.ExecutableNodeId == waitingNode).ToArray();
                Assert.Equal(scenario == "parallel-mi" ? 3 : 1, bookmarks.Length);
                if (scenario == "parallel-mi")
                    Assert.Equal(3, bookmarks.Select(bookmark => bookmark.ActivityExecutionId).Distinct().Count());
                if (scenario == "listener-rearm" && defect is null)
                {
                    Assert.True(continuing.PrivateState.CommittedAt > parent.PrivateState.CommittedAt);
                    AssertLegacyWireContract(continuing, original);
                }
                var resumedCount = 0;
                foreach (var bookmark in defect == "active-element-mismatch" ? bookmarks.Where(bookmark => bookmark.ActivityExecutionId == damagedHandle) : bookmarks)
                {
                    await host.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new { eventName = "continue" }));
                    if (scenario == "parallel-mi" && defect is null && ++resumedCount == 1)
                    {
                        var updated = await store.FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, parent.InvocationId);
                        Assert.True(updated!.PrivateState!.CommittedAt > parent.PrivateState.CommittedAt);
                        Assert.True(updated.PrivateState.Value.InlineValue!.Value.GetProperty("sequence").GetInt32() > original.GetProperty("sequence").GetInt32());
                        AssertLegacyWireContract(updated, original);
                    }
                }
            }
            var workflow = await host.Provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(WorkflowExecutionHarness.WorkflowExecutionId);
            var after = await store.ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
            var resumed = Assert.Single(after, state => state.InvocationId == parent.InvocationId);
            Assert.True(workflow!.Status == (scenario == "deferred-fault" || defect is not null ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed),
                JsonSerializer.Serialize(after.Select(state => new { state.InvocationId, state.Status, state.Fault })));
            if (defect is not null)
            {
                Assert.Contains("BPMN", resumed.Fault!.Message, StringComparison.Ordinal);
                Assert.Equal(typeof(Elsa.Activities.Bpmn.Exceptions.BpmnHostException).FullName, resumed.Fault.ExceptionType);
                Assert.Equal(original.GetRawText(), resumed.PrivateState!.Value.InlineValue!.Value.GetRawText());
                Assert.Equal(before.Count, after.Count);
            }
            if (scenario == "deferred-fault")
            {
                Assert.Equal("bpmn.gateway.no-flow-selected", resumed.Fault!.Code);
                Assert.Equal(original.GetProperty("pendingFault").GetProperty("message").GetString(), resumed.Fault.Message);
                Assert.Equal(1, resumed.PrivateState!.StateVersion);
                Assert.True(resumed.PrivateState.Value.InlineValue!.Value.TryGetProperty("activeChildren", out _));
                AssertLegacyWireContract(resumed, original);
            }
            if (defect == "active-element-mismatch")
            {
                // Refusal retains the other accepted MI waits for repair; it must not advance or
                // destructively clean their continuation state merely because one callback is corrupt.
                var remaining = await host.BookmarksAsync();
                Assert.Equal(before.Where(child => child.Execution.ExecutableNodeId == "node-work" && child.InvocationId != damagedHandle)
                    .SelectMany(child => child.BookmarkIds).Order(StringComparer.Ordinal),
                    remaining.Select(bookmark => bookmark.BookmarkId).Order(StringComparer.Ordinal));
            }
            else
                Assert.Empty(await host.BookmarksAsync());
            await host.Provider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new RuntimeResumptionSweepRequest());
            var replay = await store.ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
            Assert.Equal(after.Count, replay.Count);
            Assert.Equal(resumed.Status, Assert.Single(replay, state => state.InvocationId == parent.InvocationId).Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("queued-loser")]
    [InlineData("queued-late-fault")]
    public async Task Actual_legacy_queued_loser_callback_is_not_re_evaluated_after_pruning_and_provider_restart(string scenario)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa-bpmn-replay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = Path.Combine(directory, "upgraded.db");
            await CopyCheckpointAsync(scenario, database);
            var observations = new CallbackObservations();
            RuntimeSchedulerWorkItem persistedWork;
            string parentId;
            string loserId;
            string stablePrivateState;
            string stableFinalBookmark;
            int stableChildCount;
            string? originalFault = null;
            string? stableIncident = null;
            string? incidentId = null;
            string? originalIncidentCore = null;
            await using (var first = await OpenHostAsync(database, Enumerable.Range(0, 100).Select(index => $"replay-first-{index}"), observations))
            {
                var states = first.Provider.GetRequiredService<IActivityExecutionStateStore>();
                var before = await states.ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
                var parent = Assert.Single(before, child => child.Execution.ExecutableNodeId == BpmnRuntimeFixture.ProcessNodeId);
                var loser = Assert.Single(before, child => child.Execution.ExecutableNodeId == "node-loser");
                parentId = parent.InvocationId;
                loserId = loser.InvocationId;
                if (scenario == "queued-late-fault")
                {
                    Assert.Equal(ActivityExecutionStatus.Faulted, loser.Status);
                    Assert.Equal("TEST-FAULT", loser.Fault!.Code);
                    originalFault = JsonSerializer.Serialize(loser.Fault);
                    incidentId = Assert.Single(loser.IncidentIds);
                    var originalIncident = await first.Provider.GetRequiredService<IIncidentStateStore>()
                        .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, incidentId);
                    Assert.True(originalIncident!.IsBlocking);
                    Assert.Null(originalIncident.ResolutionOutcome);
                    originalIncidentCore = IncidentCore(originalIncident);
                }
                var tokenId = loser.Provenance.ExecutionPathId;
                Assert.Contains(parent.PrivateState!.Value.InlineValue!.Value.GetProperty("tokens").EnumerateArray(),
                    token => token.GetProperty("tokenId").GetString() == tokenId && token.GetProperty("status").GetInt32() == 4);
                await using (var scope = first.Provider.CreateAsyncScope())
                {
                    var page = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                        .ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId, 100));
                    Assert.Null(page.NextContinuationToken);
                    persistedWork = Assert.Single(page.Items, work =>
                        work.CommandKind == WorkflowExecutionCommandKind.CompleteActivity && work.Payload is { } body &&
                        body.Deserialize<RuntimeCompleteActivityCommandPayload>()?.CompletedChildActivityExecutionId == loserId);
                }
                await first.Provider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new RuntimeResumptionSweepRequest());
                var after = await states.ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
                var continuing = Assert.Single(after, child => child.InvocationId == parentId);
                Assert.Equal(ActivityExecutionStatus.Running, continuing.Status);
                stablePrivateState = continuing.PrivateState!.Value.InlineValue!.Value.GetRawText();
                Assert.DoesNotContain(continuing.PrivateState.Value.InlineValue.Value.GetProperty("tokens").EnumerateArray(),
                    token => token.GetProperty("tokenId").GetString() == tokenId);
                Assert.Equal(persistedWork.WorkItemId, Assert.Single(after, child => child.InvocationId == loserId)
                    .Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
                stableFinalBookmark = Assert.Single(await first.BookmarksAsync(), bookmark => bookmark.ExecutableNodeId == "node-final").BookmarkId;
                stableChildCount = after.Count;
                if (incidentId is not null)
                {
                    Assert.Equal(originalFault, JsonSerializer.Serialize(Assert.Single(after, child => child.InvocationId == loserId).Fault));
                    var absorbed = await first.Provider.GetRequiredService<IIncidentStateStore>()
                        .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, incidentId);
                    Assert.False(absorbed!.IsBlocking);
                    Assert.Equal(IncidentResolutionActionKinds.AbsorbFault, absorbed.ResolutionOutcome!.ActionKind);
                    Assert.Equal(IncidentResolutionSystemSources.StructuralFaultAbsorption, absorbed.ResolutionOutcome.SystemSource);
                    Assert.Equal("bpmn.work.late-fault-absorbed", absorbed.Metadata.GetValueOrDefault(RuntimeMetadataKeys.FaultAbsorptionReason));
                    Assert.Equal(parentId, absorbed.Metadata.GetValueOrDefault(RuntimeMetadataKeys.FaultAbsorbedBy));
                    Assert.Equal(originalIncidentCore, IncidentCore(absorbed));
                    stableIncident = JsonSerializer.Serialize(absorbed);
                }
                Assert.Equal(1, observations.TerminalHandles.Count(handle => handle == loserId));
            }
            await using (var restarted = await OpenHostAsync(database, [], observations))
            {
                var calls = observations.TerminalHandles.Count;
                await using (var scope = restarted.Provider.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(persistedWork);
                var queued = await restarted.Provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                    .ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId, 100));
                Assert.Equal(persistedWork.WorkItemId, Assert.Single(queued.Items).WorkItemId);
                await restarted.Provider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new RuntimeResumptionSweepRequest());
                var states = restarted.Provider.GetRequiredService<IActivityExecutionStateStore>();
                var afterReplay = await states.ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
                Assert.Equal(stableChildCount, afterReplay.Count);
                var runningParent = Assert.Single(afterReplay, child => child.InvocationId == parentId);
                Assert.Equal(ActivityExecutionStatus.Running, runningParent.Status);
                Assert.Equal(stablePrivateState, runningParent.PrivateState!.Value.InlineValue!.Value.GetRawText());
                Assert.Equal(persistedWork.WorkItemId, Assert.Single(afterReplay, child => child.InvocationId == loserId)
                    .Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
                Assert.Equal(calls, observations.TerminalHandles.Count);
                Assert.Empty((await restarted.Provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                    .ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId, 100))).Items);
                Assert.Empty(await restarted.Provider.GetRequiredService<IWorkflowSchedulerPoisonStore>()
                    .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
                if (incidentId is not null)
                {
                    Assert.Equal(originalFault, JsonSerializer.Serialize(Assert.Single(afterReplay, child => child.InvocationId == loserId).Fault));
                    Assert.Equal(stableIncident, JsonSerializer.Serialize(await restarted.Provider.GetRequiredService<IIncidentStateStore>()
                        .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, incidentId)));
                }
                var final = Assert.Single(await restarted.BookmarksAsync());
                Assert.Equal(stableFinalBookmark, final.BookmarkId);
                await restarted.ResumeAsync(final, JsonSerializer.SerializeToElement(new { eventName = "continue" }));
                Assert.Equal(WorkflowExecutionStatus.Completed, (await restarted.Provider.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId))?.Status);
                Assert.Empty(await restarted.BookmarksAsync());
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string IncidentCore(IncidentState incident) => JsonSerializer.Serialize(new
    {
        incident.IncidentId, incident.WorkflowExecutionId, incident.ActivityExecutionId, incident.ExecutableNodeId,
        incident.Severity, incident.FailureType, incident.Message, incident.CreatedAt
    });

    private static async Task CopyCheckpointAsync(string scenario, string database)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Directory.Packages.props")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var evidence = Path.Combine(repository.FullName, "specs/1006-bpmn-library-adoption/evidence/legacy");
        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, scenario + ".json")));
        var compressed = await File.ReadAllBytesAsync(Path.Combine(evidence, scenario + ".sqlite.gz"));
        Assert.Equal(receipt.RootElement.GetProperty("compressedSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(compressed)));
        await using (var input = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress))
        await using (var output = File.Create(database))
            await input.CopyToAsync(output);
        Assert.Equal(receipt.RootElement.GetProperty("databaseSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(database))));
    }

    private static async Task<BpmnRuntimeFixture> OpenHostAsync(string database, IEnumerable<string> ids, CallbackObservations? observations = null, ContinuedCallbackCapture? capture = null)
    {
        var host = await BpmnRuntimeFixture.CreateAsync(ids, services =>
        {
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(1)));
            services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
            {
                Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False",
                RecoveryContinuationSigningKey = "legacy-bpmn-recovery-fixture-key-32",
                HierarchyCursorSigningKey = "legacy-bpmn-hierarchy-fixture-key-32"
            }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
            new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
            if (capture is not null)
            {
                var selected = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
                    descriptor.ImplementationType == typeof(Elsa.Activities.Runtime.Services.WorkflowParentActivityCompletionSchedulerWorkHandler));
                services.Remove(selected);
                services.Add(new ServiceDescriptor(typeof(IWorkflowSchedulerWorkHandler), provider => new CapturedCallbackHandler(
                    (IWorkflowSchedulerWorkHandler)ActivatorUtilities.CreateInstance(provider, selected.ImplementationType!), capture), selected.Lifetime));
            }
            if (observations is not null)
            {
                var original = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBpmnProcessRuntime));
                services.Remove(original);
                services.AddScoped<IBpmnProcessRuntime>(provider => new ObservedRuntime(
                    (IBpmnProcessRuntime)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), observations));
            }
        });
        foreach (var initializer in host.Provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        host.InitializeActivityTypes();
        return host;
    }

    private sealed class CallbackObservations
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> TerminalHandles { get; } = new();
    }

    private sealed class ObservedRuntime(IBpmnProcessRuntime inner, CallbackObservations observations) : IBpmnProcessRuntime
    {
        public ValueTask<RuntimeStructuralContinuation> StartAsync(IRuntimeActivityExecutionContext context) => inner.StartAsync(context);
        public ValueTask<RuntimeStructuralContinuation> CompleteAsync(IRuntimeActivityExecutionContext context, ActivityChildCompletedContext completion)
        {
            observations.TerminalHandles.Enqueue(completion.CompletedChildActivityExecutionId);
            return inner.CompleteAsync(context, completion);
        }
        public ValueTask<RuntimeStructuralContinuation> FaultAsync(IRuntimeActivityExecutionContext context, ActivityChildFaultedContext fault)
        {
            observations.TerminalHandles.Enqueue(fault.FaultedChildActivityExecutionId);
            return inner.FaultAsync(context, fault);
        }
        public ValueTask<RuntimeStructuralContinuation> SignalAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext signal) => inner.SignalAsync(context, signal);
        public ValueTask<RuntimeStructuralContinuation> OutcomeAsync(IRuntimeActivityExecutionContext context, ActivityChildOutcomeContext outcome) => inner.OutcomeAsync(context, outcome);
    }

    // The retired old-reader parse control remains reproducible at the pre-deletion commit.
    // Current CI checks actual continuing v1 against its captured wire fields; no rollback after
    // released pruning is claimed. Actual old/current process controls are retained separately.
    private static void AssertLegacyWireContract(ActivityExecutionState continuing, JsonElement captured)
    {
        var document = Assert.IsType<ActivityPrivateState>(continuing.PrivateState);
        Assert.Equal(1, document.StateVersion);
        Assert.Equal("Elsa.Bpmn.ExecutionState", document.Value.Type.Alias);
        Assert.Equal(1, document.Value.Type.SchemaVersion);
        var written = document.Value.InlineValue!.Value;
        foreach (var property in captured.EnumerateObject())
            Assert.True(written.TryGetProperty(property.Name, out _), $"Captured v1 field '{property.Name}' was lost.");
        Assert.False(written.TryGetProperty("activeWork", out _));
        Assert.Equal(JsonValueKind.Array, written.GetProperty("activeChildren").ValueKind);
        foreach (var token in written.GetProperty("tokens").EnumerateArray())
        {
            Assert.True(token.TryGetProperty("producingActivityExecutionId", out _));
            Assert.False(token.TryGetProperty("producingWorkHandle", out _));
        }
        // For retained records, every original field remains represented even as values and
        // collection membership legitimately advance through execution and released pruning.
        foreach (var collection in new[] { "tokens", "activeChildren", "races", "loops", "compensables", "compensationRuns", "diagnostics" })
        {
            var fields = captured.GetProperty(collection).EnumerateArray().SelectMany(record => record.EnumerateObject().Select(property => property.Name))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var record in written.GetProperty(collection).EnumerateArray())
                foreach (var field in fields)
                    Assert.True(record.TryGetProperty(field, out _), $"Captured v1 {collection} field '{field}' was lost.");
        }
    }

    private static async Task ResumeAsync(BpmnRuntimeFixture host, string node)
    {
        var bookmark = Assert.Single(await host.BookmarksAsync(), candidate => candidate.ExecutableNodeId == node);
        await host.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new { eventName = "continue" }));
    }
}
