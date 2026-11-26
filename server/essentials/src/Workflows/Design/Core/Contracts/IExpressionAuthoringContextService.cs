using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>Authoritative Design location request. Clients may name a location but cannot supply graph facts or symbols.</summary>
public sealed record ResolveExpressionAuthoringContextRequest(
    ExpressionToolingContractVersion ContractVersion,
    string WorkflowDraftId,
    string NodeId,
    string PropertyKey,
    string ExpressionType,
    string DocumentRevision,
    string? ContextRevision = null,
    string? Search = null,
    int Skip = 0,
    int Take = 100);

/// <summary>Authorization result supplied by the host after it authenticates the caller.</summary>
public sealed record ExpressionAuthoringAuthorization(
    bool IsAuthorized,
    string? SubjectId = null,
    string? PermissionRevision = null,
    string? PolicyFingerprint = null);

/// <summary>
/// Host policy boundary for expression authoring. Hosts can replace the default policy to deny a
/// caller or project an external policy revision that is independent of the caller's claims.
/// </summary>
public interface IExpressionAuthoringAuthorizationPolicy
{
    ValueTask<ExpressionAuthoringAuthorization> AuthorizeAsync(
        ExpressionAuthoringAuthorization caller,
        CancellationToken cancellationToken);
}

public interface IExpressionAuthoringContextSource
{
    /// <summary>Returns null when the source does not own the requested location. Implementations validate location and policy.</summary>
    ValueTask<ExpressionAuthoringContext?> TryResolveAsync(
        ResolveExpressionAuthoringContextRequest request,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken);
}

/// <summary>
/// Host extension point for permission- or policy-specific metadata filtering. Returning <see langword="null"/>
/// denies the location without disclosing whether it exists.
/// </summary>
public interface IExpressionAuthoringContextFilter
{
    ValueTask<ExpressionAuthoringContext?> FilterAsync(
        ExpressionAuthoringContext context,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken);
}

/// <summary>
/// Host extension point for permission- or policy-specific symbol filtering. Returning
/// <see langword="null"/> omits the candidate before it enters the bounded context catalog.
/// </summary>
public interface IExpressionAuthoringSymbolFilter
{
    ValueTask<ExpressionSymbol?> FilterAsync(
        ExpressionSymbol symbol,
        ExpressionAuthoringContext context,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken);
}

public interface IExpressionAuthoringContextService
{
    ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveAsync(
        ResolveExpressionAuthoringContextRequest request,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the bounded, post-policy catalog required by a language provider. The provider still
    /// owns its response limit; this prevents paging the context before it can resolve a referenced symbol.
    /// </summary>
    ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveForProviderAsync(
        ResolveExpressionAuthoringContextRequest request,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken) => ResolveAsync(request, authorization, cancellationToken);
}
