using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfAuthoredModelAdmissionTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_committed_ordinary_queue_row_survives_unpublication_and_reconciles_before_fresh_eligibility()
    {
        await using var host = await Harness.OpenAsync();
        var request = await host.PublishAsync();
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await host.AdmitAsync(request)).Status);
        await host.UnpublishAsync(request);

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, (await host.AdmitAsync(request)).Status);
        var queued = Assert.Single((await host.Queue.ListAsync(new(request.WorkflowExecutionId))).Items);
        Assert.Equal(WorkflowExecutionCommandKind.ExecuteModel, queued.CommandKind);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeModelInput(request.Command), RuntimeCheckpointCommitFingerprint.ComputeModelInput(
            queued.Payload!.Value.Deserialize<RuntimeModelExecutionCommand>(Web)!));
        var fresh = host.Start(request.Material, request.Slot, request.Source, "fresh-after-unpublish");
        var refusal = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => host.AdmitAsync(fresh));
        Assert.Equal("authored-publication-changed", refusal.ReasonCode);
        Assert.Empty((await host.Queue.ListAsync(new(fresh.WorkflowExecutionId))).Items);
        Assert.Null(await host.Materials.FindAsync(request.Material.Identity.ArtifactId));
    }

    [Fact]
    public async Task Unpublication_that_wins_before_admission_prevents_any_ordinary_queue_write()
    {
        await using var host = await Harness.OpenAsync();
        var request = await host.PublishAsync();
        await host.UnpublishAsync(request);

        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => host.AdmitAsync(request));

        Assert.Empty((await host.Queue.ListAsync(new(request.WorkflowExecutionId))).Items);
        Assert.Null(await host.Models.FindAsync(request.WorkflowExecutionId));
    }

    [Fact]
    public async Task Queue_commit_holds_source_and_slot_authority_until_the_actual_row_is_durable()
    {
        var queueGate = new QueueWriteGate();
        await using var host = await Harness.OpenAsync(queueGate);
        var request = await host.PublishAsync();
        queueGate.Armed = true;
        var admission = host.AdmitAsync(request);
        await queueGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var slotGate = new SlotWriteSignal();
        await using var other = host.OpenOther(slotGate);
        var otherMaterial = new EfWorkflowExecutableStore(other, host.Access);
        var otherSources = new EfWorkflowExecutableSourceReferenceStore(other, host.Access, new AdmissionCodec());
        var otherSlots = new EfWorkflowActivationAuthority(other, host.Access);
        var bindings = new EfWorkflowTriggerBindingStore(other, host.Access);
        var schedules = new EfRecurringTriggerScheduleStore(other, host.Access, new AdmissionCodec());
        var coordinator = new WorkflowActivationCoordinator(otherMaterial, otherSlots,
            new EfWorkflowActivationSwitch(otherSlots, bindings, otherSources, host.Access, TimeProvider.System, schedules), otherSources,
            new WorkflowExecutableRootWriteLeaseManager(otherMaterial, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), TimeProvider.System),
            TimeProvider.System, triggerBindingStore: bindings, recurringScheduleStore: schedules);
        var unpublish = Task.Run(async () => await coordinator.DeactivateMaterialAsync(new("logical-definition", request.Material, "default",
            WorkflowActivationSource.Publishing, request.Slot.Revision)));
        try
        {
            await slotGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(unpublish.IsCompleted);
        }
        finally { queueGate.Release.TrySetResult(); }

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await admission).Status);
        Assert.True((await unpublish).Succeeded);
        Assert.Single((await host.Queue.ListAsync(new(request.WorkflowExecutionId))).Items);
        Assert.Null((await host.Slots.FindAsync("logical-definition", "default"))!.ActiveActivationId);
    }

    [Fact]
    public async Task A_lost_commit_acknowledgement_replays_the_original_queue_after_unpublication()
    {
        var fault = new CommitFault();
        await using var host = await Harness.OpenAsync(new QueueCommitArm(fault), new LostCommitAck(fault));
        var request = await host.PublishAsync();
        fault.Armed = true;
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => host.AdmitAsync(request));
        Assert.Single((await host.Queue.ListAsync(new(request.WorkflowExecutionId))).Items);
        await host.UnpublishAsync(request);

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, (await host.AdmitAsync(request)).Status);
        Assert.Single((await host.Queue.ListAsync(new(request.WorkflowExecutionId))).Items);
    }

    [Fact]
    public async Task Mixed_physical_contexts_refuse_before_any_queue_write()
    {
        await using var host = await Harness.OpenAsync();
        var request = await host.PublishAsync();
        await using var other = host.OpenOther();
        Assert.Throws<NotSupportedException>(() => new EfAuthoredModelQueue(host.Queue, host.Materials,
            new EfWorkflowExecutableSourceReferenceStore(other, host.Access, new AdmissionCodec()), host.Slots, host.Models, host.Access, TimeProvider.System));
        Assert.Empty((await host.Queue.ListAsync(new(request.WorkflowExecutionId))).Items);
    }

    [Fact]
    public async Task Malformed_authored_provenance_retains_its_decoding_cause_without_accepting_work()
    {
        await using var host = await Harness.OpenAsync();
        var valid = await host.PublishAsync();
        var command = valid.Command;
        var payload = JsonSerializer.SerializeToElement(new { authored = "invalid-provenance", sourceReferenceId = valid.Source.SourceReferenceId });
        var corrupt = valid with { Command = new(command.Executable, command.MaterialIncarnationId, command.InputId,
            command.InputKind, payload, command.Authority, command.TenantId) };
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => host.AdmitAsync(corrupt));
        Assert.Equal("runtime.model.authored-admission-unavailable", failure.Code);
        Assert.IsType<JsonException>(failure.InnerException);
        Assert.Contains(valid.WorkflowExecutionId, failure.SafeSummary);
        Assert.Empty((await host.Queue.ListAsync(new(valid.WorkflowExecutionId))).Items);
        Assert.Null(await host.Models.FindAsync(valid.WorkflowExecutionId));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, (await host.AdmitAsync(valid)).Status);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly TemporarySqliteDatabase database;
        private readonly RuntimeSqliteDbContext context;
        private readonly IWorkflowExecutableRootWriteLeaseManager leases;
        public AdmissionAccess Access { get; } = new();
        public EfWorkflowExecutableStore Materials { get; }
        public EfWorkflowExecutableSourceReferenceStore Sources { get; }
        public EfWorkflowActivationAuthority Slots { get; }
        public EfSchedulerWorkQueueStore Queue { get; }
        public IRuntimeModelExecutionReader Models { get; }
        public WorkflowActivationCoordinator Coordinator { get; }
        public EfAuthoredModelQueue Participant { get; }

        private Harness(TemporarySqliteDatabase database, RuntimeSqliteDbContext context)
        {
            this.database = database; this.context = context;
            Materials = new(context, Access); Sources = new(context, Access, new AdmissionCodec());
            Slots = new(context, Access); Queue = new(context, Access, new AdmissionCodec());
            var reader = typeof(EfWorkflowExecutableStore).Assembly.GetType("Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeModelExecutionReader", true)!;
            Models = (IRuntimeModelExecutionReader)Activator.CreateInstance(reader, [context, Access, new AdmissionCodec()])!;
            leases = new WorkflowExecutableRootWriteLeaseManager(Materials, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), TimeProvider.System);
            var bindings = new EfWorkflowTriggerBindingStore(context, Access);
            var schedules = new EfRecurringTriggerScheduleStore(context, Access, new AdmissionCodec());
            Coordinator = new(Materials, Slots, new EfWorkflowActivationSwitch(Slots, bindings, Sources, Access, TimeProvider.System, schedules),
                Sources, leases, TimeProvider.System, triggerBindingStore: bindings, recurringScheduleStore: schedules);
            Participant = new(Queue, Materials, Sources, Slots, Models, Access, TimeProvider.System);
        }

        public static async Task<Harness> OpenAsync(params IInterceptor[] interceptors)
        {
            var database = new TemporarySqliteDatabase("authored-model-admission");
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(database.ConnectionStringWith("Pooling=False"))
                .AddInterceptors(interceptors).Options;
            var context = new RuntimeSqliteDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new(database, context);
        }

        public RuntimeSqliteDbContext OpenOther(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite(database.ConnectionStringWith("Pooling=False")).AddInterceptors(interceptors).Options);

        public async Task<AuthoredModelAdmissionRequest> PublishAsync()
        {
            var seed = new WorkflowExecutableIdentity("pending", "material-producer", "producer-version", "1", "pending");
            var model = new RuntimeModelMaterial(1, seed, new("test.model", 1), "test-v1", JsonSerializer.SerializeToElement(new { process = "model" }), new(1, []), [], [], []);
            var hash = model.ComputeContentHash();
            await ((IWorkflowExecutableStore)Materials).SaveModelAsync(new(1, seed with { ArtifactId = hash, ArtifactHash = hash }, model.Protocol, model.Profile, model.Definition, model.InputContract, [], [], []));
            var material = (await ((IWorkflowExecutableStore)Materials).FindMaterialAsync(hash))!;
            var now = DateTimeOffset.UtcNow;
            var source = new WorkflowExecutableSourceReference(WorkflowActivationReferenceIdentity.Create("publication"), hash, "authored-model", "logical-version", "1",
                "logical-definition", "logical-version", "1", now, now, WorkflowExecutableReferenceScope.Published);
            var activated = await Coordinator.ActivateMaterialAsync(new("logical-definition", material, source, "default", "publication", WorkflowActivationSource.Publishing, 0));
            Assert.True(activated.Succeeded);
            return Start(material, activated.Slot, (await Sources.FindAsync(source.SourceReferenceId))!, "start");
        }

        public AuthoredModelAdmissionRequest Start(RuntimeExecutionMaterial material, WorkflowActivationSlot slot, WorkflowExecutableSourceReference source, string input)
        {
            var payload = JsonSerializer.SerializeToElement(new { inputs = new { }, sourceReferenceId = source.SourceReferenceId,
                authored = new AuthoredModelEntry(1, slot, source), submittedInputs = new { } }, Web);
            var command = new RuntimeModelExecutionCommand(material.Identity, material.IncarnationId, input, "start-authored-v1", payload,
                new("test-model", "author"), null);
            return new(slot, source, material, "execution:" + input, command, DateTimeOffset.UtcNow);
        }

        public async Task<WorkflowExecutionCommandDispatchResult> AdmitAsync(AuthoredModelAdmissionRequest request)
        {
            var envelope = "envelope:" + request.Command.InputId;
            var work = new RuntimeSchedulerWorkItem(RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(request.Command.InputId), request.WorkflowExecutionId,
                request.Command.InputId, WorkflowExecutionCommandKind.ExecuteModel, envelope, envelope, request.RecordedAt, request.RecordedAt,
                payload: JsonSerializer.SerializeToElement(request.Command, Web));
            WorkflowExecutionCommandDispatchResult? result = null;
            await leases.ExecuteFencedAsync([request.Material.Identity], "test-admission:" + Guid.NewGuid().ToString("N"),
                async (grants, token) => { result = await Participant.AdmitAsync(request, work, grants, token); });
            return result!;
        }

        public async Task UnpublishAsync(AuthoredModelAdmissionRequest request)
        {
            Assert.True((await Coordinator.DeactivateMaterialAsync(new("logical-definition", request.Material, "default", WorkflowActivationSource.Publishing, request.Slot.Revision))).Succeeded);
            await Sources.RetireAsync(request.Source.SourceReferenceId, DateTimeOffset.UtcNow, "publication-unpublished");
        }
        public async ValueTask DisposeAsync() { await context.DisposeAsync(); await database.DisposeAsync(); }
    }

    private sealed class QueueWriteGate : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<SchedulerWorkItemEntity>().Any(item => item.State == EntityState.Added))
            { Armed = false; Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }
    private sealed class SlotWriteSignal : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
    private sealed class CommitFault { public bool Armed; public bool QueueWritten; }
    private sealed class QueueCommitArm(CommitFault fault) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (fault.Armed && eventData.Context!.ChangeTracker.Entries<SchedulerWorkItemEntity>().Any(item => item.State == EntityState.Added)) fault.QueueWritten = true;
            return ValueTask.FromResult(result);
        }
    }
    private sealed class LostCommitAck(CommitFault fault) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (fault.Armed && fault.QueueWritten) { fault.Armed = false; throw new IOException("The physical queue committed before its acknowledgement was lost."); }
            return Task.CompletedTask;
        }
    }
}
