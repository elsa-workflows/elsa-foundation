# Extension points — Workflows Publishing API (transport + activity-draft)

This catalog covers the extension points owned by the **transport** feature `Elsa.Workflows.Publishing.Api`:
the HTTP endpoint surface, transport authorization, and the activity-draft publish/test-run seams. The
auth-free **publish + compile engine** — the executable compiler, the publication authority stores, policy /
preflight / activation / projection reconciliation, the compilation fan-in, and the activity-template provider
registries — moved to `Elsa.Workflows.Publishing`, which this feature obtains by `DependsOn` composition. For
those seams see [the engine extension-point catalog](../EXTENSION_POINTS.md).

Contracts live in `Elsa.Workflows.Publishing.Core`. The authority and failure invariants are owned by
[ADR 0043](../../../../../docs/adr/0043-publication-slots-define-start-authority.md); shared terms remain in the
[Elsa glossary](../../../../../docs/glossary/elsa.md) and [root glossary](../../../../../docs/glossary/root.md).

## Incident strategy discovery

`GET publishing/incident-strategies` is the permission-protected authoring discovery surface. It returns the
descriptor-only `IIncidentStrategyCatalog` in deterministic order together with the exact effective publishing
default. It never constructs scoped strategy implementations and never exposes CLR type names. Contribute a
strategy through Runtime's atomic `AddIncidentStrategy<TStrategy>(descriptor)` or attributed overload; the
compiler validates and pins the selected alias/version into each executable.

## Overridable contracts (transport + activity-draft)

| Contract | Built-in default | Replace when |
|---|---|---|
| `IActivityPublishingAuthorizationContext` | `HttpContextActivityPublishingAuthorizationContext` (scoped) | A host authenticates activity publish/test-run requests through another transport. Authorization is a transport concern; the engine neither registers nor depends on this contract. |
| `IWorkflowTestRunStore` | `InMemoryWorkflowTestRunStore` (singleton) | Test-run projections need shared/durable retention. Runtime owns the matching scope lifecycle; expiry opens an operation scope and closes Runtime before removing the projection. |
| `IActivityDraftTestRunStore` | `InMemoryActivityDraftTestRunStore` (singleton) | Activity draft Test Run receipts, idempotency, and status lookup must survive restart. The Groundwork Publishing package replaces this default. |
| `IActivityDraftTestRunCancellationPolicy` | `DefaultActivityDraftTestRunCancellationPolicy` (singleton) | A host needs to suppress or further constrain cancellation while advertising the effective capability truthfully. |

Register replacements before the feature's `TryAdd` defaults, or use `services.Replace(...)`.

## Contract obligations

### Transport authorization

`IActivityPublishingAuthorizationContext` resolves the caller's tenant/authorization scope from the active
transport (the default reads `HttpContext`). The engine is authorization-free and introduces no neutral
default, so this contract and the activity-draft services that consume it are registered only by this API
feature. A replacement must supply the same tenant/scope facts the activity-draft publish/test-run flow expects.

### Activity draft Test Runs

- `IActivityDraftTestRunStore` owns one receipt per deterministic `(OperationScope, DraftId, IdempotencyKey)`
  identity. Each tenant has a distinct durable operation scope and the explicit tenantless operation scope is
  distinct from every tenant scope. Operation ownership is derived from the caller tenant, independently of the
  resource tenant, so global activity drafts do not share receipts across tenant callers. Implementations must
  preserve the original request fingerprint, compare-and-swap receipt revisions, retain only the key hash, and
  keep the receipt beyond source-reference expiry so terminal and retention facts remain discoverable.
- `IActivityDraftTestRunCancellationPolicy` evaluates the durable receipt together with current Runtime Evidence.
  Replacements must distinguish whether cancellation is advertised from whether it is currently allowed. A
  denied or already-terminal request must not enqueue a Runtime command.
- Dispatch, retry, and cancellation use stable Runtime idempotency keys. An ambiguous acknowledgement may be
  retried, but must resolve to the same Test Run and workflow execution identity.
- Status projection may expose safe codes and diagnostics, immutable artifact/source identities, and the eventual
  outer activity execution identity. It must not expose synthetic wrapper/provider payloads or raw Runtime
  exception messages.

## HTTP endpoint surface

The endpoint surface is documented in the [module README](README.md#http-endpoint-surface). Endpoint
authorization and configuration compose through [FastEndpoints extension points](../../../Api/FastEndpoints/EXTENSION_POINTS.md).

## Persistence-provider notes

`Elsa.Workflows.Publishing.Persistence.Groundwork`'s `AddGroundworkPublishingStores()` composes durable
implementations for **both** the engine's authority stores (documented in the
[engine persistence checklist](../EXTENSION_POINTS.md#persistence-provider-checklist)) and this feature's
`IActivityDraftTestRunStore`. A durable activity-draft receipt store must:

- own one receipt per opaque hashed `(OperationScope, DraftId, IdempotencyKey)` identity;
- preserve the original request fingerprint and compare-and-swap receipt revisions;
- retain the receipt beyond source-reference expiry; and
- prove same-request replay, different-request rejection, stale-review no-write, and receipt rollback with the
  other publication documents.

## References

- Module behavior and endpoint surface: [README](README.md).
- Engine seams (compiler, authority stores, policy/preflight/activation, compilation fan-in): [engine catalog](../EXTENSION_POINTS.md).
- Publication authority decision: [ADR 0043](../../../../../docs/adr/0043-publication-slots-define-start-authority.md).
- Repo-wide index: [root extension-point index](../../../../../EXTENSION_POINTS.md).
