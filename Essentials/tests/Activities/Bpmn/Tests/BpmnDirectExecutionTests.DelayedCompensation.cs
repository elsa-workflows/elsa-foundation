using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Reflection;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Compensation;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task A_current_writer_queued_cancelled_result_recovers_without_waiting_for_its_obsolete_native_retry()
    {
        await using (var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CurrentDelayedCompensation.sqlite.gz")))
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await gzip.CopyToAsync(output);
        Assert.Equal("175a6880fe295970c6b1de3c4e2e43a23ced75b58a55d94413ad798538289fdd",
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(database))));
        await using var host = await OpenAsync(admitNewExecutions: false,
            configure: services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System)));
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        const string model = "bpmn:a29cc5c675038cc802eabef05ca3c36a517ddbf17c66304badea80379b1e56cd";
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        var before = (await reader.FindAsync(model))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalPendingSettlement, before.Lifecycle);
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(before).Cancellations!).Origin;
        var workflows = services.GetRequiredService<IWorkflowExecutionStateStore>();
        var originalWorker = JsonSerializer.Serialize(await workflows.FindAsync(origin.WorkerExecutionId), Web);
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        var retry = (await lookup.FindUndeliveredDurableAsync(origin.WorkerExecutionId, [BpmnNativeWorkBoundary.ResultKind]))!;
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, retry.Intent.Kind);
        var originalRetry = JsonSerializer.Serialize(retry, Web);
        var queue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var queued = Assert.Single((await queue.ListAsync(new(model))).Items);
        var originalResult = queued.Payload!.Value.Deserialize<RuntimeModelExecutionCommand>(Web)!.Payload;
        Assert.Null(await reader.FindInputAsync(model, queued.CommandId));
        await DrainModelAsync(host, model);
        var settled = (await reader.FindAsync(model))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, settled.Lifecycle);
        Assert.Equal("Cancelled", settled.TerminalResult!.Value.GetProperty("status").GetString());
        Assert.Equal("Accepted", (await reader.FindInputAsync(model, queued.CommandId))!.Result.GetProperty("status").GetString());
        Assert.True(JsonElement.DeepEquals(originalResult,
            JsonSerializer.SerializeToElement(Assert.Single(BpmnDirectExecutionDriver.Read(settled).NativeResults!), Web)));
        Assert.Equal(originalWorker, JsonSerializer.Serialize(await workflows.FindAsync(origin.WorkerExecutionId), Web));
        Assert.Equal(originalRetry, JsonSerializer.Serialize(await lookup.FindDurableAsync(retry.OutboxItemId), Web));
        Assert.Equal(before.Executable, settled.Executable);
        Assert.Equal(before.MaterialIncarnationId, settled.MaterialIncarnationId);
        Assert.True(WorkflowExecutionAuthoritySnapshot.Matches(settled.Authority,
            before.Authority.SystemIdentity, before.Authority.RootInitiator, before.Authority.Metadata));
    }

    [Fact]
    public async Task An_archived_pre_migration_model_hash_is_refused_without_advancing_retained_state()
    {
        await using (var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ArchivedDelayedCompensation.sqlite.gz")))
        await using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        await using (var output = File.Create(database)) await gzip.CopyToAsync(output);
        Assert.Equal("dedc7041baf31ad6d0b3de251049de7d55ff421dbe7e65e3059b6e35f9bf9e63",
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(database))));
        await using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT ContentJson FROM elsa_runtime_workflow_executable WHERE IsModelMaterial=1";
            using var material = JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
            var identity = material.RootElement.GetProperty("material").GetProperty("identity");
            var id = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Decode(identity.GetProperty("artifactId").GetString()!);
            var hash = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Decode(identity.GetProperty("artifactHash").GetString()!);
            Assert.InRange(id.Length, 1, Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.RuntimeArtifactEfModule.IdentityMaximumLength);
            Assert.InRange(hash.Length, 1, Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.RuntimeArtifactEfModule.HashMaximumLength);
            Assert.Equal("sha256:8f4c88b9ad0dd93267b5ec40e827f154e84cdae040834823006e863cf029e5c1", hash);
        }
        var before = await RetainedDocumentsAsync();
        await using (var host = await OpenAsync(admitNewExecutions: false))
        await using (var scope = await ScopeAsync(host))
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(
                    "sha256:8f4c88b9ad0dd93267b5ec40e827f154e84cdae040834823006e863cf029e5c1"));
            Assert.Equal("The persisted model material payload is corrupt.", error.Message);
            var cause = Assert.IsType<ArgumentException>(error.InnerException);
            Assert.Equal("material", cause.ParamName);
            Assert.StartsWith("Model material requires its complete behavioral content hash and a supported artifact ID.", cause.Message);
        }
        Assert.Equal(before, await RetainedDocumentsAsync());

        async Task<string[]> RetainedDocumentsAsync()
        {
            var documents = new List<string>();
            await using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
            await connection.OpenAsync();
            foreach (var table in new[] { "model_execution", "model_input", "workflow_execution_state", "post_commit_outbox", "scheduler_work_item" })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT ContentJson FROM elsa_runtime_" + table + " ORDER BY Id";
                await using var rows = await command.ExecuteReaderAsync();
                while (await rows.ReadAsync()) documents.Add(table + ":" + rows.GetString(0));
            }
            return documents.ToArray();
        }
    }

    [Fact]
    public async Task Native_cancellation_settles_before_an_obsolete_compensation_retry_becomes_due()
    {
        await using var host = await OpenAsync(configure: services =>
        {
            new ActivitiesCompensationFeature().ConfigureServices(services);
            new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System));
        }, activityIds: Enumerable.Range(1, 32).Select(i => "delayed-compensation-" + i).ToArray());
        var worker = new WorkflowExecutable(WorkflowExecutionHarness.Identity, DelayedCompensationWorker(),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(),
            inputContract: new(1, []), dependencies: null, incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        await host.PublishAsync(worker, "delayed-compensation-worker");
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        var queue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var caller = new BpmnModelCaller("operator", null);
        var process = new BpmnProcessDefinition("delayed-compensation", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("work", BpmnElementTypes.ServiceTask, bindingRef: "worker"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("start-work", "start", "work"), new("work-end", "work", "end")]);
        var publication = await runtime.PublishAsync(new(2, JsonSerializer.SerializeToElement(process, Web), [], [new("worker", worker.Identity, "test.error")]),
            "delayed-compensation-model", caller);
        var started = await runtime.StartAsync(publication.SourceReferenceId, "delayed-compensation-start", null, caller);
        var before = (await reader.FindAsync(started.ExecutionId))!;
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(before).Work).NativeWork!;
        await processor.ProcessAsync(new(limit: 100, workflowExecutionId: before.ExecutionId, intentKind: BpmnNativeWorkBoundary.StartKind));
        // The delivery can leave the admitted worker's protected continuations queued. Drive its ordinary
        // outbox and scheduler until the future retry is the remaining source, rather than assuming inline work.
        for (var pass = 0; pass < 8; pass++)
        {
            await processor.ProcessAsync(new(limit: 100, workflowExecutionId: origin.WorkerExecutionId));
            await DrainModelAsync(host, origin.WorkerExecutionId);
            if (await lookup.FindUndeliveredDurableAsync(origin.WorkerExecutionId, [BpmnNativeWorkBoundary.ResultKind]) is { } pending &&
                pending.AvailableAt > DateTimeOffset.UtcNow.AddMinutes(50)) break;
        }
        var delayed = (await lookup.FindUndeliveredDurableAsync(origin.WorkerExecutionId, [BpmnNativeWorkBoundary.ResultKind]))!;
        await CaptureAsync("waiting");
        Assert.NotNull(delayed);
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, delayed.Intent.Kind);
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, delayed.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>(Elsa.Serialization.Core.JsonBaseline.Options)!.CommandKind);
        Assert.True(delayed.AvailableAt > DateTimeOffset.UtcNow.AddMinutes(50));
        Assert.Equal(WorkflowExecutionStatus.Running,
            (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId))!.Status);

        await runtime.CancelAsync(before.ExecutionId, "cancel-delayed-compensation", "Cancel during retry wait.", caller);
        await processor.ProcessAsync(new(limit: 100, workflowExecutionId: before.ExecutionId, intentKind: BpmnNativeWorkBoundary.CancelKind));
        await DrainModelAsync(host, origin.WorkerExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Cancelled,
            (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId))!.Status);
        await processor.ProcessAsync(new(limit: 100, workflowExecutionId: origin.WorkerExecutionId, intentKind: BpmnNativeWorkBoundary.ResultKind));
        var originalInput = Assert.Single((await queue.ListAsync(new(before.ExecutionId))).Items);
        Assert.Equal(origin.ResultInputId, originalInput.CommandId);
        await CaptureAsync("queued-result");

        try { await DrainModelAsync(host, before.ExecutionId); }
        catch (RuntimeSchedulerWorkUnavailableException) { /* Preserve the failed run's actual state for the assertion/capture below. */ }
        var settled = (await reader.FindAsync(before.ExecutionId))!;
        await CaptureAsync("after-result");
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, settled.Lifecycle);
        Assert.Equal("Cancelled", settled.TerminalResult!.Value.GetProperty("status").GetString());
        Assert.Equal("Accepted", (await reader.FindInputAsync(before.ExecutionId, originalInput.CommandId))!.Result.GetProperty("status").GetString());
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await lookup.FindDurableAsync(delayed.OutboxItemId))!.Status);
        Assert.True(delayed.AvailableAt > DateTimeOffset.UtcNow.AddMinutes(50));
        Assert.Equal(before.Executable, settled.Executable);
        Assert.Equal(before.MaterialIncarnationId, settled.MaterialIncarnationId);
        Assert.True(WorkflowExecutionAuthoritySnapshot.Matches(settled.Authority,
            before.Authority.SystemIdentity, before.Authority.RootInitiator, before.Authority.Metadata));

        async Task CaptureAsync(string phase)
        {
            if (Environment.GetEnvironmentVariable("ELSA_BPMN_DELAYED_COMPENSATION_EVIDENCE") is not { Length: > 0 } root) return;
            Directory.CreateDirectory(root);
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Elsa.Server.slnx"))) repository = repository.Parent;
            Assert.NotNull(repository);
            var sourceSha = await GitAsync("rev-parse", "HEAD");
            Assert.Equal(string.Empty, await GitAsync("status", "--porcelain"));
            var names = new HashSet<string>(["Elsa.Activities.Bpmn.Tests", "Elsa.Activities.Bpmn", "Elsa.Workflows.Runtime.Core",
                "Elsa.Workflows.Runtime", "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore", "Bpmn.Model", "Bpmn.Semantics"], StringComparer.Ordinal);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => names.Contains(assembly.GetName().Name!)).ToArray();
            Assert.Equal(names.Count, assemblies.Length);
            var binaryHashes = assemblies.ToDictionary(assembly => Path.GetFileName(assembly.Location),
                assembly => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))));
            var binaryVersions = assemblies.ToDictionary(assembly => Path.GetFileName(assembly.Location),
                assembly => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty);
            Assert.EndsWith("+" + sourceSha, binaryVersions["Elsa.Activities.Bpmn.Tests.dll"]);
            var snapshot = Path.Combine(root, phase + ".sqlite");
            Assert.False(File.Exists(snapshot), "Use a new capture evidence directory.");
            await using (var source = new SqliteConnection($"Data Source={database};Pooling=False"))
            await using (var destination = new SqliteConnection($"Data Source={snapshot};Pooling=False"))
            {
                await source.OpenAsync();
                await destination.OpenAsync();
                source.BackupDatabase(destination);
            }
            var snapshotBytes = await File.ReadAllBytesAsync(snapshot);
            await File.WriteAllTextAsync(Path.Combine(root, phase + ".json"), JsonSerializer.Serialize(new
            {
                captureProvenance = new { version = 1, sourceSha, sourceDirty = false,
                    databaseSha256 = Convert.ToHexStringLower(SHA256.HashData(snapshotBytes)), databaseByteLength = snapshotBytes.Length,
                    binaryHashes, binaryVersions },
                capturedAt = DateTimeOffset.UtcNow, model = before.ExecutionId, origin, delayed,
                state = await reader.FindAsync(before.ExecutionId),
                native = await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId),
                modelQueue = (await queue.ListAsync(new(before.ExecutionId))).Items
            }, Web));

            async Task<string> GitAsync(params string[] arguments)
            {
                var start = new ProcessStartInfo("git") { WorkingDirectory = repository.FullName, UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in arguments) start.ArgumentList.Add(argument);
                using var gitProcess = Process.Start(start)!;
                var output = gitProcess.StandardOutput.ReadToEndAsync();
                var error = gitProcess.StandardError.ReadToEndAsync();
                await gitProcess.WaitForExitAsync();
                Assert.True(gitProcess.ExitCode == 0, await error);
                return (await output).Trim();
            }
        }
    }

    private static ExecutableNode DelayedCompensationWorker()
    {
        var forward = Leaf(typeof(WriteLine), "forward", "text", "Reserve before compensation.");
        var handler = Leaf(typeof(Fault), "handler");
        var fault = Leaf(typeof(Fault), "fault");
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(CompensationScope));
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        Bind("faultAfterCompensation", false);
        Bind("handlerMaxAttempts", 3);
        Bind("confirmOnComplete", false);
        Bind("handlerRetryDelay", TimeSpan.FromHours(1));
        Bind("handlerRetryBackoffMultiplier", 2d);
        Bind("handlerRetryMaxDelay", TimeSpan.FromHours(1));
        var structure = new CompensationScopeExecutableStructure([
            new("reserve", forward.ExecutableNodeId, handler.ExecutableNodeId), new("fail", fault.ExecutableNodeId, null)]);
        return new("scope", "authored-scope", typeof(CompensationScope).FullName!, "1.0.0", contract.DescriptorKind,
            contract.DescriptorPayload, bindings,
            new Dictionary<string, string> { [RuntimeMetadataKeys.StructuralFaultHandling] = RuntimeMetadataKeys.StructuralFaultHandlingDescendant },
            [new(CompensationScope.StepSlotName("reserve"), [forward]), new(CompensationScope.CompensationSlotName("reserve"), [handler]),
                new(CompensationScope.StepSlotName("fail"), [fault])],
            structure: new(CompensationScope.StructureKind, CompensationScope.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web)),
            activityContract: contract);

        void Bind<T>(string key, T value)
        {
            var input = contract.Inputs[key];
            bindings[key] = new(key, input.Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
        }
        static ExecutableNode Leaf(Type type, string id, string? inputKey = null, string? value = null)
        {
            var leafContract = ClrActivityContractTestBuilder.BuildContract(type);
            var inputs = new Dictionary<string, RuntimeInputBinding>();
            if (inputKey is not null)
            {
                var input = leafContract.Inputs[inputKey];
                inputs[inputKey] = new(inputKey, input.Type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
            }
            return new(id, "authored-" + id, type.FullName!, "1.0.0", leafContract.DescriptorKind, leafContract.DescriptorPayload,
                ClrActivityContractTestBuilder.CompleteInputBindings(leafContract, inputs),
                new Dictionary<string, string>(), activityContract: leafContract);
        }
    }
}
