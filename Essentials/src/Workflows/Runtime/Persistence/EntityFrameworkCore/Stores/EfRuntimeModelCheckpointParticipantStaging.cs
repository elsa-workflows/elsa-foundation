using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class EfRuntimeModelCheckpointParticipantStaging
{
    public static async ValueTask StageAsync(RuntimeDbContext context, RuntimeModelStateChange change,
        string scope, CancellationToken cancellationToken)
    {
        var state = change.State;
        var retainsState = change.ExpectedRevision == state.Revision;
        var id = CompositeId(scope, state.ExecutionId);
        var row = await context.Set<RuntimeModelExecutionEntity>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (row is null)
        {
            if (change.ExpectedRevision is not null)
                throw new DbUpdateConcurrencyException("The model execution expected by this decision is absent.");
            var nativeId = EfWorkflowExecutionStateStore.CreateIdForAtomicParticipant(scope, state.ExecutionId);
            if (await context.WorkflowExecutionStates.AnyAsync(x => x.Id == nativeId, cancellationToken))
                throw new InvalidOperationException("A model execution cannot replace an existing native execution.");
            row = new RuntimeModelExecutionEntity { Id = id };
            context.Set<RuntimeModelExecutionEntity>().Add(row);
        }
        else
        {
            var current = EfRuntimeModelExecutionReader.Read(row, scope, state.ExecutionId);
            if (change.ExpectedRevision != current.Revision)
                throw new DbUpdateConcurrencyException("The committed model execution advanced before this decision.");
            if (current.Protocol != state.Protocol || current.MaterialIncarnationId != state.MaterialIncarnationId ||
                !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(current.Executable, state.Executable) ||
                current.TenantId != state.TenantId || current.CreatedAt != state.CreatedAt || state.UpdatedAt < current.UpdatedAt ||
                !WorkflowExecutionAuthoritySnapshot.Matches(current.Authority, state.Authority.SystemIdentity, state.Authority.RootInitiator, state.Authority.Metadata))
                throw new InvalidOperationException("A model decision cannot change its execution authority or pinned material.");
            if (retainsState && RuntimeArtifactJson.Serialize(current) != RuntimeArtifactJson.Serialize(state))
                throw new InvalidOperationException("A retained-state input cannot change the committed model state.");
            if (!retainsState && (current.Lifecycle == RuntimeModelLifecycle.TerminalSettled ||
                current.Lifecycle == RuntimeModelLifecycle.TerminalPendingSettlement &&
                (state.Lifecycle is not (RuntimeModelLifecycle.TerminalPendingSettlement or RuntimeModelLifecycle.TerminalSettled) ||
                 !JsonElement.DeepEquals(current.TerminalResult!.Value, state.TerminalResult!.Value))))
                throw new InvalidOperationException("A terminal model execution cannot resume semantic progression or replace its result.");
        }

        await EnsureMaterialAsync(context, state, scope, cancellationToken);
        var input = change.Input;
        var inputId = CompositeId(scope, input.ExecutionId, input.InputId);
        var previous = await context.Set<RuntimeModelInputEntity>().SingleOrDefaultAsync(x => x.Id == inputId, cancellationToken);
        if (previous is not null)
        {
            _ = EfRuntimeModelExecutionReader.Read(previous, scope, input.ExecutionId, input.InputId);
            throw new InvalidOperationException("This model input already has an accepted decision; use its retained receipt.");
        }
        if (retainsState)
        {
            // Retain the exact stored bytes/timestamps, but still compare the physical row's revision at commit.
            // Without a forced update EF could omit this participant and accept a receipt for superseded state.
            context.Entry(row).Property(item => item.Revision).IsModified = true;
        }
        else
        {
            row.Id = id; row.ScopeKey = Encode(scope); row.ScopeKeyHash = Hash(scope);
            row.ExecutionId = Encode(state.ExecutionId); row.ExecutionIdHash = Hash(state.ExecutionId); row.ExecutionIdOrderKey = Order(state.ExecutionId);
            row.ArtifactIdHash = Hash(state.Executable.ArtifactId); row.Lifecycle = (int)state.Lifecycle;
            row.ContentJson = RuntimeArtifactJson.Serialize(state); row.SchemaVersion = RuntimeModelStateEfModule.SchemaVersion; row.Revision = state.Revision;
        }
        context.Set<RuntimeModelInputEntity>().Add(new()
        {
            Id = inputId, ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
            ExecutionId = Encode(input.ExecutionId), ExecutionIdHash = Hash(input.ExecutionId),
            InputId = Encode(input.InputId), InputIdHash = Hash(input.InputId),
            ContentJson = RuntimeArtifactJson.Serialize(input), SchemaVersion = RuntimeModelStateEfModule.SchemaVersion, Revision = 1
        });
    }

    private static async ValueTask EnsureMaterialAsync(RuntimeDbContext context, RuntimeModelExecutionState state,
        string scope, CancellationToken cancellationToken)
    {
        var artifactId = state.Executable.ArtifactId;
        var id = Hash($"{scope.Length}:{scope}{artifactId.Length}:{artifactId}");
        var row = await context.WorkflowExecutables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("The model decision's pinned material is unavailable.");
        var material = EfWorkflowExecutableStore.ReadMaterial(row, scope, artifactId, id);
        if (material.Model is null || material.IncarnationId != state.MaterialIncarnationId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, state.Executable) || material.Model.Protocol != state.Protocol)
            throw new InvalidDataException("The model decision's full physical pin or protocol does not match its material.");
    }
}
