# Studio SDK Slots and Contributions

Studio extensibility uses Slots and Contributions as its shared public model.
A Slot is a stable contribution target owned by the host or by a module. A
Contribution is a module-provided item registered into a Slot. Slot Kind
describes the shape accepted by the Slot, and Slot ID identifies the concrete
target, such as `studio.dashboard.widgets` or `workflow.designer.panels`.

Slot Owners define local admission rules for their surface. Host Policy runs
after Slot Owner rules and can still veto any Contribution. Disabled
modules/features hide their Contributions by default, while runtime-unavailable
Contributions can be composed with disabled reasons for active contexts.
Contribution Availability is separate from Module Load Status: module status
controls whether Contributions are registered or hidden, while availability
describes whether a registered Contribution can currently be used.

UI Contributions are registered metadata plus behavior. Components are render
implementations used by UI Contributions; they are not the Contribution as a
whole. Registries expose `slot`, `list()`, and `compose()`: use `list()` for
available Contributions, and `compose({ includeUnavailable: true })` when the
UI needs disabled entries with reasons.

Known host-owned Slot IDs:

- `studio.feature-areas`
- `studio.navigation`
- `studio.routes`
- `studio.dashboard.widgets`
- `studio.panels`
- `studio.toolbar.actions`
- `workflow.activity.editors`
- `workflow.activity.property-editors`
- `workflow.expression-editors`
- `workflow.designer.node-renderers`
- `workflow.designer.toolbox-items`
- `workflow.designer.panels`
- `studio.setting-editors`
- `studio.diagnostics`
- `studio.diagnostics.widgets`
- `studio.weaver.context-providers`
- `studio.weaver.prompt-starters`
- `studio.weaver.capabilities`
- `studio.weaver.actions`
- `studio.ai.context-providers`
- `studio.ai.prompt-actions`
- `studio.ai.tools`
- `studio.ai.proposal-renderers`
- `studio.ai.surfaces`

Module-owned Slots may use their own stable IDs and point at a parent Slot ID
when they expose nested extension targets.

## Weaver Contributions

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
