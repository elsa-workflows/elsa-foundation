using System.Collections.ObjectModel;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>Owner-derived read declarations at one actual compilation/placement stage.</summary>
internal sealed record ActivityTemplateDataSelectionFacts(
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> VariableDeclarations,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> VisibleVariableScopesByConsumer,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> RequestMembersByConsumer)
{
    internal IReadOnlyDictionary<(string Consumer, string Input), ActivityTemplateOriginalMemberRead>? OriginalMemberReads { get; init; }
    internal IReadOnlyDictionary<(string Consumer, string Input, string? Parameter), ActivityTemplateOriginalResultRead>? OriginalResultReads { get; init; }
}

internal sealed record ActivityTemplateOriginalMemberRead(
    RuntimeActivityResultReference Source,
    IReadOnlyList<string> Segments,
    Elsa.Activities.Runtime.Core.Models.ActivityResultContract? ProducerResult);

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
            facts.VisibleVariableScopesByConsumer, facts.OriginalMemberReads, facts.OriginalResultReads);
    }

    // Only original compilation calls this: dependencies are exact immutable templates, not placed nodes.
    // Each admission also requires the actual authored input and its completed source-analysis read fact.
    internal static IReadOnlyDictionary<(string Consumer, string Input), ActivityTemplateOriginalMemberRead> DescribeOriginalMemberReads(
        ExecutableNode root, ActivityDataSelectionDocument? source, ActivityDataSelectionAnalysis? analysis,
        IReadOnlyCollection<ActivityResolvedDependency> dependencies, IReadOnlyDictionary<string, ExecutableActivityTemplate> templates,
        IActivityStructureService? structures)
    {
        var reads = new Dictionary<(string Consumer, string Input), ActivityTemplateOriginalMemberRead>();
        IReadOnlyDictionary<(string Consumer, string Input), ActivityTemplateOriginalMemberRead> Empty() =>
            new ReadOnlyDictionary<(string Consumer, string Input), ActivityTemplateOriginalMemberRead>(new Dictionary<(string Consumer, string Input), ActivityTemplateOriginalMemberRead>());
        if (source?.RootActivity is null || analysis is null || !analysis.MemberReadsEnabled ||
            analysis.ContractRevision != source.ContractRevision || analysis.Findings.Any(finding => finding.Severity == ValidationSeverities.Error))
            return Empty();
        var authored = new Dictionary<string, ActivityNode>(StringComparer.Ordinal);
        var pending = new Stack<ActivityNode>();
        pending.Push(source.RootActivity);
        while (pending.TryPop(out var current))
        {
            if (!authored.TryAdd(current.NodeId, current) || current.Structure is not null && structures is null)
                return Empty();
            if (structures is not null)
                foreach (var child in structures.ProjectChildren(current).SelectMany(slot => slot.Activities))
                    pending.Push(child);
        }
        foreach (var consumer in root.DescendantsAndSelf())
        {
            if (!authored.TryGetValue(consumer.AuthoredActivityId, out var original) ||
                dependencies.Any(dependency => dependency.OccurrenceId == original.NodeId))
                continue; // A dependency frame is never an original local consumer.
            foreach (var binding in consumer.InputBindings.Values)
            {
                var inputs = original.Inputs.Where(input => input.ReferenceKey == binding.InputKey).ToArray();
                if (inputs is not [var input] ||
                    !StringComparer.OrdinalIgnoreCase.Equals(input.Value.ExpressionType, ActivityDataReadRoles.ActivityResultMember) ||
                    input.Value.Value is not JsonElement { ValueKind: JsonValueKind.Object } payload ||
                    payload.TryGetProperty("path", out _) ||
                    payload.TryGetProperty("producerScopeId", out var scope) && scope.ValueKind != JsonValueKind.Null ||
                    !payload.TryGetProperty("producerNodeId", out var producer) || producer.ValueKind != JsonValueKind.String ||
                    !payload.TryGetProperty("projectionKey", out var projection) || projection.ValueKind != JsonValueKind.String ||
                    !ActivityDataMemberPath.TryRead(payload, out var segments))
                    continue;
                var isOptional = payload.TryGetProperty("isOptional", out var optional) && optional.ValueKind == JsonValueKind.True;
                var producerId = producer.GetString()!;
                var projectionKey = projection.GetString()!;
                if (string.IsNullOrWhiteSpace(producerId) || string.IsNullOrWhiteSpace(projectionKey))
                    continue;
                var analyzed = analysis.MemberReads.Where(candidate => candidate.Path == $"{original.NodeId}/inputs/{input.ReferenceKey}").ToArray();
                if (analyzed is not [var fact] || fact.ProducerNodeId != producerId || fact.ProjectionKey != projectionKey ||
                    !fact.MemberPath.SequenceEqual(segments, StringComparer.Ordinal) || !authored.TryGetValue(producerId, out var authoredProducer))
                    continue;
                var resolved = dependencies.Where(candidate => candidate.OccurrenceId == producerId).ToArray();
                if (resolved.Length == 0)
                    continue;
                var result = resolved is [var dependency] && dependency.VersionId == authoredProducer.ActivityVersionId &&
                    templates.TryGetValue(dependency.TemplateId, out var template) && template.TemplateId == dependency.TemplateId &&
                    template.TemplateHash == dependency.TemplateHash ? template.Root.ActivityContract?.Result : null;
                if (!reads.TryAdd((consumer.ExecutableNodeId, binding.InputKey), new(
                        new(producerId, projectionKey, null, isOptional), Array.AsReadOnly(segments.ToArray()), result)))
                    return Empty();
            }
        }
        return new ReadOnlyDictionary<(string Consumer, string Input), ActivityTemplateOriginalMemberRead>(reads);
    }

    internal static void Validate(ExecutableNode root, IEnumerable<string> publicRequestMembers, bool hasSourceAnalysis,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? variableDeclarations = null,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? requestMembersByConsumer = null,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? visibleVariableScopesByConsumer = null,
        IReadOnlyDictionary<(string Consumer, string Input), ActivityTemplateOriginalMemberRead>? originalMemberReads = null,
        IReadOnlyDictionary<(string Consumer, string Input, string? Parameter), ActivityTemplateOriginalResultRead>? originalResultReads = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(publicRequestMembers);
        var errors = new List<ValidationError>();
        var members = publicRequestMembers.ToArray();
        var originalResultsByInput = originalResultReads?.GroupBy(read => (read.Key.Consumer, read.Key.Input))
            .ToDictionary(group => group.Key, group => group.ToArray());
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
            if (originalResultsByInput is not null && originalResultsByInput.TryGetValue((node.ExecutableNodeId, binding.InputKey), out var originals) &&
                originals.Any(read => !ActivityTemplateOriginalResultReadFacts.Matches(binding, read.Key.Parameter, read.Value.Source)))
            {
                errors.Add(new(path, "ActivityDataSelection/ResultContractMismatch", "The compiled result read differs from its exact original authored read."));
                continue;
            }
            if (originalMemberReads is not null && originalMemberReads.TryGetValue((node.ExecutableNodeId, binding.InputKey), out var owned) &&
                !MatchesOriginalRead(binding, owned))
            {
                errors.Add(new(path, "ActivityDataSelection/MemberContractMismatch", "The compiled member read differs from its exact original authored read."));
                continue;
            }
            if (binding.WorkflowRequest is { } request)
                Request(request.MemberKey, node.ExecutableNodeId, path);
            if (binding.Variable is { } variable)
                Variable(variable.DeclaringScopeId, variable.VariableKey, node.ExecutableNodeId, path);
            if (binding.ActivityResult is { } result)
                Result(result.ProducerExecutableNodeId, result.ProjectionKey, path, node.ExecutableNodeId, binding);
            if (binding.Expression is null)
                continue;
            foreach (var (parameterKey, parameter) in binding.Expression.Parameters)
                switch (parameter)
                {
                    case WorkflowRequestExpressionParameterBinding parameterRequest:
                        Request(parameterRequest.MemberKey, node.ExecutableNodeId, path);
                        break;
                    case VariableExpressionParameterBinding parameterVariable:
                        Variable(parameterVariable.DeclaringScopeNodeId, parameterVariable.VariableKey, node.ExecutableNodeId, path);
                        break;
                    case ActivityResultExpressionParameterBinding parameterResult:
                        Result(parameterResult.ProducerNodeId, parameterResult.ProjectionKey, path, node.ExecutableNodeId, binding, parameterKey);
                        break;
                }
        }
        if (errors.Count != 0)
            throw new ActivityDataSelectionRejectedException(errors);
        return;

        void Request(string key, string consumer, string path)
        {
            // The owner supplies runtime names or preplacement keys, never a guessed union of identities.
            var declared = requestMembersByConsumer is null ? members : requestMembersByConsumer.GetValueOrDefault(consumer, []);
            if (declared.Count(x => StringComparer.Ordinal.Equals(x, key)) != 1)
                errors.Add(new(path, "ActivityDataSelection/InputUnknown", "The compiled read does not name one declared public activity input."));
        }

        void Result(string producerId, string projectionKey, string path, string? consumer = null, RuntimeInputBinding? binding = null, string? parameter = null)
        {
            Elsa.Activities.Runtime.Core.Models.ActivityResultContract? result;
            // Dependency occurrence IDs and provider-local executable IDs are different namespaces.
            // A bound original read selects its exact dependency even when the two ID texts collide.
            if (consumer is not null && binding is not null && originalResultReads is not null &&
                originalResultReads.TryGetValue((consumer, binding.InputKey, parameter), out var originalResult))
            {
                result = originalResult.ProducerResult;
                if (result is null)
                {
                    errors.Add(new(path, "ActivityDataSelection/ProducerUnknown", "The original result read does not name one exact immutable dependency producer."));
                    return;
                }
            }
            else if (parameter is null && consumer is not null && binding is not null && originalMemberReads is not null &&
                originalMemberReads.TryGetValue((consumer, binding.InputKey), out var original))
            {
                result = original.ProducerResult;
                if (result is null)
                {
                    errors.Add(new(path, "ActivityDataSelection/ProducerUnknown", "The original read does not name one exact immutable dependency producer."));
                    return;
                }
            }
            else if (byId.TryGetValue(producerId, out var producers))
            {
                if (producers.Length != 1)
                {
                    errors.Add(new(path, "ActivityDataSelection/ProducerUnknown", "The compiled read does not name one producer in this activity."));
                    return;
                }
                result = producers[0].ActivityContract?.Result;
            }
            else
            {
                errors.Add(new(path, "ActivityDataSelection/ProducerUnknown", "The compiled read does not name one producer in this activity."));
                return;
            }
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

    private static bool MatchesOriginalRead(RuntimeInputBinding binding, ActivityTemplateOriginalMemberRead original) =>
        binding is { Source: RuntimeInputBindingSource.ActivityResultMember, Selection: { Mode: RuntimeReadSelectionMode.DeferredFit } selection, ActivityResult: { } source } &&
        source.ProducerExecutableNodeId == original.Source.ProducerExecutableNodeId && source.ProjectionKey == original.Source.ProjectionKey &&
        source.ProducerScopeId == original.Source.ProducerScopeId && source.IsOptional == original.Source.IsOptional &&
        selection.Segments.SequenceEqual(original.Segments, StringComparer.Ordinal);
}
