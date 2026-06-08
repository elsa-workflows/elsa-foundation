# Quickstart — Adding an activity kind & the construction round-trip

This unit makes "add a new activity implementation kind" a closed, three-type move inside the owning feature, with **zero** edits to the factory, registry, design domain, or reconciling handler (SC-004).

## The construction round-trip (what happens at runtime)

1. Execution path holds `(DescriptorType, payload JsonElement, inputs, outputs)` for an activity node.
2. `IActivityFactory.Create(descriptorType, payload, inputs, outputs)` → `registry.Resolve(descriptorType)`.
3. The owning `IActivityConstructor` bridge deserializes `payload → TDescriptor`, then `Construct(descriptor, inputs, outputs)`:
   - resolve CLR `Type`, activate via DI,
   - apply descriptor-sourced state,
   - bind author inputs/outputs (typed properties for CLR; bag for dynamic),
   - return a **whole** `IActivity`.

## Add a new kind — the three-type move (inside your feature)

1. **Descriptor type** — a lightweight model. If sharable, put it in a zero-dep building-block lib (like `TypeInformation`, `WorkflowIdentity`); otherwise in your feature. `DescriptorType = its FullName`.
2. **`IActivityConstructor<TYourDescriptor>`** — in your feature. Implement `Construct(descriptor, inputs, outputs)`; add the one-line bridge. Register it in your feature's `ConfigureServices` (it is auto-aggregated into the registry at startup via `OnActivityConstructorsInitializing`).
3. **`IActivityReconciliationSource`** — in your feature (design-side). Produce `ActivityVersionReconciliationModel`s with `DescriptorType = "<FullName>"` and the descriptor object.

That's it. No universal component changes — verified by the no-branch structural test.

## Worked: CLR kind (in `Elsa.Activities.Primitives`)
- Descriptor: `Elsa.Primitives.Models.TypeInformation` (existing). `DescriptorType = "Elsa.Primitives.Models.TypeInformation"`.
- Constructor: `ClrActivityConstructor : IActivityConstructor<TypeInformation>` → `LoadType()` + `ActivatorUtilities.CreateInstance` + `IActivityArgumentBinder.Bind(...)`.
- Source: `Elsa.Activities.Design.Reconciliation.Clr` scanner emits `TypeInformation.FromType(type)`.

## Worked: Workflow kind (in `Elsa.Activities.Composition`)
- Descriptor: `Elsa.Workflows.Primitives.WorkflowIdentity(DefinitionId, VersionId, Version)`. `DescriptorType = "Elsa.Workflows.Primitives.WorkflowIdentity"`.
- Constructor: `WorkflowActivityConstructor : IActivityConstructor<WorkflowIdentity>` → `typeof(WorkflowDefinitionActivity)` + apply identity + bag-fill (`IActivity.SyntheticProperties`). *(Construct-only this unit.)*
- Source: `WorkflowActivityReconciliationSource` over usable-as-activity workflow versions.

## Verifying the invariants (tests)
- **§E2.2 (G15)**: a reference test asserts no project in the runtime construction path references any `Elsa.*.Design.*` project.
- **SC-006**: a reference test asserts no feature references another feature (`Primitives`, `Composition`, `Reconciliation.Clr`).
- **SC-002**: a repo-wide search asserts zero references to the deleted types.
- **SC-005**: registering two constructors for one `DescriptorType` throws at startup.
- **Round-trip**: persist→reload→`Create` yields a whole `WriteLine`/`WorkflowDefinitionActivity` (no `Kind` consulted).

## Constraints to honor
- xunit only; **no FluentAssertions**.
- Feature classes `public`, not sealed; logic-bearing impls `public sealed`.
- `IActivityArgumentBinder` stays in `Elsa.Activities.Primitives` (core is for contributor/replacement contracts, not a bucket).
