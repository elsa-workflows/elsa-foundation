using System.Data.Common;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Bpmn.Tests;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Captures the actual old writer's whole durable store at a committed boundary. SQLite's online
/// backup retains checkpoint markers, outbox delivery/claim state and dependent tables verbatim.
/// Each control opens a separate database copy through a fresh old-host provider.
/// </summary>
public sealed class LegacyBpmnCheckpointEfTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Theory]
    [InlineData("resolved-race")]
    [InlineData("queued-loser")]
    [InlineData("parallel-mi")]
    [InlineData("listener-rearm")]
    [InlineData("deferred-fault")]
    public async Task Actual_legacy_checkpoint_reopens_in_a_fresh_old_host_control(string scenario)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa-bpmn-legacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = Path.Combine(directory, "writer.db");
            var backup = Path.Combine(directory, "checkpoint.db");
            var capture = new CheckpointBackup(database, backup, scenario);
            await using (var writer = await CreateAsync(database, capture))
            {
                var executable = Build(writer, scenario);
                await writer.RunAsync(executable);
                if (scenario is "resolved-race" or "listener-rearm")
                    await ResumeNodeAsync(writer, scenario == "resolved-race" ? "node-winner" : "node-listener");
                Assert.True(capture.Captured, $"Actual legacy boundary was not reached: {scenario}.");
            }

            // Keep the captured database immutable while the control consumes a separate copy.
            var control = Path.Combine(directory, "old-host-control.db");
            File.Copy(backup, control);
            await using (var reader = await CreateAsync(control, null))
            {
                await reader.Provider.GetRequiredService<IRuntimeResumptionService>()
                    .SweepAsync(new RuntimeResumptionSweepRequest());
                if (scenario == "listener-rearm")
                    await ResumeNodeAsync(reader, "node-listener");

                if (scenario != "deferred-fault")
                {
                    var node = scenario == "parallel-mi" ? "node-work" : scenario == "listener-rearm" ? "node-hold" : "node-final";
                    var waiting = (await reader.BookmarksAsync()).Where(bookmark => bookmark.ExecutableNodeId == node).ToArray();
                    Assert.Equal(scenario == "parallel-mi" ? 3 : 1, waiting.Length);
                    foreach (var bookmark in waiting)
                        await reader.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new { eventName = "continue" }));
                }

                var state = await reader.Provider.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId);
                var incidents = await reader.Provider.GetRequiredService<IIncidentStateStore>()
                    .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
                Assert.True(state!.Status == (scenario == "deferred-fault" ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed),
                    JsonSerializer.Serialize(incidents.Select(incident => new { incident.FailureType, incident.Message }), Web));
                if (scenario == "deferred-fault")
                {
                    var parent = Assert.Single(await reader.Provider.GetRequiredService<IActivityExecutionStateStore>()
                        .ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId), child => child.Execution.ExecutableNodeId == F.ProcessNodeId);
                    Assert.Equal("bpmn.gateway.no-flow-selected", parent.Fault!.Code);
                }
            }

            await ExportAsync(backup, scenario, capture);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Capture_rejects_loser_completion_enqueued_after_first_winner_cancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"elsa-bpmn-order-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = Path.Combine(directory, "writer.db");
            var backup = Path.Combine(directory, "must-not-capture.db");
            var delay = new DelayLoserCompletion();
            var capture = new CheckpointBackup(database, backup, "queued-loser", delay);
            await using (var writer = await CreateAsync(database, capture, delay))
                await writer.RunAsync(Build(writer, "queued-loser"));
            Assert.True(delay.Released);
            Assert.True(capture.SawLateLoserInsertion);
            Assert.True(capture.LoserQueuedObservation >= capture.FirstCancellationObservation);
            Assert.False(capture.Captured);
            Assert.False(File.Exists(backup));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<F> CreateAsync(string database, CheckpointBackup? capture, DelayLoserCompletion? delay = null)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(capture is null ? 1 : 0));
        var fixture = await F.CreateAsync(Enumerable.Range(0, 80).Select(index => $"legacy-{(capture is null ? "control" : "activity")}-{index}"), services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
            {
                Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False",
                RecoveryContinuationSigningKey = "legacy-bpmn-recovery-fixture-key-32",
                HierarchyCursorSigningKey = "legacy-bpmn-hierarchy-fixture-key-32"
            }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
            if (capture is not null)
                services.ConfigureDbContext<RuntimeSqliteDbContext>(options => options.AddInterceptors(delay is null ? [capture] : [capture, delay]));
            new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        });
        foreach (var initializer in fixture.Provider.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        fixture.InitializeActivityTypes();
        if (capture is not null)
        {
            capture.Services = fixture.Provider;
            capture.Ready = true;
        }
        return fixture;
    }

    private static async Task ResumeNodeAsync(F fixture, string node)
    {
        var bookmark = Assert.Single(await fixture.BookmarksAsync(), bookmark => bookmark.ExecutableNodeId == node);
        await fixture.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new { eventName = "continue" }));
    }

    private static WorkflowExecutable Build(F fixture, string scenario)
    {
        if (scenario == "parallel-mi")
            return fixture.NewExecutable([F.EventWaitNode("node-work", "continue")],
                [F.StartEvent(), F.MultiInstanceTask("work", "node-work", 3, false), F.EndEvent()],
                [F.Flow("start-work", "start", "work"), F.Flow("work-end", "work", "end")], [Target("node-work")]);
        if (scenario == "listener-rearm")
        {
            var body = F.NestedProcessNode("node-body",
                [F.EventSubprocessMessageStart("body-start", "notice", interrupting: false), F.EndEvent("body-end")],
                [F.Flow("body-flow", "body-start", "body-end")], []);
            return fixture.NewExecutable([F.EventWaitNode("node-hold", "hold"), F.EventWaitNode("node-listener", "notice"), body],
                [F.StartEvent(), F.Task("hold", "node-hold"), F.EndEvent(), F.EventSubprocessWithListener("event-scope", "node-body", "node-listener")],
                [F.Flow("start-hold", "start", "hold"), F.Flow("hold-end", "hold", "end")], [Target("node-hold"), Target("node-listener")]);
        }
        if (scenario == "deferred-fault")
            return fixture.NewExecutable([fixture.NewProbeNode("node-work")],
                [F.StartEvent(), F.ParallelGateway("fork"), F.Task("work", "node-work"), F.ExclusiveGateway("decision"), F.EndEvent("work-end"), F.EndEvent("left"), F.EndEvent("right")],
                [F.Flow("start-fork", "start", "fork"), F.Flow("fork-work", "fork", "work"), F.Flow("fork-decision", "fork", "decision"),
                    F.Flow("work-end", "work", "work-end"), F.Flow("decision-left", "decision", "left", "Left"), F.Flow("decision-right", "decision", "right", "Right")]);

        var queued = scenario == "queued-loser";
        return fixture.NewExecutable(
            [queued ? fixture.NewProbeNode("node-winner") : F.EventWaitNode("node-winner", "winner"),
                queued ? fixture.NewProbeNode("node-loser") : F.EventWaitNode("node-loser", "loser"), F.EventWaitNode("node-final", "final")],
            [F.StartEvent(), F.EventBasedGateway("race"), F.IntermediateCatchEvent("catch-winner", BpmnEventDefinitionTypes.Message, "node-winner"),
                F.IntermediateCatchEvent("catch-loser", BpmnEventDefinitionTypes.Message, "node-loser"),
                F.IntermediateCatchEvent("catch-final", BpmnEventDefinitionTypes.Message, "node-final"), F.EndEvent()],
            [F.Flow("start-race", "start", "race"), F.Flow("race-winner", "race", "catch-winner"), F.Flow("race-loser", "race", "catch-loser"),
                F.Flow("winner-final", "catch-winner", "catch-final"), F.Flow("loser-end", "catch-loser", "end"), F.Flow("final-end", "catch-final", "end")],
            [Target("node-winner"), Target("node-loser"), Target("node-final")]);
    }

    private static WorkflowExecutableResumeTarget Target(string node) => F.NodeResumeTarget(node, Event.ResumeTargetId);

    private static async Task ExportAsync(string backup, string scenario, CheckpointBackup capture)
    {
        var destination = Environment.GetEnvironmentVariable("ELSA_BPMN_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(destination))
            return;
        var source = Environment.GetEnvironmentVariable("ELSA_BPMN_CAPTURE_SOURCE");
        Assert.True(source is { Length: 40 } && source.All(Uri.IsHexDigit), "Exact capture source revision is required.");
        Directory.CreateDirectory(destination);
        var path = Path.Combine(destination, scenario + ".sqlite.gz");
        await using (var input = File.OpenRead(backup))
        await using (var output = new FileStream(path, FileMode.CreateNew))
        await using (var compressed = new GZipStream(output, CompressionLevel.SmallestSize))
            await input.CopyToAsync(compressed);
        await using var metadata = new FileStream(Path.Combine(destination, scenario + ".json"), FileMode.CreateNew);
        await JsonSerializer.SerializeAsync(metadata, new
        {
            scenario, captureSourceRevision = source, runtimeBaselineRevision = "fbe694d2f3d126e324c3e46578bc1a560b0217f1",
            modelPackage = "0.1.1-preview.19", interchangePackage = "0.1.1-preview.19",
            stateAlias = "Elsa.Bpmn.ExecutionState", stateVersion = 1,
            databaseSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(backup))),
            compressedSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path))),
            oldHostControl = "fresh provider over a separate exact database copy passed",
            capture.Facts
        }, Web);
    }

    /// <summary>Physical test perturbation: hold exactly the loser's queue insert until cancellation commits.</summary>
    private sealed class DelayLoserCompletion : SaveChangesInterceptor
    {
        public SchedulerWorkItemEntity? Held { get; private set; }
        public bool Released { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Held is null)
            {
                var entry = eventData.Context!.ChangeTracker.Entries<SchedulerWorkItemEntity>()
                    .FirstOrDefault(candidate => candidate.State == EntityState.Added && IsLoserCompletion(candidate.Entity));
                if (entry is not null)
                {
                    Held = entry.Entity;
                    entry.State = EntityState.Detached;
                }
            }
            return ValueTask.FromResult(result);
        }

        private static bool IsLoserCompletion(SchedulerWorkItemEntity row)
        {
            using var document = JsonDocument.Parse(row.ContentJson);
            return document.RootElement.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("CompletedChildActivityExecutionId", out var child) && child.GetString() == "legacy-activity-2";
        }

        public async Task ReleaseAsync(string database, CancellationToken cancellationToken)
        {
            Released = true;
            await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
                .UseSqlite($"Data Source={database};Pooling=False").Options);
            context.SchedulerWorkItems.Add(Held!);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CheckpointBackup(string database, string backup, string scenario, DelayLoserCompletion? delay = null) : DbTransactionInterceptor
    {
        public IServiceProvider? Services { get; set; }
        public bool Ready { get; set; }
        public bool Captured { get; private set; }
        public object? Facts { get; private set; }
        public int? LoserQueuedObservation { get; private set; }
        public int? FirstCancellationObservation { get; private set; }
        public bool SawLateLoserInsertion { get; private set; }
        private int _observation;

        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!Ready || Captured)
                return;
            await using var scope = Services!.CreateAsyncScope();
            var states = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>()
                .ListAllAsync(WorkflowExecutionHarness.WorkflowExecutionId);
            var parent = states.SingleOrDefault(state => state.Execution.ExecutableNodeId == F.ProcessNodeId);
            if (parent?.PrivateState?.Value.InlineValue is not { } raw)
                return;
            var state = raw.Deserialize<BpmnExecutionState>(Web)!;
            var bookmarks = await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
                .ListAllBookmarkStatesAsync(WorkflowExecutionHarness.WorkflowExecutionId);
            var queue = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                .ListAllAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionHarness.WorkflowExecutionId));
            var loser = states.SingleOrDefault(child => child.Execution.ExecutableNodeId == "node-loser");
            var queuedLoser = loser is not null && queue.Where(item => item.CommandKind == WorkflowExecutionCommandKind.CompleteActivity)
                .Any(item => item.Payload!.Value.Deserialize<RuntimeCompleteActivityCommandPayload>(Web)!.CompletedChildActivityExecutionId == loser?.InvocationId);
            var canceledLoser = state.Tokens.Any(token => token.AtElementId == "catch-loser" && token.Status == BpmnTokenStatus.Canceled);
            var ordinal = ++_observation;
            if (queuedLoser)
                LoserQueuedObservation ??= ordinal;
            if (canceledLoser)
                FirstCancellationObservation ??= ordinal;
            var queuedBeforeCancellation = LoserQueuedObservation is { } queuedAt &&
                FirstCancellationObservation is { } canceledAt && queuedAt < canceledAt;
            if (queuedLoser && FirstCancellationObservation is { } firstCancel && ordinal > firstCancel && !queuedBeforeCancellation)
                SawLateLoserInsertion = true;
            if (canceledLoser && delay is { Released: false, Held: not null })
            {
                await delay.ReleaseAsync(database, cancellationToken);
                // The delayed queue write uses a separate context. Observe its actual committed result too.
                await TransactionCommittedAsync(transaction, eventData, cancellationToken);
                return;
            }
            var match = scenario switch
            {
                "resolved-race" => canceledLoser && loser?.Status == ActivityExecutionStatus.Cancelled && bookmarks.Any(bookmark => bookmark.ExecutableNodeId == "node-final"),
                "queued-loser" => canceledLoser && loser?.Status == ActivityExecutionStatus.Completed && queuedLoser && queuedBeforeCancellation,
                "parallel-mi" => state.ActiveChildren.Count == 3 && bookmarks.Count(bookmark => bookmark.ExecutableNodeId == "node-work") == 3,
                "listener-rearm" => states.Count(child => child.Execution.ExecutableNodeId == "node-listener") == 2 && bookmarks.Any(bookmark => bookmark.ExecutableNodeId == "node-listener"),
                "deferred-fault" => state.PendingFault is not null,
                _ => false
            };
            if (!match)
                return;
            Assert.Equal(1, parent.PrivateState!.StateVersion);
            Facts = new
            {
                parentId = parent.InvocationId, pendingFault = state.PendingFault,
                canceledLoser, queuedLoser, queuedBeforeCancellation, LoserQueuedObservation, FirstCancellationObservation,
                activeWork = state.ActiveChildren,
                children = states.Where(child => child.ParentActivityExecutionId == parent.InvocationId)
                    .Select(child => new { child.InvocationId, node = child.Execution.ExecutableNodeId, child.Status, child.IterationId, token = child.Provenance.ExecutionPathId }),
                bookmarkCount = bookmarks.Count, queuedIds = queue.Select(item => item.WorkItemId)
            };
            using var source = new SqliteConnection($"Data Source={database};Pooling=False");
            using var destination = new SqliteConnection($"Data Source={backup};Pooling=False");
            source.Open(); destination.Open();
            source.BackupDatabase(destination);
            Captured = true;
        }
    }
}
