using System.Text.Json;
using Bpmn.Model;
using CShells.Lifecycle;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Cluster.Readability;
using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>Actual feature/mailbox/EF/interpreter decisions. Service-provider restart is not process-host proof.</summary>
public sealed class BpmnDirectExecutionTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly string database = Path.Combine(Path.GetTempPath(), "elsa-direct-bpmn-" + Guid.NewGuid().ToString("N") + ".sqlite");

    [Theory]
    [InlineData("etl.rows-rejected")]
    [InlineData("etl.other-error")]
    public async Task The_native_profile_admits_coded_service_error_boundaries_for_library_matching(string code)
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        await scope.ServiceProvider.GetRequiredService<BpmnDirectExecutionDriver>().ValidateMaterialAsync(ServiceErrorMaterial(code));
    }

    [Theory]
    [InlineData("missing-code")]
    [InlineData("blank-code")]
    [InlineData("unknown-property")]
    [InlineData("timer")]
    [InlineData("noninterrupting")]
    [InlineData("wrong-host")]
    [InlineData("bound-catcher")]
    public async Task The_native_profile_refuses_error_shapes_without_a_qualified_host_binding(string variant)
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => scope.ServiceProvider.GetRequiredService<BpmnDirectExecutionDriver>()
            .ValidateMaterialAsync(ServiceErrorMaterial("etl.rows-rejected", variant)).AsTask());
    }

    // Publication validation only; actual ETL/RunBpmn recovery is qualified by the owned HTTP journey.
    private static RuntimeModelMaterial ServiceErrorMaterial(string code, string? variant = null)
    {
        var properties = new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = variant == "blank-code" ? " " : code };
        if (variant == "missing-code") properties.Clear();
        if (variant == "unknown-property") properties["unknown"] = "refuse";
        var process = new BpmnProcessDefinition("service-errors", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "worker"),
                new("end", BpmnElementTypes.EndEvent), new("caught", BpmnElementTypes.EndEvent),
                new("error", BpmnElementTypes.BoundaryEvent, bindingRef: variant == "bound-catcher" ? "catcher" : null,
                    attachedToRef: variant == "wrong-host" ? "end" : "service", cancelActivity: variant != "noninterrupting",
                    eventDefinitions: [new(variant == "timer" ? BpmnEventDefinitionTypes.Timer : BpmnEventDefinitionTypes.Error, properties)])],
            SequenceFlows: [new("start-service", "start", "service"), new("service-end", "service", "end"), new("error-caught", "error", "caught")]);
        var pin = new WorkflowExecutableIdentity("worker-artifact", "worker-definition", "worker-version", "1.0.0", "worker-hash");
        var definition = new BpmnModelDefinition(2, JsonSerializer.SerializeToElement(process, Web), [], [new("worker", pin, code)]);
        return new(1, pin, new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), BpmnModelProtocol.NativeWorkProfile,
            JsonSerializer.SerializeToElement(definition, Web), new(1, []), [], [new("worker", pin)], []);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_in_flight_native_start_from_the_actual_cancelled_capture_requires_positive_retirement(bool removeCancellationSource)
    {
        await using (var raw = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualNativeStartRetirement.sqlite.gz")))
        await using (var zipped = new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress))
        await using (var target = File.Create(database)) await zipped.CopyToAsync(target);
        await using var host = await OpenAsync(configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System)),
            activityIds: ["retired-root-1", "retired-root-2"]);
        await using var scope = await ScopeAsync(host);
        var db = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        var model = Assert.Single((await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().ListPageAsync(new())).Items);
        var cancellation = Assert.Single(BpmnDirectExecutionDriver.Read(model).Cancellations!);
        var origin = cancellation.Origin;
        if (removeCancellationSource)
        {
            var encodedKind = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(BpmnNativeWorkBoundary.CancelKind));
            Assert.Equal(1, await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM elsa_runtime_post_commit_outbox WHERE ContentJson LIKE {"%" + encodedKind + "%"}"));
        }
        var identity = BpmnNativeWorkBoundary.WorkerIdentity(origin.Scope, origin.ExecutionId, origin.InvocationId, origin.AttemptId, origin.WorkerExecutable);
        var starts = scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>();
        var result = await starts.DispatchAsync(new(origin.WorkerExecutable.ArtifactId, model.Authority.SystemIdentity,
            origin.WorkerExecutionId, identity.StartKey, null, null, null, null, null, WorkflowRunKind.PublishedRun,
            null, WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy, null, null, model.TenantId,
            new(origin.Scope.Value), model.Authority, WorkflowExecutableStartAuthority.FromModelWork(origin)));
        if (removeCancellationSource)
        {
            // Durable transport acceptance is not retirement: unavailable proof leaves the original work queued.
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
            await AssertNativeEmptyAsync(scope.ServiceProvider);
            Assert.NotEmpty((await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(origin.WorkerExecutionId))).Items);
            var workerId = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(origin.WorkerExecutionId));
            Assert.False(await db.RuntimeCheckpointCommits.AnyAsync(row => row.WorkflowExecutionId == workerId));
            Assert.Equal(2, (await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(model.ExecutionId))!.Revision);
            return;
        }
        Assert.True(result.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Reason);
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId));
        Assert.Empty((await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(origin.WorkerExecutionId))).Items);
        await AssertNativeEmptyAsync(scope.ServiceProvider);
        var markers = await db.RuntimeCheckpointCommits.AsNoTracking().ToArrayAsync();
        Assert.Single(markers, row => row.WorkflowExecutionId == Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(origin.WorkerExecutionId)));
        // Replaying the same stale start must converge without another admission or checkpoint marker.
        await starts.DispatchAsync(new(origin.WorkerExecutable.ArtifactId, model.Authority.SystemIdentity,
            origin.WorkerExecutionId, identity.StartKey, null, null, null, null, null, WorkflowRunKind.PublishedRun,
            null, WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy, null, null, model.TenantId,
            new(origin.Scope.Value), model.Authority, WorkflowExecutableStartAuthority.FromModelWork(origin)));
        Assert.Equal(markers.Length, await db.RuntimeCheckpointCommits.CountAsync());
        await AssertNativeEmptyAsync(scope.ServiceProvider);
    }

    [Fact]
    public async Task Cancellation_keeps_its_exact_receipt_reason_and_final_state_across_provider_restart()
    {
        string execution;
        BpmnHumanWork work;
        string retained;
        var caller = new BpmnModelCaller("operator", null);
        await using (var host = await OpenAsync())
        {
            await using var scope = await ScopeAsync(host);
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            var published = await runtime.PublishAsync(Material().Definition.Deserialize<BpmnModelDefinition>(Web)!, "cancel-publish", caller);
            execution = (await runtime.StartAsync(published.SourceReferenceId, "cancel-start", null, caller)).ExecutionId;
            work = Assert.Single((await runtime.ListWorkAsync(execution, new(), caller)).Items);
            var result = await runtime.CancelAsync(execution, "cancel", "Owner cancelled the pending approval.", caller);
            Assert.False(result.Pending);
            Assert.Equal("Accepted", result.Result!.Value.GetProperty("status").GetString());
            var state = (await runtime.FindExecutionAsync(execution, caller))!;
            Assert.Equal(RuntimeModelLifecycle.TerminalSettled, state.Lifecycle);
            Assert.Equal("Cancelled", state.TerminalResult!.Value.GetProperty("status").GetString());
            Assert.Equal("Owner cancelled the pending approval.", state.TerminalResult.Value.GetProperty("reason").GetString());
            Assert.Empty((await runtime.ListWorkAsync(execution, new(), caller)).Items);
            retained = JsonSerializer.Serialize(state, Web);
            await AssertNativeEmptyAsync(scope.ServiceProvider);
        }
        await using (var host = await OpenAsync(admitNewExecutions: false))
        {
            await using var scope = await ScopeAsync(host);
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            Assert.True((await runtime.CancelAsync(execution, "cancel", "Owner cancelled the pending approval.", caller)).Replay);
            await Assert.ThrowsAsync<Elsa.Workflows.Runtime.Core.Exceptions.RuntimeCheckpointReplayConflictException>(() =>
                runtime.CancelAsync(execution, "cancel", "Different reason", caller).AsTask());
            var late = await runtime.CompleteHumanAsync(execution, "late-human", work.InvocationId, work.AttemptId, null, caller);
            Assert.Equal("execution-terminal", late.Result!.Value.GetProperty("code").GetString());
            Assert.Equal("execution-terminal", (await runtime.CancelAsync(execution, "another-cancel", null, caller)).Result!.Value.GetProperty("code").GetString());
            Assert.Equal(retained, JsonSerializer.Serialize(await runtime.FindExecutionAsync(execution, caller), Web));
            await AssertNativeEmptyAsync(scope.ServiceProvider);
        }
    }

    [Fact]
    public async Task Native_default_composition_runs_while_model_reads_refuse_without_a_selected_provider()
    {
        await using var host = WorkflowExecutionHarness.Create().ConfigureServices(services =>
        {
            services.AddLogging();
            new EventsFeature().ConfigureServices(services);
            new ActivitiesBpmnHostingFeature().ConfigureServices(services);
        }).Build("native-invocation");
        foreach (var initializer in host.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
        foreach (var task in host.Services.GetServices<IStartupTask>()) await task.ExecuteAsync(CancellationToken.None);
        var run = await host.RunAsync(WorkflowExecutionHarness.NewExecutable(WorkflowExecutionHarness.NewProbeNode("native")));
        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState!.Status);
        await using var scope = await ScopeAsync(host);
        Assert.Null(scope.ServiceProvider.GetService<IRuntimeModelExecutionReader>());
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        await Assert.ThrowsAsync<Elsa.Workflows.Runtime.Core.Exceptions.RuntimeSchedulerWorkUnavailableException>(() =>
            runtime.FindExecutionAsync("unavailable", new("operator", null)).AsTask());
    }

    [Theory]
    [InlineData("duplicate-protocol")]
    [InlineData("duplicate-driver")]
    [InlineData("singleton-driver")]
    public async Task Actual_startup_refuses_ambiguous_protocols_and_driver_lifetimes(string invalid)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => OpenAsync(configure: services =>
        {
            if (invalid == "duplicate-protocol") services.AddSingleton<IRuntimeExecutionProtocolContributor, DuplicateProtocol>();
            else if (invalid == "duplicate-driver") services.AddScoped<BpmnDirectExecutionDriver>();
            else
            {
                services.RemoveAll<BpmnDirectExecutionDriver>();
                services.AddSingleton<BpmnDirectExecutionDriver>();
            }
        }));
    }

    [Fact]
    public async Task Accepted_first_input_stays_rooted_and_recovers_after_restart_with_new_admission_disabled()
    {
        RuntimeExecutionMaterial material;
        await using (var host = await OpenAsync())
        {
            await using var scope = await ScopeAsync(host);
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            var definition = Material();
            await store.SaveModelAsync(definition);
            material = (await store.FindMaterialAsync(definition.Identity.ArtifactId))!;
            var now = DateTimeOffset.UtcNow;
            var input = new RuntimeModelExecutionCommand(material.Identity, material.IncarnationId, "start", "start",
                JsonSerializer.SerializeToElement(new { }, Web), new("bpmn-test", "bpmn-test"), null);
            await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(new("accepted", "direct-execution", "start",
                WorkflowExecutionCommandKind.ExecuteModel, "accepted", "start", now, now, payload: JsonSerializer.SerializeToElement(input, Web)));
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync("direct-execution"));
            Assert.Contains(material.Identity.ArtifactId, await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().ListPinnedExecutableArtifactIdsAsync());
        }
        await using (var host = await OpenAsync(admitNewExecutions: false))
        {
            await using var scope = await ScopeAsync(host);
            var now = DateTimeOffset.UtcNow;
            var actor = await host.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(
                new("direct-execution", WorkflowExecutionActorActivationReason.Start, now, "recovery", WorkflowExecutionActorCapabilities.InProcessMailbox));
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await actor.EnqueueAsync(new("recover", "direct-execution",
                new("recover", "direct-execution", WorkflowExecutionCommandKind.RunSchedulerWork, now, null, new Dictionary<string, string>()),
                "recover", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now))).Status);
            var state = (await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync("direct-execution"))!;
            Assert.Equal(1, state.Revision);
            var work = Assert.Single(state.Payload.Deserialize<BpmnDirectExecutionDriver.Document>(Web)!.Work);
            await SendAsync(host, material, "complete", "complete-human", new { work.InvocationId, work.AttemptId });
            Assert.Equal(RuntimeModelLifecycle.TerminalSettled, (await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync("direct-execution"))!.Lifecycle);
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            var caller = new BpmnModelCaller("operator", null);
            var published = await runtime.PublishAsync(Material().Definition.Deserialize<BpmnModelDefinition>(Web)!, "publish", caller);
            await Assert.ThrowsAsync<Elsa.Workflows.Runtime.Core.Exceptions.RuntimeSchedulerWorkUnavailableException>(() =>
                runtime.StartAsync(published.SourceReferenceId, "new-start", null, caller).AsTask());
            Assert.False(await scope.ServiceProvider.GetRequiredService<RuntimeDbContext>().SchedulerWorkItems.AnyAsync());
            await AssertNativeEmptyAsync(scope.ServiceProvider);
        }
    }

    private sealed class DuplicateProtocol : IRuntimeExecutionProtocolContributor
    {
        public IEnumerable<RuntimeExecutionProtocolRegistration> GetProtocols() =>
            [new(new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), typeof(BpmnDirectExecutionDriver)) { InputKinds = ["start"] }];
    }

    [Fact]
    public async Task Published_source_commands_keep_exact_replay_human_attempts_and_tenant_disclosure_boundaries()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("operator", null);
        var definition = Material().Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var published = await runtime.PublishAsync(definition, "publish", caller);
        Assert.Equal(published, await runtime.PublishAsync(definition, "publish", caller));
        var started = await runtime.StartAsync(published.SourceReferenceId, "request-start", null, caller);
        Assert.False(started.Pending);
        Assert.False(started.Replay);
        Assert.True((await runtime.StartAsync(published.SourceReferenceId, "request-start", new Dictionary<string, JsonElement>(), caller)).Replay);
        var work = Assert.Single((await runtime.ListWorkAsync(started.ExecutionId, new(), caller)).Items);
        var beforeObsolete = (await runtime.FindExecutionAsync(started.ExecutionId, caller))!;
        var obsolete = await runtime.CompleteHumanAsync(started.ExecutionId, "obsolete", work.InvocationId, "wrong-attempt", null, caller);
        Assert.Equal("Rejected", obsolete.Result!.Value.GetProperty("status").GetString());
        Assert.Equal("obsolete-human-work", obsolete.Result.Value.GetProperty("code").GetString());
        Assert.Equal(JsonSerializer.Serialize(beforeObsolete, Web), JsonSerializer.Serialize(await runtime.FindExecutionAsync(started.ExecutionId, caller), Web));
        await Assert.ThrowsAsync<Elsa.Workflows.Runtime.Core.Exceptions.RuntimeCheckpointReplayConflictException>(() =>
            runtime.CompleteHumanAsync(started.ExecutionId, "obsolete", work.InvocationId, work.AttemptId, null, caller).AsTask());
        var completed = await runtime.CompleteHumanAsync(started.ExecutionId, "complete", work.InvocationId, work.AttemptId, null, caller);
        Assert.Equal("Accepted", completed.Result!.Value.GetProperty("status").GetString());
        Assert.True((await runtime.CompleteHumanAsync(started.ExecutionId, "complete", work.InvocationId, work.AttemptId, ["Done"], caller)).Replay);
        Assert.True((await runtime.CompleteHumanAsync(started.ExecutionId, "complete", work.InvocationId, work.AttemptId, [], caller)).Replay);
        var terminal = (await runtime.FindExecutionAsync(started.ExecutionId, caller))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, terminal.Lifecycle);
        var late = await runtime.CompleteHumanAsync(started.ExecutionId, "late", work.InvocationId, work.AttemptId, null, caller);
        Assert.False(late.Pending);
        Assert.Equal("execution-terminal", late.Result!.Value.GetProperty("code").GetString());
        Assert.Equal(JsonSerializer.Serialize(terminal, Web), JsonSerializer.Serialize(await runtime.FindExecutionAsync(started.ExecutionId, caller), Web));
        Assert.False(await scope.ServiceProvider.GetRequiredService<RuntimeDbContext>().SchedulerWorkItems.AnyAsync());
        var foreign = new BpmnModelCaller("other-operator", "other-tenant");
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.FindExecutionAsync(started.ExecutionId, foreign).AsTask());
        await using (var foreignScope = await host.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new("other-tenant")))
        {
            var foreignRuntime = foreignScope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            Assert.Null(await foreignRuntime.FindExecutionAsync(started.ExecutionId, foreign));
            await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => foreignRuntime.FindCommandAsync(started.ExecutionId, "complete", foreign).AsTask());
            await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() => foreignRuntime.StartAsync(published.SourceReferenceId, "foreign-start", null, foreign).AsTask());
        }
        await AssertNativeEmptyAsync(scope.ServiceProvider);
    }

    [Fact]
    public async Task Direct_human_work_survives_service_provider_restart_and_completes_without_native_state()
    {
        RuntimeExecutionMaterial material;
        BpmnDirectExecutionDriver.Work work;
        await using (var host = await OpenAsync())
        {
            await using var scope = await ScopeAsync(host);
            var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
            var definition = Material();
            await store.SaveModelAsync(definition);
            material = (await store.FindMaterialAsync(definition.Identity.ArtifactId))!;
            await SendAsync(host, material, "start", "start", new { });
            var state = Assert.IsType<RuntimeModelExecutionState>(await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync("direct-execution"));
            Assert.Equal(RuntimeModelLifecycle.Running, state.Lifecycle);
            Assert.Equal(1, state.Revision);
            work = Assert.Single(state.Payload.Deserialize<BpmnDirectExecutionDriver.Document>(Web)!.Work);
            Assert.Equal("human", work.ElementId);
            await AssertNativeEmptyAsync(scope.ServiceProvider);
        }
        await using (var host = await OpenAsync())
        {
            await using var scope = await ScopeAsync(host);
            var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
            var waiting = (await reader.FindAsync("direct-execution"))!;
            Assert.Equal(work, Assert.Single(waiting.Payload.Deserialize<BpmnDirectExecutionDriver.Document>(Web)!.Work));
            Assert.Equal(material.Identity, waiting.Executable);
            Assert.Equal(material.IncarnationId, waiting.MaterialIncarnationId);
            await SendAsync(host, material, "complete", "complete-human", new { work.InvocationId, work.AttemptId });
            var completed = (await reader.FindAsync("direct-execution"))!;
            Assert.Equal(RuntimeModelLifecycle.TerminalSettled, completed.Lifecycle);
            Assert.Equal(2, completed.Revision);
            Assert.Equal("Completed", completed.TerminalResult!.Value.GetProperty("status").GetString());
            Assert.Empty(completed.Payload.Deserialize<BpmnDirectExecutionDriver.Document>(Web)!.Work);
            Assert.Equal("Accepted", (await reader.FindInputAsync("direct-execution", "complete"))!.Result.GetProperty("status").GetString());
            await SendAsync(host, material, "complete", "complete-human", new { work.InvocationId, work.AttemptId }, "replay-transport");
            Assert.Equal(2, (await reader.FindAsync("direct-execution"))!.Revision);
            Assert.Empty((await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new("direct-execution"))).Items);
            await AssertNativeEmptyAsync(scope.ServiceProvider);
        }
    }

    private async Task<WorkflowExecutionHarness> OpenAsync(bool admitNewExecutions = true, Action<IServiceCollection>? configure = null,
        params string[] activityIds)
    {
        var host = WorkflowExecutionHarness.Create().ConfigureServices(services =>
        {
            services.AddLogging();
            services.AddEfSchemaReadability();
            new EventsFeature().ConfigureServices(services);
            new ActivitiesBpmnHostingFeature { AdmitNewExecutions = admitNewExecutions }.ConfigureServices(services);
            services.AddRuntimeEntityFrameworkCore(new()
            {
                Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False",
                RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('h', 32)
            }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
            configure?.Invoke(services);
        }).Build(activityIds);
        try
        {
            foreach (var initializer in host.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
            foreach (var task in host.Services.GetServices<IStartupTask>()) await task.ExecuteAsync(CancellationToken.None);
            return host;
        }
        catch { await host.DisposeAsync(); throw; }
    }

    private static ValueTask<PersistenceOperationScope> ScopeAsync(WorkflowExecutionHarness host) =>
        host.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new(PersistenceScope.DefaultValue));

    private static async Task SendAsync(WorkflowExecutionHarness host, RuntimeExecutionMaterial material,
        string inputId, string kind, object payload, string? transport = null)
    {
        var now = DateTimeOffset.UtcNow;
        var command = new RuntimeModelExecutionCommand(material.Identity, material.IncarnationId, inputId, kind,
            JsonSerializer.SerializeToElement(payload, Web), new("bpmn-test", "bpmn-test"), null);
        var actor = await host.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(
            new("direct-execution", WorkflowExecutionActorActivationReason.Start, now, "bpmn-test", WorkflowExecutionActorCapabilities.InProcessMailbox));
        var result = await actor.EnqueueAsync(new(transport ?? inputId, "direct-execution",
            new(inputId, "direct-execution", WorkflowExecutionCommandKind.ExecuteModel, now,
                JsonSerializer.SerializeToElement(command, Web), new Dictionary<string, string>()),
            transport ?? inputId, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
    }

    private static RuntimeModelMaterial Material()
    {
        var process = new BpmnProcessDefinition("direct-process", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("human", BpmnElementTypes.UserTask, bindingRef: "human-work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-human", "start", "human"), new("to-end", "human", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, Web), ["human-work"]);
        var pin = new WorkflowExecutableIdentity("direct-artifact", "definition", "definition-v1", "1.0.0", "pending");
        var material = new RuntimeModelMaterial(1, pin, new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), BpmnModelProtocol.Profile,
            JsonSerializer.SerializeToElement(definition, Web), new(1, []), [], [], []);
        return new(1, pin with { ArtifactHash = material.ComputeContentHash() }, material.Protocol, material.Profile, material.Definition,
            material.InputContract, [], [], []);
    }

    private static async Task AssertNativeEmptyAsync(IServiceProvider services)
    {
        var database = services.GetRequiredService<RuntimeDbContext>();
        Assert.False(await database.WorkflowExecutionStates.AnyAsync());
        Assert.False(await database.ActivityExecutionStates.AnyAsync());
        Assert.False(await database.Bookmarks.AnyAsync());
    }

    public void Dispose()
    {
        foreach (var path in new[] { database, database + "-wal", database + "-shm" }) File.Delete(path);
    }
}
