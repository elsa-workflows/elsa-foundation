using System.Data;
using System.Text.Json;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>One physical slot/source/material/ordinary-queue admission over the selected EF runtime stores.</summary>
public sealed class EfAuthoredModelQueue : IAuthoredModelQueue
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly EfWriteRetry Writes = new(EfWriteRetry.DefaultMaxAttempts,
        EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient);
    private readonly RuntimeDbContext context;
    private readonly EfSchedulerWorkQueueStore queue;
    private readonly EfWorkflowExecutableStore materials;
    private readonly EfWorkflowExecutableSourceReferenceStore sources;
    private readonly EfWorkflowActivationAuthority slots;
    private readonly EfRuntimeModelExecutionReader models;
    private readonly IPersistenceAccessContextAccessor access;
    private readonly TimeProvider clock;

    public EfAuthoredModelQueue(IWorkflowSchedulerWorkQueue selectedQueue, IWorkflowExecutableStore selectedMaterials,
        IWorkflowExecutableSourceReferenceStore selectedSources, IWorkflowActivationAuthority selectedSlots,
        IRuntimeModelExecutionReader selectedModels, IPersistenceAccessContextAccessor access, TimeProvider clock,
        CoalescingInner<IWorkflowSchedulerWorkQueue>? physicalQueue = null)
    {
        queue = (selectedQueue.GetType() == typeof(CoalescingWorkflowSchedulerWorkQueue) ? physicalQueue?.Value : selectedQueue)
            as EfSchedulerWorkQueueStore ?? throw Unsupported();
        context = queue.Context;
        materials = selectedMaterials.GetMaterialStore() as EfWorkflowExecutableStore ?? throw Unsupported();
        sources = selectedSources as EfWorkflowExecutableSourceReferenceStore ?? throw Unsupported();
        slots = selectedSlots as EfWorkflowActivationAuthority ?? throw Unsupported();
        models = selectedModels as EfRuntimeModelExecutionReader ?? throw Unsupported();
        this.access = access; this.clock = clock;
        if (!ReferenceEquals(context, materials.Context) || !ReferenceEquals(context, sources.Context) ||
            !ReferenceEquals(context, slots.Context) || !models.Uses(context, access)) throw Unsupported();
    }

    public async ValueTask<WorkflowExecutionCommandDispatchResult> AdmitAsync(AuthoredModelAdmissionRequest request,
        RuntimeSchedulerWorkItem work, IReadOnlyList<WorkflowExecutableRootWriteLease> grants, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(work); ArgumentNullException.ThrowIfNull(grants);
        Validate(request, work);
        cancellationToken.ThrowIfCancellationRequested();
        var scope = access.Current.RequireScope().Value;
        access.Current.EnsureTenantScope(request.Command.TenantId);
        foreach (var participant in new[] { queue.AccessContext, materials.AccessContext, sources.AccessContext, slots.AccessContext })
        {
            if (participant.RequireScope().Value != scope) throw Unsupported();
            participant.EnsureTenantScope(request.Command.TenantId);
        }
        if (context.Database.CurrentTransaction is not null || context.Database.GetEnlistedTransaction() is not null ||
            Transaction.Current is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Authored model admission requires a clean context and its own transaction.");
        try
        {
            return await Writes.RunAsync(context, AttemptAsync,
                conflict => ValueTask.FromException<WorkflowExecutionCommandDispatchResult>(Unavailable(conflict)), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException || EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { throw Unavailable(exception); }

        async ValueTask<WorkflowExecutionCommandDispatchResult> AttemptAsync()
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            try
            {
                // Reconcile actual durable acceptance before checking publication eligibility again.
                if (await AcceptedAsync(request, work, cancellationToken))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return Result(WorkflowExecutionCommandDispatchStatus.Duplicate);
                }
                if (await models.FindAsync(request.WorkflowExecutionId, cancellationToken) is not null ||
                    (await queue.ListAsync(new(request.WorkflowExecutionId, limit: 1), cancellationToken)).Items.Count != 0)
                    throw Refused("authored-execution-exists", "The execution identity already has different committed model work.");
                var closure = new List<RuntimeExecutionMaterial>();
                foreach (var grant in grants)
                    closure.Add(await ((IWorkflowExecutableStore)materials).FindMaterialAsync(grant.ArtifactId, cancellationToken)
                        ?? throw Refused("authored-material-missing", "The exact model material closure is unavailable."));
                var root = closure.SingleOrDefault(item => item.Identity.ArtifactId == request.Material.Identity.ArtifactId);
                if (root?.Model is null || root.IncarnationId != request.Material.IncarnationId ||
                    !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(root.Identity, request.Material.Identity) ||
                    !WorkflowExecutableDependencyGraph.ResolveMaterialClosure([root.Identity], closure).Select(item => item.Identity.ArtifactId)
                        .ToHashSet(StringComparer.Ordinal).SetEquals(grants.Select(item => item.ArtifactId)))
                    throw Refused("authored-material-changed", "The exact model material and complete grants differ.");
                var deadlines = await materials.FenceRootWriteLeasesAsync(grants, clock, cancellationToken);
                if (!await EfAdmissionTargetFence.LockSourceAsync(sources, scope, request.Source, cancellationToken) ||
                    !await EfAdmissionTargetFence.LockSlotAsync(context, scope, request.Slot.WorkflowDefinitionId, request.Slot.SlotName, request.Slot, cancellationToken))
                    throw Refused("authored-publication-changed", "The selected model publication changed before admission.");
                EnsureLive();
                var actual = await queue.EnqueueAsync(work, cancellationToken);
                CheckCommand(actual, request.Command);
                if (!await AcceptedAsync(request, work, cancellationToken))
                    throw Refused("authored-queue-unconfirmed", "The actual ordinary model queue did not retain this command.");
                EnsureLive();
                await transaction.CommitAsync(cancellationToken);
                return Result(WorkflowExecutionCommandDispatchStatus.Accepted);

                void EnsureLive()
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!request.Source.IsLive(clock.GetUtcNow())) throw Refused("authored-source-expired", "The model publication expired before admission committed.");
                    foreach (var deadline in deadlines)
                        if (clock.GetUtcNow() >= deadline.ExpiresAt)
                            throw new WorkflowExecutableRootWriteLeaseLostException(deadline.Grant.ArtifactId, deadline.Grant.LeaseId);
                }
            }
            finally { context.ChangeTracker.Clear(); }
        }

        WorkflowExecutionCommandDispatchResult Result(WorkflowExecutionCommandDispatchStatus status) =>
            new(work.EnvelopeId, request.WorkflowExecutionId, status, clock.GetUtcNow());
        RuntimeSchedulerWorkUnavailableException Unavailable(Exception? cause) => new("runtime.model.authored-admission-unavailable",
            $"Protected authored model admission for '{request.WorkflowExecutionId}' could not commit.", cause);
    }

    private async ValueTask<bool> AcceptedAsync(AuthoredModelAdmissionRequest request, RuntimeSchedulerWorkItem work, CancellationToken cancellationToken)
    {
        if (await queue.FindAdmissionAsync(request.WorkflowExecutionId, work.WorkItemId, cancellationToken) is { } queued)
        { CheckCommand(queued, request.Command); return true; }
        if (await models.FindInputAsync(request.WorkflowExecutionId, request.Command.InputId, cancellationToken) is not { } receipt) return false;
        if (receipt.InputKind != request.Command.InputKind || receipt.ContentFingerprint != RuntimeCheckpointCommitFingerprint.ComputeModelInput(request.Command))
            throw new RuntimeCheckpointReplayConflictException(request.Command.InputId);
        var state = await models.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (state is null || state.TenantId != request.Command.TenantId || state.MaterialIncarnationId != request.Material.IncarnationId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.Executable, request.Material.Identity) ||
            !WorkflowExecutionAuthoritySnapshot.Matches(state.Authority, request.Command.Authority.SystemIdentity,
                request.Command.Authority.RootInitiator, request.Command.Authority.Metadata))
            throw new RuntimeCheckpointReplayConflictException(request.Command.InputId);
        return true;
    }

    private static void Validate(AuthoredModelAdmissionRequest request, RuntimeSchedulerWorkItem work)
    {
        ArgumentNullException.ThrowIfNull(request.Slot); ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentNullException.ThrowIfNull(request.Material); ArgumentNullException.ThrowIfNull(request.Command);
        var source = request.Source; var slot = request.Slot; var command = request.Command;
        if (request.Material.Model is null) throw new NotSupportedException("The authored model queue cannot admit native material.");
        if (slot.ActiveActivationId is null || slot.Revision <= 0 || slot.Source?.IsSameOwnerAs(WorkflowActivationSource.Publishing) != true ||
            slot.SlotId != WorkflowActivationSlotIdentity.Create(slot.WorkflowDefinitionId, slot.SlotName) ||
            source.DefinitionId != slot.WorkflowDefinitionId || source.SlotId != slot.SlotId || source.ActivationId != slot.ActiveActivationId ||
            source.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(slot.ActiveActivationId) ||
            source.ArtifactId != request.Material.Identity.ArtifactId || source.Scope != WorkflowExecutableReferenceScope.Published ||
            source.DeletedAt is not null || source.PublishedAt is null || source.TenantId != command.TenantId ||
            command.MaterialIncarnationId != request.Material.IncarnationId || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(command.Executable, request.Material.Identity) ||
            work.WorkflowExecutionId != request.WorkflowExecutionId || work.CommandId != command.InputId ||
            work.WorkItemId != RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(command.InputId) || command.InputKind != "start-authored-v1" ||
            !command.Payload.TryGetProperty("authored", out var provenance) || provenance.Deserialize<AuthoredModelEntry>(Web) is not { FormatVersion: 1 } entry ||
            entry.Slot != slot || !WorkflowExecutableSourceReferenceComparer.SameSnapshot(entry.Source, source) ||
            command.Payload.GetProperty("sourceReferenceId").GetString() != source.SourceReferenceId)
            throw Refused("authored-context-mismatch", "The authored command differs from its exact logical publication and pinned model material.");
        CheckCommand(work, command);
    }

    private static void CheckCommand(RuntimeSchedulerWorkItem work, RuntimeModelExecutionCommand expected)
    {
        if (work.CommandKind != WorkflowExecutionCommandKind.ExecuteModel || work.CommandId != expected.InputId ||
            work.Payload?.Deserialize<RuntimeModelExecutionCommand>(Web) is not { } command ||
            RuntimeCheckpointCommitFingerprint.ComputeModelInput(command) != RuntimeCheckpointCommitFingerprint.ComputeModelInput(expected))
            throw new RuntimeCheckpointReplayConflictException(expected.InputId);
    }
    private static WorkflowExecutableStartRejectedException Refused(string code, string message) => new(WorkflowExecutableStartRejectionCategory.Authority, code, message);
    private static NotSupportedException Unsupported() => new("Authored model admission requires the actual shared EF model material, queue, reader, source and slot participants.");
}
