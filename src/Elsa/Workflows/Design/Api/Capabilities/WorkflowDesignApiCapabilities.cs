using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;

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
            new("workflow-draft-validations", "design/workflows/drafts/{draftId}/validations", templated: true),
            new("workflow-versions", "design/workflows/versions/{versionId}", templated: true)
        ],
        SourceFeatureId);
}

/// <summary>Advertises only authoring operations backed by services in the active shell.</summary>
public sealed class WorkflowDesignOperationalCapabilitySource(
    ScopedVariableAuthoringContract? scopedVariables = null,
    IEnumerable<IActivityInputOptionsProvider>? inputOptionsProviders = null) : IApiCapabilitySource
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
