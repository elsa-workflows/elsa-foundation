# Decisions Design extension points

## Overridable contracts

### IDecisionAccessPolicy

- Kind: single scoped replacement; Layer 3 policy boundary consumed by Design orchestration.
- Signature: `IsAllowedAsync(DecisionCaller, permission, resource, cancellationToken)` returns a current authorization decision.
- Default: internal `UnsupportedDecisionAccessPolicy` in `Elsa.Decisions.Design`, returning explicit unavailability. HTTP provider: internal `HttpDecisionAccessPolicy` in `Elsa.Decisions.Api`.
- Registration: `AddDecisionsDesign`; provider installation uses `UseDecisionsAccessPolicy<TPolicy>`, preserving explicit replacements and rejecting competing policies.
- Override for a trusted non-HTTP host authority. Bind tenant and persisted subject from trusted host context, check current grants, propagate cancellation and never treat unavailable authority as permission.
- Consumers: every protected `DecisionDesignService` read, mutation, publication, invocation, test and disclosure.

## Implementable contributor interfaces

None. There is no handler fan-in or custom dispatch loop.

## Events

None. No background task, scheduler or execution authority is introduced.
