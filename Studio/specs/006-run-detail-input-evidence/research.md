# Research: Run Detail Input Evidence Workbench

## Repository and domain evidence

### Shell allocation and empty-space defect

- `ShellFrame` already allocates the main application and resizable bottom console with grid rows. Its content cell is the correct source of available height.
- The Run detail page is a flex page, but `.wf-instance-detail-workbench` caps itself with `min(760px, calc(100vh - 230px))` and does not grow. The canvas also has a 560px minimum height.
- The bottom console is independently resizable from 140px to 560px; a hard-coded viewport subtraction cannot track it.
- At the current narrow media rule, explicit child grid columns survive a one-column grid declaration and create implicit columns.
- The workflow definition editor already proves the full-bleed pattern: remove host content padding for the route root and carry `height: 100%`, `min-height: 0`, and `flex: 1` through the page chain.

**Decision**: Use the shell’s content-cell allocation, a full-bleed route root, intrinsic header/toolbar rows, and a `minmax(0, 1fr)` workbench. Observe workbench width for responsive modes.

### Authored expression and compiled binding model

- Elsa authoring stores an open `Expression { Type, Value }` model. `ArgumentState` additionally carries evaluation/storage aliases and nullable `IsSensitive`.
- Compilation special-cases `Literal`, `Object`, and `Variable`; other expression types compile to runtime expressions. Variable source may be structured JSON with reference and declaring-scope identity.
- Runtime bindings are a discriminated union: Literal, Expression, ActivityOutput, DurableValue, or Reference.
- The executable-inspection API currently degrades this to `{ inputName, source, summary }`, truncates values, and loses authored-vs-compiled distinctions.
- ADR 0038 makes executables content-addressed over behavioral execution material only. Different authoring forms that compile to the same behavior can share an artifact. ADR 0039 places per-publish visual provenance on the Source Reference.
- ADR 0035 requires opaque authored JSON to remain verbatim `JsonElement`, never an open CLR `object` graph.
- ADR 0002 requires expression-specific behavior to remain owned by expression modules.

**Decision**: Preserve structured compiled bindings on the executable. Add an Authored Inputs Sidecar of verbatim `JsonElement` values to the per-publish Source Reference and resolve it from the Run's pinned `sourceReferenceId`. Extend the public expression-editor contribution with an optional read-only renderer and retain a generic fallback.

### Runtime input evidence

- Activity execution snapshots currently include subject, capture mode/state, type, timestamp, optional snapshot/payload, capture reason, sensitivity, and metadata.
- Snapshot projection appends records, so repeated capture can already exist.
- Inputs can be materialized on initial invocation, bookmark resume, and parent completion; these can occur under one activity execution identity.
- Capture currently lacks stable input identity, evaluation phase, and sequence, so the UI cannot distinguish or deterministically order rematerialization.

**Decision**: Carry input `ReferenceKey` as `inputKey` within the node/execution scope. Add replay-idempotent `evaluationId`, `evaluationPhase`, and checkpoint-atomic `evaluationSequence`. Latest sequence drives compact display; expanded history is ascending and phase-labelled.

### Security and diagnostics

- ADR 0009 requires bounded, policy-sanitized Diagnostic Snapshots by default and separately authorized/audited Payload References.
- Current policy supports not-captured, metadata-only, captured, and unavailable states, with diagnostic nodes for redaction, truncation, unsupported data, errors, permission hiding, and payload references.
- Input/output snapshot builders currently pass `isSensitive: false`, and compiler metadata does not propagate authored `IsSensitive`.
- Source text can itself contain protected values; hiding it client-side is insufficient.

**Decision**: Propagate sensitivity through Source Reference, compiled metadata, materializer, all three scheduler capture paths, persistence, and projection. Protect authored-source endpoints with existing `workflow-publishing.read` and evidence endpoints with existing `workflow-runtime.read`; remove protected fields before serialization. Do not add reveal/download behavior.

## Alternatives considered

### Compute height from viewport and CSS offsets

Rejected because global header/sidebar and bottom-console dimensions are dynamic and owned by the shell.

### Send one presentation-ready combined input DTO

Rejected because source and evidence have different ownership, lifecycle, and permissions. Combining them server-side would couple APIs and make independent authorization harder to reason about.

### Join by input display name

Rejected because labels can change, collide, localize, or be absent on legacy/orphan records.

### Re-read the current workflow draft

Rejected because a Run must be explained from its pinned executable, not mutable authoring state.

### Parse the legacy summary string

Rejected because it is truncated, lossy, type-ambiguous, and potentially unsafe.

### Store only compiled runtime binding

Rejected because compilation normalizes authored forms (for example Object to Literal) and can make diagnostic presentation diverge from author intent.

### Store authored source on the executable

Rejected after ADR review because non-behavioral authoring provenance would violate content-addressed artifact identity and become ambiguous when behaviorally identical publishes share an executable. The Source Reference sidecar is the correct immutable per-publish location.

### Always stack inspector below canvas

Rejected because it wastes width at medium sizes and conflicts with the requirement to avoid route-level scrolling. Overlay and focus modes better preserve each task surface.

## Unknowns resolved for delivery

- Multiple captures are expected for invoke/resume/parent-completion rematerialization.
- The latest evaluation is the highest sequence, not merely the latest timestamp.
- Desktop is at least 830px. Inspector default is 400px; min 340px; max `min(640px, 50%, width - 10px - 480px)`; rail 42px. The default clamps below 890px.
- Medium is 480–829px with a modal overlay drawer; narrow is below 480px with mutually exclusive views.
- Legacy records are displayed honestly with compatibility warnings rather than heuristically joined as canonical evidence.
