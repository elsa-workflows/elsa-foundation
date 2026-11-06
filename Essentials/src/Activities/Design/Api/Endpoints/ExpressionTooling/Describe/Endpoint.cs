using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Design.Core.Contracts;
using MinimalEndpoints;

namespace Elsa.Activities.Design.Api.Endpoints.ExpressionTooling.Describe;

[Get("/design/activities/expression-tooling/descriptors")]
[RequirePermission(ActivityDesignPermissions.Read)]
public sealed class Endpoint(IEnumerable<IExpressionToolingProvider> providers, IEnumerable<IExpressionToolingOperations> operations)
    : ApiEndpointWithoutRequest<ActivityExpressionToolingResponse<IReadOnlyList<ActivityExpressionToolingDescriptor>>>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "ActivitiesExpressionToolingDescribe";

    public override Task<ActivityExpressionToolingResponse<IReadOnlyList<ActivityExpressionToolingDescriptor>>> HandleAsync(CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        if (operations.Take(2).Count() != 1)
            return Task.FromResult(new ActivityExpressionToolingResponse<IReadOnlyList<ActivityExpressionToolingDescriptor>>(
                ExpressionToolingOutcome<IReadOnlyList<ActivityExpressionToolingDescriptor>>.Failure(
                    ExpressionToolingOutcomeState.Unavailable, ExpressionToolingContractVersion.V1, "activity-expression-tooling-unavailable")));
        IReadOnlyList<ActivityExpressionToolingDescriptor> descriptors = providers.OrderBy(provider => provider.ExpressionType, StringComparer.Ordinal)
            .Select(provider =>
            {
                var assembly = provider.GetType().Assembly.GetName();
                return new ActivityExpressionToolingDescriptor(provider.ExpressionType,
                    assembly.Name ?? provider.GetType().Namespace ?? provider.ExpressionType, assembly.Version?.ToString() ?? "0.0.0.0",
                    provider.SupportedVersion, provider.DeclaredCapabilities);
            }).ToArray();
        return Task.FromResult(new ActivityExpressionToolingResponse<IReadOnlyList<ActivityExpressionToolingDescriptor>>(
            descriptors.Count == 0
                ? ExpressionToolingOutcome<IReadOnlyList<ActivityExpressionToolingDescriptor>>.SupportedEmpty(descriptors, ExpressionToolingContractVersion.V1, "descriptors", "descriptors")
                : ExpressionToolingOutcome<IReadOnlyList<ActivityExpressionToolingDescriptor>>.Success(descriptors, ExpressionToolingContractVersion.V1, "descriptors", "descriptors")));
    }
}
