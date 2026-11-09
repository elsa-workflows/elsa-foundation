using System.Text.Json;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Services;

/// <summary>
/// The runtime state view of an agent occurrence: only whether it is the agent's own invocation boundary, so run inspection
/// can link it to the agent's evidence. Agents are user-defined, so their activity types are not known in advance; the
/// projector reads every node of the specialist descriptor kind. It carries no turn content, values or journal facts:
/// the linked page reads and re-authorizes those itself.
/// </summary>
internal sealed class SpecialistInvocationStateViewProjector : IActivityStateViewProjector
{
    internal const string Kind = "elsa.specialist.invocation";

    // A key no private state uses: the boundary is recognized by its descriptor kind, with or without private state.
    public string StateTypeAlias => "Elsa.Agent.Specialists.InvocationReference";
    public string? DescriptorKind => SpecialistRuntimeIds.ConsumerKey;
    public string ViewKind => Kind;
    public int ViewVersion => 1;

    public ValueTask<JsonElement> ProjectAsync(ActivityStateViewContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Project(context));

    public ValueTask<JsonElement?> ProjectWithoutStateAsync(ActivityStateViewContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<JsonElement?>(Project(context));

    private static JsonElement Project(ActivityStateViewContext context) =>
        JsonSerializer.SerializeToElement(new { invocation = context.ExecutableNode.ActivityContract?.Activation.Capability == SpecialistRuntimeIds.FacadeCapability });
}
