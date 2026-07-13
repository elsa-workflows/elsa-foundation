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
- `studio.weaver.tool-slots`
- `studio.weaver.tool-contracts`
- `studio.weaver.result-renderers`
- `studio.ai.context-providers`
- `studio.ai.prompt-actions`
- `studio.ai.tools`
- `studio.ai.proposal-renderers`
- `studio.ai.surfaces`

## Expression editor Contributions

Expression descriptors carry one required semantic `editingMode`: `literal`,
`text`, `structured`, or `reference`. The Workflows Slot Owner uses that mode to
choose the property surface; expression modules do not need to duplicate host
layout rules.

Modules may contribute inline and expanded expression editor surfaces
independently through `api.expressionEditors`. Text mode always remains usable:
when a module omits either surface, Workflows supplies a single-line inline or
multiline expanded text fallback. Expression-specific diagnostics and enhanced
editing stay in the expression language's owning module.

Module-owned Slots may use their own stable IDs and point at a parent Slot ID
when they expose nested extension targets.

## Dashboard Widget Contributions

`api.dashboardWidgets` accepts zero, one, or many contributions from any module;
there is no one-widget-per-module rule. Contribute only a useful operational or
decision-making surface. Dashboard owns the title and frame, semantic
small/medium/wide/full placement, accessibility controls, loading and failure
states, timeout, cache/stale timing, refresh, retry, and layout preferences.

A contribution supplies stable identity and module ownership, supported/default
sizes, an optional cancellable loader, optional refresh/cache/timeout limits,
optional versioned settings, and a body component. The body receives the
host-managed snapshot and validated settings and must not render a competing
card frame. Data-backed widgets use independently authorized and tenant-scoped
backend resources. Loader-less widgets remain valid and do not participate in
managed refresh.

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
- `toolSlots` group resource-aware Weaver tools by surface and invocation mode.
- `toolContracts` declare input, target, result, risk, permissions,
  availability, invocation modes, and renderer expectations.
- `resultRenderers` render module-specific tool results inside the host-owned
  Review Shell, with structured fallback when no renderer matches.

Use stable IDs, set `moduleId` when the contribution belongs to a module, scope
`surfaces` narrowly, and include `requiredPermissions` for anything not available
to every authenticated user. Mutating capabilities should use `review-required`,
`destructive`, or `admin` risk and must be executed through backend proposals.
