using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring.DescribeExpressionTooling;

[Get("expression-tooling/descriptors")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(IEnumerable<IExpressionToolingProvider> providers)
    : ApiEndpointWithoutRequest<ExpressionToolingDescriptorsResponse>
{
    public override void Configure(ApiEndpointOptions options) =>
        options.Operation = "AuthoringDescribeExpressionTooling";

    public override Task<ExpressionToolingDescriptorsResponse> HandleAsync(CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        return Task.FromResult(new ExpressionToolingDescriptorsResponse(DescribeProviders()));
    }

    private ExpressionToolingOutcome<IReadOnlyList<ExpressionToolingDescriptor>> DescribeProviders()
    {
        var descriptors = providers
            .OrderBy(provider => provider.ExpressionType, StringComparer.Ordinal)
            .Select(provider =>
            {
                var assembly = provider.GetType().Assembly.GetName();
                return new ExpressionToolingDescriptor(
                    provider.ExpressionType,
                    assembly.Name ?? provider.GetType().Namespace ?? provider.ExpressionType,
                    assembly.Version?.ToString() ?? "0.0.0.0",
                    provider.SupportedVersion,
                    provider.DeclaredCapabilities);
            })
            .ToArray();

        return descriptors.Length == 0
            ? ExpressionToolingOutcome<IReadOnlyList<ExpressionToolingDescriptor>>.SupportedEmpty(descriptors, ExpressionToolingContractVersion.V1, "descriptors", "descriptors")
            : ExpressionToolingOutcome<IReadOnlyList<ExpressionToolingDescriptor>>.Success(descriptors, ExpressionToolingContractVersion.V1, "descriptors", "descriptors");
    }
}
