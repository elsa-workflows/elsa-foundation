# Elsa Foundation Studio

Elsa Foundation Studio is the browser-based workspace for managing Elsa workflow design, operations, and supporting runtime assets.

## Language

**Slot**:
A contribution target where Studio modules can provide UI, actions, context, diagnostics, or other Studio capabilities. A Slot can be owned by the Studio host or by a module, and it can represent a whole routed experience or a smaller region inside an existing Studio surface.
_Avoid_: Extension point, placeholder, plugin area

**Slot Kind**:
The category of a Slot, such as route, widget, panel, action, prompt starter, or diagnostics widget. Slot Kind describes the shape of contributions a Slot accepts, not the identity of a concrete Slot.
_Avoid_: Slot type when referring to a specific Slot

**Slot ID**:
The stable identity of a concrete Slot used to route contributions to the correct place in Studio. A Slot ID must be specific enough to identify the owning surface and purpose of the Slot.
_Avoid_: Panel slot, widget slot, area name

**Slot Owner**:
The host or module responsible for defining a Slot's identity, accepted contribution shape, ordering rules, and local admission rules. A Slot Owner can allow nested module contributions, but host policy still has final authority over whether a contribution is available.
_Avoid_: Slot provider, parent module

**Host Policy**:
Studio-wide rules that decide whether Contributions, Weaver Tools, Slots, and Resource Target mutations are available for the current user, tenant, feature state, module state, and risk boundary.
_Avoid_: Slot Owner rules, module policy

**Module Policy**:
Module-owned rules that decide how a module's Slots, Contributions, Weaver Tools, and Resource Targets may be used before Host Policy is applied.
_Avoid_: Host policy

**Contribution**:
A module-provided item registered into a Slot, such as a route, widget, panel tab, action, editor, context provider, prompt starter, backend tool, or diagnostics signal. A Contribution is available only when it satisfies the Slot Owner's rules and host policy.
_Avoid_: Plugin, extension, component when referring to the registered module-provided item

**Contribution Availability**:
The runtime state that explains whether a registered Contribution can currently be used, based on backend readiness, permissions, policy, or other conditions after its owning module or feature is enabled.
_Avoid_: Dependency, module load status

**Unavailable Contribution**:
A registered Contribution that is visible in its active context but cannot currently be used because backend readiness, permissions, policy, or another runtime condition blocks it.
_Avoid_: Disabled feature, missing module

**UI Contribution**:
A Contribution that renders visible Studio UI, such as a route, widget, panel tab, or editor.
_Avoid_: Component when referring to the registered contribution as a whole

**Component**:
The render implementation used by a UI Contribution.
_Avoid_: Contribution

**Dashboard**:
The dedicated Studio capability that owns the `/dashboard` surface, Dashboard Slot, widget framing, layout, refresh orchestration, and dashboard preference schema. The Studio host composes Dashboard but does not own domain widgets.
_Avoid_: Studio Web host, Attention, module catalog

**Dashboard Slot**:
A host-framed Slot for workspace summary UI Contributions that help users understand what to know or do next. A module may contribute zero, one, or multiple Dashboard Widgets; no screen space or widget count is reserved by module identity. Dashboard Contributions are ordered deterministically by explicit order and stable fallback values, not by module load order.
_Avoid_: Overview, diagnostics page

**Dashboard Widget**:
A UI Contribution to the Dashboard Slot whose frame, title, accessibility semantics, loading and error treatment, and layout controls are owned by Dashboard. The contributing module supplies widget metadata and a body Component.
_Avoid_: Self-framed dashboard card, Attention Item

**Dashboard Integration Module**:
A dedicated Studio adapter module that contributes one or more domain-owned widgets to the Dashboard Slot without coupling the domain itself to Dashboard. Its name identifies the Studio domain and the singular Dashboard surface.
_Avoid_: DashboardWidgets module, dashboard code embedded in a domain core module

**Dashboard Data Module**:
A dedicated backend satellite module that translates complete authorized domain data into bounded snapshots for Dashboard Widgets. It depends on the domain while keeping the domain itself independent of Dashboard.
_Avoid_: Dashboard queries embedded in a domain core module, client-side full-dataset aggregation

**Studio Preferences**:
A governed capability for storing versioned, bounded preference documents scoped to a user, tenant, and Studio host. Preference namespaces must be registered; owning domains define defaults, validation, migration, and meaning while Studio Preferences owns persistence, quotas, concurrency, and transport.
_Avoid_: Arbitrary JSON store, domain settings, secret storage

**Preference Namespace**:
The stable identity and governance boundary of one kind of Studio Preference document, such as Dashboard layout preferences or Attention snoozes.
_Avoid_: Unregistered storage key, widget ID

**Attention**:
A cross-cutting capability for discovering, normalizing, correlating, and managing conditions that may require a user's attention. Attention is independent of Dashboard; Dashboard may present Attention through a widget, while other Studio surfaces may consume the same capability.
_Avoid_: Dashboard subdomain, dashboard widget framework

**Attention Item**:
A normalized, permission-scoped description of one condition that may require attention, including its stable identity, severity, occurrence, destination, sensitivity, and correlation identifiers.
_Avoid_: Dashboard Widget, notification, domain action

**Attention Contributor**:
A domain-owned provider that evaluates its complete authorized dataset and returns bounded Attention Items through the shared Attention contract. The contributor owns the domain meaning of a condition; shared Attention infrastructure owns orchestration and structural policy.
_Avoid_: Dashboard Contributor, widget provider

**Attention Integration Module**:
A dedicated satellite module within a domain that translates domain state into Attention Items. It depends on the domain and Attention capabilities while keeping the domain itself independent of Attention.
_Avoid_: Attention code embedded in a domain core module, Dashboard integration module

**Workflow Designer Slot**:
A Slot owned by the Workflows module for extending the workflow designer, such as panel tabs, toolbar actions, node actions, or property editors. Built-in designer tabs are also Contributions into these Slots.
_Avoid_: Hardcoded designer area

**Activity Property Editor Slot**:
A Workflow Designer Slot for editor Contributions that edit activity property values. Contributions to this Slot match by activity property metadata, value type, expression type, and priority.
_Avoid_: Generic panel slot, component

**JavaScript Expression Module**:
A module that owns JavaScript expression support in Studio, including editor Contributions, expression metadata, validation diagnostics, Weaver Tools, and JavaScript-specific help.
_Avoid_: Workflows module when referring to JavaScript-specific behavior

**Diagnostics Slot**:
A Slot for troubleshooting and observability Contributions that show what is unhealthy, failing, noisy, slow, or operationally significant.
_Avoid_: Dashboard, module load status page

**Snapshot Diagnostics Contribution**:
A Diagnostics Contribution that loads current diagnostic status or counts on demand.
_Avoid_: Live diagnostics contribution

**Live Diagnostics Contribution**:
A Diagnostics Contribution that subscribes to a diagnostic stream and updates continuously while reporting its own connection state.
_Avoid_: Snapshot diagnostics contribution

**Module Load Status**:
Module management state that describes whether a Studio module was discovered, accepted, activated, disabled, or rejected. Module Load Status is administration state, not a Diagnostics Contribution.
_Avoid_: Diagnostics signal, observability widget

**Weaver**:
The user-facing assistant experience in Studio.
_Avoid_: Agent when addressing users

**Weaver Provider**:
A backend-provided Weaver capability source or profile that can answer, stream, propose, apply, or report supported operations according to policy.
_Avoid_: Agent when shown as a user-facing choice

**Agent**:
An internal compatibility term used by backend contracts, provider-neutral profiles, capabilities, and module contribution registries.
_Avoid_: Assistant when referring to user-facing Weaver

**Operation Batch**:
A structured set of workflow draft changes proposed by Weaver for review and draft-local application.
_Avoid_: Execution, save, publish

**Weaver Tool**:
A module-contributed capability that Weaver can invoke to inspect, propose, or mutate Studio-managed resources according to the user's goal, available skills, permissions, and policy.
_Avoid_: Button, UI action, provider capability

**Tool Contract**:
The metadata and schemas that define how Weaver may invoke a Weaver Tool, including input shape, Resource Target shape, Tool Result shape, risk, permissions, availability, and supported invocation modes.
_Avoid_: UI action contract, provider contract

**Tool Invocation**:
One call to a Weaver Tool within a Weaver session.
_Avoid_: Proposal, chat message

**Invocation Mode**:
The allowed way Weaver may use a Weaver Tool, such as read-only inspection, direct mutation, proposal creation, or privileged mutation.
_Avoid_: Tool kind when referring to the invocation policy

**Tool Slot**:
A Slot whose Contributions are Weaver Tools.
_Avoid_: Separate tool registry, action list

**Read-only Tool**:
A Weaver Tool that inspects or explains state without mutating Studio-managed resources.
_Avoid_: Safe tool when the meaning is specifically non-mutating

**Direct Tool**:
A Weaver Tool that can mutate a Resource Target immediately when user intent, tool permission, module policy, host policy, and risk all allow it.
_Avoid_: Proposal Tool

**Proposal Tool**:
A Weaver Tool that returns a Proposal for review before a mutation can occur.
_Avoid_: Direct Tool

**Privileged Tool**:
A Weaver Tool that requires stronger approval or policy controls because it can perform high-impact, sensitive, or administrative mutations.
_Avoid_: Direct Tool

**Tool Result**:
The structured output produced by a Weaver Tool invocation.
_Avoid_: Chat response, component

**Result Renderer**:
A module-contributed UI Contribution that renders a Tool Result or Proposal payload inside a host-owned review shell.
_Avoid_: Review shell, proposal component

**Review Shell**:
The host-owned Weaver UI frame for reviewing a Proposal or mutating Tool Result, including target identity, risk labels, approval or apply controls, denial controls, audit status, errors, loading states, and disabled reasons.
_Avoid_: Result Renderer

**Proposal**:
A review and governance wrapper around a recommended mutation to any Studio-managed resource. A Proposal identifies its Resource Target, risk, permissions, review state, and mutation semantics.
_Avoid_: Operation Batch, suggestion when mutation is possible

**Mutation Semantics**:
The Resource Target-specific rules for what Apply does, including whether it updates a draft, changes configuration, dispatches work, creates an artifact, records audit, supports undo, or requires follow-up persistence.
_Avoid_: Save semantics when the mutation is not a save

**Resource Target**:
The Studio-managed resource a Weaver Tool, Tool Result, or Proposal refers to, such as a workflow draft, workflow definition, agent definition, extension, feature, package feed, diagnostic setting, or module-owned resource.
_Avoid_: Workflow when the target can be any Studio-managed resource

**Theme Store**:
The Studio-managed collection of theme definitions, material texture assets, default-theme metadata, and publication state used to control Studio presentation.
_Avoid_: Workflow theme state, runtime theme policy

**Theme Definition**:
A named Studio theme record that describes light and dark presentation variants, material references, publication state, and availability for selection in Studio.
_Avoid_: CSS file, theme preset, user preference

**Built-in Theme**:
A read-only Theme Definition supplied by Studio as a seed option. Users customize a Built-in Theme by duplicating it into a Custom Theme.
_Avoid_: Editable preset, default theme

**Custom Theme**:
A user-managed Theme Definition that can be edited, validated, published, disabled, deleted, imported, or exported through the Theme Store.
_Avoid_: User preference, arbitrary CSS

**Material Texture Asset**:
A reusable Theme Store asset referenced by Theme Definitions to provide tiled material texture for Studio surfaces.
_Avoid_: Embedded CSS, data URI token, workflow asset

**Published Theme**:
A Theme Definition that is ready for general Studio use. A Published Theme is available for selection by Studio users only when it is also enabled.
_Avoid_: Saved theme, default theme

**Draft Theme**:
A saved Theme Definition that can be edited and previewed but is not yet available for normal user selection.
_Avoid_: Disabled theme, unpublished CSS

**Disabled Theme**:
A Theme Definition that remains in the Theme Store but is unavailable for normal user selection.
_Avoid_: Draft theme, deleted theme

**Theme Selection Preference**:
A user's local choice of active Theme Definition and light or dark mode for their Studio session.
_Avoid_: Theme Store state, default theme, host policy

**Extension Resource**:
A Resource Target owned by Extension Builder, such as an extension project, build, generated module, manifest, or package.
_Avoid_: Workflow resource

**Activity Property Value**:
A Resource Target representing one editable property value on an activity inside a workflow draft.
_Avoid_: Workflow draft when referring to the specific edited value

**Apply**:
To commit a reviewed Proposal or allowed Direct Tool result to its Resource Target according to that target's mutation semantics.
_Avoid_: Execute, accept

**Interactive Weaver Apply**:
A foreground Weaver action where the user applies a reviewed Proposal or allowed Direct Tool result from the current UI context, such as applying an Operation Batch to the active workflow draft in the workflow designer.
_Avoid_: Background mutation, autonomous execution

**Dispatch**:
To start a workflow run from Studio.
_Avoid_: Apply, publish

**Test Run**:
A Studio-initiated transient dispatch of a workflow draft for validation. A Test Run may create transient runtime artifacts and should link to workflow execution evidence when available without saving, promoting, or publishing the draft.
_Avoid_: Published run, apply

**Runtime Evidence**:
Execution information produced by a dispatched workflow, such as status, instance identity, activity count, incident count, logs, traces, artifact references, timing, and expiration.
_Avoid_: Artifact inventory

**Runtime Payload Capture Policy**:
The runtime rule that decides how much evidence may be persisted for a runtime value. Host Policy caps the maximum capture level allowed by the deployment.
_Avoid_: Studio setting, logging level

**Diagnostic Snapshot**:
A bounded, policy-sanitized representation of a runtime value intended for troubleshooting. A Diagnostic Snapshot is not the original payload and must not be treated as replayable data.
_Avoid_: Payload, raw value, serialized object

**Runtime Payload Reference**:
A protected pointer to payload material that is not stored inline in Runtime Evidence, such as a secret value, blob, artifact, or large runtime value. A Runtime Payload Reference can appear as a leaf inside a Diagnostic Snapshot, but resolving it requires separate authorization and audit.
_Avoid_: Diagnostic snapshot, inline payload, attachment

**Workflow Runtime Diagnostics Setting**:
A Studio-managed Workflows setting that requests a runtime payload capture level for workflow diagnostics, subject to Host Policy.
_Avoid_: Host Policy, runtime payload capture policy

**Runtime Value Evidence Level**:
The user-facing level of runtime value evidence requested or allowed for workflow diagnostics: Off, Metadata only, Diagnostic snapshots, or Full payloads.
_Avoid_: Capture mode, log level

**Run Kind**:
The category of workflow run shown in runtime views, such as Test Run, Published Run, Background Weaver Run, or Unknown.
_Avoid_: Status

**Run**:
The user-facing term for a dispatched workflow execution.
_Avoid_: Workflow Instance in navigation and general UI labels

**Published Run**:
A Run dispatched from an Executable rather than directly from a draft.
_Avoid_: Test Run

**Background Weaver Run**:
A Run dispatched by an Autonomous Weaver Session according to the user's goal, available Weaver Tools, and policy.
_Avoid_: Test Run, published run

**Workflow Instance**:
The durable execution record for a Run.
_Avoid_: Run when referring specifically to the persisted record identity

**Executable**:
A content-addressed, runnable workflow artifact identified by its Artifact Hash and dispatched for Published Runs. An Executable carries its own Execution Material, so it can be executed and inspected without resolving its source Workflow Definition; one or more Source References point at it.
_Avoid_: Run, workflow instance, publish record

**Source Reference**:
A self-contained record linking one publish or Test Run source to the Executable its behavior resolves to, carrying the per-source facts: source identity, artifact version label, published time, scope, optional expiry, and the embedded Layout Sidecar. Behaviorally identical sources produce distinct Source References to the same Executable, and a Source Reference remains meaningful in environments where its source does not exist. An Executable is retained while any live Source Reference points at it.
_Avoid_: Executable, artifact copy

**Test Run Reference**:
An expiring Source Reference created by a Test Run, pointing from a draft snapshot to the Executable the draft compiles to. When it resolves to the same Executable as a published version, the draft is behaviorally identical to that version.
_Avoid_: Transient artifact, second executable

**Execution Material**:
The behavior-defining content of an Executable: the activity graph, per-activity configuration, and activity type and version references. Execution Material references activity implementations by identity; it never embeds them.
_Avoid_: Layout, design metadata, activity descriptors

**Artifact Hash**:
The content identity of an Executable, computed over Execution Material only. Two Executables with the same Artifact Hash are behaviorally identical regardless of visual layout.
_Avoid_: Checksum over layout, version number

**Layout Sidecar**:
The visual arrangement of an Executable's activity graph, copied at publish time and embedded on the Source Reference. It enables read-only rendering of the Executable in any environment but never contributes to the Artifact Hash; an Executable with no Source Reference at hand renders with automatic layout.
_Avoid_: Execution material, design-time draft layout, artifact property

**Executable Inspector**:
The routed, read-only Studio surface for one Executable: its rendered activity graph, identity, and Source References. Inspect is the action that opens it; the Inspector never mutates the Executable.
_Avoid_: Explain, executable editor, preview

**Runtime Tab**:
A workflow designer panel Contribution for viewing Runtime Evidence for recent dispatches and selected workflow executions.
_Avoid_: Artifacts tab, console

**Execute**:
To perform workflow runtime behavior after dispatch, including running the root activity and its child activities according to workflow semantics.
_Avoid_: Apply, save

**Autonomous Weaver Session**:
A goal-driven Weaver session that can run outside the current foreground interaction and may spawn child sessions or background work according to the user's goal, available tools, available skills, and policy.
_Avoid_: Interactive apply, chat message

**Child Weaver Session**:
A Weaver session spawned under an Autonomous Weaver Session to perform part of the parent goal. Child session activity is shown under the parent goal, and user prompts or approvals roll up through the parent session.
_Avoid_: Separate user conversation

**Weaver Session Indicator**:
A persistent Studio UI signal for active or background Weaver sessions, including their count, goal label, state, pending proposals, pending artifacts, and a way to open session details.
_Avoid_: Notification, toast, chat message

**Secret**:
A named metadata record that lets workflows refer to sensitive material without exposing the material in Studio. A Secret never includes a current raw value in frontend-visible responses.
_Avoid_: Credential, config value

**Secret Reference**:
A workflow property value that points to a Secret by name, with optional type and scope metadata.
_Avoid_: Secret value, credential binding

**Secret Store**:
The backing source category for a Secret, such as Elsa-managed encrypted storage or host configuration lookup.
_Avoid_: Provider, vault

**Secret Type**:
The expected shape or use of a Secret, such as text, RSA key, or X.509 certificate.
_Avoid_: Value kind, credential type

**Rotation**:
Replacement of the material or configuration lookup associated with a Secret while preserving its technical name.
_Avoid_: Reveal, reset
