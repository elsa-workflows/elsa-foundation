using Elsa.Activities.Design.Core.Contracts;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Activities.Design.Api.Capabilities;

/// <summary>Advertises the activity-owned surface only when its graph, authority and optional tooling are composed.</summary>
internal sealed class ActivityExpressionToolingCapabilitySource(
    IEnumerable<IExpressionToolingOperations> operations,
    IEnumerable<IExpressionAuthoringContextService> contexts,
    IEnumerable<IActivityDraftAuthoringReader> readers,
    IEnumerable<IActivityDraftExpressionGraphProjection> projections,
    IEnumerable<IExpressionToolingProvider> providers,
    IEnumerable<IExpressionAuthoringAuthorizationPolicy> policies) : IApiCapabilitySource
{
    public ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyCollection<ApiCapabilityDeclaration> declarations = operations.Take(2).Count() == 1 && contexts.Take(2).Count() == 1 &&
            readers.Take(2).Count() == 1 && projections.Any() && providers.Any() && policies.Take(2).Count() == 1
            ? [new("activity-expressions.tooling.v1", 1,
                [
                    new("expression-tooling-descriptors", "design/activities/expression-tooling/descriptors"),
                    new("expression-tooling-context", "design/activities/expression-tooling/context"),
                    new("expression-tooling-validate", "design/activities/expression-tooling/validate"),
                    new("expression-tooling-symbols", "design/activities/expression-tooling/symbols"),
                    new("expression-tooling-completions", "design/activities/expression-tooling/completions"),
                    new("expression-tooling-hover", "design/activities/expression-tooling/hover")
                ], "ActivitiesDesignApi.ExpressionTooling")]
            : [];
        return ValueTask.FromResult(declarations);
    }
}
