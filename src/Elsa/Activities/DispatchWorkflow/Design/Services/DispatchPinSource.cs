using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Design.Services;

/// <summary>Resolves each static DispatchWorkflow target to one exact live Published executable/source pin.</summary>
public sealed class DispatchPinSource(
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableInputValidator inputValidator,
    TimeProvider timeProvider) : IExecutableCompilationSource, IExecutableNodeMetadataSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<IReadOnlyCollection<ExecutableNodeMetadataContribution>> GetMetadataAsync(
        ExecutableNodeMetadataContext context,
        CancellationToken cancellationToken = default)
    {
        var contribution = await GetContributionAsync(
            new ExecutableCompilationContext(context.Request, context.Source, context.RootActivity),
            cancellationToken);
        return contribution.NodeMetadata
            .Select(claim => new ExecutableNodeMetadataContribution(
                claim.ExecutableNodeId,
                claim.Key,
                claim.Value))
            .ToArray();
    }

    public async ValueTask<ExecutableCompilationContribution> GetContributionAsync(
        ExecutableCompilationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var dispatchNodes = Flatten(context.RootActivity)
            .Where(node => StringComparer.Ordinal.Equals(node.ActivityType, DispatchWorkflowConstants.ActivityType))
            .ToArray();
        if (dispatchNodes.Length == 0)
            return new ExecutableCompilationContribution();

        if (context.Request.Scope is not (WorkflowExecutableReferenceScope.Published or WorkflowExecutableReferenceScope.TestRun))
            throw new ArgumentException($"DispatchWorkflow is not supported in executable scope '{context.Request.Scope}'.");

        var references = await sourceReferenceStore.ListAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: timeProvider.GetUtcNow(),
            cancellationToken);
        var referencesByDefinition = references
            .Where(reference => StringComparer.Ordinal.Equals(reference.TenantId, context.TenantId))
            .GroupBy(reference => reference.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var metadata = new List<ExecutableNodeMetadataClaim>(dispatchNodes.Length);
        var dependencies = new List<ExecutableDependencyClaim>(dispatchNodes.Length);

        foreach (var node in dispatchNodes.OrderBy(item => item.ExecutableNodeId, StringComparer.Ordinal))
        {
            var definitionId = ReadDefinitionId(node);
            if (!referencesByDefinition.TryGetValue(definitionId, out var candidates))
                throw new ArgumentException($"DispatchWorkflow node '{node.ExecutableNodeId}' target '{definitionId}' must resolve to exactly one accessible live Published artifact.");

            var artifactIds = candidates
                .Select(candidate => candidate.ArtifactId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (artifactIds.Length != 1)
                throw new ArgumentException($"DispatchWorkflow node '{node.ExecutableNodeId}' target '{definitionId}' must resolve to exactly one accessible live Published artifact.");

            var artifactId = artifactIds[0];
            var source = candidates
                .Where(candidate => StringComparer.Ordinal.Equals(candidate.ArtifactId, artifactId))
                .OrderByDescending(candidate => candidate.PublishedAt)
                .ThenBy(candidate => candidate.SourceReferenceId, StringComparer.Ordinal)
                .First();
            var executable = await executableStore.FindAsync(artifactId, cancellationToken)
                ?? throw new ArgumentException($"DispatchWorkflow node '{node.ExecutableNodeId}' target '{definitionId}' references missing executable '{artifactId}'.");
            if (!StringComparer.Ordinal.Equals(executable.Identity.ArtifactId, artifactId))
                throw new ArgumentException($"DispatchWorkflow node '{node.ExecutableNodeId}' target '{definitionId}' resolved inconsistent executable/source identity.");
            if (executable.InputContract is null ||
                executable.InputContract.Version != WorkflowExecutableInputContract.CurrentVersion)
            {
                throw new ArgumentException(
                    $"DispatchWorkflow node '{node.ExecutableNodeId}' target '{definitionId}' resolves a legacy executable that must be recompiled and republished.");
            }

            var (literalInputs, isComplete) = ReadInputs(node);
            var validation = isComplete
                ? inputValidator.Validate(executable.InputContract, literalInputs)
                : inputValidator.ValidateKnownInputs(executable.InputContract, literalInputs);
            if (!validation.IsValid)
            {
                throw new ArgumentException(
                    $"DispatchWorkflow node '{node.ExecutableNodeId}' target '{definitionId}' has invalid child inputs: " +
                    string.Join(" ", validation.Findings.Select(finding => finding.Message)));
            }

            var pin = new DispatchWorkflowPin(executable.Identity, WorkflowExecutableSourceProvenance.From(source));
            metadata.Add(new ExecutableNodeMetadataClaim(
                node.ExecutableNodeId,
                DispatchWorkflowConstants.PinnedTargetMetadataKey,
                JsonSerializer.Serialize(pin, SerializerOptions)));
            dependencies.Add(new ExecutableDependencyClaim(
                node.ExecutableNodeId,
                executable.Identity.ArtifactId,
                executable.Identity.ArtifactHash));
        }

        return new ExecutableCompilationContribution(metadata, dependencies);
    }

    private static string ReadDefinitionId(ExecutableNode node)
    {
        if (!node.InputBindings.TryGetValue("WorkflowDefinitionId", out var binding) ||
            binding.Source != RuntimeInputBindingSource.Literal ||
            binding.LiteralValue is not { ValueKind: JsonValueKind.String } literal ||
            string.IsNullOrWhiteSpace(literal.GetString()))
            throw new ArgumentException($"DispatchWorkflow node '{node.ExecutableNodeId}' requires a literal nonblank WorkflowDefinitionId.");

        return literal.GetString()!.Trim();
    }

    private static (IReadOnlyCollection<KeyValuePair<string, JsonElement>> Inputs, bool IsComplete) ReadInputs(
        ExecutableNode node)
    {
        if (!node.InputBindings.TryGetValue("Inputs", out var binding))
            return (Array.Empty<KeyValuePair<string, JsonElement>>(), true);

        if (binding.Source != RuntimeInputBindingSource.Literal)
            return (Array.Empty<KeyValuePair<string, JsonElement>>(), false);

        if (binding.LiteralValue is not { } literal || literal.ValueKind == JsonValueKind.Null)
            return (Array.Empty<KeyValuePair<string, JsonElement>>(), true);
        if (literal.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"DispatchWorkflow node '{node.ExecutableNodeId}' literal Inputs value must be a JSON object.");

        return (
            literal.EnumerateObject()
                .Select(property => new KeyValuePair<string, JsonElement>(property.Name, property.Value.Clone()))
                .ToArray(),
            true);
    }

    private static IEnumerable<ExecutableNode> Flatten(ExecutableNode root)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in node.ChildSlots.SelectMany(slot => slot.Activities).Reverse())
                stack.Push(child);
        }
    }
}
