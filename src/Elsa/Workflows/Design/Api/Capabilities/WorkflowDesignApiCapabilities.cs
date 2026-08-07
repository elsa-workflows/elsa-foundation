using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Expressions.Core.Contracts;
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
            new("workflow-definition-submit-schema", "design/workflows/definitions/submit/schema"),
            new("activity-structures", "design/workflows/structures"),
            new("workflow-drafts", "design/workflows/drafts/{draftId}", templated: true),
            new("workflow-draft-validations", "design/workflows/drafts/{draftId}/validations", templated: true),
            new("workflow-draft-promote-version-preflight", "design/workflows/drafts/{draftId}/promotion-preflight", templated: true),
            new("workflow-draft-promote-exact-version", "design/workflows/drafts/{draftId}/promote", templated: true),
            new("workflow-versions", "design/workflows/versions/{versionId}", templated: true)
        ],
        SourceFeatureId);
}

/// <summary>Advertises only authoring operations backed by services in the active shell.</summary>
public sealed class WorkflowDesignOperationalCapabilitySource(
    ScopedVariableAuthoringContract? scopedVariables = null,
    IEnumerable<IActivityInputOptionsProvider>? inputOptionsProviders = null,
    IExpressionAuthoringContextService? expressionTooling = null,
    IEnumerable<IExpressionToolingProvider>? expressionToolingProviders = null,
    IExpressionToolingProviderResolver? expressionToolingResolver = null) : IApiCapabilitySource
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
        var declarations = new List<ApiCapabilityDeclaration>();
        if (links.Count > 0)
            declarations.Add(new(
                WorkflowDesignApiCapabilities.CapabilityId,
                1,
                links,
                $"{WorkflowDesignApiCapabilities.SourceFeatureId}.Operational"));
        if (expressionTooling is not null &&
            expressionToolingResolver is not null &&
            expressionToolingProviders?.Any() == true)
            declarations.Add(new(
                "expressions.tooling.v1",
                1,
                [
                    new("expression-tooling-descriptors", "design/workflows/expression-tooling/descriptors"),
                    new("expression-tooling-context", "design/workflows/expression-tooling/context"),
                    new("expression-tooling-validate", "design/workflows/expression-tooling/validate"),
                    new("expression-tooling-symbols", "design/workflows/expression-tooling/symbols"),
                    new("expression-tooling-completions", "design/workflows/expression-tooling/completions"),
                    new("expression-tooling-hover", "design/workflows/expression-tooling/hover")
                ],
                $"{WorkflowDesignApiCapabilities.SourceFeatureId}.ExpressionTooling"));
        return ValueTask.FromResult<IReadOnlyCollection<ApiCapabilityDeclaration>>(declarations);
    }
}
