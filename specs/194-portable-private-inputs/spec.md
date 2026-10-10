# Feature Specification: Portable compositions with required private inputs

**Feature Branch**: `codex/2457-portable-input-contract`

**Created**: 2026-10-06

**Status**: Draft — specification checkpoint #2457; no production portability delivered

**Input**: Sipke's explicit approval of reviewed nonsecret public intent plus separately supplied required private configuration at the destination, recorded on [program #1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959#issuecomment-6023232082). [Specification #2457](https://github.com/elsa-workflows/elsa-foundation/issues/2457) follows completed [discovery #2327](https://github.com/elsa-workflows/elsa-foundation/issues/2327).

## User Scenarios & Testing

### User Story 1 - Carry a composition without publishing its private settings (Priority: P1)

A developer imports a supported host configuration, reviews the public settings and exact feature selection, and shares a composition that explains which private configuration input the destination still needs. The operator supplies that input through their own private configuration process. The original machine need not remain available.

**Why this priority**: Sharing only a safe subset loses unknown values. Copying all settings into the public composition exposes private configuration. A declared dependency makes the missing configuration visible without reviewing every unknown field.

**Independent Test**: Import and accept a fixture containing reviewed values and private unknown siblings. Move the complete private input to a second machine/directory through a fixture-owned handoff, make the original directory unavailable, then generate a fresh candidate. Compare all supported source content, allowing only reviewed edits, and scan every public artifact for private canaries.

**Acceptance Scenarios**:

1. **Given** reviewed nonsecret intent and a separately supplied matching private input, **when** generation is accepted at the destination, **then** the candidate preserves all supported original unknown content and applies only reviewed changes, without contacting the origin.
2. **Given** private unknown objects, arrays, null, false, zero and empty values across supported files, **when** import and generation complete, **then** none disappear or change type; source layering is retained rather than flattened.
3. **Given** a required private input is absent, unreadable or bound to another composition/input revision, **when** generation is attempted, **then** it refuses with a safe explanation and publishes no candidate.
4. **Given** the receiving machine contains a different compatible host configuration, **when** it is supplied without explicit replacement review, **then** the operation refuses instead of using its unknown values as if they came from the origin.

### User Story 2 - Review a replacement private configuration (Priority: P2)

An operator can intentionally use another owner-supplied complete configuration bundle, such as one with a destination-specific connection. The tool explains that this replaces the required input and may change private effective values. A fresh explicit review creates a new association; the previous public intent and private input remain intact.

**Why this priority**: Cross-machine use often requires destination configuration. The operator must control that change without an implicit merge or a false reproduction claim.

**Independent Test**: Supply a complete second bundle with different fake private values, explicitly review a rebind, and generate from a new accepted artifact. Verify replacement values come from the selected replacement bundle, public reviewed edits still apply, and neither old input nor existing destination files changed.

**Acceptance Scenarios**:

1. **Given** a different complete private input, **when** replacement is not explicitly accepted, **then** the old association remains unchanged and generation cannot silently substitute it.
2. **Given** an explicit replacement review, **when** a new association is accepted, **then** the tool labels the result as using a replacement input, preserves granular selection/catalog pins, and requires another generation review.
3. **Given** either public or private input changes after review, **when** output publication is attempted, **then** it refuses and requires a fresh capture/review.

### User Story 3 - Keep existing local compositions honest (Priority: P3)

A developer keeps using the existing local file bridge without a forced upgrade. Entering the portable workflow is explicit. Already-authored opaque settings must pass public-output safety review rather than inheriting a portability claim from selection acceptance.

**Why this priority**: Existing accepted documents and immutable catalog pins must keep their meaning. The new workflow must not certify an old arbitrary settings object as safe to share.

**Independent Test**: Exercise unchanged local v1 commands and a deliberately contaminated authored composition. Verify the old command behavior remains, while public portable publication refuses unreviewed opaque values and never prints their contents.

**Acceptance Scenarios**:

1. **Given** a local v1 composition, **when** existing commands run without portability opt-in, **then** they retain their current local preservation and unresolved-readiness behavior.
2. **Given** an already-authored opaque setting with no exact safety review, **when** portable publication is requested, **then** publication refuses; accepting feature selection alone does not certify the setting.
3. **Given** a generated portable candidate, **when** supported intended inspection runs, **then** it uses the same selection and persistence rules and retains unobserved/unverified activation, deployed-state and physical-readiness labels.

### Edge Cases

- Missing versus explicit null/empty/false/zero; nested unknown siblings; nonempty arrays preserved privately without allowing new public array edit semantics.
- Added, deleted, renamed or changed supported sibling files; changed review/catalog/workspace-profile input; stale private receipts; case-equivalent duplicate filenames or keys; unsafe paths and links.
- A private input is complete but belongs to another input identity/revision, shell or environment; a replacement has unsupported activation shape or missing reviewed resource/path mapping.
- Public names that contain credentials or paths; public fingerprints derived from private content; malformed private diagnostics that could echo values.
- Existing output directory, cancellation or publication failure: no overwritten source/destination and no partial published candidate.
- A private bundle and its association are operator-owned evidence, not signed ownership attestation. Malicious edits to both are outside an integrity-only local trust boundary.

## Requirements

### Functional Requirements

- **FR-001**: The portable workflow MUST separate reviewed nonsecret public intent from a declared required private configuration input. The required-input declaration MUST contain only generated logical identities, safe revision tokens and the fixed supported input kind; actual shell/environment labels MUST remain private and redacted in the portable lane. Declarations MUST never contain private values, physical paths or content-derived fingerprints of private data.
- **FR-002**: The destination MUST supply the matching complete private input and its association. Generation MUST refuse missing, unreadable, mismatched or drifted inputs, without consulting the original path, caller ambient settings or an unrelated destination configuration.
- **FR-003**: The supported private bundle MUST retain every supported host configuration file and all unedited content, including unrelated shells/root nodes and selected/unselected overlays. Known source precedence and value presence/kinds MUST remain intact. Unknown content is preserved, not classified or interpreted.
- **FR-004**: Reviewed public edits MUST apply only through existing supported mappings and exact safety/type review. Nonempty arrays may be retained privately; existing public array-edit restrictions MUST remain explicit. Selection resolution MUST reuse the existing pinned planner.
- **FR-005**: Replacement of the required input MUST require a distinct explicit review and create a fresh association/revision. The result MUST distinguish original reproduction from replacement configuration. No field-level merge, automatic destination override or in-place apply is admitted in this slice.
- **FR-006**: Every supplied public/private/review/catalog/profile file and complete supported file inventory MUST be captured and rechecked before publication. Changed or substituted inputs invalidate acceptance. A review MUST bind both public intent and private input.
- **FR-007**: Public preview, authored output, diagnostics and logs MUST omit all private values/paths/fingerprints. Import safety and feature-selection acceptance MUST remain distinct; unreviewed opaque authored content MUST refuse public portable publication. Known physical persistence/credential fields MUST remain excluded from public settings even with a misleading review.
- **FR-008**: Generation MUST require a redacted diff review and publish only to a fresh candidate directory. Refusal/cancellation MUST leave source and existing destination files unchanged and publish no partial candidate. Private carrier/association files MUST NOT be installed as host configuration or included in a shareable export.
- **FR-009**: The new boundary MUST be explicitly versioned and opt-in. Existing authored v1 schema, commands, profile/catalog pins and intended-environment inspection semantics MUST retain their meanings. Unsupported versions/capabilities MUST refuse rather than fall back to the local lane.
- **FR-010**: Missing input, invalid declaration/association, mismatched revision/context, source drift, unsafe public value, unsupported mapping and output conflict MUST have fixed safe refusal classes. Raw parser/peer/IO exception text MUST NOT escape into public output.
- **FR-011**: The tool MUST NOT automatically export/upload private configuration or credentials, resolve secrets from remote stores, or require origin access. Operators supply configuration through their own authorized private process; absent declared private inputs leave generation incomplete. Bundle integrity does not detect an undeclared missing secret/option key inside unknown content; its usability remains operator-owned and runtime readiness unverified.
- **FR-012**: Generation MUST remain file-only. Supported inspection MUST consume the generated candidate using existing intended-input rules and MUST NOT claim deployed attestation, database readiness, connectivity, migration authority or activation. The private input is neither a new host source layer nor a general settings framework.

### Key Entities

- **Portable intent**: Versioned public artifact containing pinned granular selection, reviewed nonsecret settings and a required private-input declaration.
- **Required private input**: Complete supported configuration bundle supplied privately by its operator, with a logical identity and revision association.
- **Private association**: Operator-owned context and integrity record connecting public intent to the captured private bundle; never part of public output.
- **Replacement review**: Explicit approval of a different complete input, producing a new public/private association without overwriting the old one.
- **Generated candidate**: Fresh private host-file bundle reflecting the accepted input and supported reviewed edits; it is not a deployed host.

## Success Criteria

### Measurable Outcomes

- **SC-001**: With the origin unavailable, every supported source file and unedited JSON node survives destination generation; zero unknown values are replaced by an unrelated destination bundle.
- **SC-002**: All documented object/array/null/empty/false/zero and source-layer examples preserve their meaning; only explicitly reviewed supported edits change candidate content.
- **SC-003**: Private value/path canaries occur zero times in every public stream/artifact. Public tokens contain zero content-derived private fingerprints.
- **SC-004**: Every missing/mismatched/drifted/unreviewed input case refuses before publication; every refusal/cancellation leaves originals unchanged and publishes zero partial candidates.
- **SC-005**: An explicitly reviewed replacement produces a distinct association and correctly labeled candidate; the same replacement without review refuses. Existing local compositions retain unchanged semantics.
- **SC-006**: The produced-tool handoff and supported intended inspection demonstrate one coherent accepted selection, with zero promoted deployed/physical readiness claims. Synthetic results do not count as human usability evidence.

## Assumptions

- The supported carrier is the existing Workbench-style host JSON bundle from [Spec176](../176-composition-file-bridge/spec.md), not arbitrary third-party configuration or process environment. Shell/environment are explicit, fixed context; changing them requires a fresh import.
- Operators own private transport and can supply an origin bundle or explicitly reviewed complete replacement. The tool performs local integrity checks, not cryptographic ownership authentication or secure credential delivery.
- Fresh-directory generation is the smallest coexistence rule: it never merges into an existing destination. Operator-prepared replacement bundles provide destination-specific configuration; partial merge/override tooling is separate future work.
- Settings classification remains deferred under framework §2.12 / Elsa §E4. This specification does not ratify that taxonomy. [Discovery report](../../docs/reports/runtime-composition/portable-unknown-settings.md) supplies current source/evidence boundaries.
- Authoring-only API decoupling (#1961), six real UX participants (#2064), production builder and supported activation/recovery remain independently required program outcomes.
