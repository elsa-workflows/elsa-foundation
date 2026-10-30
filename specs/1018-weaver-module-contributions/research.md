# Research: Module-owned Weaver contributions

Status: candidate design for #3437; #3438 package evidence precedes signature freeze. Decisions use current source and the [M0 baseline](../../docs/reports/weaver-baseline.md); no live-provider success is claimed.

## Default implementation and feature composition

**Decision:** Move active defaults and `FoundationAgentAbstractionsFeature` from Core into new `Extensions/Agent/src/Elsa.Agent/Elsa.Agent.csproj`. Preserve configured feature identity `FoundationAgentAbstractions`. API/provider/workflow features inherit the default feature and declare Type dependencies; base registration is idempotent. Move deterministic provider behavior into tests and remove all 16 Core implementation-shape exceptions after their types move.

**Rationale:** Core currently contains orchestration, stores and implementations. A real implementation package permits normal Core rules and gives consumers a supported default. Singleton stores retain data records; scoped facades and executors use current authorization and scoped tools.

**Alternatives considered:** More Core exceptions perpetuate the violation. A renamed configured feature breaks consumers. Production deterministic defaults conceal missing provider configuration.

The default declares a string-valued `Events` dependency in the existing object array without referencing the Events implementation. Consumers explicitly discover ordinary Events infrastructure; a leaf Type dependency discovers the Agent default, not Events recursively. Missing Events must fail clearly. Composition tests prove this exact boundary and repeated base/leaf registration.

## Named collection and admitted tools

**Decision:** Introduce Core `AgentContextCollecting`, `AgentCapabilitiesCollecting` and `AgentToolsCollecting` events with directly readable request/output collections. Evolve existing source interfaces and use `TryAddEventHandler` for one aggregator per event; publish through `IInlineEventPublisher` (Sequential). Feed the existing tool registry/invoker from the admitted snapshot, not a second registry.

**Rationale:** Named fan-in is the constitutional extension mechanism. Multiple applicable sources must compose; first-match context and unrelated frontend registries currently lose participation.

**Alternatives considered:** Anonymous discovery, parallel inert registries and per-module handler policy loops add hidden behavior. Sequential remains unshielded: optional absence/unavailability is a typed contributor outcome with diagnostics; unexpected exceptions and cancellation propagate. No catch-and-ignore or new run-all dispatch policy is introduced.

## Canonical frontend contributions and prompt transport

**Decision:** Make existing `api.agent` canonical for context/tool/presentation contributions. Migrate all existing producers; retain `api.ai` only as a prompt-dispatch compatibility alias if needed. Forward complete prompt requests, including attachments, source, correlation and mode, to admission. Authoritative execution contracts remain backend-owned.

**Rationale:** The current split leaves richer registrations unused while the host drops prompt data and learns workflow details. Evolving one existing path avoids another registry. Canonical SDK exports and wrapper packages must remain consistent.

**Alternatives considered:** Keeping both registration paths produces apparently successful but inert registration. Route decoding cannot establish resource identity. Busy/steer handling must preserve the complete request or return an explicit unsupported response rather than lose correlation.

## Native working state and bounded acknowledgement

**Decision:** Modules register contributions once through Slots/Contributions and expose current editor state through private live bindings. A generation identifies originating editor/selection lifetime; the module interprets draft revision. The workflow example uses real `SetDesignerPosition`, native atomic apply and observed document state before acknowledging. Require exact definition ID, base revision and generation; remove `active-draft` fallback from authorized execution. Whole-batch undo refuses after an intervening revision.

**Rationale:** Existing workflow transaction hooks clone and validate operations, but alone neither enforce base revision nor prove committed editor state. Direct callbacks avoid window broadcasts and a second engine. A successful receipt requires observed owning-state evidence.

**Alternatives considered:** Deferring the bridge fails the thin local journey. An unattended stream wait requires additional generic completion orchestration; the candidate instead mandates explicit review for local mutations even in FullAuto. UI Apply receives HTTP 202, runs the originating callback, sends authenticated acknowledgement and resumes the paused turn from its stored receipt. In-memory deduplication is bounded; after dispatch, lost acknowledgement means uncertainty or an available receipt, never no-op success or silent replay. Durable recovery remains M2.

## Generic results and proof boundary

**Decision:** Preserve generic result type, schema version and JSON payload through both providers; module renderers interpret payload inside common review controls. Keep policy-bearing tool projection/default logic in the implementation package behind Core contracts/thin bridges. Use an unrelated server-effect fixture alongside the native workflow example.

**Rationale:** Providers and host must not deserialize workflow semantics. The second effect exposes accidental workflow coupling. Source fixtures enable candidate iteration; they do not establish independent package consumption.

**Alternatives considered:** A universal resource schema overfits unproved needs; text-only success loses evidence. #3438 must consume real packed .NET/frontend artifacts in a separately authored module and host with zero shared implementation edits before freeze.
