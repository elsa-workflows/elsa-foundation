# Feature Specification: Local composition file bridge

**Feature Branch**: 1306-composition-file-bridge

**Created**: 2026-09-25

**Status**: Implemented — file-only import and reviewed candidate generation; live-host and in-place apply remain separate

**Input**: [Task #2009](https://github.com/elsa-workflows/elsa-foundation/issues/2009), the [source-boundary investigation](../../docs/reports/runtime-composition/import-export-boundary.md), the [selection planner](../174-profile-selection-planner/spec.md), and the [offline plan command](../175-offline-composition-plan/spec.md).

## User Scenarios & Testing

### User Story 1 - Review an existing host as authored intent (Priority: P1)

A developer selects one local host, shell, environment, and pinned catalog. They see exact enabled and disabled feature IDs, effective settings and resource references with file provenance, and unresolved facts before accepting an authored baseline. Preview masks every value not reviewed as safe, including values under unknown fields.

**Why this priority**: Developers already have CShells files; a trustworthy, non-destructive import lets them use the shared planner without manually transcribing dozens of features.

**Independent Test**: Use the two-shell fixture in the [v1 contract](contracts/file-bridge-v1.md). Preview the default shell for Production, explicitly accept it, and inspect the resulting no-profile authored document. Confirm exact feature state, provenance, redaction, no invented locks or runtime claims, and no source-file edits.

**Acceptance Scenarios**:

1. **Given** the base default shell enables A, disables B, and has A.Flag=false and A.Limit=0, **when** the Production overlay sets A.Flag=true, **then** preview reports A enabled, B disabled, Flag=true from the overlay, Limit=0 from the base, and Future.x=null without collapsing null, zero, or false.
2. **Given** the shell binds A to named resource primary and the host defines provider and connection name locally, **when** the developer previews import, **then** the logical binding is visible with source provenance and the connection value is not printed or copied into portable intent.
3. **Given** the preview is not accepted, **when** inspection ends, **then** no authored document or generated host file is written and original files are unchanged.
4. **Given** explicit acceptance of the current preview and catalog pin, **when** the authored baseline is produced, **then** it has no claimed released profile or package locks, records exact accepted enabled IDs, retains disabled B, and labels inventory, uninspected source precedence, and persistence unchecked.

### User Story 2 - Generate a reviewed host-file candidate (Priority: P2)

From accepted authored intent and explicit local sources, a developer previews a redacted source-to-output diff and writes a candidate to a new directory. The candidate changes reviewed existing setting paths and logical resource references; unedited shells, layers, and unrelated configuration stay intact. A changed feature selection without a reviewed activation-layer mapping refuses. It does not replace the live host.

**Why this priority**: Portable selection is useful only when it produces a host-file candidate without destroying existing configuration or pretending to validate the running host.

**Independent Test**: Change A.Limit from 0 to 1 in reviewed intent, generate at a new directory path, compare parsed source and output trees, and verify only that value changed. Confirm source files remain byte-identical and generated output makes no runtime-ready claim.

**Acceptance Scenarios**:

1. **Given** the two-shell fixture and an accepted edit to A.Limit, **when** output is generated, **then** tenant-b, CustomRoot, unrelated overlay and appsettings nodes, A.Future.x=null, and disabled B remain semantically intact; A.Limit is 1 in the candidate and the source bundle is unchanged.
2. **Given** an unknown feature ID or setting in source, **when** it is not reviewed for portable editing, **then** it remains anchored in local source copies and is marked unresolved rather than removed, interpreted, or printed raw in a shareable document.
3. **Given** the destination exists or a selected source changed after preview, **when** generation is requested, **then** the operation refuses before publishing output and overwrites neither source nor destination.

### Edge Cases

- Object-map `false` preserves explicit disabled state. Array entries are enabled declarations in the pinned CShells package; an `Enabled` field on an array object is a setting, not a disable marker. Import may preserve an array's raw source and enabled entries, but an authored explicit removal targeting array syntax refuses in v1. Duplicate IDs within one file/layer, including case-equivalent IDs, refuse; selected object-map overlays may override settings for an object-valued base entry. A scalar/object change for the same feature across layers refuses because the pinned package retains the base scalar value. Array overlays merge by numeric index, so an effective duplicate also refuses.
- Missing, unreadable, malformed, or duplicated selected files refuse; a change to any copied file refuses with a stable class and no partial published output.
- Explicit false, zero, null, empty string, empty object, and empty array remain distinguishable in source-preserving output.
- An inline credential or unknown nonempty field cannot become a portable value by inference. A named external reference is not proof that it resolves or that a database is reachable.
- Base and selected environment overlay are inspected as files. Process environment, command-line overrides, unselected overlays, loaded packages, and running-host state remain unchecked.
- Unknown features and host-owned stores can remain unresolved after safe preview; no finding silently becomes a ready verdict.

## Requirements

### Functional Requirements

- **FR-001**: The bridge MUST require an explicit local host source, selected shell, selected environment, and pinned catalog. It MUST identify selected source files and provenance without treating the caller process's environment as host evidence.
- **FR-002**: Import preview MUST distinguish authored feature selections from effective values in selected base and overlay files, including object-map disabled entries and explicit false/zero/null/empty values. It MUST preserve unknown feature IDs and JSON fields in the local source association. An array entry MUST NOT be interpreted as explicitly disabled from a field named `Enabled` or `State`.
- **FR-003**: The first slice MUST use a local, invocation-scoped source association. It MUST NOT add a source layer or raw source bundle to the portable authored schema. Every file copied into a candidate, including unselected environment files, MUST belong to the frozen source snapshot and be checked for change before publication; a changed snapshot refuses.
- **FR-004**: Acceptance MUST be an explicit action on a displayed preview. The accepted v1 document MUST be pinned to the supplied catalog, have no invented starting profile or package locks, and record exact enabled and disabled decisions without a second feature-selection algorithm.
- **FR-005**: Portable authored output MUST contain only reviewed safe values and logical external references. Known named persistence references MAY be carried as names; provider, connection, secret material, unknown opaque values, and host-owned/private store settings remain local unless separately reviewed and mapped under a later contract.
- **FR-006**: Preview, errors, logs, the redacted plan, and shareable authored output MUST never expose raw connection values or raw unknown-field values. Local generated host files MAY preserve those existing values without printing them.
- **FR-007**: The existing selection planner MUST remain the sole authority for feature selection and dependency findings. Import and export MUST not infer packages, runtime activation, database readiness, or physical persistence compatibility from file presence.
- **FR-008**: Generation MUST write a reviewed candidate to a fresh destination by copying the supported source files and patching only accepted changes. It MUST preserve all unedited sibling shells, selected and unselected overlays, unrelated root nodes, and unknown content semantically; untouched files SHOULD remain byte-identical.
- **FR-009**: Generation MUST show a redacted diff and require acceptance before publishing. It MUST never overwrite an existing destination or modify a source file in this slice. A refusal or cancellation MUST leave no partial published destination.
- **FR-010**: The bridge MUST report distinct, stable refusal classes for missing/unreadable source, malformed/duplicate configuration, source change, unsafe portable value, unresolved reviewed mapping, and output conflict. Diagnostics MUST identify safe file/field identities without echoing raw values.
- **FR-011**: The file-only path MUST NOT load packages, start a host or worker, call a database or package feed, run migrations, or use the feature-management save API.

### Key Entities

- **Local source association**: One invocation's selected host files, shell/environment identity, provenance, and private change tokens; it is not portable authored intent.
- **Import preview**: Redacted exact feature state, safe setting/resource identities and value types, source provenance, unresolved findings, and unchecked evidence boundaries.
- **Accepted authored composition**: Pinned, no-profile user intent with exact enabled/disabled decisions and only reviewed portable references; it is not a copy of the source bundle.
- **Generated host candidate**: Fresh-directory copies of the supported local files with reviewed changes and all other content preserved.
- **Review decision**: Explicit acceptance tied to one preview and source snapshot; a stale decision cannot authorize a changed source.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Across the two-shell fixture and Workbench source sample, every selected feature ID and every explicit state the source shape can express is accounted for in preview; zero unknown IDs or unrelated JSON nodes disappear from generated files.
- **SC-002**: All false/zero/null/empty and base/overlay provenance examples produce documented distinctions; repeated runs over unchanged inputs produce the same redacted preview and semantically equivalent generated candidate.
- **SC-003**: Canary credentials placed in a connection value and an unknown setting appear zero times in standard output, standard error, logs, plan output, and portable authored output. Existing values may appear only in local source/generated host files.
- **SC-004**: Every refusal/cancellation example leaves source files byte-identical and publishes zero partial destination files; a successful write changes only reviewed candidate fields.
- **SC-005**: The complete preview/accept/generate fixture works without a running host, database, package feed, or migration operation, and labels all such facts unchecked.

## Assumptions

- This bounded first slice comes from [#2005](https://github.com/elsa-workflows/elsa-foundation/issues/2005). The [v1 contract](contracts/file-bridge-v1.md) supplies exact source/output and refusal examples. The file-only import checkpoint was delivered under #2019; generation is tracked under [#2023](https://github.com/elsa-workflows/elsa-foundation/issues/2023).
- Authored composition v1 remains strict at the top level. Invocation-scoped source context keeps base/overlay files available; it is not serialized into settings, resources, or a shareable sidecar.
- A portable document can name an existing logical resource. The local host files retain its provider, connection name, and connection value. Unknown values remain local until a later reviewed portability contract exists.
- The first target is an existing CShells host with a selected base file and at most one selected environment overlay. In-place apply, whole-bundle revision/recovery, live host checks, process-override parity, and arbitrary third-party layouts belong to later work under [#1964](https://github.com/elsa-workflows/elsa-foundation/issues/1964) or [#1962](https://github.com/elsa-workflows/elsa-foundation/issues/1962).
- Framework configuration classification (§2.12) and Elsa configuration (§E4) remain deferred. This bounded file contract does not ratify a global settings taxonomy.
