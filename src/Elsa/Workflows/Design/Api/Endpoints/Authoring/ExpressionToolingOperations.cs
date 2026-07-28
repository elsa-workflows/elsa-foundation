using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring;

internal static class ExpressionToolingOperationLimits
{
    public static ValueTask<ExpressionAuthoringAuthorization> AuthorizationAsync(
        HttpContext httpContext,
        IExpressionAuthoringAuthorizationPolicy authorizationPolicy,
        CancellationToken cancellationToken)
    {
        var subjectId = httpContext.User.FindFirstValue("sub") ??
                        httpContext.User.Identity?.Name;
        var permissionRevision = Revision(httpContext.User.Claims
            .Select(claim => $"{claim.Type}\u001f{claim.Value}")
            .OrderBy(value => value, StringComparer.Ordinal));
        return authorizationPolicy.AuthorizeAsync(
            new(true, subjectId, permissionRevision, permissionRevision),
            cancellationToken);
    }

    private static string Revision(IEnumerable<string> values)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001e', values)));
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }

    public static async ValueTask<ExpressionToolingOutcome<T>> InvokeProviderAsync<T>(
        Func<ValueTask<ExpressionToolingOutcome<T>>> invoke,
        ExpressionAuthoringContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ExpressionToolingOutcome<T>.Failure(
                ExpressionToolingOutcomeState.Canceled,
                ExpressionToolingContractVersion.V1,
                "provider-canceled",
                documentRevision: context.Document.DocumentRevision,
                contextRevision: context.ContextRevision);
        }
        catch
        {
            return ExpressionToolingOutcome<T>.Failure(
                ExpressionToolingOutcomeState.Unavailable,
                ExpressionToolingContractVersion.V1,
                "provider-failed",
                documentRevision: context.Document.DocumentRevision,
                contextRevision: context.ContextRevision);
        }
    }

    public static async ValueTask<ExpressionToolingOutcome<T>> InvokeSupportedProviderAsync<T>(
        IExpressionToolingProvider provider,
        ExpressionAuthoringContext context,
        Func<ExpressionToolingCapabilities, bool> isSupported,
        string unsupportedCode,
        Func<ValueTask<ExpressionToolingOutcome<T>>> invoke,
        CancellationToken cancellationToken)
    {
        if (!isSupported(context.Capabilities))
            return ExpressionToolingOutcome<T>.Failure(
                ExpressionToolingOutcomeState.Unavailable,
                ExpressionToolingContractVersion.V1,
                unsupportedCode,
                documentRevision: context.Document.DocumentRevision,
                contextRevision: context.ContextRevision);
        var scope = new ExpressionToolingRequestScope(
            ExpressionToolingContractVersion.V1,
            context.Document,
            context);
        var capabilities = await InvokeProviderAsync(
            () => provider.GetCapabilitiesAsync(scope, cancellationToken),
            context,
            cancellationToken);
        if (!capabilities.IsSuccess)
            return ExpressionToolingOutcome<T>.Failure(
                capabilities.State,
                capabilities.ContractVersion,
                capabilities.Code,
                capabilities.Message,
                capabilities.DocumentRevision,
                capabilities.ContextRevision);
        if (!isSupported(capabilities.Payload!))
            return ExpressionToolingOutcome<T>.Failure(
                ExpressionToolingOutcomeState.Unavailable,
                ExpressionToolingContractVersion.V1,
                unsupportedCode,
                documentRevision: context.Document.DocumentRevision,
                contextRevision: context.ContextRevision);

        return await InvokeProviderAsync(invoke, context, cancellationToken);
    }
}

internal sealed class SearchExpressionToolingSymbols(
    IExpressionAuthoringContextService contextService,
    IExpressionToolingProviderResolver providerResolver,
    IExpressionAuthoringAuthorizationPolicy authorizationPolicy)
    : ElsaEndpoint<ExpressionToolingContextRequest, ExpressionToolingOperationResponse<ExpressionToolingItems>>
{
    public override void Configure()
    {
        Post(RouteConstants.ExpressionToolingSymbols);
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
        var result = context.IsSuccess && providerResolver.Find(request.ExpressionType) is null
            ? ExpressionToolingOutcome<ExpressionToolingItems>.Failure(
                ExpressionToolingOutcomeState.Unavailable,
                ExpressionToolingContractVersion.V1,
                "provider-unavailable",
                documentRevision: context.Payload!.Document.DocumentRevision,
                contextRevision: context.Payload.ContextRevision)
            : context.IsSuccess
            ? (context.Payload!.RootSymbols.Count == 0
                ? ExpressionToolingOutcome<ExpressionToolingItems>.SupportedEmpty(new([]), ExpressionToolingContractVersion.V1, context.Payload.Document.DocumentRevision, context.Payload.ContextRevision)
                : ExpressionToolingOutcome<ExpressionToolingItems>.Success(new(context.Payload.RootSymbols.Select(symbol => new ExpressionToolingItem(symbol.Name, symbol.ValueShape?.DisplayName, symbol.Documentation, symbol.Name, symbol.Kind)).ToArray()), ExpressionToolingContractVersion.V1, context.Payload.Document.DocumentRevision, context.Payload.ContextRevision))
            : ExpressionToolingOutcome<ExpressionToolingItems>.Failure(context.State, context.ContractVersion, context.Code, context.Message, context.DocumentRevision, context.ContextRevision);
        await Send.OkAsync(new ExpressionToolingOperationResponse<ExpressionToolingItems>(result), cancellationToken);
    }
}

internal sealed class CompleteExpressionTooling(
    IExpressionAuthoringContextService contextService,
    IExpressionToolingProviderResolver providerResolver,
    IExpressionAuthoringAuthorizationPolicy authorizationPolicy)
    : ElsaEndpoint<ExpressionToolingCompletionRequest, ExpressionToolingOperationResponse<ExpressionToolingItems>>
{
    public override void Configure()
    {
        Post(RouteConstants.ExpressionToolingCompletions);
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }

    public override async Task HandleAsync(ExpressionToolingCompletionRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var authorization = await ExpressionToolingOperationLimits.AuthorizationAsync(HttpContext, authorizationPolicy, cancellationToken);
        var context = await contextService.ResolveForProviderAsync(new(
            request.ContractVersion,
            request.WorkflowDraftId,
            request.NodeId,
            request.PropertyKey,
            request.ExpressionType,
            request.DocumentRevision,
            ContextRevision: request.ContextRevision),
            authorization,
            cancellationToken);
        ExpressionToolingOutcome<ExpressionToolingItems> result;
        if (!context.IsSuccess)
            result = ExpressionToolingOutcome<ExpressionToolingItems>.Failure(context.State, context.ContractVersion, context.Code, context.Message, context.DocumentRevision, context.ContextRevision);
        else if (providerResolver.Find(request.ExpressionType) is not { } provider)
            result = ExpressionToolingOutcome<ExpressionToolingItems>.Failure(
                ExpressionToolingOutcomeState.Unavailable,
                ExpressionToolingContractVersion.V1,
                "provider-unavailable",
                documentRevision: context.Payload!.Document.DocumentRevision,
                contextRevision: context.Payload.ContextRevision);
        else if (!request.Cursor.IsValid)
            result = ExpressionToolingOutcome<ExpressionToolingItems>.Failure(
                ExpressionToolingOutcomeState.Incompatible,
                ExpressionToolingContractVersion.V1,
                "invalid-cursor",
                documentRevision: context.Payload!.Document.DocumentRevision,
                contextRevision: context.Payload.ContextRevision);
        else
            result = await ExpressionToolingOperationLimits.InvokeSupportedProviderAsync(
                provider,
                context.Payload!,
                capabilities => capabilities.SupportsCompletions,
                "completion-unsupported",
                () => provider.GetCompletionsAsync(
                    new(new(request.ContractVersion, context.Payload!.Document, context.Payload), request.Source, request.Cursor),
                    cancellationToken),
                cancellationToken);
        await Send.OkAsync(new ExpressionToolingOperationResponse<ExpressionToolingItems>(result), cancellationToken);
    }
}

internal sealed class HoverExpressionTooling(
    IExpressionAuthoringContextService contextService,
    IExpressionToolingProviderResolver providerResolver,
    IExpressionAuthoringAuthorizationPolicy authorizationPolicy)
    : ElsaEndpoint<ExpressionToolingHoverRequest, ExpressionToolingOperationResponse<ExpressionHover>>
{
    public override void Configure()
    {
        Post(RouteConstants.ExpressionToolingHover);
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }

    public override async Task HandleAsync(ExpressionToolingHoverRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var authorization = await ExpressionToolingOperationLimits.AuthorizationAsync(HttpContext, authorizationPolicy, cancellationToken);
        var context = await contextService.ResolveForProviderAsync(new(
            request.ContractVersion,
            request.WorkflowDraftId,
            request.NodeId,
            request.PropertyKey,
            request.ExpressionType,
            request.DocumentRevision,
            ContextRevision: request.ContextRevision),
            authorization,
            cancellationToken);
        ExpressionToolingOutcome<ExpressionHover> result;
        if (!context.IsSuccess)
            result = ExpressionToolingOutcome<ExpressionHover>.Failure(context.State, context.ContractVersion, context.Code, context.Message, context.DocumentRevision, context.ContextRevision);
        else if (providerResolver.Find(request.ExpressionType) is not { } provider)
            result = ExpressionToolingOutcome<ExpressionHover>.Failure(
                ExpressionToolingOutcomeState.Unavailable,
                ExpressionToolingContractVersion.V1,
                "provider-unavailable",
                documentRevision: context.Payload!.Document.DocumentRevision,
                contextRevision: context.Payload.ContextRevision);
        else if (!request.Position.IsValid)
            result = ExpressionToolingOutcome<ExpressionHover>.Failure(
                ExpressionToolingOutcomeState.Incompatible,
                ExpressionToolingContractVersion.V1,
                "invalid-position",
                documentRevision: context.Payload!.Document.DocumentRevision,
                contextRevision: context.Payload.ContextRevision);
        else
            result = await ExpressionToolingOperationLimits.InvokeSupportedProviderAsync(
                provider,
                context.Payload!,
                capabilities => capabilities.SupportsHover,
                "hover-unsupported",
                () => provider.GetHoverAsync(
                    new(new(request.ContractVersion, context.Payload!.Document, context.Payload), request.Source, request.Position),
                    cancellationToken),
                cancellationToken);
        await Send.OkAsync(new ExpressionToolingOperationResponse<ExpressionHover>(result), cancellationToken);
    }
}

internal sealed class ValidateExpressionTooling(
    IExpressionAuthoringContextService contextService,
    IExpressionToolingProviderResolver providerResolver,
    IExpressionAuthoringAuthorizationPolicy authorizationPolicy)
    : ElsaEndpoint<ExpressionToolingSourceRequest, ExpressionToolingOperationResponse<ExpressionDiagnosticSet>>
{
    public override void Configure()
    {
        Post(RouteConstants.ExpressionToolingValidate);
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }

    public override async Task HandleAsync(ExpressionToolingSourceRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var authorization = await ExpressionToolingOperationLimits.AuthorizationAsync(HttpContext, authorizationPolicy, cancellationToken);
        var context = await contextService.ResolveForProviderAsync(new(
            request.ContractVersion,
            request.WorkflowDraftId,
            request.NodeId,
            request.PropertyKey,
            request.ExpressionType,
            request.DocumentRevision,
            ContextRevision: request.ContextRevision),
            authorization,
            cancellationToken);
        ExpressionToolingOutcome<ExpressionDiagnosticSet> result;
        if (!context.IsSuccess)
            result = ExpressionToolingOutcome<ExpressionDiagnosticSet>.Failure(context.State, context.ContractVersion, context.Code, context.Message, context.DocumentRevision, context.ContextRevision);
        else if (providerResolver.Find(request.ExpressionType) is not { } provider)
            result = ExpressionToolingOutcome<ExpressionDiagnosticSet>.Failure(
                ExpressionToolingOutcomeState.Unavailable,
                ExpressionToolingContractVersion.V1,
                "provider-unavailable",
                documentRevision: context.Payload!.Document.DocumentRevision,
                contextRevision: context.Payload.ContextRevision);
        else
            result = await ExpressionToolingOperationLimits.InvokeSupportedProviderAsync(
                provider,
                context.Payload!,
                capabilities => capabilities.SupportsValidation,
                "validation-unsupported",
                () => provider.ValidateAsync(
                    new(new(request.ContractVersion, context.Payload!.Document, context.Payload), request.Source),
                    cancellationToken),
                cancellationToken);
        await Send.OkAsync(new ExpressionToolingOperationResponse<ExpressionDiagnosticSet>(result), cancellationToken);
    }
}
