# Feature Specification: Consumer Contract Fragments as Build Output

**Feature Branch**: `1165-consumer-contract-fragments`

**Created**: 2026-08-08

**Status**: Draft — implements RFC #1191 sequencing steps 1 and 2 only (fragments + merged contracts + CI check + equivalence test; completeness gates G1/G2). Later RFC steps (consumer notes, analyze findings, consumer guide, package shipping, resource-backed endpoint flip) are explicitly out of scope.

**Input**: User description: "Consumer contracts as a first-class build output — RFC #1191, sequencing steps 1 and 2 only: shared projection library + per-feature contract fragment emitter + merged docs/contracts/ with CI regeneration check + equivalence test, fragments embedded as assembly resources from day one; completeness gates G1 (inputs with CLR defaults emit defaultValue) and G2 (output descriptors emit isRequired)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read the authoring contract at a pinned commit (Priority: P1)

A consumer integrating elsa-foundation (a human engineer or an AI agent in an external authoring workspace) pins a commit and needs the full authoring contract surface — activity contracts, structure kinds, expression-language surface, intrinsics, and feature metadata — without pulling an image, composing a server, authenticating, and dumping endpoints. They open the committed contract directory at that commit and read everything needed to author workflow definitions against it, then verify by fingerprint that what they read matches what was built.

**Why this priority**: This is the core recurring tax the RFC exists to remove — every consumer, every version bump, pays a boot-and-dump cycle today, and it silently breaks when image publishing breaks. Removing it is the RFC's principle made concrete: building a feature *is* publishing its contract.

**Independent Test**: At any commit on the feature branch, read the committed contract directory and confirm (a) every contract-contributing feature has a fragment, (b) the manifest's per-fragment fingerprints match the fragment contents, and (c) a known activity (e.g. the HTTP endpoint trigger) is fully described — inputs with types/requiredness/defaults, outputs, ports, container structure — without any server running.

**Acceptance Scenarios**:

1. **Given** a checkout of a commit, **When** a consumer reads the merged contract directory, **Then** they obtain per-feature contract fragments covering activity contracts, registered structure kinds and payload schemas, contributed expression-language surface, engine intrinsics, and feature metadata (feature id, options schema, dependency closure) — with no server, no composition step, and no source reading.
2. **Given** the merged contract directory, **When** the consumer compares the manifest's per-fragment fingerprints against the fragment contents, **Then** they match by string compare, proving the contracts correspond to the pinned commit.
3. **Given** a consumer's own feature selection (their shell composition), **When** they intersect the merged fragments with their enabled feature ids, **Then** they obtain exactly their composition's static catalog — fragments carry the feature id needed for this filtering.
4. **Given** any fragment, **When** the consumer inspects it, **Then** it declares its own contract-schema version, so future schema evolution is detectable by readers.
5. **Given** the merged contracts, **When** the consumer searches for assigned activity version ids or availability/addable flags, **Then** they find none — server state is never published as contract.

---

### User Story 2 - Trust defaults and output requiredness (Priority: P1)

A consumer authoring against the contracts (or against the running catalog endpoint) reads an activity input's default value and an output's requiredness directly from the descriptors, instead of discovering them by tripping over runtime behavior or failing a publish.

**Why this priority**: These are the two known truthfulness gaps (RFC gates G1/G2) with concrete reproductions: the HTTP endpoint's response-mode input defaults to asynchronous in code while the served catalog says its default is null (making the "202 with no response body" trap invisible), and the HTTP endpoint's request/route-data outputs must be bound for a successful publish while the catalog does not say so. They dissolve the largest class of consumer-side hand-maintained claims.

**Independent Test**: Project the HTTP endpoint activity through the new pipeline and confirm its response-mode input carries the asynchronous default and its request/route-data outputs carry required-to-bind; confirm the same values appear both in the committed fragment and in the served catalog.

**Acceptance Scenarios**:

1. **Given** an activity input whose backing property has a default value in code, **When** the input descriptor is projected (into a fragment or served by the catalog endpoint), **Then** the descriptor carries that default value (G1).
2. **Given** the HTTP endpoint activity, **When** its contract is read, **Then** the response-mode input's default is the asynchronous mode — not null (known G1 repro).
3. **Given** any activity output descriptor, **When** it is projected, **Then** it states whether the output is required to bind (G2).
4. **Given** the HTTP endpoint activity, **When** its outputs are read, **Then** the request and route-data outputs state that they are required (known G2 repro).
5. **Given** a new activity added later with a defaulted input, **When** the projection runs, **Then** the default is emitted automatically — the gate is mechanical completeness of the generator, requiring no per-activity authoring.

---

### User Story 3 - See contract changes in the causing PR (Priority: P2)

A maintainer reviewing a PR that renames an input key, changes a default, or adds an activity sees that contract change as an explicit diff of the committed contract directory in the same PR — before merge, not in a consumer bug report afterwards.

**Why this priority**: This is the maintainer-side value that makes the artifact self-sustaining: CI enforces that committed contracts never lag the code, so the contract diff between two tags becomes a machine-readable compatibility report.

**Independent Test**: Change a contract-visible property on a feature branch without regenerating contracts; confirm the CI check fails. Regenerate; confirm the check passes and the diff shows exactly the contract change.

**Acceptance Scenarios**:

1. **Given** a PR that changes a consumer-visible contract (input added, key renamed, default changed), **When** contracts are regenerated, **Then** the change appears as a diff inside the committed contract directory of that same PR.
2. **Given** a PR whose code changes a contract but whose committed contract directory was not regenerated, **When** CI runs, **Then** the contract check fails, identifying the stale state.
3. **Given** a PR with no contract-visible change, **When** contracts are regenerated, **Then** the contract directory is byte-identical — regeneration is deterministic, so no-op changes produce no diff noise.

---

### User Story 4 - Runtime catalog cannot drift from published contracts (Priority: P2)

A consumer who authored against the committed contracts talks to a running server of the same commit and observes the same static surface: the catalog endpoint's feature-provided content equals the merged fragments of the enabled features, plus only additive dynamic registrations (store-fed activities) and a server-state overlay (assigned version ids, availability).

**Why this priority**: The one-projection rule is the key technical risk called out in the RFC — two code paths projecting the same types would be a drift generator, and the whole artifact loses trust the first time the file and the server disagree.

**Independent Test**: Compose a representative host, request the activity catalog, and assert programmatically that its feature-provided portion equals the merged fragments of the composed features overlaid with server state — as an automated equivalence test in the repo's suite.

**Acceptance Scenarios**:

1. **Given** a representative composed host, **When** the equivalence test compares the catalog endpoint output with the merged fragments of enabled features plus server-state overlay, **Then** they are equal for the entire feature-provided surface.
2. **Given** an activity registered dynamically at runtime (graph/reusable/store-fed), **When** the catalog is served, **Then** it appears additively alongside the static surface — the equivalence check treats it as union, never as a re-projection of static content.
3. **Given** a feature assembly built on this branch, **When** its binary is inspected, **Then** its contract fragment is embedded as an assembly resource whose bytes equal the fragment merged into the contract directory — the groundwork for the later resource-backed endpoint flip, without flipping serving now.
4. **Given** existing catalog endpoint clients, **When** this feature ships, **Then** they observe only additive changes (new default-value and requiredness data); no existing route, field, or permission changes.

---

### Edge Cases

- A feature assembly contributes no consumer-visible authoring surface → it emits no fragment and does not appear in the manifest; absence is meaningful ("contributes nothing"), not an error.
- An input's default value is not statically representable as a wire value (computed at runtime, environment-dependent) → the descriptor explicitly represents "no static default" distinctly from "default is null", and the G1 gate accepts it only for genuinely non-static cases.
- A defaulted input of nullable type whose default *is* null → emitted as an explicit null default, distinguishable from "no default declared".
- Two builds of the same commit on different machines/OS → byte-identical fragments (deterministic ordering, formatting, culture-invariant value rendering); otherwise the CI check would flap.
- A feature's structure kind declares no published payload schema → the fragment lists the kind with the payload schema explicitly absent (opaque by choice), mirroring the served registry's behavior.
- The merged directory contains a fragment for a feature that fails to load at merge time → the merge fails loudly; a silently partial contract set is worse than no contract set.
- Fragment schema evolves later → every fragment self-declares its schema version; readers can detect and reject versions they do not understand.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every feature assembly that contributes to any consumer-visible authoring surface MUST emit, at build time, a contract fragment describing that contribution: activity contracts (type key; inputs with types, requiredness, and defaults; outputs; ports; container structure), structure kinds with their payload schemas, contributed expression-language surface (registered expression types and script-sandbox functions/globals), engine intrinsics (for the engine fragment), and feature metadata (feature id, options schema, dependency closure as structural data).
- **FR-002**: Fragment emission MUST be deterministic: the same source at the same commit produces byte-identical fragments regardless of build machine, OS, or culture — stable key order, stable formatting, culture-invariant value rendering.
- **FR-003**: Every fragment MUST self-declare a contract-schema version so future schema evolution is detectable by readers; the schema MUST be extensible without breaking existing readers (additive evolution).
- **FR-004**: Each contributing feature assembly MUST embed its fragment as an assembly resource at build time, byte-identical to the fragment merged into the repository artifact. *(Implemented by embedding the committed fragment file itself — identity, not a copy; see plan/research R4 for the deviation rationale.)*
- **FR-005**: The repository MUST commit a merged contract directory (sibling of the existing generated-maps area) containing all fragments plus a manifest of per-fragment content fingerprints, so a consumer verifies "contracts match the pinned commit" by string compare.
- **FR-006**: CI MUST verify on every PR that the committed contract directory matches what the tree would regenerate, failing when stale — riding the same regenerate-and-commit convention as the existing maps machinery.
- **FR-007**: One shared projection MUST produce the descriptor views consumed by both the fragment emitter and the runtime catalog endpoint; neither may re-implement the projection of feature-provided surface.
- **FR-008**: The runtime catalog endpoint's feature-provided content MUST equal the merged fragments of enabled features plus a server-state overlay (assigned version ids, availability) and the additive union of runtime-registered activities; an automated equivalence test against a representative composed host MUST enforce this.
- **FR-009** (G1): Every activity input whose backing property declares a default value MUST emit that default in its descriptor, in fragments and in the served catalog alike; the known repro (the HTTP endpoint's response-mode defaulting to asynchronous while the catalog said null) MUST be fixed by construction.
- **FR-010** (G2): Every activity output descriptor MUST state whether the output is required to bind; the known repro (the HTTP endpoint's request and route-data outputs being required-to-bind but unstated) MUST be fixed by construction.
- **FR-011**: G1 and G2 MUST be enforced as hard, mechanical completeness gates on the projection — they verify the generator emits what the code already declares and require no per-activity human authoring.
- **FR-012**: Published contracts MUST NOT contain server state: no assigned activity version ids, no availability/addable flags.
- **FR-013**: The generation CLI MUST be process-isolated from the build it observes, MUST be runnable standalone against an arbitrary set of feature assemblies (the same tool consumers run against their own activity packages), and MUST emit its diagnostics in canonical MSBuild warning/error format so CI, IDEs, and coding agents surface them natively; fragment embedding MUST be build-integrated. *(Revised from "per-project in-build emission" — RFC resolved position 2 — after implementation surfaced an unresolvable reference cycle: the emitter's product projection references include assemblies that are themselves contributors. See research R4; explicitly flagged for maintainer review in the hand-off.)*
- **FR-014**: All changes to the served catalog MUST be additive and non-breaking for existing clients: no existing route, field, permission, persisted shape, or content-hash contract changes; new descriptor data (defaults, output requiredness) appears as new fields.

### Key Entities

- **Contract fragment**: one per contributing feature assembly — self-declared schema version, feature metadata (feature id, options schema, dependency closure), activity contracts, structure kinds with payload schemas, expression-language surface contributions, intrinsics (engine fragment only). Identified by feature id; activities identified by type key plus contract fingerprint, never by assigned version id.
- **Merged contract directory**: the committed, deterministic merge of all fragments plus a **manifest** of per-fragment content fingerprints; the whole-surface view for repo-pinned consumers.
- **Descriptor view**: the single projected shape of a feature-provided authoring surface element (activity, input, output, structure kind, expression contribution), produced once and consumed by both the emitter and the runtime endpoint.
- **Server-state overlay**: the runtime-only data (assigned version ids, availability) the endpoint layers over static contracts; explicitly not part of any published fragment.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A consumer at a pinned commit obtains the complete feature-provided authoring contract surface by reading committed files only — zero servers booted, zero endpoints dumped, zero source files read.
- **SC-002**: Regenerating contracts twice from the same commit — including on different operating systems — produces byte-identical output, 100% of the time.
- **SC-003**: 100% of PRs that change a consumer-visible contract either show the change in the committed contract directory's diff or fail CI; a contract change can no longer merge silently.
- **SC-004**: 100% of activity inputs with statically declared defaults carry that default in fragments and in the served catalog; the HTTP endpoint's response-mode default reads as asynchronous, not null.
- **SC-005**: 100% of activity output descriptors state their requiredness; the HTTP endpoint's request and route-data outputs read as required.
- **SC-006**: The equivalence test (runtime catalog = merged enabled fragments + overlay + dynamic union) passes in CI on a representative host composition.
- **SC-007**: Existing catalog endpoint clients observe zero breaking change — all pre-existing routes, fields, and fingerprint contracts behave identically.

## Assumptions

- The committed contract directory's authority comes from generation, fingerprints, and CI gates — not from its path; `docs/` is simply this repo's established home for committed generated knowledge (per RFC placement note).
- The existing served submit-body schema (already static reflection) joins the contract directory as-is; it is not re-designed here.
- "Representative host" for the equivalence test means a composition broad enough to exercise activities, structures, expression contributions, and intrinsics — not every possible composition; consumers with custom compositions rely on the filter-by-feature-id property (US1 scenario 3).
- Runtime-registered activities (store-fed, design-authored) legitimately exist only server-side and are out of fragment scope by design; the equivalence test treats them as additive union.
- RFC steps 3–5 (consumer-note attributes, analyze findings F1–F3, consumer-guide claims file, package content-file shipping, resource-backed endpoint serving flip) are out of scope; the only forward-looking accommodations permitted are the self-declared fragment schema version (FR-003) and the embedded resource (FR-004), both explicitly requested by the RFC for step 1.
- Delivery is validated externally: the maintainer verifies this branch from a consumer workspace against a locally built container image before any merge; the branch is handed off, not merged, when CI is green.
- Enum default values are published in the wire spelling (camelCase per the FastEndpoints enum converter): `HttpEndpoint.ResponseMode` publishes `"async"`, matching what the server itself writes for that enum on the wire.
- Deviation from RFC resolved position 2, flagged for review: fragment *generation* runs post-build (CLI `merge`/`check` + CI gate) rather than per-project during compilation, because the emitter's product-code references make in-build emission cyclic for the projection assemblies themselves; *embedding* remains build-integrated. Diagnostics keep canonical MSBuild form and surface in the same PR's CI run.
