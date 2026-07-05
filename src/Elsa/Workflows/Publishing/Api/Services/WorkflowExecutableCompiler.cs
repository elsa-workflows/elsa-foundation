using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed class WorkflowExecutableCompiler(
    IWorkflowDefinitionVersionStore workflowVersions,
    IActivityDefinitionVersionStore activityVersions,
    IActivityStructureService activityStructureService,
    IWellKnownTypeRegistry wellKnownTypeRegistry,
    RuntimeInputBindingCompiler inputBindingCompiler)
    : IWorkflowExecutableCompiler
{
    private const string ArtifactHashPrefix = "sha256:";
    private const int ArtifactIdHashLength = 12;

    public async ValueTask<WorkflowExecutable> CompileAsync(
        WorkflowExecutableCompileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        WorkflowExecutableCompileSource? source = null;
        try
        {
            source = request.Source ?? await GetVersionSourceAsync(request.VersionId, cancellationToken);
            var state = source.State;
            ArgumentNullException.ThrowIfNull(state);

            var rootActivity = state.RootActivity
                ?? throw new ArgumentException("Workflow version has no root activity to publish.");

            var activities = FlattenActivities(rootActivity).ToArray();
            ValidateActivityTree(activities);

            var activityRows = new Dictionary<string, ActivityDefinitionVersion>(StringComparer.Ordinal);
            foreach (var activityVersionId in activities.Select(x => x.ActivityVersionId).Distinct(StringComparer.Ordinal))
                activityRows[activityVersionId] = await activityVersions.GetWithDefinitionAsync(activityVersionId, cancellationToken);

            var compiledRoot = CompileNode(rootActivity, activityRows);
            var artifactHash = ComputeHash(source, compiledRoot);
            var artifactId = CreateArtifactId(request.ArtifactIdPrefix, artifactHash);
            var metadata = (request.CompatibilityMetadata ?? new Dictionary<string, string>())
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

            return new WorkflowExecutable(
                identity: new WorkflowExecutableIdentity(
                    ArtifactId: artifactId,
                    DefinitionId: source.DefinitionId,
                    DefinitionVersionId: source.DefinitionVersionId,
                    ArtifactVersion: source.ArtifactVersion,
                    ArtifactHash: artifactHash,
                    Source: source.SourceReference),
                rootActivity: compiledRoot,
                resumeTargets: BuildResumeTargets(compiledRoot),
                createdAt: request.CreatedAt,
                publishedAt: request.PublishedAt,
                compatibilityMetadata: metadata,
                scope: request.Scope,
                expiresAt: request.ExpiresAt);
        }
        catch (ArgumentException exception) when (exception is not WorkflowExecutableCompilationException)
        {
            throw new WorkflowExecutableCompilationException(source?.DefinitionId, source?.DefinitionVersionId, exception.Message, exception);
        }
    }

    private async Task<WorkflowExecutableCompileSource> GetVersionSourceAsync(
        string versionId,
        CancellationToken cancellationToken)
    {
        var version = await workflowVersions.GetWithDefinitionAsync(versionId, cancellationToken);
        return new WorkflowExecutableCompileSource(
            DefinitionId: version.DefinitionId,
            DefinitionVersionId: version.Id,
            ArtifactVersion: version.Version,
            State: version.State,
            SourceReference: new WorkflowExecutableSourceReference("WorkflowDefinitionVersion", version.Id, version.Version));
    }

    private static string CreateArtifactId(string artifactIdPrefix, string artifactHash)
    {
        if (!artifactHash.StartsWith(ArtifactHashPrefix, StringComparison.Ordinal) ||
            artifactHash.Length < ArtifactHashPrefix.Length + ArtifactIdHashLength)
            throw new ArgumentException($"Artifact hash '{artifactHash}' does not use the expected '{ArtifactHashPrefix}' format.", nameof(artifactHash));

        return $"{artifactIdPrefix}{artifactHash[ArtifactHashPrefix.Length..(ArtifactHashPrefix.Length + ArtifactIdHashLength)]}";
    }

    private ExecutableNode CompileNode(
        ActivityNode activity,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows)
    {
        var activityVersion = activityRows[activity.ActivityVersionId];

        var inputDefinitionsByReferenceKey = activityVersion.Inputs.ToDictionary(input => input.ReferenceKey, StringComparer.Ordinal);
        var inputBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        var childSlots = CompileChildSlots(activityStructureService.ProjectChildren(activity), activityRows);

        foreach (var inputState in activity.Inputs)
        {
            if (!inputDefinitionsByReferenceKey.TryGetValue(inputState.ReferenceKey, out var inputDefinition))
                throw new ArgumentException($"Activity node '{activity.NodeId}' input '{inputState.ReferenceKey}' does not match any input definition on activity version '{activity.ActivityVersionId}'.");

            inputBindings[inputDefinition.Name] = inputBindingCompiler.Compile(activity.NodeId, inputDefinition, inputState.Value);
        }

        var activityType = activityVersion.Definition?.ActivityTypeKey
            ?? throw new ArgumentException($"Activity version '{activity.ActivityVersionId}' did not include its activity definition.");

        return new ExecutableNode(
            executableNodeId: activity.NodeId,
            authoredActivityId: activity.NodeId,
            activityType: activityType,
            activityTypeVersion: activityVersion.Version,
            descriptorType: activityVersion.DescriptorType,
            descriptorPayload: activityVersion.DescriptorPayload,
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>
            {
                ["authoredNodeId"] = activity.NodeId,
                [TriggerNodeMetadata.ExecutionTypeKey] = activityVersion.ExecutionType.ToString()
            },
            childSlots: childSlots,
            structure: CompileStructure(activityStructureService.CompileExecutableStructure(activity)));
    }

    private IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> BuildResumeTargets(ExecutableNode root)
    {
        // Index [ResumeTarget] handlers declared by each node's activity CLR type into the executable's
        // resume-target map. Suspending activities (e.g. Delay) create a durable bookmark against a resume
        // target id; the CreateBookmark handler validates that id against this map, and the resume handler
        // reflects the matching method back at resume time. Activities without resume targets (all existing
        // activities) contribute nothing, so the map stays empty for them.
        var resumeTargets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal);

        foreach (var node in FlattenExecutableNodes(root))
        {
            if (!wellKnownTypeRegistry.TryGetTypeOrDefault(node.ActivityType, out var activityType) || activityType is null || activityType == typeof(object))
                continue;

            foreach (var method in activityType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attribute = method.GetCustomAttribute<ResumeTargetAttribute>();
                if (attribute is null)
                    continue;

                ValidateResumeTargetSignature(activityType, method);

                var resumeTargetId = attribute.ResumeTargetId;
                if (resumeTargets.TryGetValue(resumeTargetId, out var existing))
                    throw new ArgumentException(
                        $"Resume target '{resumeTargetId}' is declared by executable nodes '{existing.ExecutableNodeId}' and '{node.ExecutableNodeId}'. A resume target id must be unique within a workflow executable; multiple instances of the same resume-target activity in one workflow are not yet supported.");

                resumeTargets[resumeTargetId] = new WorkflowExecutableResumeTarget(
                    ResumeTargetId: resumeTargetId,
                    ExecutableNodeId: node.ExecutableNodeId,
                    HandlerKey: method.Name,
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
            }
        }

        return resumeTargets;
    }

    private static void ValidateResumeTargetSignature(Type activityType, MethodInfo method)
    {
        var parameters = method.GetParameters();
        var hasSupportedParameter =
            parameters.Length == 0 ||
            parameters.Length == 1 && (parameters[0].ParameterType == typeof(IActivityExecutionContext) || parameters[0].ParameterType == typeof(JsonElement));
        var hasSupportedReturn =
            method.ReturnType == typeof(void) ||
            method.ReturnType == typeof(Task) ||
            method.ReturnType == typeof(ValueTask);

        if (!hasSupportedParameter || !hasSupportedReturn)
            throw new ArgumentException(
                $"Resume target method '{activityType.FullName}.{method.Name}' has an unsupported signature. A resume target must take no parameters or a single {nameof(IActivityExecutionContext)}/{nameof(JsonElement)} parameter and return void, Task, or ValueTask.");
    }

    private static IEnumerable<ExecutableNode> FlattenExecutableNodes(ExecutableNode root)
    {
        yield return root;

        foreach (var slot in root.ChildSlots)
            foreach (var child in slot.Activities)
                foreach (var descendant in FlattenExecutableNodes(child))
                    yield return descendant;
    }

    private IReadOnlyCollection<ExecutableChildSlot> CompileChildSlots(
        IEnumerable<ActivityChildProjection> childSlots,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows)
    {
        return childSlots
            .Select(slot => new ExecutableChildSlot(
                slot.Name,
                slot.Activities.Select(activity => CompileNode(activity, activityRows)).ToArray()))
            .ToArray();
    }

    private static ExecutableActivityStructure? CompileStructure(ActivityNodeStructure? structure) =>
        structure is null
            ? null
            : new ExecutableActivityStructure(structure.Kind, structure.SchemaVersion, structure.Payload);

    private static void ValidateActivityTree(IReadOnlyCollection<ActivityNode> activities)
    {
        if (activities.Count == 0)
            throw new ArgumentException("Workflow version has no root activity to publish.");

        var duplicateNodeId = activities.GroupBy(activity => activity.NodeId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateNodeId is not null)
            throw new ArgumentException($"Workflow version contains duplicate activity node id '{duplicateNodeId}'.");
    }

    private static string ComputeHash(
        WorkflowExecutableCompileSource source,
        ExecutableNode rootActivity)
    {
        var nodes = FlattenExecutableActivities(rootActivity).ToArray();
        var payload = string.Join(
            '\n',
            source.SourceReference.SourceKind,
            source.SourceReference.SourceId,
            source.SourceReference.SourceVersion,
            source.DefinitionId,
            source.DefinitionVersionId,
            source.ArtifactVersion,
            rootActivity.ExecutableNodeId,
            string.Join('|', nodes.OrderBy(node => node.ExecutableNodeId, StringComparer.Ordinal)
                .Select(FormatNode)));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string FormatInputBinding(KeyValuePair<string, RuntimeInputBinding> input)
    {
        var metadata = string.Join(',', input.Value.Metadata
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}={item.Value}"));

        var payload = input.Value.Source switch
        {
            RuntimeInputBindingSource.Expression => $"{input.Value.Source}:{input.Value.Expression?.Language}:{input.Value.Expression?.Expression}",
            _ => input.Value.LiteralValue?.GetRawText()
        };

        return $"{input.Key}={payload}[{metadata}]";
    }

    private static string FormatNode(ExecutableNode node)
    {
        var childSlots = string.Join(',', node.ChildSlots
            .OrderBy(slot => slot.Name, StringComparer.Ordinal)
            .Select(slot =>
            {
                var activities = string.Join(';', slot.Activities.Select(activity => activity.ExecutableNodeId).Order(StringComparer.Ordinal));
                return $"{slot.Name}({activities})";
            }));
        var structure = node.Structure is null
            ? string.Empty
            : $"{node.Structure.Kind}:{node.Structure.SchemaVersion}:{node.Structure.Payload.GetRawText()}";
        return $"{node.ExecutableNodeId}:{node.ActivityType}:{node.ActivityTypeVersion}:{node.DescriptorType}:{node.DescriptorPayload.GetRawText()}:{structure}:{string.Join(',', node.InputBindings.OrderBy(input => input.Key, StringComparer.Ordinal).Select(FormatInputBinding))}:{childSlots}";
    }

    private IEnumerable<ActivityNode> FlattenActivities(ActivityNode rootActivity)
    {
        var stack = new Stack<ActivityNode>();
        stack.Push(rootActivity);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in activityStructureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }

    private static IEnumerable<ExecutableNode> FlattenExecutableActivities(ExecutableNode rootActivity)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(rootActivity);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in node.ChildSlots.SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}
