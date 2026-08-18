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

## Portable artifact export targets

### `IWorkflowArtifactExportTarget` *(Core contract — `Elsa.Workflows.Publishing.Core`)*

- **Kind:** Contribution (fan-in; enumerable) — the export-side mirror of the import side's
  `IWorkflowArtifactReconciliationSource`. **Targets contribute, they never replace.** A folder writer or blob
  push arriving later must stand beside the built-in download rather than displace it, so registration is
  `services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowArtifactExportTarget, MyTarget>())` and consumers
  resolve `IEnumerable<IWorkflowArtifactExportTarget>` and select by `TargetId`. Registering one with
  `Replace(...)`, or expecting `TryAdd` first-wins semantics, silently removes every other destination the host
  composed.
- **Signature:** `string TargetId { get; }` and
  `Task<WorkflowArtifactExportDelivery> DeliverAsync(WorkflowArtifactClosure closure, CancellationToken)`.
- **`TargetId` is self-identifying**, a property of the target rather than a key it is registered under: a
  caller selecting a destination is naming a behaviour, not a DI slot, and a registration-supplied key would let
  one target answer to two names. It must not change across restarts.
- **Delivery kind decides what transport may bind it.** `WorkflowArtifactExportDeliveryKind.InlinePayload`
  returns the encoded closure and writes nothing anywhere — safe, repeatable, and the only shape a GET may bind
  to. `Receipt` means the closure went somewhere external and only a locator came back; that is a side effect a
  crawler, retry or cache could repeat, so a receipt-producing target arrives with its own POST command surface
  carrying an explicit idempotency contract. Construct deliveries through `WorkflowArtifactExportDelivery.Inline`
  / `.Receipt` rather than the positional constructor, so the payload/location pairing is checked where it is
  made.
- **Encoding belongs to the target, not to the contract.** Targets take the closure model and encode it through
  Runtime's `IWorkflowArtifactClosureSerializer` — the same codec the JSON import reader decodes with — so an
  export/import round trip cannot drift. A target that reached for `JsonSerializer` directly would be a second
  wire format nobody declared. This is also what keeps the engine's closure factory destination-agnostic; see
  the [engine catalog](../EXTENSION_POINTS.md#portable-artifact-export).

**Known implementation (shipped):** `DownloadWorkflowArtifactExportTarget` *(intra-domain — the v1 built-in;
`TargetId` = `DownloadWorkflowArtifactExportTarget.Id` = `"download"`)*. Encodes as UTF-8 **without** a BOM: the
envelope is JSON, RFC 8259 requires UTF-8 on the wire, and a BOM would make the exported bytes differ from the
store-round-tripped ones for no semantic reason. It is contributed by this feature and not by the engine,
because a destination is a transport concern; an engine composed without a transport resolves an empty
enumerable rather than failing.

### The export endpoint that consumes it

`GET publishing/workflows/{versionId}/executable-export` binds to the `download` target **only** — there is no
target selector in v1, by design (see the delivery-kind rule above). Its pins for
[elsa-foundation-studio#493](https://github.com/elsa-workflows/elsa-foundation-studio/issues/493) are stable
contract: route as above, capability `elsa.api.publishing`, link relation `workflow-executable-export`
(declared in `PublishingApiCapabilities` as a templated link and discovered through `GET /capabilities`).

Authorization is `PermissionNames.WorkflowPublishingRead` — deliberately **not** a new `.export` action. Executable
content is already readable under that family, so export differs only by bundling the transitive closure into one
response. If the executable-inspection endpoints are ever tightened, this must be tightened with them.

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
- Engine seams (compiler, authority stores, policy/preflight/activation, closure factory, compilation fan-in): [engine catalog](../EXTENSION_POINTS.md).
- The import counterpart of the export-target seam: [Workflows Runtime Reconciliation catalog](../../Runtime/Reconciliation/EXTENSION_POINTS.md).
- Publication authority decision: [ADR 0043](../../../../../docs/adr/0043-publication-slots-define-start-authority.md).
- Repo-wide index: [root extension-point index](../../../../../EXTENSION_POINTS.md).
