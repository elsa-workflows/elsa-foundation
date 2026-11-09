using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>Shared operation implementation; HTTP owners supply only their own authorized locations.</summary>
internal sealed class ExpressionToolingOperations(
    IExpressionAuthoringContextService contextService,
    IExpressionToolingProviderResolver providerResolver) : IExpressionToolingOperations
{
    public async ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveContextAsync(
        ResolveExpressionAuthoringContextRequest request, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken)
    {
        var resolved = await contextService.ResolveAsync(
            request,
            authorization,
            cancellationToken);
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
            () => provider.GetCapabilitiesAsync(new(request.ContractVersion, resolved.Payload!.Document, resolved.Payload), cancellationToken),
            resolved.Payload!, cancellationToken);
        if (!capabilities.IsSuccess)
            return ExpressionToolingOutcome<ExpressionAuthoringContext>.Failure(
                capabilities.State, capabilities.ContractVersion, capabilities.Code, capabilities.Message,
                capabilities.DocumentRevision, capabilities.ContextRevision);

        var payload = resolved.Payload! with { Capabilities = capabilities.Payload! };
        return resolved.State == ExpressionToolingOutcomeState.SupportedEmpty
            ? ExpressionToolingOutcome<ExpressionAuthoringContext>.SupportedEmpty(payload, resolved.ContractVersion, payload.Document.DocumentRevision, payload.ContextRevision)
            : ExpressionToolingOutcome<ExpressionAuthoringContext>.Success(payload, resolved.ContractVersion, payload.Document.DocumentRevision, payload.ContextRevision);
    }

    public async ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> SearchSymbolsAsync(
        ResolveExpressionAuthoringContextRequest request, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken)
    {
        var resolved = await contextService.ResolveAsync(
            request,
            authorization, cancellationToken);
        if (!resolved.IsSuccess)
            return ExpressionToolingOutcome<ExpressionToolingItems>.Failure(resolved.State, resolved.ContractVersion, resolved.Code, resolved.Message, resolved.DocumentRevision, resolved.ContextRevision);
        if (providerResolver.Find(request.ExpressionType) is null)
            return ExpressionToolingOutcome<ExpressionToolingItems>.Failure(ExpressionToolingOutcomeState.Unavailable, ExpressionToolingContractVersion.V1, "provider-unavailable", documentRevision: resolved.Payload!.Document.DocumentRevision, contextRevision: resolved.Payload.ContextRevision);
        return resolved.Payload!.RootSymbols.Count == 0
            ? ExpressionToolingOutcome<ExpressionToolingItems>.SupportedEmpty(new([]), ExpressionToolingContractVersion.V1, resolved.Payload.Document.DocumentRevision, resolved.Payload.ContextRevision)
            : ExpressionToolingOutcome<ExpressionToolingItems>.Success(new(resolved.Payload.RootSymbols.Select(symbol => new ExpressionToolingItem(symbol.Name, symbol.ValueShape?.DisplayName, symbol.Documentation, symbol.Name, symbol.Kind)).ToArray()), ExpressionToolingContractVersion.V1, resolved.Payload.Document.DocumentRevision, resolved.Payload.ContextRevision);
    }

    public async ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> CompleteAsync(
        ResolveExpressionAuthoringContextRequest request, string source, ExpressionToolingPosition cursor, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken)
    {
        var resolved = await contextService.ResolveForProviderAsync(request, authorization, cancellationToken);
        if (!resolved.IsSuccess)
            return ExpressionToolingOutcome<ExpressionToolingItems>.Failure(resolved.State, resolved.ContractVersion, resolved.Code, resolved.Message, resolved.DocumentRevision, resolved.ContextRevision);
        if (providerResolver.Find(request.ExpressionType) is not { } provider)
            return Unavailable<ExpressionToolingItems>(resolved.Payload!);
        if (!cursor.IsValid)
            return ExpressionToolingOutcome<ExpressionToolingItems>.Failure(ExpressionToolingOutcomeState.Incompatible, ExpressionToolingContractVersion.V1, "invalid-cursor", documentRevision: resolved.Payload!.Document.DocumentRevision, contextRevision: resolved.Payload.ContextRevision);
        return await InvokeSupportedProviderAsync(provider, resolved.Payload!, capabilities => capabilities.SupportsCompletions, "completion-unsupported", () => provider.GetCompletionsAsync(new(new(request.ContractVersion, resolved.Payload!.Document, resolved.Payload), source, cursor), cancellationToken), cancellationToken);
    }

    public async ValueTask<ExpressionToolingOutcome<ExpressionHover>> HoverAsync(
        ResolveExpressionAuthoringContextRequest request, string source, ExpressionToolingPosition position, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken)
    {
        var resolved = await contextService.ResolveForProviderAsync(request, authorization, cancellationToken);
        if (!resolved.IsSuccess)
            return ExpressionToolingOutcome<ExpressionHover>.Failure(resolved.State, resolved.ContractVersion, resolved.Code, resolved.Message, resolved.DocumentRevision, resolved.ContextRevision);
        if (providerResolver.Find(request.ExpressionType) is not { } provider)
            return Unavailable<ExpressionHover>(resolved.Payload!);
        if (!position.IsValid)
            return ExpressionToolingOutcome<ExpressionHover>.Failure(ExpressionToolingOutcomeState.Incompatible, ExpressionToolingContractVersion.V1, "invalid-position", documentRevision: resolved.Payload!.Document.DocumentRevision, contextRevision: resolved.Payload.ContextRevision);
        return await InvokeSupportedProviderAsync(provider, resolved.Payload!, capabilities => capabilities.SupportsHover, "hover-unsupported", () => provider.GetHoverAsync(new(new(request.ContractVersion, resolved.Payload!.Document, resolved.Payload), source, position), cancellationToken), cancellationToken);
    }

    public async ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(
        ResolveExpressionAuthoringContextRequest request, string source, ExpressionAuthoringAuthorization authorization, CancellationToken cancellationToken)
    {
        var resolved = await contextService.ResolveForProviderAsync(request, authorization, cancellationToken);
        if (!resolved.IsSuccess)
            return ExpressionToolingOutcome<ExpressionDiagnosticSet>.Failure(resolved.State, resolved.ContractVersion, resolved.Code, resolved.Message, resolved.DocumentRevision, resolved.ContextRevision);
        if (providerResolver.Find(request.ExpressionType) is not { } provider)
            return Unavailable<ExpressionDiagnosticSet>(resolved.Payload!);
        return await InvokeSupportedProviderAsync(provider, resolved.Payload!, capabilities => capabilities.SupportsValidation, "validation-unsupported", () => provider.ValidateAsync(new(new(request.ContractVersion, resolved.Payload!.Document, resolved.Payload), source), cancellationToken), cancellationToken);
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

}
