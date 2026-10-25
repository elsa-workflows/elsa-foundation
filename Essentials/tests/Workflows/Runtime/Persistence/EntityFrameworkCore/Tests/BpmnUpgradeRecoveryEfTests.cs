using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells.Lifecycle;
using Elsa.Activities.Bpmn.Tests;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Consumes frozen databases from the preview host; this test never invokes their old writer.</summary>
public sealed class BpmnUpgradeRecoveryEfTests
{
    [Theory]
    [InlineData("resolved-race")]
    [InlineData("queued-loser")]
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

    private static async Task RecoverAsync(string scenario, string? defect = null)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Directory.Packages.props")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var evidence = Path.Combine(repository.FullName, "specs/1006-bpmn-library-adoption/evidence/legacy");
        var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, scenario + ".json")));
        var compressed = await File.ReadAllBytesAsync(Path.Combine(evidence, scenario + ".sqlite.gz"));
        Assert.Equal(receipt.RootElement.GetProperty("compressedSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(compressed)));
        var directory = Path.Combine(Path.GetTempPath(), $"elsa-bpmn-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = Path.Combine(directory, "upgraded.db");
            await using (var input = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress))
            await using (var output = File.Create(database))
                await input.CopyToAsync(output);
            Assert.Equal(receipt.RootElement.GetProperty("databaseSha256").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(database))));

            await using var host = await BpmnRuntimeFixture.CreateAsync(Enumerable.Range(0, 100).Select(index => $"upgraded-activity-{index}"), services =>
            {
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow.AddDays(1)));
                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False",
                    RecoveryContinuationSigningKey = "legacy-bpmn-recovery-fixture-key-32",
                    HierarchyCursorSigningKey = "legacy-bpmn-hierarchy-fixture-key-32"
                }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                new Elsa.Workflows.Runtime.Resumption.WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
            });
            foreach (var initializer in host.Provider.GetServices<IShellInitializer>())
                await initializer.InitializeAsync();
            host.InitializeActivityTypes();
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
                    var loser = Assert.Single(before, state => state.Execution.ExecutableNodeId == "node-loser");
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
                foreach (var bookmark in bookmarks)
                    await host.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new { eventName = "continue" }));
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
            }
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

    private static async Task ResumeAsync(BpmnRuntimeFixture host, string node)
    {
        var bookmark = Assert.Single(await host.BookmarksAsync(), candidate => candidate.ExecutableNodeId == node);
        await host.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new { eventName = "continue" }));
    }
}
