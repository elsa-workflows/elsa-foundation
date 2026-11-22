using System.Collections.ObjectModel;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

internal sealed record ActivityTemplateOriginalResultRead(RuntimeActivityResultReference Source,
    Elsa.Activities.Runtime.Core.Models.ActivityResultContract? ProducerResult);

/// <summary>Exact original reads of dependency results by staged graph intrinsics, before dependency placement.</summary>
internal static class ActivityTemplateOriginalResultReadFacts
{
    // Literal data and the other owned roles never become portable expressions because their JSON resembles one.
    private static readonly HashSet<string> OwnedRoles = new(StringComparer.OrdinalIgnoreCase)
    { "Literal", "Object", "Default", "Secret", "Connection", "Input", "InputMember", "WorkflowRequest", "Variable", "ActivityResult", "ActivityResultMember", "VariableMember", "WorkflowRequestMember" };

    internal static IReadOnlyDictionary<(string Consumer, string Input, string? Parameter), ActivityTemplateOriginalResultRead> Describe(
        ExecutableNode root, ActivityDataSelectionDocument? source, ActivityDataSelectionAnalysis? analysis,
        IReadOnlyCollection<ActivityResolvedDependency> dependencies, IReadOnlyDictionary<string, ExecutableActivityTemplate> templates,
        IActivityStructureService? structures)
    {
        var reads = new Dictionary<(string Consumer, string Input, string? Parameter), ActivityTemplateOriginalResultRead>();
        IReadOnlyDictionary<(string Consumer, string Input, string? Parameter), ActivityTemplateOriginalResultRead> Empty() =>
            new ReadOnlyDictionary<(string Consumer, string Input, string? Parameter), ActivityTemplateOriginalResultRead>(
                new Dictionary<(string Consumer, string Input, string? Parameter), ActivityTemplateOriginalResultRead>());
        if (source?.RootActivity is null || analysis is null || analysis.ContractRevision != source.ContractRevision ||
            analysis.Findings.Any(finding => finding.Severity == ValidationSeverities.Error) || structures is null)
            return Empty();
        var authored = new Dictionary<string, ActivityNode>(StringComparer.Ordinal);
        var pending = new Stack<ActivityNode>();
        pending.Push(source.RootActivity);
        while (pending.TryPop(out var node))
        {
            if (!authored.TryAdd(node.NodeId, node)) return Empty();
            foreach (var child in structures.ProjectChildren(node).SelectMany(slot => slot.Activities)) pending.Push(child);
        }
        foreach (var consumer in root.ChildSlots.Where(slot => slot.Name == "graph-local-nodes").SelectMany(slot => slot.Activities))
        {
            if (!authored.TryGetValue(consumer.AuthoredActivityId, out var original) ||
                !(consumer.IntrinsicKind == WorkflowIntrinsicKind.SetOutput && original.Intrinsic?.Kind == AuthoredWorkflowIntrinsicKind.SetOutput && original.ActivityVersionId == "elsa.intrinsic.set-output@1" ||
                  consumer.IntrinsicKind == WorkflowIntrinsicKind.Set && original.Intrinsic?.Kind == AuthoredWorkflowIntrinsicKind.Set && original.ActivityVersionId == "elsa.intrinsic.set@1"))
                continue;
            foreach (var input in original.Inputs)
            {
                if (input.Value is null) continue;
                if (StringComparer.OrdinalIgnoreCase.Equals(input.Value.ExpressionType, "ActivityResult"))
                {
                    // Use the binding role's actual parser, including existing scope and optional normalization.
                    var definition = new InputDefinition(input.ReferenceKey, input.ReferenceKey, new("Elsa.Any"), null, input.ReferenceKey, null, true);
                    if (!Add(consumer.ExecutableNodeId, input.ReferenceKey, null,
                        RuntimeInputBindingCompiler.ReadActivityResultReference(original.NodeId, definition, input.Value))) return Empty();
                }
                else if (!OwnedRoles.Contains(input.Value.ExpressionType ?? "Literal") &&
                         RuntimeInputBindingCompiler.TryReadPortableExpressionDefinition(input.Value.Value, out var expression))
                {
                    foreach (var (parameter, value) in expression.Parameters)
                        if (value is ActivityResultExpressionParameterBinding result &&
                            !Add(consumer.ExecutableNodeId, input.ReferenceKey, parameter, new(result.ProducerNodeId, result.ProjectionKey, null, result.IsOptional))) return Empty();
                }
            }
        }
        return new ReadOnlyDictionary<(string Consumer, string Input, string? Parameter), ActivityTemplateOriginalResultRead>(reads);

        bool Add(string consumer, string input, string? parameter, RuntimeActivityResultReference reference)
        {
            var resolved = dependencies.Where(dependency => dependency.OccurrenceId == reference.ProducerExecutableNodeId).ToArray();
            var result = authored.TryGetValue(reference.ProducerExecutableNodeId, out var producer) && resolved is [var dependency] &&
                dependency.VersionId == producer.ActivityVersionId && templates.TryGetValue(dependency.TemplateId, out var template) &&
                template.TemplateId == dependency.TemplateId && template.TemplateHash == dependency.TemplateHash
                    ? template.Root.ActivityContract?.Result : null;
            // Retain unresolved original reads as refusals; never fall back to an unrelated colliding local runtime ID.
            return reads.TryAdd((consumer, input, parameter), new(reference, result));
        }
    }

    internal static bool Matches(RuntimeInputBinding binding, string? parameter, RuntimeActivityResultReference expected)
    {
        if (parameter is null)
            return binding.Source == RuntimeInputBindingSource.ActivityResult && binding.ActivityResult is { } actual &&
                actual.ProducerExecutableNodeId == expected.ProducerExecutableNodeId && actual.ProjectionKey == expected.ProjectionKey &&
                actual.ProducerScopeId == expected.ProducerScopeId && actual.IsOptional == expected.IsOptional;
        return binding.Source == RuntimeInputBindingSource.Expression && binding.Expression is { } expression &&
            expression.Parameters.TryGetValue(parameter, out var value) && value is ActivityResultExpressionParameterBinding result &&
            result.ProducerNodeId == expected.ProducerExecutableNodeId && result.ProjectionKey == expected.ProjectionKey && result.IsOptional == expected.IsOptional;
    }
}
