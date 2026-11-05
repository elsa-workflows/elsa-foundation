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
- `workflow.run-input.editors`
- `workflow.run.related-links`
- `workflow.designer.node-renderers`
- `workflow.designer.toolbox-items`
- `workflow.designer.panels`
- `studio.setting-editors`
- `studio.diagnostics`
- `studio.diagnostics.widgets`
- `studio.weaver.context-providers`
- `studio.weaver.prompt-starters`
- `studio.weaver.prompt-actions`
- `studio.weaver.capabilities`
- `studio.weaver.actions`
- `studio.weaver.tool-slots`
- `studio.weaver.tool-contracts`
- `studio.weaver.result-renderers`
- `studio.weaver.result-controls`

A result renderer may present another module's results: set `resourceModuleIds` to the resource modules it presents,
and the host matches the result's resource module against them instead of the renderer's own `moduleId` (unset keeps the
previous match). A result control (`api.agent.resultControls`) is the resource-owning module's interactive control for
a result type, such as a designer-history Undo of a change it applied; the host renders every matching control after
the renderer, so a renderer from another package never needs the owning module's internals.

## Related run links

Hosts may expose the optional `api.workflowRunRelatedLinks` registry. A `StudioWorkflowRunRelatedLinkContribution` names its native activity types and exact public state-view kind/version, then synchronously returns related Studio routes from that authorized projection. A provider-owned family of user-defined activities, whose type keys are not known in advance, sets `anyActivityType: true` instead; the host then reads the state view of every selected activity, and the view kind alone decides. The native inspector owns the read and scope retirement; the host rejects foreign destinations/backends and the target route authorizes its own reads. Contributions never receive private activity frames. Hosts without this Slot retain ordinary native inspection.

## Activity icons

Third-party activity packages can supply a designer icon without growing the Workflows
entry bundle. The catalog field is `iconName`, written from `[Activity(Icon = …)]` on the
CLR activity. Accepted values:

`iconName` takes precedence over historical `icon` values and the generic reusable icon.
Recommended activities retain their module icon when the catalog assigns them definition
IDs. Use a vendor-prefixed extension name such as `ext:acme-slack` to avoid name collisions.

| Value | Renders as |
|---|---|
| A lucide export or kebab name (`Rocket`, `git-branch`) | That lucide glyph, loaded on demand when it is not already in the shared table. An unknown name shows the category placeholder briefly while the lazy import resolves, then the glyph or the fallback. |
| `asset:<package>/<path>` | An `<img>` of the package's static web asset at `{studio-base}/_content/<package>/<path>`. Images do not follow `--studio-wf-activity-*` tone tokens. |
| `ext:<name>` | The React component registered under that name. Names match case-insensitively. |

Register `ext:` icons from the extension module's `register()`. A late registration
re-renders mounted glyphs, so the icon appears without a remount. Use `currentColor` on
lucide and `ext:` icons so they follow `--studio-wf-activity-*` tone tokens
(`asset:` `<img>` values cannot):

```ts
import { registerActivityIcons, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { SlackIcon } from "./SlackIcon";

export function register(api: ElsaStudioModuleApi) {
  registerActivityIcons({ slack: SlackIcon });
  // Prefer the standalone export. The host alias is optional:
  // api.workflowDesigner.registerActivityIcons?.({ slack: SlackIcon });
}
```

Then declare `[Activity(Icon = "ext:slack")]` on the activity. An unknown, invalid or
failed value uses the semantic type/category fallback (or placeholder) and never crashes the
palette or canvas. `Icon` is presentation only: the `elsa.activity-icon` facet is excluded
from the descriptor hash, so adding or changing it does not throw
`ActivityVersionHashMismatchException`.

Existing persisted versions retain their icon facets during duplicate reconciliation.
Changing the attribute alone does not refresh an already stored version's icon metadata;
a newly contributed version carries the declaration. Older entries without metadata use
the semantic type/category fallback. No workflow or executable identity changes for icons.

### Module navigation icons

Sidebar navigation uses the module contribution's own component, independently of
activity catalog metadata. Register it with the existing navigation slot:

```ts
api.navigation.add({
  id: "acme-slack",
  label: "Slack",
  path: "/acme/slack",
  icon: SlackIcon
});
```

Both forms keep icon code and assets in the contributing module; neither needs a Studio
source change. Use `currentColor` and accept the `size` prop on navigation components too.

## Workspace tabs

Every routed screen renders inside a host-owned workspace tab. Most routes need nothing: the host
derives the tab title and icon from navigation and route metadata. While a tab is in the background
React keeps the screen's state but tears down its effects (polling, subscriptions, `popstate` and
navigation-guard listeners), and mounts them again when the tab returns with its own address.

```ts
api.routes.add({
  id: "acme-order",
  path: "/acme/orders",
  label: "Orders",
  component: OrdersPage,
  // Lists return nothing; an editor names the resource so two tabs never edit it at once.
  workspace: { resourceKey: location => location.searchParams.get("order") ? `acme-order:${location.searchParams.get("order")}` : null }
});

function OrderEditor({ order }: { order: Order }) {
  useStudioWorkspaceTitle(order.name, "Draft"); // tab reads "Order 42 · Draft"
  const location = useStudioRouteLocation();   // read this, not window.location, while rendering
  // …
}
```

- `openStudioWorkspaceTab(path)` opens an in-app path in a tab (or focuses the tab that shows it) and
  returns false on hosts without workspace tabs. `isStudioWorkspaceTabClick(event)` is the Alt-click
  gesture; leave Ctrl/Cmd, Shift and middle clicks to the browser.
- Navigation guards receive `StudioNavigationRequest.intent`: `navigate`, `close`, or `probe`. Cancel
  a `probe` when the screen holds unsaved or pending work, but never prompt or change UI for it; the
  host uses it to mark background tabs.
- Effects mount again when a tab returns. An effect that loads data and seeds editable state (a draft,
  undo history, a form) must do so once per resource, not on every mount, or returning to the tab
  replaces unsaved edits with the server copy. Refetching read-only data on return is fine.
- React Flow canvases render `RetainFlowViewport` (Workflows) so the viewport survives the return;
  React Flow otherwise rebuilds its pan-zoom from `defaultViewport`.
- Write history from user actions or effects. Navigation that follows async work (a create that opens
  what it created) goes through `useStudioWorkspaceWhenActive()`: it runs at once while the tab is
  active and otherwise waits until the tab returns, so a background tab never writes the active tab's
  address.

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
card frame. Contributions can provide an `isEmpty` predicate and empty-state
copy so Dashboard, rather than module bodies, renders the standard empty
semantics. Data-backed widgets use independently authorized and tenant-scoped
backend resources. Loader-less widgets remain valid and do not participate in
managed refresh.

## Workflow run-input editor Contributions

Modules register type-aware execution-input controls through
`api.workflowRunInputEditors`. Match only stable declared metadata such as
`input.type.alias` and `input.type.collectionKind`; Foundation intentionally
does not send CLR implementation details or module-private editor metadata.

A Contribution owns its visible control, draft validation, and conversion to
the JSON value sent on the execution wire. Lower `order` values win when more
than one Contribution supports an input. Keep `supports`, `validate`, and
`serialize` deterministic and free of side effects. Studio contains callback
and render failures, reports an accessible field error, preserves the current
draft, and switches that field to the built-in primitive or honest JSON
fallback instead of taking down or permanently blocking the run dialog. After
a callback failure, the user can review the fallback value and submit again.

The Workflows module exports `createEnumWorkflowRunInputEditorContribution`
for scalar enums. The registering module supplies the stable type match and
either a static option catalog or an input-derived option callback. Unsupported
custom types continue to use JSON entry.

## Workflow Test Run stimulus editor Contributions

Modules register one-off trigger controls through
`api.workflowTestStimulusEditors`. Match on the backend's stable `driverId` and
`modeId`; do not inspect activity types or depend on module-private configuration
fields. The mode's `configurationSchema` describes the user-supplied stimulus
value. Its `configuration` is readonly context returned by the backend, such as
a prepared route or authored schedule.

A Contribution owns its editor, validation, serialization, optional prepared
action, and optional action-result view. Prepared actions use
`context.backend.http.request` when they need to preserve a non-JSON protocol
response; this request uses the configured authenticated transport and returns
the unparsed `Response`, including non-2xx status, headers, and body. Contributions
must treat the action URL as untrusted and restrict it to the backend origin.
The workflow designer only resolves Contributions from the selected backend
driver/mode and never selects editors by activity type.

The Runtime panel reuses the same Contribution for API-reported supported waits
by adapting each wait to the `option` shape with its wait ID, authored node ID,
driver ID, and modes. This lets a module own both initial trigger data and later
logical delivery without adding a workflow-activity switch to Studio. The host
keeps retry IDs stable when a delivery response is uncertain.

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
  Review Shell, with structured fallback when no renderer matches. A renderer may present another module's
  results through `resourceModuleIds`.
- `resultControls` (optional; hosts that predate it omit it, so guard its use) add the resource-owning module's
  interactive control for a result type, rendered after whichever renderer presents it.

Use stable IDs, set `moduleId` when the contribution belongs to a module, scope
`surfaces` narrowly, and include `requiredPermissions` for anything not available
to every authenticated user. Mutating capabilities should use `review-required`,
`destructive`, or `admin` risk and must be executed through backend proposals.

## Weaver module participation (candidate)

Register context, prompt actions, capabilities, tool contracts, result renderers and, where the host offers them,
result controls through `api.agent`. A renderer contributed by one package for another module's results declares that
module in `resourceModuleIds`; a control that only the resource-owning module can perform, such as a designer-history
Undo of a change it applied, is that module's result control, so neither package needs the other's internals. `api.ai` remains a prompt-dispatch compatibility alias; its old contribution
registries and `studio.ai.*` Slots have been removed. The SDK package re-exports the canonical
host barrel, so package consumers receive these same declarations. This candidate is still
subject to the independent #3438 package witness before signature freeze.

Context providers return attachments with owner/schema/provenance and typed availability
diagnostics. Caller attachments, request source, delivery mode and correlation survive prompt
dispatch; installed backend sources admit and redact them. Client origin never grants authority.
The existing array return form is accepted as an attachment collection without diagnostics.

A backend-prepared client invocation carries frozen arguments, target/revision, approval digest,
expiry and originating `{clientId,generationId}`. A module may provide `executeClient` on its
existing tool-contract contribution. The generic runner calls it only after explicit review and
backend pending acceptance. Resolve with an observed resource outcome, never merely after enqueueing
an editor action. Matching duplicate delivery coalesces within this in-memory lifetime; post-dispatch
loss yields uncertainty rather than replay. Backend acknowledgement validates the module result.

Preview (#3441): a module may also provide `clientTargetAvailability` on the same contribution. The runner
asks it before dispatch whether the invocation's exact unsaved target (its editor, working generation or other
module-private state) still exists in this page. Anything other than `available`, or a failure to answer, refuses
the operation with zero effect and the module's reason. The runner never reconstructs vanished unsaved state, and
a missing adapter is reported as such. An operation already dispatched without a receipt stays uncertain; after a
reload or a lost page its owner can resolve it explicitly, and that statement is never a receipt.

Modules own live bindings, revision interpretation, validation, presentation and native undo.
Resource targets now require `moduleId` and nullable `resourceId`/`revision`; mutations enforce the
exact applicable identity and revision. Typed `{typeId,schemaVersion,data}` results pass through
providers opaquely. Renderer selection predicates and components run inside presentation boundaries;
structured evidence and common review controls remain usable after a contribution fails or its version
is unsupported. Workflow snapshots/window events are no longer a host/SDK transport contract.
Local working-state receipts do not mean save/publish; the native module holds automatic persistence
until the reviewed changes are explicitly saved, published or undone. An older or queued save cannot
release a newer hold: the successful explicit save must cover the current held snapshot and generation.
A failed covering save preserves the hold/undo and stops publication before preflight or mutations.

## User Task form renderers

The additive `user-task.form-renderers` slot exposes `api.userTaskForms`. Contribute a unique `id`, the server form `provider` ID and a component accepting `StudioUserTaskFormProps` (`form`, `value`, `readOnly`, `errors`, `onChange`). Treat form values/error keys as opaque case-sensitive data and read only own properties. Respect read-only state and preserve entered values on validation/conflict. Exactly one renderer must match a task's pinned provider; missing/duplicate matches disable completion. Server-side `IUserTaskFormProvider` validation remains authoritative. The optional UserTasksStudio module contributes the basic renderer and owns inbox/forms/editor flows; the SDK defines only the contribution contract.
