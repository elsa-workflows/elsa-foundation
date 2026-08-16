using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Design.Api;

internal static class ExpressionToolingApiHandlers
{
    public static ValueTask<ExpressionAuthoringAuthorization> AuthorizationAsync(
        HttpContext context,
        IExpressionAuthoringAuthorizationPolicy policy,
        CancellationToken cancellationToken)
    {
        var subjectId = context.User.FindFirstValue("sub") ?? context.User.Identity?.Name;
        var permissionRevision = Revision(context.User.Claims.Select(claim => $"{claim.Type}\u001f{claim.Value}")
            .OrderBy(value => value, StringComparer.Ordinal));
        return policy.AuthorizeAsync(new(true, subjectId, permissionRevision, permissionRevision), cancellationToken);
    }

    public static async ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveContextAsync(
        HttpContext context,
        ExpressionToolingContextRequest request)
    {
        var contextService = context.RequestServices.GetRequiredService<IExpressionAuthoringContextService>();
        var providerResolver = context.RequestServices.GetRequiredService<IExpressionToolingProviderResolver>();
        var authorizationPolicy = context.RequestServices.GetRequiredService<IExpressionAuthoringAuthorizationPolicy>();
        var authorization = await AuthorizationAsync(context, authorizationPolicy, context.RequestAborted);
        var resolved = await contextService.ResolveAsync(
            new(request.ContractVersion, request.WorkflowDraftId, request.NodeId, request.PropertyKey, request.ExpressionType, request.DocumentRevision, request.ContextRevision, request.Search, request.Skip, request.Take),
            authorization,
            context.RequestAborted);
        if (!resolved.IsSuccess)
            return resolved;
        if (providerResolver.Find(request.ExpressionType) is not { } provider)
            return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(
                ExpressionToolingOutcomeState.Unavailable,
                ExpressionToolingContractVersion.V1,
                "provider-unavailable",
                documentRevision: resolved.Payload!.Document.DocumentRevision,
                contextRevision: resolved.Payload.ContextRevision);

        var capabilities = await InvokeProviderAsync(
            () => provider.GetCapabilitiesAsync(new(request.ContractVersion, resolved.Payload!.Document, resolved.Payload), context.RequestAborted),
            resolved.Payload!, context.RequestAborted);
        if (!capabilities.IsSuccess)
            return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(
                capabilities.State, capabilities.ContractVersion, capabilities.Code, capabilities.Message,
                capabilities.DocumentRevision, capabilities.ContextRevision);

        var payload = resolved.Payload! with { Capabilities = capabilities.Payload! };
        return resolved.State == ExpressionToolingOutcomeState.SupportedEmpty
            ? ExpressionToolingOutcome<ExpressionAuthoringContext>.SupportedEmpty(payload, resolved.ContractVersion, payload.Document.DocumentRevision, payload.ContextRevision)
            : ExpressionToolingOutcome<ExpressionAuthoringContext>.Success(payload, resolved.ContractVersion, payload.Document.DocumentRevision, payload.ContextRevision);
    }

    public static async ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> SearchSymbolsAsync(
        HttpContext context,
        ExpressionToolingContextRequest request)
    {
        var contextService = context.RequestServices.GetRequiredService<IExpressionAuthoringContextService>();
        var providerResolver = context.RequestServices.GetRequiredService<IExpressionToolingProviderResolver>();
        var authorization = await AuthorizationAsync(context,
            context.RequestServices.GetRequiredService<IExpressionAuthoringAuthorizationPolicy>(), context.RequestAborted);
        var resolved = await contextService.ResolveAsync(
            new(request.ContractVersion, request.WorkflowDraftId, request.NodeId, request.PropertyKey, request.ExpressionType, request.DocumentRevision, request.ContextRevision, request.Search, request.Skip, request.Take),
            authorization, context.RequestAborted);
        if (!resolved.IsSuccess)
            return ExpressionToolingOutcome<ExpressionToolingItems>.Failure(resolved.State, resolved.ContractVersion, resolved.Code, resolved.Message, resolved.DocumentRevision, resolved.ContextRevision);
        if (providerResolver.Find(request.ExpressionType) is null)
            return ExpressionToolingOutcome<ExpressionToolingItems>.Failure(ExpressionToolingOutcomeState.Unavailable, ExpressionToolingContractVersion.V1, "provider-unavailable", documentRevision: resolved.Payload!.Document.DocumentRevision, contextRevision: resolved.Payload.ContextRevision);
        return resolved.Payload!.RootSymbols.Count == 0
            ? ExpressionToolingOutcome<ExpressionToolingItems>.SupportedEmpty(new([]), ExpressionToolingContractVersion.V1, resolved.Payload.Document.DocumentRevision, resolved.Payload.ContextRevision)
            : ExpressionToolingOutcome<ExpressionToolingItems>.Success(new(resolved.Payload.RootSymbols.Select(symbol => new ExpressionToolingItem(symbol.Name, symbol.ValueShape?.DisplayName, symbol.Documentation, symbol.Name, symbol.Kind)).ToArray()), ExpressionToolingContractVersion.V1, resolved.Payload.Document.DocumentRevision, resolved.Payload.ContextRevision);
    }

    public static async ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> CompleteAsync(
        HttpContext context,
        ExpressionToolingCompletionRequest request)
    {
        var contextService = context.RequestServices.GetRequiredService<IExpressionAuthoringContextService>();
        var providerResolver = context.RequestServices.GetRequiredService<IExpressionToolingProviderResolver>();
        var authorization = await AuthorizationAsync(context,
            context.RequestServices.GetRequiredService<IExpressionAuthoringAuthorizationPolicy>(), context.RequestAborted);
        var resolved = await contextService.ResolveForProviderAsync(new(request.ContractVersion, request.WorkflowDraftId, request.NodeId, request.PropertyKey, request.ExpressionType, request.DocumentRevision, request.ContextRevision), authorization, context.RequestAborted);
        if (!resolved.IsSuccess)
            return ExpressionToolingOutcome<ExpressionToolingItems>.Failure(resolved.State, resolved.ContractVersion, resolved.Code, resolved.Message, resolved.DocumentRevision, resolved.ContextRevision);
        if (providerResolver.Find(request.ExpressionType) is not { } provider)
            return Unavailable<ExpressionToolingItems>(resolved.Payload!);
        if (!request.Cursor.IsValid)
            return ExpressionToolingOutcome<ExpressionToolingItems>.Failure(ExpressionToolingOutcomeState.Incompatible, ExpressionToolingContractVersion.V1, "invalid-cursor", documentRevision: resolved.Payload!.Document.DocumentRevision, contextRevision: resolved.Payload.ContextRevision);
        return await InvokeSupportedProviderAsync(provider, resolved.Payload!, capabilities => capabilities.SupportsCompletions, "completion-unsupported", () => provider.GetCompletionsAsync(new(new(request.ContractVersion, resolved.Payload!.Document, resolved.Payload), request.Source, request.Cursor), context.RequestAborted), context.RequestAborted);
    }

    public static async ValueTask<ExpressionToolingOutcome<ExpressionHover>> HoverAsync(HttpContext context, ExpressionToolingHoverRequest request)
    {
        var contextService = context.RequestServices.GetRequiredService<IExpressionAuthoringContextService>();
        var providerResolver = context.RequestServices.GetRequiredService<IExpressionToolingProviderResolver>();
        var authorization = await AuthorizationAsync(context, context.RequestServices.GetRequiredService<IExpressionAuthoringAuthorizationPolicy>(), context.RequestAborted);
        var resolved = await contextService.ResolveForProviderAsync(new(request.ContractVersion, request.WorkflowDraftId, request.NodeId, request.PropertyKey, request.ExpressionType, request.DocumentRevision, request.ContextRevision), authorization, context.RequestAborted);
        if (!resolved.IsSuccess)
            return ExpressionToolingOutcome<ExpressionHover>.Failure(resolved.State, resolved.ContractVersion, resolved.Code, resolved.Message, resolved.DocumentRevision, resolved.ContextRevision);
        if (providerResolver.Find(request.ExpressionType) is not { } provider)
            return Unavailable<ExpressionHover>(resolved.Payload!);
        if (!request.Position.IsValid)
            return ExpressionToolingOutcome<ExpressionHover>.Failure(ExpressionToolingOutcomeState.Incompatible, ExpressionToolingContractVersion.V1, "invalid-position", documentRevision: resolved.Payload!.Document.DocumentRevision, contextRevision: resolved.Payload.ContextRevision);
        return await InvokeSupportedProviderAsync(provider, resolved.Payload!, capabilities => capabilities.SupportsHover, "hover-unsupported", () => provider.GetHoverAsync(new(new(request.ContractVersion, resolved.Payload!.Document, resolved.Payload), request.Source, request.Position), context.RequestAborted), context.RequestAborted);
    }

    public static async ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(HttpContext context, ExpressionToolingSourceRequest request)
    {
        var contextService = context.RequestServices.GetRequiredService<IExpressionAuthoringContextService>();
        var providerResolver = context.RequestServices.GetRequiredService<IExpressionToolingProviderResolver>();
        var authorization = await AuthorizationAsync(context, context.RequestServices.GetRequiredService<IExpressionAuthoringAuthorizationPolicy>(), context.RequestAborted);
        var resolved = await contextService.ResolveForProviderAsync(new(request.ContractVersion, request.WorkflowDraftId, request.NodeId, request.PropertyKey, request.ExpressionType, request.DocumentRevision, request.ContextRevision), authorization, context.RequestAborted);
        if (!resolved.IsSuccess)
            return ExpressionToolingOutcome<ExpressionDiagnosticSet>.Failure(resolved.State, resolved.ContractVersion, resolved.Code, resolved.Message, resolved.DocumentRevision, resolved.ContextRevision);
        if (providerResolver.Find(request.ExpressionType) is not { } provider)
            return Unavailable<ExpressionDiagnosticSet>(resolved.Payload!);
        return await InvokeSupportedProviderAsync(provider, resolved.Payload!, capabilities => capabilities.SupportsValidation, "validation-unsupported", () => provider.ValidateAsync(new(new(request.ContractVersion, resolved.Payload!.Document, resolved.Payload), request.Source), context.RequestAborted), context.RequestAborted);
    }

    private static async ValueTask<ExpressionToolingOutcome<T>> InvokeSupportedProviderAsync<T>(IExpressionToolingProvider provider, ExpressionAuthoringContext context, Func<ExpressionToolingCapabilities, bool> supported, string unsupportedCode, Func<ValueTask<ExpressionToolingOutcome<T>>> invoke, CancellationToken cancellationToken)
    {
        if (!supported(context.Capabilities))
            return Unavailable<T>(context, unsupportedCode);
        var capabilities = await InvokeProviderAsync(() => provider.GetCapabilitiesAsync(new(ExpressionToolingContractVersion.V1, context.Document, context), cancellationToken), context, cancellationToken);
        if (!capabilities.IsSuccess)
            return ExpressionToolingOutcome<T>.Failure(capabilities.State, capabilities.ContractVersion, capabilities.Code, capabilities.Message, capabilities.DocumentRevision, capabilities.ContextRevision);
        if (!supported(capabilities.Payload!))
            return Unavailable<T>(context, unsupportedCode);
        return await InvokeProviderAsync(invoke, context, cancellationToken);
    }

    private static async ValueTask<ExpressionToolingOutcome<T>> InvokeProviderAsync<T>(Func<ValueTask<ExpressionToolingOutcome<T>>> invoke, ExpressionAuthoringContext context, CancellationToken cancellationToken)
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
            return ExpressionToolingOutcome<T>.Failure(ExpressionToolingOutcomeState.Canceled, ExpressionToolingContractVersion.V1, "provider-canceled", documentRevision: context.Document.DocumentRevision, contextRevision: context.ContextRevision);
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            return ExpressionToolingOutcome<T>.Failure(ExpressionToolingOutcomeState.Unavailable, ExpressionToolingContractVersion.V1, "provider-failed", documentRevision: context.Document.DocumentRevision, contextRevision: context.ContextRevision);
        }
    }

    private static ExpressionToolingOutcome<T> Unavailable<T>(ExpressionAuthoringContext context, string code = "provider-unavailable") =>
        ExpressionToolingOutcome<T>.Failure(ExpressionToolingOutcomeState.Unavailable, ExpressionToolingContractVersion.V1, code, documentRevision: context.Document.DocumentRevision, contextRevision: context.ContextRevision);

    private static string Revision(IEnumerable<string> values) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001e', values))).AsSpan(0, 16)).ToLowerInvariant();
}
