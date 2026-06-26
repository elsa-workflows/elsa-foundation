# Studio SDK Slots and Contributions

Studio extensibility is described with Slots and Contributions.

- A `StudioSlotDefinition` names a stable Slot ID, Slot Kind, Slot Owner, accepted Contribution type, and unavailable-state behavior.
- A `StudioContribution` is a module-provided item registered into a Slot. UI Contributions add a `StudioComponent` that renders the visible surface.
- A Slot Owner defines the local contribution shape and admission rules. Host Policy can still hide, disable, or veto any Contribution through `StudioContributionAvailability`.
- Module Policy describes module-owned rules, but it does not bypass Host Policy.
- An unavailable Contribution can be represented with `StudioUnavailableContribution` when a surface should show a disabled reason instead of hiding the item.

The public registries on `ElsaStudioModuleApi` map to Slot definitions through
their optional `slot` metadata. For example, `api.routes.slot.id` is
`studio.routes`, `api.dashboardWidgets.slot.id` is `studio.dashboard.widgets`,
`api.panels.slot.id` is `studio.shell.panels`, and workflow property/expression
editor registries map to `workflow.activity.property-editors` and
`workflow.expression.editors`.

Modules should register Contributions through the existing typed registries.
The Slot metadata is descriptive and additive, so module code that calls
`api.routes.add(...)`, `api.dashboardWidgets.add(...)`, or
`api.expressionEditors.add(...)` continues to work unchanged.

## Weaver contributions

Studio modules can enrich Weaver through `api.agent` without calling an external
agent provider from the browser. Weaver is the visible assistant name; `agent`
is retained for provider-neutral profiles and contribution registries. Provider
access, policy, redaction, approvals, execution, and audit stay in the
Elsa.Server backend.

Register only declarative, scoped contributions:

- `contextProviders` collect minimized context for matching surfaces.
- `promptStarters` offer route-aware suggested prompts.
- `capabilities` describe what the module can help with, including risk and
  permission metadata.
- `actions` describe reviewable proposal shapes for mutating work.

Use stable IDs, set `moduleId` when the contribution belongs to a module, scope
`surfaces` narrowly, and include `requiredPermissions` for anything not available
to every authenticated user. Mutating capabilities should use `review-required`,
`destructive`, or `admin` risk and must be executed through backend proposals.
