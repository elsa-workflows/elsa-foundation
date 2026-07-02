# Elsa.Activities.Composition.Runtime

The **runtime half of the Workflow activity kind**: constructing a workflow-backed activity. A runtime
feature — references **no** `Elsa.*.Design.*` project (Elsa §E2.2) and **no** other feature project
(G4 / SC-006). The kind is split into two projects precisely so this runtime side stays Design-free; the
design/discovery half lives in [`../Design`](../Design/README.md).

## What this feature provides

`ActivitiesCompositionRuntimeFeature.ConfigureServices` registers:

- **`WorkflowActivityConstructor`** → contributed as `IActivityConstructor` — owns descriptor type
  `Elsa.Workflows.Primitives.Models.WorkflowIdentity`. From a `WorkflowIdentity(definitionId, versionId,
  version)` payload plus the author arguments, it produces a `WorkflowDefinitionActivity` with the identity
  applied (typed state) and the author arguments pre-set in the dynamic bag. It does its **own** bag-filling
  — no reference to `Primitives`' `ActivityArgumentBinder`. Two different identities produce two instances
  differing only by identity. Construct-only; the execution body is deferred.
- **`WorkflowDefinitionActivity`** — the single backing CLR `IActivity` for every workflow-backed activity.
  Because it is an ordinary CLR activity, it is also catalogued under a `ClrActivityDescriptor`; the Workflow
  kind selects the *specific* backing workflow at construction time via the `WorkflowIdentity`.

The runtime feature's `ActivityConstructorsStartupTask` aggregates the constructor into the registry — this
feature registers nothing else to wire it in.

## Cross-references

- The construction seam this plugs into: [`../../Runtime/README.md`](../../Runtime/README.md).
- The design/discovery half of this kind: [`../Design/README.md`](../Design/README.md).
- The sibling CLR kind: [`../../Primitives/README.md`](../../Primitives/README.md).
- Constitutional basis: §2.6.1 (contribution contract); Elsa §E2.2; G4 (no feature → feature).
