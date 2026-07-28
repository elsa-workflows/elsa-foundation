using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring;

internal sealed class DescribeExpressionTooling(IEnumerable<IExpressionToolingProvider> providers)
    : ElsaEndpointWithoutRequest<ExpressionToolingDescriptorsResponse>
{
    public override void Configure()
    {
        Get(RouteConstants.ExpressionToolingDescriptors);
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        cancellationToken.ThrowIfCancellationRequested();
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
        var result = descriptors.Length == 0
            ? ExpressionToolingOutcome<IReadOnlyList<ExpressionToolingDescriptor>>.SupportedEmpty(
                descriptors,
                ExpressionToolingContractVersion.V1,
                "descriptors",
                "descriptors")
            : ExpressionToolingOutcome<IReadOnlyList<ExpressionToolingDescriptor>>.Success(
                descriptors,
                ExpressionToolingContractVersion.V1,
                "descriptors",
                "descriptors");
        await Send.OkAsync(new(result), cancellationToken);
    }
}
