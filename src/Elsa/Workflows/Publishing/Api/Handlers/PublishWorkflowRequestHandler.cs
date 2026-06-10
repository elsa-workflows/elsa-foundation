using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
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
    IWorkflowExecutableStore executableStore)
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
        var activities = state.Activities.ToArray();

        ValidateGraphShape(activities, state.ActivityConnections);

        var activityRows = new Dictionary<string, ActivityDefinitionVersion>(StringComparer.Ordinal);
        foreach (var activityVersionId in activities.Select(x => x.ActivityVersionId).Distinct(StringComparer.Ordinal))
            activityRows[activityVersionId] = await activityVersions.GetVersionInlcudingDefinition(activityVersionId, cancellationToken);

        var nodes = activities.Select(activity => CompileNode(activity, activityRows[activity.ActivityVersionId])).ToArray();
        var edges = state.ActivityConnections
            .Select(connection => new ExecutableEdge(
                connection.Source.ActivityNodeId,
                connection.Source.Port,
                connection.Target.ActivityNodeId,
                connection.Target.Port))
            .ToArray();
        var startNodeIds = activities.Where(activity => activity.IsStart).Select(activity => activity.NodeId).ToArray();
        var artifactHash = ComputeHash(version, nodes, edges, startNodeIds);
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
            nodes: nodes,
            edges: edges,
            startNodeIds: startNodeIds,
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
            executable.Nodes.Count,
            executable.Edges.Count,
            executable.StartNodeIds.ToArray());
    }

    private static string CreateArtifactId(string artifactHash)
    {
        if (!artifactHash.StartsWith(ArtifactHashPrefix, StringComparison.Ordinal) ||
            artifactHash.Length < ArtifactHashPrefix.Length + ArtifactIdHashLength)
            throw new ArgumentException($"Artifact hash '{artifactHash}' does not use the expected '{ArtifactHashPrefix}' format.", nameof(artifactHash));

        return $"artifact-{artifactHash[ArtifactHashPrefix.Length..(ArtifactHashPrefix.Length + ArtifactIdHashLength)]}";
    }

    private static ExecutableNode CompileNode(ActivityNode activity, ActivityDefinitionVersion activityVersion)
    {
        if (activity.ChildActivities.Any())
            throw new ArgumentException($"Activity node '{activity.NodeId}' contains child activities, which are not supported by this vertical slice.");

        var inputDefinitionsByReferenceKey = activityVersion.Inputs.ToDictionary(input => input.ReferenceKey, StringComparer.Ordinal);
        var inputBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);

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
                ["isTerminal"] = activity.IsTerminal.ToString(CultureInfo.InvariantCulture)
            });
    }

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

    private static void ValidateGraphShape(IReadOnlyCollection<ActivityNode> activities, IEnumerable<ActivityConnection> connections)
    {
        if (activities.Count == 0)
            throw new ArgumentException("Workflow version has no activity nodes to publish.");

        var duplicateNodeId = activities.GroupBy(activity => activity.NodeId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateNodeId is not null)
            throw new ArgumentException($"Workflow version contains duplicate activity node id '{duplicateNodeId}'.");

        var startNodes = activities.Where(activity => activity.IsStart).ToArray();
        if (startNodes.Length != 1)
            throw new ArgumentException($"Sequential publishing requires exactly one start activity, but the workflow has {startNodes.Length}.");

        var nodeIds = activities.Select(activity => activity.NodeId).ToHashSet(StringComparer.Ordinal);
        var connectionSnapshot = connections.ToArray();
        foreach (var connection in connectionSnapshot)
        {
            if (!nodeIds.Contains(connection.Source.ActivityNodeId))
                throw new ArgumentException($"Workflow connection source activity '{connection.Source.ActivityNodeId}' does not exist.");

            if (!nodeIds.Contains(connection.Target.ActivityNodeId))
                throw new ArgumentException($"Workflow connection target activity '{connection.Target.ActivityNodeId}' does not exist.");
        }

        var fanOut = connectionSnapshot.GroupBy(connection => connection.Source.ActivityNodeId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (fanOut is not null)
            throw new ArgumentException($"Sequential publishing does not support fan-out from activity node '{fanOut.Key}'.");

        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? current = startNodes[0].NodeId;
        while (current is not null)
        {
            if (!visited.Add(current))
                throw new ArgumentException($"Workflow graph contains a cycle at activity node '{current}'.");

            current = connectionSnapshot.FirstOrDefault(connection => connection.Source.ActivityNodeId == current)?.Target.ActivityNodeId;
        }

        var unreachable = nodeIds.Except(visited, StringComparer.Ordinal).ToArray();
        if (unreachable.Length > 0)
            throw new ArgumentException($"Workflow graph contains unreachable activity nodes: {string.Join(", ", unreachable)}.");
    }

    private static string ComputeHash(
        WorkflowDefinitionVersion version,
        IReadOnlyCollection<ExecutableNode> nodes,
        IReadOnlyCollection<ExecutableEdge> edges,
        IReadOnlyCollection<string> startNodeIds)
    {
        var payload = string.Join(
            '\n',
            version.Id,
            version.Version,
            string.Join('|', startNodeIds.Order(StringComparer.Ordinal)),
            string.Join('|', nodes.OrderBy(node => node.ExecutableNodeId, StringComparer.Ordinal)
                .Select(node => $"{node.ExecutableNodeId}:{node.ActivityType}:{node.ActivityTypeVersion}:{node.DescriptorType}:{node.DescriptorPayload.GetRawText()}:{string.Join(',', node.InputBindings.OrderBy(input => input.Key, StringComparer.Ordinal).Select(input => $"{input.Key}={input.Value.LiteralValue?.GetRawText()}"))}")),
            string.Join('|', edges
                .OrderBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
                .ThenBy(edge => edge.SourcePort, StringComparer.Ordinal)
                .ThenBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
                .ThenBy(edge => edge.TargetPort, StringComparer.Ordinal)
                .Select(edge => $"{edge.SourceNodeId}:{edge.SourcePort}>{edge.TargetNodeId}:{edge.TargetPort}")));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
