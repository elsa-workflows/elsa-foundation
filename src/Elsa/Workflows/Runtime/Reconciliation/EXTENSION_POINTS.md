# Extension points — Workflows.Runtime.Reconciliation domain

The per-domain catalog (framework §2.22.1), anchored at `Elsa.Workflows.Runtime.Reconciliation` — the
composition root where the reconciler, its startup pass and the JSON source are registered. The source contract
lives in `Elsa.Workflows.Runtime.Reconciliation.Core` so a future source package can be written without
referencing this feature; the reconciler and reader contracts live in this feature project because their only
callers are inside it.

This domain imports **portable workflow-executable closures** (spec 151, FR-B-002/007) into a runtime that may
carry no design, publishing or compiler surface. It owns no activation machinery: making an imported artifact
live is a single call to Runtime's `IWorkflowActivationCoordinator`, and a second copy of that sequence here
would be the duplicated authority FR-B-006 exists to remove. What the feature is for and how to compose it is
the [README](README.md); this file is only about how to extend it.

---

## Overridable contracts

Both contracts below are single-implementation: the reconciler *is* the import pipeline, and a second decoder
would mean a second wire format. Neither is registered with `TryAdd` — the base feature registers them with a
plain `AddScoped`, so the sanctioned replacement route is **feature inheritance**: derive, call
`base.ConfigureServices(services)`, then re-register the one contract afterwards (last registration wins in the
container). Registering *before* the base call does nothing here, which is the opposite of the `TryAdd`
first-wins convention used by `AddWorkflowRuntime()`.

| Contract | Built-in default | Replace when |
|---|---|---|
| `IWorkflowArtifactReconciler` *(feature — `Elsa.Workflows.Runtime.Reconciliation`)* | `WorkflowArtifactReconciler` (scoped, registered by `WorkflowsArtifactReconciliationFeature`) | The gate order, the isolation unit, or the supersession rule must differ. |
| `IWorkflowArtifactClosureReader` *(feature — `Elsa.Workflows.Runtime.Reconciliation`)* | `JsonWorkflowArtifactClosureReader` (scoped, registered by `JsonWorkflowArtifactReconciliationFeature`) | Envelopes arrive as something other than a readable file path — an embedded resource, a stream, a remote fetch. |

### `IWorkflowArtifactReconciler` — what a replacement inherits

The default runs a fixed gate order per closure unit — format gate → envelope-only closure validation →
content-hash recompute → requirements gate → idempotency/supersession → one activation request — and **every
gate completes for the whole unit before the first write**. A replacement that interleaves validation with
persistence breaks the guarantee the rest of the feature is built on: a failed unit leaves the engine as it
found it, and one broken export never takes the rest of the mounted set down.

It also has **no journal, deliberately**. Recovery is the next pass: every step is idempotent by
content-addressed identity and the coordinator compensates its own partial activations. A replacement that adds
importer-side bookkeeping creates a second record of what is live.

Rejections are values on `WorkflowArtifactReconciliationResult`, never throws. Only a pass-aborting condition
(a configured mount that does not exist) propagates, as `WorkflowArtifactReconciliationException`.

**`WorkflowArtifactClosurePlanner` is not a seam.** Envelope-only validation and the dependencies-first
activation order live in that `public static class` — deliberately static and contract-free, because envelope
validity must be environment-independent: the same file has to fail identically on every runtime, and a
replaceable validator is a place for an engine to become permissive. A replacement reconciler is free to call
it; nothing lets a host swap it out from underneath one that does.

### `IWorkflowArtifactClosureReader` — what a replacement inherits

The format gate **rejects rather than adapts**: an unknown or newer `FormatVersion` is refused outright
(`WorkflowArtifactClosureFormat.CurrentVersion` is `1`; `IsSupported` is the gate). There is no upcast and no
partial import in v1 — because the store is create-only and content-addressed, a wrong guess at an unseen
format would become that artifact id's content permanently.

Decoding itself is **not** this contract's to reinvent: the default delegates to Runtime's
`IWorkflowArtifactClosureSerializer`, the one codec the export side also encodes with. A reader that called
`JsonSerializer` directly would be a second wire format nobody declared.

§2.23.5 is enforced at this boundary, not below it: every `IOException` and `JsonException` is wrapped in
`InvalidWorkflowArtifactClosureException` carrying the offending origin, with the original as `InnerException`.
The codec itself lets `JsonException` through precisely because it does not own a file path to name.

---

## Implementable contributor interfaces

### `IWorkflowArtifactReconciliationSource` *(Core contract — `Elsa.Workflows.Runtime.Reconciliation.Core`)*

- **Kind:** Source (pull — returns closures). **Fan-in: implementations contribute, they never replace one
  another.** Getting this wrong is the expensive mistake here: registering a source with `Replace(...)` or
  expecting first-wins semantics silently disables every other mounted set on the engine. The reconciler runs
  one pass per registered source and reports them all on one result.
- **Signature:**
  ```
  string SourceId { get; }
  string SourceKind { get; }
  IAsyncEnumerable<WorkflowArtifactClosureFile> ReadAsync(CancellationToken cancellationToken);
  ```
- **`SourceId`:** required and self-identifying — and it is the **activation ownership descriptor**, not just a
  label. Two sources pointed at different mounts are different owners, and the activation authority will refuse
  a transition from a source that does not own the definition. It must therefore be chosen deliberately and stay
  stable across restarts; a path-derived or generated value would make a source's own definitions look foreign
  to it the moment the mount moved.
- **`SourceKind`:** the category (`"Json"` for the shipped source), stamped on every minted source reference as
  provenance.
- **`ReadAsync`:** called once per pass and enumerated lazily, so a re-run picks up whatever the mount holds at
  that moment. Each yielded `WorkflowArtifactClosureFile` carries `(Origin, Closure, TenantId)`. `Origin` is
  operator-facing diagnostics only and is **never parsed**. `TenantId` rides on the file rather than the source
  contract so the interface keeps exactly three members; it is the tenant of the *minted source reference*, not
  an activation-slot key (the slot is deliberately untenanted) and not the execution tenant.
- **Failure discipline:** a fault that makes the whole pass meaningless throws
  `WorkflowArtifactReconciliationException`; a fault scoped to one input throws
  `InvalidWorkflowArtifactClosureException` carrying that input's origin. Per-artifact *rejections* are never
  exceptions — they are diagnostics on the pass result.
- **Register:** `services.AddScoped<IWorkflowArtifactReconciliationSource, MySource>()` inside a feature
  deriving from `WorkflowsArtifactReconciliationFeature`, or hand instances to that feature's `Sources`
  property, which registers each as a singleton. Both accumulate.
- **Consumed by:** `WorkflowArtifactReconciler`, which injects `IEnumerable<IWorkflowArtifactReconciliationSource>`.

**Known implementations (shipped):**
- `JsonWorkflowArtifactReconciliationSource` *(intra-domain — default; `SourceKind = "Json"`)* — reads closure
  envelopes from a single `FilePath`, an ordered `Files` list, or a scanned `FolderPath`. Feature:
  `JsonWorkflowArtifactReconciliation` (opt-in; not enabled in any default shell).
- A blob-store or OCI-registry source is the deferred next one; it reuses `IWorkflowArtifactClosureReader`'s
  format gate rather than re-implementing it.

**Mirror on the export side.** The outbound counterpart is Publishing's `IWorkflowArtifactExportTarget`, the
same fan-in shape with the same self-identification rule — see the
[Publishing API catalog](../../Publishing/Api/EXTENSION_POINTS.md).

---

## Feature inheritance point

### `WorkflowsArtifactReconciliationFeature` *(abstract — `Elsa.Workflows.Runtime.Reconciliation`)*

- **Kind:** Inherit (§2.5 structural cross-feature coupling). `public abstract`, **not sealed** (§2.23.3) and
  carrying **no** `[ShellFeature]` attribute, so it is not composable on its own — arming the import lifecycle
  with no source would run a pass over nothing on every boot.
- **What the base arms:** `AddWorkflowRuntime()` (idempotent, ADR 0029), the host-supplied `Sources`, the
  startup-task options, `IWorkflowArtifactReconciler`, and the `IStartupTask`. Registering the startup task in
  the base rather than per variant means enabling *any* concrete reconciliation feature activates the lifecycle
  exactly once however many sources are composed — the reconciler already loops over all of them.
- **What a derived feature supplies:** the source (and, if its envelopes are not files, a reader). Override
  `ConfigureServices`, **call `base.ConfigureServices(services)` first**, then add. `JsonWorkflowArtifactReconciliationFeature`
  is the worked example: it validates its options, calls the base, then contributes the reader and the source.
- **What a derived feature must not skip:** the concrete feature owns the `[ShellFeature]` attribute and its
  `DependsOn` set. `WorkflowsRuntimeTriggers` belongs there — the base's `AddWorkflowRuntime()` does not supply
  the trigger binding/schedule/indexer spine, and without it the activation coordinator refuses to activate.
  A locking feature is required too and is deliberately *not* a `DependsOn`; see the README.
- **Validation belongs at registration.** `JsonWorkflowArtifactReconciliationFeature.ConfigureServices` throws
  `InvalidOperationException` for a blank `SourceId` or for anything other than exactly one of
  `FilePath` / `Files` / `FolderPath`. A source-variant feature should fail the same way rather than let an
  ambiguous mount configuration reach a pass.

---

## Options

- **`JsonWorkflowArtifactReconciliationOptions`** *(Core — `…Reconciliation.Core.Options`)*, bound to the
  `JsonWorkflowArtifactReconciliation` shell feature:
  - `FilePath` — single-file shorthand.
  - `Files` — ordered `JsonWorkflowArtifactReconciliationFileOption(Order, FilePath)` entries, read in ascending
    `Order` with ordinal file-name tie-breaking.
  - `FolderPath` — the mounted-volume/GitOps shape. **Non-recursive by design** (a Kubernetes ConfigMap's
    `..data` symlink tree would otherwise be read twice), ordinal file-name order, `*.json` only. A *missing*
    folder aborts the pass; an existing folder with no matches is a logged no-op — the distinction is what stops
    an unmounted volume from looking like a healthy empty one.
  - `SourceId` — **required**, and the activation ownership descriptor (see above).
  - `TenantId` — optional; `null` is the untenanted engine. One source serves one tenant; per-tenant fan-out is
    deferred, so a second tenant needs a second source.
  - Exactly one of `FilePath` / `Files` / `FolderPath` — enforced by the feature, not by the options class, so
    the source stays free of configuration policy.
- **`WorkflowArtifactReconcilerStartupTaskOptions`** *(feature — `…Reconciliation.Options`)*, settable through
  the base feature's `StartupTaskOptions` property:
  - `LockTimeoutMs` (default `5000`) — how long the startup pass waits for the distributed reconcile lock. A
    node that does not get it is not failing; another node is already reconciling the same mount.

---

## Registered tasks

### `WorkflowArtifactReconcilerStartupTask` *(`IStartupTask` — contract in `Elsa.Tasks.Core`)*

- **Cadence:** once per shell activation, **before readiness**, and again on shell reload — which is what makes
  re-reconciliation need no new trigger machinery (FR-B-008).
- **`[SingleNodeTask]` + `IDistributedLockProvider`.** Several nodes booting against one mounted set would
  otherwise contend for the same activation slots and every loser would take a CAS conflict for work already
  done. A node that cannot take the lock logs and returns — the expected outcome on all but one node.
  **No default `IDistributedLockProvider` is registered anywhere in the framework, deliberately**; compose a
  locking feature. See the [README](README.md#locking-is-required) for why absence is the safety property.
- **`[TaskDependency(typeof(RegisterActivityTypesStartupTask))]`.** The requirements gate's second axis asks
  whether each node's CLR activity type is present in this runtime; before the assembly scan has populated the
  well-known type registry the honest answer is "not yet" for every type, so running first would reject every
  artifact on a cold start.
- **Failure policy:** rejections are logged as warnings, not thrown. A broken closure unit must not stop the
  shell from starting with the units that did import.
- Catalog for the task substrate: [`Elsa.Tasks/EXTENSION_POINTS.md`](../../../Tasks/EXTENSION_POINTS.md).

---

## Events

This domain declares and publishes no `IEvent` types. Unlike the design-side reconciler — which fans sources in
through a `WorkflowVersionsReconciling` event and announces completion with `WorkflowVersionsReconciled` — the
artifact reconciler resolves `IEnumerable<IWorkflowArtifactReconciliationSource>` directly and returns its report
as a value. There is no completion event to subscribe to: the observable effect of a successful import is the
activation itself, so a consumer that needs to react registers an `IWorkflowTriggerIndexObserver` with Runtime,
which the activation coordinator notifies on every path including compensation.

---

## Cross-references

- Activation, the requirements checker, the hasher and the closure codec this feature consumes:
  [Workflows Runtime extension points](../EXTENSION_POINTS.md).
- The export side that produces the envelopes this feature imports:
  [Workflows Publishing engine](../../Publishing/EXTENSION_POINTS.md) (the closure factory) and
  [Workflows Publishing API](../../Publishing/Api/EXTENSION_POINTS.md) (the export-target seam).
- The design-side family this mirrors:
  [Workflows Design Reconciliation](../../Design/Reconciliation/EXTENSION_POINTS.md).
- Content-addressing invariant that makes the hash recompute meaningful:
  [ADR 0038](../../../../../docs/adr/0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md).
- Repo-wide index: [root extension-point index](../../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.5 (feature inheritance), §2.6.1 (source contribution), §2.6.2 (replacement contracts),
  §2.22.1, §2.23.5 (exception wrapping).
