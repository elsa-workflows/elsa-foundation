using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring;

internal sealed class ResolveExpressionToolingContext(
    IExpressionAuthoringContextService contextService,
    IExpressionToolingProviderResolver providerResolver,
    IExpressionAuthoringAuthorizationPolicy authorizationPolicy)
    : ElsaEndpoint<ExpressionToolingContextRequest, ExpressionToolingContextResponse>
{
    public override void Configure()
    {
        Post(RouteConstants.ExpressionToolingContext);
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }

    public override async Task HandleAsync(ExpressionToolingContextRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var authorization = await ExpressionToolingOperationLimits.AuthorizationAsync(HttpContext, authorizationPolicy, cancellationToken);
        var context = await contextService.ResolveAsync(
            new(request.ContractVersion, request.WorkflowDraftId, request.NodeId, request.PropertyKey, request.ExpressionType, request.DocumentRevision, request.ContextRevision, request.Search, request.Skip, request.Take),
            authorization,
            cancellationToken);
        var result = await WithProviderCapabilitiesAsync(context, request, cancellationToken);
        await Send.OkAsync(new ExpressionToolingContextResponse(result), cancellationToken);
    }

    private async ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> WithProviderCapabilitiesAsync(
        ExpressionToolingOutcome<ExpressionAuthoringContext> context,
        ExpressionToolingContextRequest request,
        CancellationToken cancellationToken)
    {
        if (!context.IsSuccess)
            return context;
        if (providerResolver.Find(request.ExpressionType) is not { } provider)
            return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(
                ExpressionToolingOutcomeState.Unavailable,
                ExpressionToolingContractVersion.V1,
                "provider-unavailable",
                documentRevision: context.Payload!.Document.DocumentRevision,
                contextRevision: context.Payload.ContextRevision);

        var capabilities = await ExpressionToolingOperationLimits.InvokeProviderAsync(
            () => provider.GetCapabilitiesAsync(
                new(request.ContractVersion, context.Payload!.Document, context.Payload),
                cancellationToken),
            context.Payload!,
            cancellationToken);
        if (!capabilities.IsSuccess)
            return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(
                capabilities.State,
                capabilities.ContractVersion,
                capabilities.Code,
                capabilities.Message,
                capabilities.DocumentRevision,
                capabilities.ContextRevision);

        var payload = context.Payload! with { Capabilities = capabilities.Payload! };
        return context.State == ExpressionToolingOutcomeState.SupportedEmpty
            ? ExpressionToolingOutcome<ExpressionAuthoringContext>.SupportedEmpty(
                payload,
                context.ContractVersion,
                payload.Document.DocumentRevision,
                payload.ContextRevision)
            : ExpressionToolingOutcome<ExpressionAuthoringContext>.Success(
                payload,
                context.ContractVersion,
                payload.Document.DocumentRevision,
                payload.ContextRevision);
    }
}
