# Elsa.Activities.Design.Reconciliation

Reconciliation lifecycle for the activity catalog (Sipke item 6 — idempotent reconciliation). Replaces the older `Provisioning` framing: "provisioning" was a single trigger of a broader lifecycle; "reconciliation" is the lifecycle.

## What this feature provides

- **`IActivityVersionReconciler`** — the public contract (`Reconcile(CancellationToken)`). Implementation in this feature dispatches the contribution event, processes contributed versions, and updates the reconciliation-state sibling.
- **`DefaultActivityDefinitionHasher`** — default `IActivityDefinitionHasher` (SHA-256 over canonical JSON of definition + version). Replaceable per §2.6.2.
- **`ActivityVersionReconcilerStartupTask`** — registered as an `IStartupTask`, runs `IActivityVersionReconciler.Reconcile()` on every node, without a lock. `[Order(1)]`.

## Cross-domain contributions

- **`IStartupTask`** *(Core — `Elsa.Tasks.Core`)* — `ActivityVersionReconcilerStartupTask` runs the reconciliation pass at startup on every node, without a lock: its inputs are node-local, and concurrent passes converge (#2189, #2192). Catalog: [`Elsa.Tasks/EXTENSION_POINTS.md`](../Elsa.Tasks/EXTENSION_POINTS.md)

## Events published

- **`ActivityVersionsReconciling`** (declared in `Elsa.Activities.Design.Core`, namespace `Elsa.Activities.Design.Core.Reconciliation`). Carries a mutable `ICollection<IActivityDefinitionVersion>`. Source modules handle the event and contribute the activities they observe. The reconciler then upserts the catalog and the reconciliation-state sibling.

## Startup tasks

- `ActivityVersionReconcilerStartupTask` — order 1, on every node. Runs `Reconcile` without a lock (#2192).

## Options

- `ActivityVersionReconcilerOptions.DuplicateHandling` — `Skip` (default) or `Throw` when a contributed version already exists in the catalog.

## Replaceable services (per §2.6.2)

- `IActivityDefinitionHasher` — scoped (§2.5.1: it executes a hash, not application-wide static state); provider modules may override to swap the canonicalisation / hash algorithm.

## Naming history

`Elsa.Activities.Design.Provisioning.*` → `Elsa.Activities.Design.Reconciliation.*` on 2026-05-28 (Unit B). The rename is a NuGet identity change (§G10 violation, justified at clarify session 2 — pre-ratification reshape; the new name names the lifecycle accurately).
