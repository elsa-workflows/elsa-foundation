using Elsa.Activities.Compensation.Exceptions;
using Elsa.Activities.Compensation.Models;
using Elsa.Workflows.Runtime.Core.Models;
using CompensationScopeActivity = Elsa.Activities.Compensation.Activities.CompensationScope;

namespace Elsa.Activities.Compensation.Internal;

/// <summary>
/// Resolves a compensation scope's executable steps by reading its compiled structure and matching the recorded node
/// ids against the named child slots. The structure is the source of truth for step order and identity; the slots
/// carry the executable nodes. Cached per executable node, so it reflects the pinned artifact.
/// </summary>
internal sealed class CompensationScopeNavigator
{
    private readonly Dictionary<string, int> _stepIndexByForwardNodeId;
    private readonly Dictionary<string, int> _stepIndexByHandlerNodeId;
    private readonly Dictionary<string, int> _stepIndexByName;

    private CompensationScopeNavigator(IReadOnlyList<CompensationScopeStep> steps)
    {
        Steps = steps;
        _stepIndexByForwardNodeId = new Dictionary<string, int>(StringComparer.Ordinal);
        _stepIndexByHandlerNodeId = new Dictionary<string, int>(StringComparer.Ordinal);
        _stepIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (!_stepIndexByName.TryAdd(step.Name, index))
                throw new CompensationScopeExecutionException($"Compensation scope structure declares step '{step.Name}' more than once.");
            if (!_stepIndexByForwardNodeId.TryAdd(step.Forward.ExecutableNodeId, index))
                throw new CompensationScopeExecutionException($"Compensation scope structure references node '{step.Forward.ExecutableNodeId}' from more than one step.");
            if (step.Handler is not null && !_stepIndexByHandlerNodeId.TryAdd(step.Handler.ExecutableNodeId, index))
                throw new CompensationScopeExecutionException($"Compensation scope structure references handler node '{step.Handler.ExecutableNodeId}' from more than one step.");
        }
    }

    public IReadOnlyList<CompensationScopeStep> Steps { get; }

    public static CompensationScopeNavigator From(ExecutableNode executableNode)
    {
        ArgumentNullException.ThrowIfNull(executableNode);

        if (executableNode.ChildSlots.Count == 0 && executableNode.Structure is null)
            return new CompensationScopeNavigator([]);

        var structure = ExecutableStructureReader.ReadStructure<CompensationScopeExecutableStructure>(
            executableNode, "Compensation scope", CompensationScopeActivity.StructureKind, CompensationScopeActivity.StructureSchemaVersion, Fail);

        var steps = structure.Steps
            .Select(step =>
            {
                var stepSlot = CompensationScopeActivity.StepSlotName(step.Name);
                var forward = Match(executableNode, stepSlot, "activity", $"step '{step.Name}'", step.Activity)
                              ?? throw new CompensationScopeExecutionException($"Compensation scope executable node '{executableNode.ExecutableNodeId}' step '{step.Name}' has no activity.");
                var handler = Match(executableNode, CompensationScopeActivity.CompensationSlotName(step.Name), "compensation", $"step '{step.Name}' compensation", step.Compensation);
                return new CompensationScopeStep(step.Name, forward, handler);
            })
            .ToArray();

        return new CompensationScopeNavigator(steps);
    }

    public int? StepIndexOfForward(string executableNodeId) =>
        _stepIndexByForwardNodeId.TryGetValue(executableNodeId, out var index) ? index : null;

    public int? StepIndexOfHandler(string executableNodeId) =>
        _stepIndexByHandlerNodeId.TryGetValue(executableNodeId, out var index) ? index : null;

    public CompensationScopeStep? FindStep(string name) =>
        _stepIndexByName.TryGetValue(name, out var index) ? Steps[index] : null;

    private static ExecutableNode? Match(ExecutableNode executableNode, string slotName, string noun, string declaredPhrase, string? structureNodeId)
    {
        var slotChild = ExecutableStructureReader.ResolveSingleSlotChild(executableNode, slotName, "Compensation scope", noun, Fail);
        return ExecutableStructureReader.MatchSingleSlotChild(executableNode, "Compensation scope", slotName, noun, declaredPhrase, structureNodeId, slotChild, Fail);
    }

    private static CompensationScopeExecutionException Fail(string message, Exception? inner) =>
        inner is null ? new(message) : new(message, inner);
}

/// <summary>One executable step: its name, forward node and optional handler node.</summary>
internal sealed record CompensationScopeStep(string Name, ExecutableNode Forward, ExecutableNode? Handler);
