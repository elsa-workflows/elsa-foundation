using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Extensions;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa.Workflows.Publishing.Api.Handlers;

public sealed class PublishWorkflowRequestHandler(
    IQueries<WorkflowDefinitionVersion> workflowVersions,
    IQueries<ActivityDefinitionVersion> activityVersions,
    IWorkflowExecutableStore executableStore,
    IActivityStructureService activityStructureService)
    : IRequestHandler<PublishWorkflow, PublishedWorkflowView>
{
    private const string LiteralExpressionType = "Literal";
    private const string InputTypeMetadataKey = "typeName";
    private const string ReferenceKeyMetadataKey = "referenceKey";
    private const string ArtifactHashPrefix = "sha256:";
    private const int ArtifactIdHashLength = 12;

    public async Task<PublishedWorkflowView> Handle(PublishWorkflow request, CancellationToken cancellationToken)
    {
        var version = await workflowVersions.GetVersionIncludingDefinition(request.VersionId, cancellationToken);
        var state = version.State;
        var rootActivity = state.RootActivity
            ?? throw new ArgumentException("Workflow version has no root activity to publish.");

        var activities = FlattenActivities(rootActivity).ToArray();
        ValidateActivityTree(activities);

        var activityRows = new Dictionary<string, ActivityDefinitionVersion>(StringComparer.Ordinal);
        foreach (var activityVersionId in activities.Select(x => x.ActivityVersionId).Distinct(StringComparer.Ordinal))
            activityRows[activityVersionId] = await activityVersions.GetVersionInlcudingDefinition(activityVersionId, cancellationToken);

        var compiledRoot = CompileNode(rootActivity, activityRows);
        var artifactHash = ComputeHash(version, compiledRoot);
        var artifactId = CreateArtifactId(artifactHash);
        var now = DateTimeOffset.UtcNow;

        var executable = new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(
                ArtifactId: artifactId,
                DefinitionId: version.DefinitionId,
                DefinitionVersionId: version.Id,
                ArtifactVersion: version.Version,
                ArtifactHash: artifactHash,
                Source: new WorkflowExecutableSourceReference("WorkflowDefinitionVersion", version.Id, version.Version)),
            rootActivity: compiledRoot,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: now,
            publishedAt: now,
            compatibilityMetadata: new Dictionary<string, string>
            {
                ["slice"] = "workflow-execution-vertical-slice"
            });

        await executableStore.SaveAsync(executable, cancellationToken);

        return new PublishedWorkflowView(
            executable.Identity.ArtifactId,
            executable.Identity.DefinitionId,
            executable.Identity.DefinitionVersionId,
            executable.Identity.ArtifactVersion,
            executable.Identity.ArtifactHash,
            executable.RootActivity.ExecutableNodeId,
            executable.Nodes.Count);
    }

    private static string CreateArtifactId(string artifactHash)
    {
        if (!artifactHash.StartsWith(ArtifactHashPrefix, StringComparison.Ordinal) ||
            artifactHash.Length < ArtifactHashPrefix.Length + ArtifactIdHashLength)
            throw new ArgumentException($"Artifact hash '{artifactHash}' does not use the expected '{ArtifactHashPrefix}' format.", nameof(artifactHash));

        return $"artifact-{artifactHash[ArtifactHashPrefix.Length..(ArtifactHashPrefix.Length + ArtifactIdHashLength)]}";
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

            inputBindings[inputDefinition.Name] = CompileLiteralInput(activity.NodeId, inputDefinition, inputState.Value);
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
                ["authoredNodeId"] = activity.NodeId
            },
            childSlots: childSlots,
            structure: CompileStructure(activityStructureService.CompileExecutableStructure(activity)));
    }

    private IReadOnlyCollection<ExecutableChildSlot> CompileChildSlots(
        IEnumerable<ActivityChildProjection> childSlots,
        IReadOnlyDictionary<string, ActivityDefinitionVersion> activityRows)
    {
        return childSlots
            .Select(slot => new ExecutableChildSlot(
                MapSlotName(slot.Name),
                slot.Activities.Select(activity => CompileNode(activity, activityRows)).ToArray()))
            .ToArray();
    }

    private static string MapSlotName(string name)
    {
        return name;
    }

    private static ExecutableActivityStructure? CompileStructure(ActivityNodeStructure? structure) =>
        structure is null
            ? null
            : new ExecutableActivityStructure(structure.Kind, structure.SchemaVersion, structure.Payload);

    private static RuntimeInputBinding CompileLiteralInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value)
    {
        if (!string.Equals(value.ExpressionType, LiteralExpressionType, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type '{value.ExpressionType}', but only literal inputs are supported by this vertical slice.");

        var inputType = inputDefinition.Type.LoadType();
        var converted = ConvertLiteral(value.Value, inputType);
        var literal = JsonSerializer.SerializeToElement(converted, inputType);

        return new RuntimeInputBinding(
            inputName: inputDefinition.Name,
            source: RuntimeInputBindingSource.Literal,
            literalValue: literal,
            metadata: new Dictionary<string, string>
            {
                [InputTypeMetadataKey] = GetRuntimeTypeName(inputType),
                [ReferenceKeyMetadataKey] = inputDefinition.ReferenceKey
            });
    }

    private static string GetRuntimeTypeName(Type type)
    {
        var fullName = type.FullName
            ?? throw new ArgumentException($"Input type '{type}' does not have a stable full name.", nameof(type));

        return $"{fullName}, {type.Assembly.GetName().Name}";
    }

    private static object? ConvertLiteral(string? value, Type targetType)
    {
        if (value is null)
            return null;

        var nullableTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nullableTargetType == typeof(string))
            return value;

        if (nullableTargetType.IsEnum)
            return Enum.Parse(nullableTargetType, value, ignoreCase: true);

        return Convert.ChangeType(value, nullableTargetType, CultureInfo.InvariantCulture);
    }

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
        WorkflowDefinitionVersion version,
        ExecutableNode rootActivity)
    {
        var nodes = FlattenExecutableActivities(rootActivity).ToArray();
        var payload = string.Join(
            '\n',
            version.Id,
            version.Version,
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

        return $"{input.Key}={input.Value.LiteralValue?.GetRawText()}[{metadata}]";
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
