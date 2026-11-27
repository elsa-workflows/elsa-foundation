using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>
/// Resolves context-sensitive options against the submitted authored workflow rather than against
/// a global activity descriptor. Keeping this operation in Workflow Design prevents Activity Design
/// from acquiring knowledge of workflow state and node structure.
/// </summary>
public sealed class ActivityInputOptionsAuthoringService(
    IActivityDefinitionVersionStore activityVersionStore,
    IActivityStructureService activityStructureService,
    IActivityInputOptionsProviderResolver providerResolver,
    ILogger<ActivityInputOptionsAuthoringService> logger)
{
    public async Task<ActivityInputOptionsResolution> ResolveAsync(
        string activityVersionId,
        string inputName,
        string? nodeId,
        WorkflowDefinitionStateView? workflowStateView,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || workflowStateView is null)
            return ActivityInputOptionsResolution.BadRequest("A node id and workflow state are required.");

        Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersion activityVersion;
        try
        {
            activityVersion = await activityVersionStore.GetAsync(activityVersionId, cancellationToken);
        }
        catch (EntityNotFoundException)
        {
            return ActivityInputOptionsResolution.NotFound($"Activity version '{activityVersionId}' was not found.");
        }

        var input = activityVersion.Inputs.FirstOrDefault(x => string.Equals(x.Name, inputName, StringComparison.Ordinal));
        if (input is null)
            return ActivityInputOptionsResolution.NotFound($"Input '{inputName}' was not found on activity version '{activityVersionId}'.");

        if (!TryGetOptionsProviderKey(input.UISpecifications, out var providerKey))
            return ActivityInputOptionsResolution.Conflict($"Input '{inputName}' does not declare a dynamic options provider.");

        var workflowState = workflowStateView.ToState();
        var activity = FindActivityNode(workflowState.RootActivity, nodeId);
        if (activity is null)
            return ActivityInputOptionsResolution.BadRequest($"Activity node '{nodeId}' was not found in the submitted workflow state.");

        if (!string.Equals(activity.ActivityVersionId, activityVersionId, StringComparison.Ordinal))
            return ActivityInputOptionsResolution.Conflict($"Activity node '{nodeId}' does not use activity version '{activityVersionId}'.");

        var provider = providerResolver.Find(providerKey);
        if (provider is null)
            return ActivityInputOptionsResolution.Unavailable(inputName);

        try
        {
            var options = await provider.GetOptionsAsync(new ActivityInputOptionsContext(workflowState, activity, input), cancellationToken);
            return ActivityInputOptionsResolution.Success(options);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Activity input options provider {ProviderKey} failed for input {InputName}", providerKey, inputName);
            return ActivityInputOptionsResolution.Unavailable(inputName);
        }
    }

    private static bool TryGetOptionsProviderKey(JsonElement? uiSpecifications, out string key)
    {
        key = string.Empty;
        if (uiSpecifications is not { ValueKind: JsonValueKind.Object } specifications ||
            !specifications.TryGetProperty("optionsProvider", out var provider) ||
            provider.ValueKind != JsonValueKind.Object ||
            !provider.TryGetProperty("key", out var keyProperty) ||
            keyProperty.ValueKind != JsonValueKind.String)
            return false;

        key = keyProperty.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(key);
    }

    private ActivityNode? FindActivityNode(ActivityNode? root, string nodeId)
    {
        if (root is null)
            return null;

        var stack = new Stack<ActivityNode>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        stack.Push(root);

        while (stack.TryPop(out var node))
        {
            if (!visited.Add(node.NodeId))
                continue;
            if (string.Equals(node.NodeId, nodeId, StringComparison.Ordinal))
                return node;

            foreach (var child in activityStructureService.ProjectChildren(node).SelectMany(x => x.Activities))
                stack.Push(child);
        }

        return null;
    }
}

public sealed record ActivityInputOptionsResolution(
    int StatusCode,
    IReadOnlyList<ActivityInputOption>? Options = null,
    string? Error = null,
    string? Code = null)
{
    public static ActivityInputOptionsResolution Success(IReadOnlyList<ActivityInputOption> options) => new(200, options);
    public static ActivityInputOptionsResolution BadRequest(string error) => new(400, Error: error);
    public static ActivityInputOptionsResolution NotFound(string error) => new(404, Error: error);
    public static ActivityInputOptionsResolution Conflict(string error) => new(409, Error: error);
    public static ActivityInputOptionsResolution Unavailable(string inputName) =>
        new(503, Error: $"Options for input '{inputName}' are temporarily unavailable.", Code: "OPTIONS_PROVIDER_UNAVAILABLE");
}
