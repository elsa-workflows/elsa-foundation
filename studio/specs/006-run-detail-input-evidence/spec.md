# Product Requirements Document: Run Detail Input Evidence Workbench

**Feature Branch**: `codex/run-detail-input-evidence`
**Created**: 2026-07-16
**Status**: Approved for implementation
**Input**: Redesign `/workflows/instances/:id` so the canvas/inspector fills its shell allocation and each activity input pairs runtime evidence with its immutable authored binding.

## Problem Statement

The Run detail page does not use the height allocated by the Studio shell. A capped viewport calculation leaves an empty band on tall screens and can overflow when the shell header, responsive sidebar, or resizable bottom console consumes more space. The current responsive grid can also create implicit columns instead of a deliberate narrow-screen inspector mode.

The Activity > Inputs view presents only runtime snapshots. Operators cannot reliably compare what the pinned executable said an input should evaluate from with what the selected activity execution actually received. The current executable-inspection API collapses rich binding information into a truncated string, runtime evidence is joined only by display name, authored sensitivity is not propagated end to end, and repeated materializations are not identified by phase or sequence.

## Solution

Create a shell-aware, full-bleed Run workbench whose header and toolbar remain fixed inside the route while the canvas/inspector consumes `minmax(0, 1fr)` of the shell's remaining content cell. Internal regions own their scrolling; the route itself does not vertically scroll.

Replace the Inputs list with union rows keyed by a stable `inputKey`. Each row pairs two canonical, independently authorized records:

1. Runtime evidence captured for the selected activity execution, including evaluation phase, sequence, time, capture state, safe diagnostics, and bounded snapshot.
2. Frozen authored source from the Run's pinned Source Reference sidecar plus the structured compiled runtime binding from the content-addressed executable, represented canonically rather than parsed from a summary string.

Compact rows answer “what was used?” and “where did it come from?” at a glance. Expanded rows expose the runtime snapshot, evaluation history, authored expression, compiled binding differences, provenance, and safe failure details. Expression modules may contribute semantic renderers; an always-available generic fallback supports arbitrary Elsa expression types.

## Goals

- Make the Run detail workbench fill all width and height allocated by the Studio shell through header, toolbar, console resizing, and responsive shell changes.
- Let an operator compare evaluated runtime evidence with the exact source frozen on the Run's pinned Source Reference and the compiled binding on its executable, without consulting the mutable workflow draft.
- Represent missing, redacted, unavailable, unknown, orphaned, duplicate, repeated-evaluation, and failed-evaluation states explicitly.
- Preserve Elsa's open expression model and module ownership while providing a safe generic rendering path.
- Enforce independent source/evidence authorization and end-to-end sensitivity propagation.
- Keep outputs runtime-only and preserve existing bounded Diagnostic Snapshot semantics.

## Non-goals

- Editing bindings or workflow definitions from Run detail.
- Revealing secret values, resolving payload references inline, or adding payload download/reveal endpoints.
- Reconstructing authored source from a current draft or parsing the legacy truncated summary.
- Changing output evidence into a paired authored/runtime model.
- Redesigning the global shell, bottom console, or workflow definition editor.
- Defining language-specific expression editors inside the Workflows module.

## Actors

- **Run operator**: Investigates why a Run behaved as it did.
- **Workflow author**: Correlates authored intent with runtime evaluation.
- **Support engineer**: Diagnoses evaluation failures using safe evidence and incident references.
- **Security administrator**: Controls access to authored sources and runtime evidence independently.
- **Expression module contributor**: Provides semantic display for a supported expression type.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Use the whole Run workspace (Priority: P1)

As a Run operator, I can use the full content area remaining after the Studio header, Run controls, and bottom console so that the canvas and inspector do not waste screen space or cause route-level scrolling.

**Why this priority**: The layout defect affects every Run inspection and makes both requested capabilities materially harder to use.

**Independent Test**: Open a Run detail route at tall, short, desktop, medium, and narrow workbench widths; resize the bottom console and inspector; verify the workbench always occupies exactly the remaining shell allocation with no page scrollbar.

**Acceptance Scenarios**:

1. **Given** a tall desktop viewport, **When** Run detail loads, **Then** the workbench extends to the top edge of the bottom console with no empty band beneath it.
2. **Given** the bottom console is resized, **When** its height changes, **Then** the workbench tracks the shell allocation without a viewport subtraction or overlap.
3. **Given** the desktop inspector is resized, **When** it reaches its limits, **Then** it remains between 340px and `min(640px, 50% of workbench width, workbench width - 10px - 480px)`, and the canvas retains at least 480px.
4. **Given** the workbench becomes medium or narrow, **When** the layout mode changes, **Then** the desktop resize handle and explicit grid columns do not create implicit overflow columns.

---

### User Story 2 - Compare runtime input with authored source (Priority: P1)

As an operator inspecting an activity execution, I can see each input's evaluated runtime evidence beside the immutable authored source from the Run's pinned Source Reference and compiled binding from its executable so that I can distinguish source intent from runtime outcome.

**Why this priority**: This is the core diagnostic capability and requires the backend contract to retain information that is currently lost.

**Independent Test**: Execute activities with literal, JavaScript, variable, activity-output, durable-value, and reference bindings plus a Studio fixture with an unknown API discriminator; inspect Inputs and verify each compact and expanded row accurately pairs source and evidence by `inputKey`.

**Acceptance Scenarios**:

1. **Given** a captured scalar input, **When** Inputs opens, **Then** its compact row shows name/type, evaluated value summary, evidence status, binding kind, and authored-source preview.
2. **Given** an expression whose compiled representation differs from its authored form, **When** the row expands, **Then** authored source is primary and compiled binding is shown as a distinct secondary representation.
3. **Given** an arbitrary authored expression type without a contributed renderer, **When** it is inspected, **Then** a generic renderer displays its type and authorized opaque JSON value without inventing CLR type semantics.
4. **Given** an input exists on only one side of the join, **When** Inputs opens, **Then** the union row names the missing, orphaned, or unknown side rather than silently omitting it.

---

### User Story 3 - Trust the evidence and its boundaries (Priority: P2)

As an operator, I can tell when, why, and under which capture/security state an input was materialized so that I do not mistake metadata, redaction, or a later re-evaluation for the original runtime value.

**Why this priority**: Diagnostic data is useful only when its provenance and security boundaries are unambiguous.

**Independent Test**: Exercise sensitive, unauthorized, metadata-only, unavailable, failed, and multiply evaluated inputs; verify labels, history, safe diagnostics, and permissions without exposing protected content.

**Acceptance Scenarios**:

1. **Given** an activity input is materialized more than once for one activity execution, **When** its row is compact, **Then** the latest evaluation is shown; **When** expanded, **Then** ordered history identifies phase and monotonically increasing sequence.
2. **Given** evidence access is denied but source access is allowed, **When** the row renders, **Then** source remains visible and evidence is permission-hidden without revealing snapshot content.
3. **Given** source access is denied but evidence access is allowed, **When** the row renders, **Then** bounded evidence remains visible and source is permission-hidden.
4. **Given** an authored input is sensitive, **When** it is compiled, captured, stored, projected, and rendered, **Then** sensitivity remains true and neither source nor evidence leaks through summaries or diagnostics.
5. **Given** evaluation fails for one input, **When** the activity is inspected, **Then** that row and Issues show a safe diagnostic, phase/time, and incident correlation when available, while other inputs remain inspectable.

---

### User Story 4 - Inspect effectively at every supported width (Priority: P2)

As an operator on a constrained screen, I can focus on either the canvas or activity inspector without horizontal overflow, while selecting an activity reliably brings its inspector into view.

**Why this priority**: Run investigation must remain operable when the available workbench width changes independently of viewport width.

**Independent Test**: Resize only the workbench container across desktop, medium, and narrow thresholds; verify panel mode, focus, keyboard access, and scroll ownership.

**Acceptance Scenarios**:

1. **Given** desktop width, **When** Run detail loads, **Then** canvas and a resizable 400px-default inspector are visible side by side with a 42px collapsed rail.
2. **Given** medium width, **When** an activity is selected, **Then** the inspector opens as an overlay drawer and the canvas remains behind it.
3. **Given** a workbench narrower than 480px, **When** an activity is selected, **Then** canvas and inspector become mutually exclusive views and the inspector is shown.
4. **Given** a row is expanded, **When** keyboard focus moves through controls, **Then** expansion state, evidence/source sections, history, and diagnostics are operable without hover or pointer-only actions.

## Edge Cases

- Declared input has no pinned binding and no runtime evidence.
- Pinned binding exists for an input absent from the activity descriptor.
- Runtime evidence arrives for an unknown or renamed input.
- Multiple records claim the same stable key or only legacy name-based identity is available.
- Authored expression type is registered after the executable was pinned or is unknown to the current Studio build.
- Runtime evidence is late, duplicated, out of sequence, metadata-only, unavailable, truncated, redacted, unsupported, or permission-hidden.
- Expression source is blank, structurally invalid, extremely long, binary-like, or sensitive.
- Activity is resumed from a bookmark or rematerialized after parent completion under the same activity execution ID.
- Selected activity disappears while data refreshes.
- Bottom console changes height while an inspector drawer is open.
- Workbench is embedded in a host whose width changes without a viewport resize.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Run detail route MUST render as a full-bleed, `min-height: 0` layout inside the shell content cell.
- **FR-002**: The Run header and Run toolbar MUST occupy intrinsic rows; the canvas/inspector workbench MUST occupy a `minmax(0, 1fr)` row.
- **FR-003**: The route MUST NOT calculate available height from `100vh` or fixed header/console offsets.
- **FR-004**: The route MUST NOT own vertical page scrolling; canvas, inspector, trees, and code/source regions MUST scroll internally as appropriate.
- **FR-005**: Run detail MUST own a route-specific layout state and storage namespace so its inspector defaults and responsive modes do not change the workflow designer or executable inspector.
- **FR-006**: Desktop mode MUST apply at workbench widths of 830px or greater. Inspector width MUST default to 400px, clamp to `340px..min(640px, 50% of width, width - 10px - 480px)`, persist in the Run-specific namespace, and use a 42px collapsed rail.
- **FR-007**: Medium mode MUST apply from 480px through 829px and use a modal overlay drawer. Narrow mode MUST apply below 480px and use mutually exclusive canvas/inspector views. Mode MUST be selected from workbench/container width, not viewport width alone.
- **FR-008**: Selecting an activity MUST expose its inspector in medium/narrow modes. The medium drawer MUST have a labelled dialog, close button, Escape and backdrop dismissal, initial focus, trapped focus while open, inert background, and focus restoration to the selected canvas activity. Narrow mode MUST provide a labelled Back to canvas action and restore focus without modal semantics.
- **FR-009**: The activity inspector MUST order content as compact summary, Inputs, Outputs, then collapsed Execution details.
- **FR-010**: Outputs MUST remain runtime-only evidence.
- **FR-011**: Inputs MUST render the union of declared inputs, pinned authored/compiled bindings, and runtime evidence.
- **FR-012**: `inputKey` MUST equal the activity input `ReferenceKey` within the executable-node/activity-execution scope. The activity authoring catalog MUST expose `referenceKey`; display names MUST NOT be the canonical join key.
- **FR-013**: The content-addressed executable MUST retain structured compiled bindings only. A per-publish Authored Inputs Sidecar on `WorkflowExecutableSourceReference`, keyed by executable node ID and input `ReferenceKey`, MUST retain frozen authored `ArgumentState` as opaque `JsonElement` values plus sensitivity. It MUST NOT contribute to the artifact hash.
- **FR-014**: Run detail MUST resolve the authored sidecar through the Run's pinned `sourceReferenceId` and MUST NOT consult a mutable workflow draft. When no pinned reference/sidecar survives, it MUST show compiled-only/source-unavailable state.
- **FR-015**: Authored source MUST be the primary source representation; compiled binding MUST appear when it differs or provides additional provenance.
- **FR-016**: The source contract MUST structurally represent authored literal/expression/variable values and compiled literal, expression, activity output, durable value, and reference bindings without parsing a display summary. Studio MUST tolerate unknown API discriminators generically; Foundation is not required to persist unknown compiled binding variants.
- **FR-017**: The public SDK expression-editor contribution MUST gain an optional read-only source-rendering surface. Expression modules own semantic renderers through that admitted/ordered Host Policy slot; Workflows MUST own only selection and a generic fallback.
- **FR-018**: Compact input rows MUST show input identity/type, latest runtime summary and status, authored binding kind, and source preview without exposing sensitive content.
- **FR-019**: Expanded input rows MUST show bounded runtime evidence, capture metadata, authored source, compiled binding differences, provenance, and evaluation history.
- **FR-020**: Each runtime materialization MUST carry an `evaluationId`, `evaluationPhase`, monotonic `evaluationSequence` within its activity execution/input, and capture timestamp. `evaluationId` MUST be replay-idempotent and sequence allocation/deduplication MUST be atomic with the runtime checkpoint.
- **FR-021**: Compact rows MUST show the latest evaluation by sequence; expanded rows MUST show ordered evaluation history.
- **FR-022**: Input rows MUST explicitly distinguish missing declaration, missing binding, no evaluation, orphan evidence, unknown key, duplicate key, redacted, permission-hidden, metadata-only, unavailable, unsupported, truncated, and failed evaluation states.
- **FR-023**: Input materialization MUST return per-input success/failure results so successful evidence is retained when another input fails. Failures MUST remain scoped to the row and MUST also appear in Issues with a safe diagnostic, phase/time, and incident reference when available before the activity faults.
- **FR-024**: Authored-source access MUST require existing `workflow-publishing.read`; runtime-evidence access MUST require existing `workflow-runtime.read`. Separate endpoints/projections MUST allow every permission combination without broadening either grant.
- **FR-025**: On source denial, authored value, expression/literal text, reference/durable/activity-output IDs, compiled source, metadata, and legacy summary MUST be absent. On evidence denial, payload, snapshot, payload-reference leaves, diagnostic/failure message, and metadata MUST be absent. Each response MUST retain only identity plus explicit permission-hidden state.
- **FR-026**: Server projections MUST remove protected fields before serialization; the client MUST NOT receive protected source or payload data merely to hide it visually.
- **FR-027**: Authored `IsSensitive` MUST propagate through sidecar capture, compilation metadata, runtime materialization, every invoke/resume/parent-completion capture path, persistence, API projection, summaries, and rendering.
- **FR-028**: The UI MUST identify runtime values as “Evaluated at runtime” and display the existing evidence mode/provenance so snapshots are not presented as live payloads.
- **FR-029**: The feature MUST add provenance fields without changing existing `ActivityInput` subject, `None | MetadataOnly | DiagnosticSnapshot | Payload` capture modes, state casing, `RuntimeValueTypeDescriptor`, or Diagnostic Snapshot union. Payload references remain snapshot leaves under ADR 0009; no inline secret or payload reveal is added.
- **FR-030**: Existing executables, Source References without authored sidecars, and legacy evidence lacking the new fields MUST render a safe compatibility state rather than fail or fabricate provenance.
- **FR-031**: Row expansion, panel switching, resize controls, diagnostics, and source/evidence navigation MUST meet keyboard and semantic accessibility requirements.
- **FR-032**: Module styling MUST use the public `--studio-*` token contract and MUST pass the raw-colour lint rule.
- **FR-033**: Full-height behavior MUST hold during loading, failed-load, source-unavailable, selected-activity removal, responsive host changes around 1024px, and bottom-console collapsed (40px), 140px, 260px, 560px, maximize, and restore states.

### Key Entities

- **Authored Inputs Sidecar**: Immutable per-publish provenance embedded on the pinned Source Reference, keyed by executable node ID and input ReferenceKey. Contains opaque frozen authored values and sensitivity and never affects artifact identity.
- **Executable Input Binding**: Immutable behavioral record on the content-addressed executable. Contains stable input ReferenceKey and the structured compiled runtime binding.
- **Input Evaluation Evidence**: One bounded runtime materialization record. Contains stable key, phase, sequence, time, evidence state/mode, value type, safe snapshot/diagnostic, sensitivity, and incident correlation.
- **Input Inspection Row**: Client-side union projection that pairs zero or one source record with zero or more ordered evaluation records and derives explicit compatibility/anomaly states.
- **Expression Source Renderer**: Module contribution that converts a known authored expression type into semantic display data; the generic renderer consumes the canonical fallback representation.
- **Run Workbench Layout State**: Container-derived desktop/medium/narrow mode, persisted desktop width/collapse state, and current narrow focus view.

## Implementation Decisions

1. Use separate pinned-source, compiled-binding, and runtime-evidence projections joined in Studio, so lifecycle and authorization remain explicit.
2. Put non-behavioral authored provenance on the Source Reference sidecar per Foundation ADRs 0038/0039; keep compiled behavior on the content-addressed executable.
3. Never parse or depend on the legacy `summary` field.
4. Use `InputDefinition.ReferenceKey` as `inputKey` inside the selected executable-node/activity-execution scope and expose it through the activity catalog.
5. Treat invocation, bookmark resume, and parent-completion rematerialization as distinct phases. Replay-idempotent evaluation ID plus atomic sequence is authoritative; timestamps are evidence, not identity.
6. Use a Run-specific container-observed layout state; do not mutate shared designer defaults.
7. Extend the SDK's admitted expression-editor contribution with an optional read-only source renderer; keep the mandatory fallback in Workflows.
8. Extend the existing Runtime Value Evidence wire model additively rather than introducing unbounded payload access or redefining its enums/unions.

## Testing Decisions

- Contract and serialization tests cover Source Reference sidecar round-trip, every compiled binding discriminator, Studio unknown-discriminator tolerance, legacy records, permission combinations, and redaction.
- Compiler/runtime tests prove ReferenceKey propagation, sidecar/executable separation and hash invariance, sensitivity propagation, replay-safe evaluation ID/sequence, resume/parent rematerialization, and per-input failure isolation.
- Studio unit tests cover union-join derivation, ordering, compatibility/anomaly states, generic expression rendering, and no summary parsing.
- Component tests cover compact/expanded rows, independent protected states, history, failures, activity-tab order, keyboard behavior, and outputs remaining runtime-only.
- Vitest covers mode state/classes, Run-specific persistence, contribution admission, disclosure, and keyboard semantics. Playwright browser tests cover actual shell geometry, bounding rectangles, route/list padding regression, console states, container-only resizing, responsive host changes, and scroll ownership.

## Success Criteria *(mandatory)*

- **SC-001**: At supported viewport heights and bottom-console states 40px, 140px, 260px, 560px, maximize, and restore, the workbench ends at the console edge with no visible empty band, overlap, or route-level vertical scrollbar.
- **SC-002**: At least 95% of supported compact input rows expose runtime status and source kind without expansion; every row exposes complete allowed detail after expansion.
- **SC-003**: Automated fixtures for all compiled binding kinds plus an unknown Studio API discriminator render without parsing `summary` and without unhandled exceptions.
- **SC-004**: All independent source/evidence authorization combinations pass automated tests with zero protected source or payload content in unauthorized API responses.
- **SC-005**: A sensitive authored input remains sensitive at every tested boundary and produces no value/source leakage in summaries, diagnostics, logs, or rendered DOM.
- **SC-006**: Repeated materializations under one activity execution render in deterministic sequence and identify phase in 100% of new-contract records.
- **SC-007**: Desktop, medium, and narrow modes remain operable with keyboard alone and have no unintended horizontal page overflow.
- **SC-008**: Existing runtime evidence, executable-inspection, workflow editor, lint, typecheck, build, and targeted test suites remain green.

## Assumptions

- A Run's pinned executable is authoritative for behavior; its pinned Source Reference is authoritative for per-publish authored provenance and may legitimately lack an authored sidecar on legacy/promoted data.
- Runtime evidence remains diagnostic and bounded; it is not a durable business-payload history.
- Existing permissions can be extended or separated without changing the user-facing authentication model.
- Workbench breakpoints will be tuned against the existing canvas minimum viable width, with 480px as the narrow fallback trigger.
- Legacy executable/evidence records may lack stable keys or source detail and therefore require honest compatibility states.

## Further Notes

- ADR 0002 assigns expression-language behavior to expression modules.
- ADR 0005 requires “Run” as the user-facing term.
- ADR 0009 defines Diagnostic Snapshots and separately authorized/audited Payload References.
- Delivery spans `/Users/sipke/Projects/Elsa/elsa-foundation` and `/Users/sipke/Projects/Elsa/elsa-foundation-studio`; backend contract/runtime support must land before Studio depends on it.
