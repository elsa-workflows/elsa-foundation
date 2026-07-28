# Core Contract: Expression Tooling

## Package ownership

`Elsa.Expressions.Core` exposes the reusable contracts. It must not reference `Elsa.Workflows.Design.*`, Studio, a language engine, an evaluator implementation, or host APIs. `Elsa.Workflows.Design.Core` adapts workflow-specific facts into `ExpressionAuthoringContext`.

## Provider contract

```csharp
public interface IExpressionToolingProvider
{
    string ExpressionType { get; }
    ExpressionToolingContractVersion SupportedVersion { get; }
    ExpressionToolingCapabilities DeclaredCapabilities { get; }

    ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>>
        GetCapabilitiesAsync(ExpressionToolingRequestScope scope, CancellationToken cancellationToken);

    ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>>
        GetCompletionsAsync(ExpressionCompletionRequest request, CancellationToken cancellationToken);

    ValueTask<ExpressionToolingOutcome<ExpressionHover>>
        GetHoverAsync(ExpressionHoverRequest request, CancellationToken cancellationToken);

    ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>>
        ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken);
}
```

### Invariants

- `ExpressionType` matches the registered expression descriptor type exactly and is unique after host composition.
- Every request carries the current contract version, document identity/revision, and a Design-built, policy-filtered context snapshot.
- A provider receives no runtime evaluator, runtime execution context, service provider, live values, mutation callback, or raw host policy.
- `OperationCanceledException` for the caller token propagates; callers map it to `canceled` and do not cache partial output.
- Provider result ranges are relative to submitted source and all diagnostics identify document revision.
- The provider may return `supported-empty`; it must never turn unavailable/incompatible input into a synthetic generic result.

## Resolver contract

```csharp
public interface IExpressionToolingProviderResolver
{
    IExpressionToolingProvider? Find(string expressionType);
}
```

Resolver construction fails deterministically on duplicate expression types. Provider registration remains independent of evaluation-handler registration.

## Design context builder contract

```csharp
public interface IExpressionAuthoringContextService
{
    ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveAsync(
        ResolveExpressionAuthoringContextRequest request,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken);

    ValueTask<ExpressionToolingOutcome<ExpressionAuthoringContext>> ResolveForProviderAsync(
        ResolveExpressionAuthoringContextRequest request,
        ExpressionAuthoringAuthorization authorization,
        CancellationToken cancellationToken);
}
```

The Design service resolves the draft/activity/property location, expected type, visible workflow inputs, lexical variables, definitely available activity results, and authored metadata. It applies caller authorization and host policy before paging client context responses. Providers receive the same bounded, post-policy catalog before client paging, so completion, hover, and validation can resolve a visible symbol beyond the current response page; providers independently bound their returned candidates. It produces bounded symbols and inline member shapes to depth four; v1 providers declare lazy members unsupported. It does not accept client-supplied symbol lists, permission claims, expected types, or workflow graph state as authority.

## Full-draft validation contract

```csharp
public interface IExpressionDraftSemanticValidator
{
    ValueTask<ExpressionDraftValidationResult> ValidateAsync(
        WorkflowDefinitionState state,
        string documentScope,
        CancellationToken cancellationToken);
}
```

The result preserves the aggregate state and authored-path diagnostics. A Design validator maps every non-valid state to the existing `ValidationError` shape. The same strict validation lifecycle is consumed at Test Run, publication, and promotion boundaries; read paths retain existing shielded validation behavior.
