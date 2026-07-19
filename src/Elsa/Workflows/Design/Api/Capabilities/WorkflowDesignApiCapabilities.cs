using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Tagging.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Capabilities;

public static class WorkflowDesignApiCapabilities
{
    public const string CapabilityId = "elsa.api.workflow-design";
    public const string SourceFeatureId = "WorkflowsDesignApi";

    public static ApiCapabilityDeclaration StaticDeclaration { get; } = new(
        CapabilityId,
        1,
        [
            new("workflow-definitions", "design/workflows/definitions"),
            new("workflow-drafts", "design/workflows/drafts/{draftId}", templated: true),
            new("workflow-versions", "design/workflows/versions/{versionId}", templated: true)
        ],
        SourceFeatureId);
}

/// <summary>Advertises only authoring operations backed by services in the active shell.</summary>
public sealed class WorkflowDesignOperationalCapabilitySource(
    ScopedVariableAuthoringContract? scopedVariables = null,
    IEnumerable<IActivityInputOptionsProvider>? inputOptionsProviders = null,
    IWorkflowDefinitionTagStore? workflowDefinitionTags = null,
    ITagDefinitionStore? tagDefinitions = null,
    ITagDefinitionCatalogPersistence? tagCatalogPersistence = null) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var links = new List<ApiCapabilityLink>();
        if (scopedVariables is not null)
            links.Add(new("scoped-variable-analysis", "design/workflows/scoped-variables/analyze"));
        if (inputOptionsProviders?.Any() == true)
            links.Add(new(
                "activity-input-options",
                "design/workflows/activities/{activityVersionId}/inputs/{inputName}/options",
                templated: true));
        if (workflowDefinitionTags is not null
            && tagDefinitions is not null
            && tagCatalogPersistence is not null)
            links.Add(new(
                "workflow-definition-tags",
                "design/workflows/definitions/{definitionId}/tags",
                templated: true));

        IReadOnlyCollection<ApiCapabilityDeclaration> declarations = links.Count == 0
            ? []
            : [new(
                WorkflowDesignApiCapabilities.CapabilityId,
                1,
                links,
                $"{WorkflowDesignApiCapabilities.SourceFeatureId}.Operational")];
        return ValueTask.FromResult(declarations);
    }
}
