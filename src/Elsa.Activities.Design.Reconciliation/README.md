# Elsa.Activities.Design.Reconciliation

Reconciliation lifecycle for the activity catalog (Sipke item 6 — idempotent reconciliation). Replaces the older `Provisioning` framing: "provisioning" was a single trigger of a broader lifecycle; "reconciliation" is the lifecycle.

## What this feature provides

- **`IActivityVersionReconciler`** — the public contract (`Reconcile(CancellationToken)`). Implementation in this feature dispatches the contribution event, processes contributed versions, and updates the reconciliation-state sibling.
- **`DefaultActivityDefinitionHasher`** — default `IActivityDefinitionHasher` (SHA-256 over canonical JSON of definition + version). Replaceable per §2.6.2.
- **`ActivityVersionReconcilerStartupTask`** — registered as an `IStartupTask`, acquires a distributed lock, runs `IActivityVersionReconciler.Reconcile()`. `[SingleNodeTask] [Order(1)]`.

## Cross-domain contributions

- **`IStartupTask`** *(Core — `Elsa.Tasks.Core`)* — `ActivityVersionReconcilerStartupTask` runs the reconciliation pass at startup under a distributed lock. Catalog: [`Elsa.Tasks/EXTENSION_POINTS.md`](../Elsa.Tasks/EXTENSION_POINTS.md)

## Events published

- **`OnActivityVersionsReconciling`** (declared in `Elsa.Activities.Design.Reconciliation.Core`). Carries a mutable `ICollection<IActivityDefinitionVersion>`. Source modules handle the event and contribute the activities they observe. The reconciler then upserts the catalog and the reconciliation-state sibling.

## Startup tasks

- `ActivityVersionReconcilerStartupTask` — order 1, single-node. Runs `Reconcile` under a distributed lock.

## Options

- `ActivityVersionReconcilerOptions.DuplicateHandling` — `Skip` (default) or `Throw` when a contributed version already exists in the catalog.
- `ActivityVersionReconcilerStartupTaskOptions.LockTimeoutMs` — distributed-lock timeout (default 10s).

## Replaceable services (per §2.6.2)

- `IActivityDefinitionHasher` — singleton; provider modules may override to swap the canonicalisation / hash algorithm.

## Naming history

`Elsa.Activities.Design.Provisioning.*` → `Elsa.Activities.Design.Reconciliation.*` on 2026-05-28 (Unit B). The rename is a NuGet identity change (§G10 violation, justified at clarify session 2 — pre-ratification reshape; the new name names the lifecycle accurately).
