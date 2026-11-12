using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>Owner-derived read declarations at one actual compilation/placement stage.</summary>
internal sealed record ActivityTemplateDataSelectionFacts(
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> VariableDeclarations,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> VisibleVariableScopesByConsumer,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> RequestMembersByConsumer,
    IReadOnlySet<string>? OpenRequestConsumers = null);

/// <summary>
/// Checks typed reads in a provider's compiled native boundary without inspecting opaque descriptor JSON.
/// An authored graph requires its owner projection for declaration and causal facts.
/// </summary>
internal static class ActivityTemplateDataSelectionGuard
{
    internal static void Validate(ExecutableNode root, bool hasSourceAnalysis, ActivityTemplateDataSelectionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        Validate(root, [], hasSourceAnalysis, facts.VariableDeclarations, facts.RequestMembersByConsumer,
            facts.VisibleVariableScopesByConsumer, facts.OpenRequestConsumers);
    }

    internal static void Validate(ExecutableNode root, IEnumerable<string> publicRequestMembers, bool hasSourceAnalysis,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? variableDeclarations = null,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? requestMembersByConsumer = null,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? visibleVariableScopesByConsumer = null,
        IReadOnlySet<string>? openRequestConsumers = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(publicRequestMembers);
        var errors = new List<ValidationError>();
        var members = publicRequestMembers.ToArray();
        var nodes = root.DescendantsAndSelf().ToArray();
        var byId = nodes.GroupBy(x => x.ExecutableNodeId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        var parents = nodes.SelectMany(parent => parent.ChildSlots.SelectMany(slot => slot.Activities)
                .Select(child => (Child: child.ExecutableNodeId, Parent: parent.ExecutableNodeId)))
            .GroupBy(x => x.Child, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Select(y => y.Parent).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        foreach (var node in nodes)
        foreach (var binding in node.InputBindings.Values)
        {
            var path = $"{node.AuthoredActivityId}/inputs/{binding.InputKey}";
            if (binding.WorkflowRequest is { } request)
                Request(request.MemberKey, node.ExecutableNodeId, path);
            if (binding.Variable is { } variable)
                Variable(variable.DeclaringScopeId, variable.VariableKey, node.ExecutableNodeId, path);
            if (binding.ActivityResult is { } result)
                Result(result.ProducerExecutableNodeId, result.ProjectionKey, path);
            if (binding.Expression is null)
                continue;
            foreach (var parameter in binding.Expression.Parameters.Values)
                switch (parameter)
                {
                    case WorkflowRequestExpressionParameterBinding parameterRequest:
                        Request(parameterRequest.MemberKey, node.ExecutableNodeId, path);
                        break;
                    case VariableExpressionParameterBinding parameterVariable:
                        Variable(parameterVariable.DeclaringScopeNodeId, parameterVariable.VariableKey, node.ExecutableNodeId, path);
                        break;
                    case ActivityResultExpressionParameterBinding parameterResult:
                        Result(parameterResult.ProducerNodeId, parameterResult.ProjectionKey, path);
                        break;
                }
        }
        if (errors.Count != 0)
            throw new ActivityDataSelectionRejectedException(errors);
        return;

        void Request(string key, string consumer, string path)
        {
            // An open request belongs only to the explicitly marked workflow consumers.
            if (openRequestConsumers?.Contains(consumer) == true) return;
            // The owner supplies runtime names or preplacement keys, never a guessed union of identities.
            var declared = requestMembersByConsumer is null ? members : requestMembersByConsumer.GetValueOrDefault(consumer, []);
            if (declared.Count(x => StringComparer.Ordinal.Equals(x, key)) != 1)
                errors.Add(new(path, "ActivityDataSelection/InputUnknown", "The compiled read does not name one declared public activity input."));
        }

        void Result(string producerId, string projectionKey, string path)
        {
            if (!byId.TryGetValue(producerId, out var producers) || producers.Length != 1)
            {
                errors.Add(new(path, "ActivityDataSelection/ProducerUnknown", "The compiled read does not name one producer in this activity."));
                return;
            }
            var result = producers[0].ActivityContract?.Result;
            if (result is null || projectionKey != "$result" && !result.Projections.ContainsKey(projectionKey))
            {
                errors.Add(new(path, "ActivityDataSelection/ProjectionUnknown", "The compiled producer does not declare the selected result projection."));
                return;
            }
            // A compiled child relation does not establish execution ordering or variable declarations.
            if (!hasSourceAnalysis) NeedsProjection(path);
        }

        void Variable(string scope, string key, string consumer, string path)
        {
            if (variableDeclarations is null)
            {
                errors.Add(new(path, "ActivityDataSelection/VariableDeclarationUnavailable",
                    "The compiled variable read requires the owning scope's actual declarations."));
                return;
            }
            if (!variableDeclarations.TryGetValue(scope, out var declarations) ||
                declarations.Count(x => StringComparer.Ordinal.Equals(x, key)) != 1)
                errors.Add(new(path, "ActivityDataSelection/VariableUnknown", "The compiled variable read does not name one declaration in its exact scope."));
            else if (!Visible(scope, consumer))
                errors.Add(new(path, "ActivityDataSelection/VariableScopeInvisible", "The compiled variable scope does not enclose this consumer."));
        }

        bool Visible(string scope, string consumer)
        {
            if (visibleVariableScopesByConsumer is not null)
                return visibleVariableScopesByConsumer.TryGetValue(consumer, out var visible) && visible.Contains(scope, StringComparer.Ordinal);
            if (scope == "workflow") return true; // Only the explicitly supplied workflow declarations admit this scope.
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (visited.Add(consumer) && parents.TryGetValue(consumer, out var enclosing) && enclosing.Length == 1)
            {
                consumer = enclosing[0];
                if (consumer == scope) return true;
            }
            return false;
        }

        void NeedsProjection(string path) => errors.Add(new(path, "ActivityDataSelection/ProviderProjectionMissing",
            "The activity provider must expose declaration and control-flow facts for this source read."));
    }
}
