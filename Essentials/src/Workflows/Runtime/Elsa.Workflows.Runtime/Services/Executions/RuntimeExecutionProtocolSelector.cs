using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>Protocol authority comes from checked physical material and committed state, never an input hint.</summary>
internal sealed class RuntimeExecutionProtocolSelector(RuntimeExecutionProtocolRegistry registry,
    IWorkflowExecutableStore materialStore,
    IWorkflowExecutionStateStore nativeStateReader, IRuntimeCheckpointCommitStore checkpointStore,
    IPersistenceAccessContextAccessor access, IServiceProvider scopedServices, IWorkflowSchedulerWorkQueue queue,
    IRuntimeModelExecutionReader? stateReader = null)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    public bool IsSelectedModel(string executionId) => selected.Contains(executionId);

    public async ValueTask<bool> IsModelAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken)
    {
        if (envelope.Command.Kind == WorkflowExecutionCommandKind.ExecuteModel) return true;
        if (stateReader is null && checkpointStore.SupportsModelCheckpoint)
            throw Unavailable("The selected model persistence has no state reader.");
        var state = stateReader is null ? null : await stateReader.FindAsync(envelope.WorkflowExecutionId, cancellationToken);
        if (state is null)
        {
            var head = await queue.ListAsync(new(envelope.WorkflowExecutionId, limit: 1), cancellationToken);
            return head.Items.FirstOrDefault()?.CommandKind == WorkflowExecutionCommandKind.ExecuteModel;
        }
        selected.Add(envelope.WorkflowExecutionId);
        return true;
    }

    public async ValueTask<Selection> SelectAsync(RuntimeSchedulerWorkItem work, CancellationToken cancellationToken)
    {
        if (work.CommandKind != WorkflowExecutionCommandKind.ExecuteModel || work.Payload is null)
            throw Unavailable("The scheduler item is not a supported model input.");
        if (stateReader is null || !materialStore.SupportsModelMaterial || !checkpointStore.SupportsModelCheckpoint ||
            !checkpointStore.CanCommitModelState(stateReader))
            throw Unavailable("The selected physical stores do not support atomic model execution.");
        var command = work.Payload.Value.Deserialize<RuntimeModelExecutionCommand>(Web)
            ?? throw Unavailable("The model input envelope is missing.");
        if (command.InputId != work.CommandId) throw Unavailable("The model input differs from its durable command identity.");
        var scope = access.Current.Scope ?? throw Unavailable("A model execution requires a physical persistence scope.");
        access.Current.EnsureTenantScope(command.TenantId);
        var state = await stateReader.FindAsync(work.WorkflowExecutionId, cancellationToken);
        if (await nativeStateReader.FindAsync(work.WorkflowExecutionId, cancellationToken) is not null)
            throw Unavailable("The execution identity already belongs to the native protocol.");
        var material = await materialStore.FindMaterialAsync(command.Executable.ArtifactId, cancellationToken);
        if (material?.Model is not { } model || material.IncarnationId != command.MaterialIncarnationId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(material.Identity, command.Executable))
            throw Unavailable("The exact pinned model material is unavailable.");
        if (state is not null && (state.Scope != scope || state.Protocol != model.Protocol ||
            state.MaterialIncarnationId != material.IncarnationId || state.TenantId != command.TenantId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.Executable, material.Identity) ||
            !WorkflowExecutionAuthoritySnapshot.Matches(state.Authority, command.Authority.SystemIdentity,
                command.Authority.RootInitiator, command.Authority.Metadata)))
            throw Unavailable("The model input does not match its committed execution authority and material.");
        var registration = registry.Find(model.Protocol) ?? throw Unavailable("The committed execution protocol is not installed.");
        if (!registration.InputKinds.Contains(command.InputKind)) throw Unavailable("The execution protocol does not support this input kind.");
        var driver = (IRuntimeExecutionProtocolDriver)scopedServices.GetRequiredService(registration.DriverType);
        await driver.ValidateMaterialAsync(model, cancellationToken);
        selected.Add(work.WorkflowExecutionId);
        return new(new(scope, material, state, command, work), driver);
    }

    private static RuntimeSchedulerWorkUnavailableException Unavailable(string summary) =>
        new("runtime.model.protocol-unavailable", summary);
    internal sealed record Selection(RuntimeModelExecutionContext Context, IRuntimeExecutionProtocolDriver Driver);
}
