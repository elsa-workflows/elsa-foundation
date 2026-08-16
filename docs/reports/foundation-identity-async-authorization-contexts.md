# Foundation Identity async authorization contexts

Status: implementation evidence for issue #1356

## Scope

This work unit migrates the first-party Activity Design and Workflow Runtime API callers from
synchronous request authorization contexts to asynchronous sibling seams. It also exposes the
canonical Foundation Identity decision as `IPermissionAuthorizationService`, so endpoint handlers,
Minimal API callers, FastEndpoints callers, and feature services share the same normalized-principal,
catalog/evaluator, tenant, resource-handler, and cancellation semantics.

The work unit does not migrate every Elsa endpoint or remove FastEndpoints. The existing synchronous
interfaces remain source-compatible for an advisory replacement window; built-in HTTP adapters mark
permission members obsolete and fail closed rather than performing sync-over-async. Removal is a
next-major-release candidate.

## Evidence matrix

| Concern | Evidence |
|---|---|
| Exact, implied, wildcard, normalized claims | Existing Foundation Identity evaluator and policy tests, plus the shared service path used by both contexts. |
| Replacement evaluator and catalog | `IPermissionEvaluator` remains a replaceable DI contract; request contexts resolve only `IPermissionAuthorizationService`. |
| Resource grant/veto | `ActivityProviderAuthorizationResource` carries provider and tenant into resource handlers; design-context tests cover grant, payload-only separation, and hard veto. Runtime passes `WorkflowExecutionState` as the protected resource. |
| Trusted identity and tenant isolation | Both HTTP contexts first select exactly one Foundation-normalized identity, then derive tenant, actor/audit subject, and permission evaluation from that same immutable principal. Mixed trusted/untrusted identity tests cover no claim union, same-tenant, cross-tenant, and audit-subject behavior. |
| Stable request profile | Profiles are derived from effective async decisions and cached through a thread-safe lazy request snapshot. Concurrent callers do not duplicate evaluator operations; a canceled waiter cannot poison the shared computation, and faulted/canceled snapshots are evicted for retry. |
| Cancellation and operational failures | The shared service links explicit, context, and HTTP request-abort cancellation tokens; resource/evaluator exceptions propagate. Delayed resource cancellation is covered. |
| Direct claim bypass | Architecture scanner now treats both HTTP authorization contexts as guarded boundary paths; mutation fixtures fail on direct permission-claim readers. |
| First-party caller migration | Activity Design management, authoring, dependency, fork, upgrade, diff, and recommendation callers, plus Workflow Runtime inspection, incident, instance, hierarchy, layout, and value-payload callers use `IActivityAuthoringContextAsync`, `IActivityDependencyContextAsync`, `IActivityInspectionContextAsync`, or the canonical Foundation service. Legacy synchronous interfaces are obsolete for next-major removal; public sealed adapters preserve one host replacement window with safe `ValueTask` bridges. |

## Follow-up

1. Migrate remaining first-party endpoint/service authorization callers to
   `IPermissionAuthorizationService` or an async feature seam.
2. Publish an API compatibility/deprecation notice for external context implementations and set the
   removal target in the next major-version plan.
3. Add feature-owned `IPermissionResourceHandler` implementations where provider or domain state
   requires more than normalized permission claims.
4. Keep production changes that alter public route behavior, collision policy, or endpoint framework
   selection in separately reviewed issues.
