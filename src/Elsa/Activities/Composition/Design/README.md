# Elsa.Activities.Composition.Design

The **design/discovery half of the Workflow activity kind**: turning workflow definition versions marked
*usable-as-activity* into activity catalog rows. Split from the runtime half
([`../Runtime`](../Runtime/README.md)) so that runtime side carries no Design dependency (Elsa §E2.2). This
project references no other **feature** project (G4 / SC-006); its dependency on Workflows.Design read ports
is contained behind a single adapter (§2.7 port + adapter).

## What this feature provides

`ActivitiesCompositionDesignFeature.ConfigureServices` registers, as a standalone source feature (§2.6.1 —
it does **not** derive from the reconciliation feature):

- **`WorkflowActivityReconciliationSource`** → `IActivityReconciliationSource` — a pure mapper over the
  `IUsableAsActivityWorkflowSource` port. Emits one `ActivityVersionReconciliationModel` per usable workflow
  version: `ActivityTypeKey = definitionId`, `Version` = SemVer, `DescriptorType =
  typeof(WorkflowIdentity).FullName`, descriptor `WorkflowIdentity(defId, versionId, version)`, with the
  workflow's inputs/outputs mirrored. It touches no Workflows.Design types directly.
- **`WorkflowDefinitionUsableAsActivitySource`** → `IUsableAsActivityWorkflowSource` — the **only** class
  that reaches into Workflows.Design read ports. Discovery is a full scan
  (`IWorkflowDefinitionStore.ListAsync` → `IWorkflowDefinitionVersionStore.ListByDefinitionAsync`) filtered
  on `WorkflowActivityOptions.UsableAsActivity` (and skipping soft-deleted definitions).

The reconciliation feature's universal `CollectActivityVersions` handler discovers the source from DI and
persists the returned `(DescriptorType, DescriptorPayload)` rows opaquely.

## Runtime dependency (not declared via `DependsOn`)

The adapter requires `IWorkflowDefinitionStore` + `IWorkflowDefinitionVersionStore`, so a shell must also
enable a Workflows.Design persistence provider (EF Core, Groundwork, …); otherwise reconciliation throws at
startup when it resolves the source. This is intentionally **not** a `DependsOn` entry, because those stores
are a provider-neutral contract with no single feature to name — pinning one provider would break provider
neutrality. Composition must ensure a design persistence provider is present (see spec 006 T029).

## Cross-references

- The runtime half of this kind: [`../Runtime/README.md`](../Runtime/README.md).
- The reconciliation lifecycle it plugs into: `Elsa.Activities.Design.Reconciliation`.
- Constitutional basis: §2.6.1 (DI source); §2.7 (port + adapter); Elsa §E2.2; G4 (no feature → feature).
