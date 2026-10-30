using System.Reflection;
using System.Text.Json;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfModelAdmissionLeaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelling_one_admission_keeps_the_other_material_root_until_its_queue_write(bool unique)
    {
        await using var database = new TemporarySqliteDatabase("model-admission");
        var connectionString = database.ConnectionStringWith("Pooling=False");
        var access = new AdmissionAccess();
        RuntimeSqliteDbContext Open() => new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connectionString).Options);
        await using (var c = Open())
            await c.Database.EnsureCreatedAsync();
        var seed = new WorkflowExecutableIdentity("pending", "definition", "v1", "1.0.0", "pending");
        var definition = JsonSerializer.SerializeToElement(new { process = "leased-model" });
        var draft = new RuntimeModelMaterial(1, seed, new("test.model", 1), "test-profile", definition, new(1, []), [], [], []);
        var hash = draft.ComputeContentHash();
        var pin = seed with { ArtifactId = hash, ArtifactHash = hash };
        var model = new RuntimeModelMaterial(1, pin, draft.Protocol, draft.Profile, definition, draft.InputContract, [], [], []);
        string incarnation;
        await using (var c = Open())
        { var s = (IWorkflowExecutableStore)new EfWorkflowExecutableStore(c, access); await s.SaveModelAsync(model); incarnation = (await s.FindMaterialAsync(hash))!.IncarnationId; }
        var options = Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero });
        await using (var c = Open())
        {
            var store = (IWorkflowExecutableStore)new EfWorkflowExecutableStore(c, access);
            var sources = new EfWorkflowExecutableSourceReferenceStore(c, access, new AdmissionCodec());
            var at = DateTimeOffset.UtcNow;
            var reference = new WorkflowExecutableSourceReference("source-model", hash, "BpmnModel", "source-model", "1", pin.DefinitionId, pin.DefinitionVersionId, pin.ArtifactVersion, at, at, WorkflowExecutableReferenceScope.Published);
            await new WorkflowExecutableRootWriteLeaseManager(store, options, TimeProvider.System).ExecuteFencedAsync([pin], "initial-publication:" + Guid.NewGuid().ToString("N"), async (grants, t) => { _ = await sources.SaveWithRootWriteLeasesAsync(reference, pin, grants, t); });
        }
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopFirst = new CancellationTokenSource();
        await using var firstContext = Open();
        await using var secondContext = Open();

        var services = new ServiceCollection();
        services.AddScoped<AdmissionDriver>();
        using var provider = services.BuildServiceProvider();

        object Router(RuntimeSqliteDbContext context, bool first)
        {
            IWorkflowExecutableStore store = new EfWorkflowExecutableStore(context, access);
            var actualQueue = new EfSchedulerWorkQueueStore(context, access, new AdmissionCodec());
            var queue = DispatchProxy.Create<IWorkflowSchedulerWorkQueue, AdmissionQueueGate>();
            var gate = (AdmissionQueueGate)(object)queue;
            gate.Inner = actualQueue;
            gate.Entered = first ? firstEntered : secondEntered;
            gate.Release = first ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : releaseSecond;
            var assembly = typeof(WorkflowExecutableRootWriteLeaseManager).Assembly;
            Type Type(string name) => assembly.GetType("Elsa.Workflows.Runtime.Services.Executions." + name, true)!;
            var registry = Activator.CreateInstance(Type("RuntimeExecutionProtocolRegistry"), true)!;
            Type("RuntimeExecutionProtocolRegistry").GetMethod("Initialize")!.Invoke(registry, [new[] { new RuntimeExecutionProtocolRegistration(model.Protocol, typeof(AdmissionDriver)) { InputKinds = ["start"] } }, services]);
            var readerType = typeof(EfWorkflowExecutableStore).Assembly.GetType("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeModelExecutionReader", true)!;
            var reader = Activator.CreateInstance(readerType, [context, access, new AdmissionCodec()])!;
            var native = new EfWorkflowExecutionStateStore(context, access, new AdmissionCodec());
            var commits = new EfRuntimeCheckpointCommitStore(context, access);
            var selector = Activator.CreateInstance(Type("RuntimeExecutionProtocolSelector"), [registry, store, native, commits, access, provider, queue, reader])!;
            IWorkflowExecutableRootWriteLeaseManager leases = new WorkflowExecutableRootWriteLeaseManager(store, options, TimeProvider.System);
            if (unique)
                leases = new AdmissionUniqueLeaseManager(leases);
            return Activator.CreateInstance(Type("RuntimeExecutionProtocolRouter"), [selector, null!, queue, new AdmissionStubDrain(), new RuntimeExecutionOwnershipService(new InMemoryExecutionLivenessStateStore()), new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), null!, TimeProvider.System, leases, null!])!;
        }
        async Task Process(object router, string execution, CancellationToken token)
        {
            var now = DateTimeOffset.UtcNow;
            var command = new RuntimeModelExecutionCommand(pin, incarnation, "start", "start", JsonSerializer.SerializeToElement(new { inputs = new { }, sourceReferenceId = "source-model" }), new("system", execution), null);
            var envelope = new WorkflowExecutionCommandEnvelope(execution, execution, new("start", execution, WorkflowExecutionCommandKind.ExecuteModel, now, JsonSerializer.SerializeToElement(command, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new Dictionary<string, string>()), execution, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now);
            var result = (ValueTask<WorkflowExecutionCommandProcessResult>)router.GetType().GetMethod("ProcessAsync")!.Invoke(router, [envelope, WorkflowExecutionCommandDispatchOptions.Default, token])!;
            await result;
        }
        var firstTask = Process(Router(firstContext, true), "first-execution", stopFirst.Token);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(60));
        var secondTask = Process(Router(secondContext, false), "second-execution", CancellationToken.None);
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await stopFirst.CancelAsync();
        string firstFailure;
        try
        { await firstTask; firstFailure = "none"; }
        catch (Exception ex) { firstFailure = ex.GetType().Name; }
        int deleted;
        bool sourceRetired;
        await using (var c = Open())
        {
            var sources = new EfWorkflowExecutableSourceReferenceStore(c, access, new AdmissionCodec());
            sourceRetired = await sources.RetireAsync("source-model", DateTimeOffset.UtcNow, "Qualification retirement while both initial admissions are pending");
            var gc = new WorkflowExecutableReferenceGarbageCollector(new EfWorkflowExecutableStore(c, access), sources, new EfWorkflowExecutionStateStore(c, access, new AdmissionCodec()), options, TimeProvider.System, NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);
            deleted = (await gc.SweepAsync()).DeletedArtifactCount;
        }
        releaseSecond.SetResult();
        string secondFailure;
        try
        { await secondTask; secondFailure = "none"; }
        catch (Exception ex) { secondFailure = ex.GetType().Name + ": " + ex.Message; }
        await using var final = Open();
        var exists = await ((IWorkflowExecutableStore)new EfWorkflowExecutableStore(final, access)).FindMaterialAsync(hash) is not null;
        var pending = await new EfSchedulerWorkQueueStore(final, access, new AdmissionCodec()).ListAsync(new("second-execution"));
        Assert.Equal("TaskCanceledException", firstFailure);
        Assert.True(sourceRetired);
        Assert.Equal(0, deleted);
        Assert.Equal("none", secondFailure);
        Assert.True(exists);
        Assert.Single(pending.Items);
    }
}

public class AdmissionQueueGate : DispatchProxy
{
    public IWorkflowSchedulerWorkQueue Inner = null!; public TaskCompletionSource Entered = null!; public TaskCompletionSource Release = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == nameof(IWorkflowSchedulerWorkQueue.EnqueueAsync))
            return Enqueue((RuntimeSchedulerWorkItem)args![0]!, (CancellationToken)args[1]!);
        try
        { return method.Invoke(Inner, args); }
        catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
    }
    async ValueTask<RuntimeSchedulerWorkItem> Enqueue(RuntimeSchedulerWorkItem work, CancellationToken token)
    { Entered.SetResult(); await Release.Task.WaitAsync(token); return await Inner.EnqueueAsync(work, token); }
}
public sealed class AdmissionDriver : IRuntimeExecutionProtocolDriver
{
    public ValueTask ValidateMaterialAsync(RuntimeModelMaterial m, CancellationToken t = default) => ValueTask.CompletedTask;
    public ValueTask ValidateInputAsync(RuntimeModelExecutionContext c, CancellationToken t = default) => ValueTask.CompletedTask;
    public ValueTask ExecuteAsync(RuntimeModelExecutionContext c, CancellationToken t = default) => ValueTask.CompletedTask;
}
sealed class AdmissionUniqueLeaseManager(IWorkflowExecutableRootWriteLeaseManager inner) : IWorkflowExecutableRootWriteLeaseManager
{
    public ValueTask ExecuteAsync(string id, string lease, Func<CancellationToken, ValueTask> write, CancellationToken t = default) => inner.ExecuteAsync(id, lease + ":" + Guid.NewGuid().ToString("N"), write, t);
    public ValueTask ExecuteAsync(WorkflowExecutableIdentity pin, string lease, Func<CancellationToken, ValueTask> write, CancellationToken t = default) => inner.ExecuteAsync(pin, lease + ":" + Guid.NewGuid().ToString("N"), write, t);
}
sealed class AdmissionStubDrain : IWorkflowSchedulerDrainer { public ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest r, CancellationToken t = default) => ValueTask.FromResult(new RuntimeSchedulerDrainResult(r.WorkflowExecutionId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [])); }
sealed class AdmissionAccess : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("default")); }
sealed class AdmissionCodec : IRuntimeRecoveryContinuationCodec { public string Encode(string p, ReadOnlySpan<byte> b) => p + ":" + Convert.ToBase64String(b); public byte[] Decode(string p, string t) => Convert.FromBase64String(t[(p.Length + 1)..]); }
