# Contract: `IActivityFactory`

**Location.** `Elsa.Activities.Runtime.Core.Contracts.IActivityFactory`

**Kind.** Replacement contract. One implementation per host (`ActivityFactory` in the activities runtime feature). Conflicts at registration MUST be detected at startup per framework §2.6.2.

**Constitutional citation.** Framework §2.6.2 (replacement contracts); Elsa §E2.6 (runtime contract — executable-always-runs); framework §2.7 (adapter pattern — factory wraps DI activation behind a domain contract).

## Surface

```csharp
namespace Elsa.Activities.Runtime.Core.Contracts;

public interface IActivityFactory
{
    ValueTask<IActivity> Create(
        IImplementationDescriptor descriptor,
        IEnumerable<InputState> inputs,
        IEnumerable<OutputState> outputs,
        CancellationToken cancellationToken);
}
```

## Behaviour

1. Look up the resolver for `descriptor`'s kind via `IActivityImplementationResolverRegistry`.
2. Call `resolver.Resolve(descriptor)` to obtain the CLR `Type`.
3. Instantiate the type via `ActivatorUtilities.CreateInstance` (so DI-resolvable constructor dependencies flow through).
4. Map each `InputState` / `OutputState` onto the activity's corresponding `Input<T>` / `Output<T>` property by `ReferenceKey` ↔ `ArgumentDefinition.ReferenceKey`. Transform `ArgumentValue` into a runtime `IExpression` based on `ArgumentValue.ExpressionType`.
5. Return the constructed and configured `IActivity`.

## Failure modes

| Cause | Path |
|---|---|
| Unknown `ImplementationKind` (no resolver registered) | Throw `ActivityResolutionException` — runtime / domain path per Elsa §E2.6.1. Not a system failure. |
| Descriptor payload structurally invalid for its declared kind | Throw — data integrity violation surfaced at construction time. |
| Resolver returns null `Type` | Throw — resolver contract violation. |
| Activation fails (missing DI dependency) | Propagate the activation exception. |
| `InputState` / `OutputState` `ReferenceKey` doesn't match any `Input<T>` / `Output<T>` on the activity | Throw — design-time / runtime contract mismatch. |

## Dependencies

- `IActivityImplementationResolverRegistry` (kind-typed dispatch).
- `IServiceProvider` (DI activation).
- Expression-domain services (`IExpressionFactory` or equivalent, currently in `Elsa.Expressions.Core`) for `ArgumentValue` → `IExpression` transformation.

## Test surface

- Construction with a known CLR descriptor → returns an `IActivity` of the expected concrete type.
- Construction with all `InputState` values → activity's `Input<T>` properties carry the expected `IExpression`.
- Construction with unknown `ImplementationKind` → throws `ActivityResolutionException`.
- Construction with mismatched `ReferenceKey` → throws (specific exception type per plan).
