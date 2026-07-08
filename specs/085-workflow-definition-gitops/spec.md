# Feature Specification: Workflow-Definition GitOps — Git Reconciliation Source + Export Sink

**Feature Branch**: `claude/musing-spence-f8a6c3`

**Created**: 2026-07-07

**Status**: Draft (design skeleton — implementation not started; sharpened by a grilling pass, see
[ADR 0034](../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md) decisions D1–D11)

**Input**: Implement [ADR 0034](../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md):
a concrete git-backed `IWorkflowReconciliationSource` that reads immutable workflow-definition
*versions* from git into the operational catalog, and an **export reconciler** that mirrors the
catalog's versions back to git. Git is a GitOps source + export sink over the existing catalog — NOT a
replacement operational store, NOT the Extension Builder git stack, and (v1) **single-writer** only.

**Program goal**: `none/free-flow`. Keep portable to a future `elsa-workspace`.

## Context

The reconciliation seam exists and is empty; git is named but unimplemented, and the workflow
reconciliation lifecycle is not wired into any shell. This feature fills the gap under a set of
invariants established in ADR 0034:

- **Two authorities (D1):** git is *content-authoritative* per version (over a canonical form);
  the catalog is *retention-authoritative* (append-only, immutable, never deletes). No merge, ever.
- **Single-writer (D2/D11):** exactly one catalog promotes-and-exports; all others import read-only.
  Multi-writer/git-first authoring is deferred (needs author-assigned versions).
- **Canonical identity (D3/D8):** content identity is a canonical (deterministic) serialization; the
  shared payload serializer is made deterministic so `StateSource` is the hash preimage.
- **Export = reconciler (D4):** a set-diff sweep, not an event; loop-avoidance is structural.
- **Mutability split (D5):** `versions/*.json` immutable content; `definition.json` mutable metadata.

All version upserts obey **Model X / FR-016** (`specs/002`). Full rationale, alternatives, and the
decision log are in ADR 0034; this spec is the implementation skeleton.

## Dependencies & Sequencing

1. **`Elsa.Git` shared library (prereq, D9/D10)** — ✅ **DONE & merged** (commit `7275e0d8`, "refactor(git):
   extract shared Elsa.Git library from ExtensionBuilder"). Public `Elsa.Git`
   at `src/Elsa/Git/` exposes `IGitClient` (`RunAsync`/`RunOrDefault`/`IsGitRepository`) + `GitClient`
   + `GitClientOptions` + `AddGitClient()`; true leaf project (only DI/Logging abstractions). The
   Design feature just adds a `<ProjectReference>` — no extraction work remains.
2. **Deterministic shared payload serializer (prereq, D3/D8)** — the real weight of "canonical."
   ✅ **DONE & merged** (PR #549). Own unit: [`specs/086-deterministic-payload-serialization`](../086-deterministic-payload-serialization/spec.md).
3. **Import-reconciler definition-metadata update path (D5)** — ✅ the capability **already landed**
   (`WorkflowsVersionReconciler.UpdateDefinitionMetadata`, PR #546); what remains is a **correction**,
   folded into this unit as **FR-008a** (gate the apply to the newest version). The standalone
   [`specs/087-reconciler-definition-metadata-update`](../087-reconciler-definition-metadata-update/spec.md)
   is superseded by FR-008a — not a separate unit to build.
4. Then: **inbound source (US2)** → **export reconciler (US3)** → **coherence hardening (US4)** as
   FR-016a lands a persisted content hash.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — One shared git stack, reusable outside the app (Priority: P1) — ✅ DELIVERED

`GitClient` lives in a shared public `Elsa.Git` library (`IGitClient` + `GitClient` +
`GitClientOptions` + `AddGitClient()`) at `src/Elsa/Git/`, delivered by the ExtensionBuilder module
refactor (commit `7275e0d8`); the EB module was rewired to `IGitClient` and its module-internal
`GitClient` deleted. The Design-layer git feature consumes it by `<ProjectReference>` — no extraction
work remains in this unit (the original FR-001 is obviated). *This story is recorded for provenance;
the only residual task is adding the reference once `7275e0d8` merges to this branch.*

**Why this priority**: The Design layer cannot reference the Server app / EB module; nothing compiles
until the client has a shared home. That home now exists as a true leaf lib (only DI/Logging
abstractions), so the dependency-envelope guard stays clean.

**Independent Test**: A Design-layer project resolves `IGitClient` and runs a git command in a temp
repo; EB tests still pass against the shared type (reported 75/75); the `GIT_TERMINAL_PROMPT=0`
assertion holds.

**Acceptance Scenarios**:

1. **Given** the extracted `Elsa.Git`, **When** the EB module and the git feature both resolve
   `IGitClient`, **Then** they share one implementation and no second git wrapper exists. *(Met:
   EB rewired, old wrapper deleted, 49/49 arch guard green.)*
2. **Given** a credential-protected remote, **When** any `IGitClient` call runs, **Then** it fails
   fast (no interactive prompt).

### User Story 2 — Versions authored in git appear in the catalog (Priority: P1)

An operator points a **Consumer**-role feature at a repo whose
`workflows/{definitionId}/versions/{semver}.json` files describe immutable versions, with mutable
name/description/delete state in `workflows/{definitionId}/definition.json`. Reconciliation upserts
versions into the catalog and applies definition metadata.

**Why this priority**: The inbound half — the point of a git *source*.

**Independent Test**: Seed a temp repo with two versions + a `definition.json`, run a pass, assert
both `WorkflowDefinitionVersion` rows exist with correct SemVer and commit-time `SourceCreatedAt`, and
the definition's name matches `definition.json`.

**Acceptance Scenarios**:

1. **Given** `versions/1.0.0.json` + `2.0.0.json` for definition `X`, **When** the reconciler runs,
   **Then** the catalog holds `X` with both versions, `State = Published`.
2. **Given** a version file introduced by a commit dated `T`, **Then** the persisted
   `SourceCreatedAt == T` (committer date).
3. **Given** `definition.json` renames `X`, **When** re-imported, **Then** the existing definition's
   `Name` is **updated** (the current reconciler's missing capability).
4. **Given** `definition.json` marks `X` deleted, **When** re-imported, **Then** `X` is soft-deleted;
   no version rows are deleted.
5. **Given** an unchanged repo, **When** re-run, **Then** zero new rows (idempotent).

### User Story 3 — The writer mirrors its catalog versions to git (Priority: P2)

On the single **Writer**-role node, an export reconciler ensures every catalog version has a matching
`versions/{semver}.json` file, committing the absent ones.

**Why this priority**: The outbound half — GitOps needs the writer's versions in git for review and
distribution.

**Independent Test**: Author two versions, run the export reconciler, assert two committed files whose
canonical `state` re-hashes to the persisted version hash, authored by the machine identity.

**Acceptance Scenarios**:

1. **Given** catalog versions absent from git, **When** the export reconciler runs, **Then** exactly
   those files are written + committed; already-present versions are skipped (idempotent).
2. **Given** `PushMode = Manual`, **Then** commits are local until the explicit export/push runs.
3. **Given** `PushMode = Immediate` and a non-divergent remote, **Then** the ff-only push succeeds;
   **given** a divergent remote, **Then** the push is refused (no force, no merge).

### User Story 4 — The system round-trips without looping or corrupting (Priority: P2)

Writer exports and consumer imports compose with no ping-pong; a single-writer violation cannot
corrupt silently.

**Why this priority**: Bidirectional coherence + the enforcement of the single-writer invariant.

**Independent Test**: Export a version, run an import pass on the writer and on a consumer; assert both
are no-ops for that version. Simulate a second writer; assert a rejected push or a loud import throw.

**Acceptance Scenarios**:

1. **Given** a just-exported version, **When** any node imports it, **Then** reconciliation skips it
   (present `(id, version)`; matching hash once FR-016a lands).
2. **Given** two writers minting `v2.0.0` with different content, **When** the second pushes, **Then**
   the ff-only push is rejected; **and** the second writer's next import **throws** on the hash
   mismatch (never silent divergence).
3. **Given** a git file whose content differs from an already-persisted `(id, version)`, **When**
   imported, **Then** the catalog is not mutated and the mismatch is surfaced (throws under Model X
   once the content hash is persisted).

### Edge Cases

- **Unreachable / unauthorized remote**: fail fast (no hang); catalog untouched.
- **Malformed version file**: skip with a diagnostic; other entries still reconcile.
- **Writer clone divergence** (non-ff on integrate): stop and surface (the D7 single-writer signal) —
  never merge or hard-reset the writer clone.
- **Bootstrap**: a fresh writer catalog is seeded by importing an existing repo before it exports.
- **Drafts**: never cross the reconciliation boundary (D6); the optional WIP snapshot is one-way.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: ✅ **Satisfied** by `Elsa.Git` (commit `7275e0d8`). The shared public `Elsa.Git` library
  (`IGitClient` + `GitClient` + `GitClientOptions` + `AddGitClient()`, `GIT_TERMINAL_PROMPT=0`) exists
  as a true leaf project. The Design feature MUST consume it by `<ProjectReference>` (resolving
  `IGitClient` via `AddGitClient()`), and MUST NOT depend on the Server app or the EB module. Residual:
  add the `<ProjectReference>` + `AddGitClient()` wiring when the Design git feature project is created
  (`Elsa.Git` is already merged to main).
- **FR-002**: `StateSource` MUST be a canonical, stable hash preimage. The determinism contract this
  relies on (stable dictionary-key ordering; deterministic object-member order) is owned by
  **[spec 086](../086-deterministic-payload-serialization/spec.md)** (✅ merged, PR #549) — this unit
  consumes that contract, it does not re-specify it.
- **FR-003**: A `GitWorkflowReconciliationSource : IWorkflowReconciliationSource` (`SourceKind =
  "git"`) MUST read version files from a local working clone and emit one
  `WorkflowVersionReconciliationModel` per version file.
- **FR-004**: The repo layout MUST be `{WorkflowsPath}/{definitionId}/versions/{semver}.json`
  (immutable canonical content, **no** name/description) + `{WorkflowsPath}/{definitionId}/definition.json`
  (mutable name, description, `deleted` flag). The git file's `state` MUST be `indent(canonical
  StateSource)`; content identity is the hash of its canonical (whitespace-stripped) form.
- **FR-005**: `SourceCreatedAt` MUST be the committer date of the commit that introduced each version
  file (`git log -1 --format=%cI -- {path}`).
- **FR-006**: Version upserts MUST obey Model X (`specs/002` FR-016). Until FR-016a persists a content
  hash, dedup MUST fall back to `(id, version)` existence + configured `DuplicateHandling`; a
  same-`(id, version)`-different-content case MUST be surfaced (logged now; throw once the hash
  persists).
- **FR-007**: `WorkflowVersionReconciliationModel` MUST gain an **optional `ContentHash`** (additive)
  so the source carries the canonical hash ahead of a persisted home.
- **FR-008**: The import reconciler MUST gain a **definition-metadata update path**: `definition.json`
  is the sole authority for an existing definition's name/description/`deleted`, applied every pass.
  Soft-delete MUST propagate as a flag; version files/rows MUST never be deleted.
- **FR-008a** *(carried-over defect from spec 087; rationale in [ADR 0034](../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md) D5)*:
  the already-landed apply (`WorkflowsVersionReconciler.UpdateDefinitionMetadata`, PR #546) currently runs
  for **every** incoming version entry, unconditionally and *before* the outdated-version skip — so an
  older/stale entry (or non-SemVer-ordered git version files) can overwrite current metadata with a prior
  version's values, order-dependently (violates D5 "latest-wins"). Beyond making `definition.json` the
  metadata authority (FR-008), this unit MUST **gate the per-version apply to the authoritative (newest)
  version** — moved after the outdated check and run only when
  `latestVersion is null || CompareOrdinal(candidateSortKey, latestVersion.SemVerSortKey) >= 0`. A test
  MUST assert an **older** incoming entry does **not** change existing definition metadata.
- **FR-009**: Export MUST be an **export reconciler** (set-diff sweep): for each catalog version,
  ensure `versions/{semver}.json` exists (write + commit if absent; skip if present). There MUST be
  **no** promotion domain event and **no** export commit trailer. Loop-avoidance MUST be structural.
- **FR-010**: Export MUST run only on a **Writer**-role node; **Consumer**-role nodes MUST NOT export.
  Export commits MUST use a machine identity (`Elsa Design <design@elsa.local>`, set per-invocation),
  message `Publish {name} v{version} ({definitionId})`, and MAY tag `wf/{definitionId}/v{version}`.
- **FR-011**: The single-writer invariant MUST be enforced by **fast-forward-only push** (no force;
  refuse on divergence) and the **Model X hash-mismatch tripwire**. An optional repo `writer.json`
  claim file MAY provide upfront operator-friendly rejection.
- **FR-012**: Clone modes MUST be role-based: **Writer** = persistent working copy, `fetch` + ff-only
  integrate, holds un-pushed commits, never `reset --hard`; **Consumer** = disposable mirror, `fetch`
  + `reset --hard origin/{Branch}`, read-only.
- **FR-013**: Credentials MUST be supplied out-of-band (SSH deploy key, credential helper, or a
  `Secret` token) via `CredentialsMode` (`SshKey` | `Token` | `HostDefault`), never on the git
  command line.
- **FR-014**: Configuration MUST be a concrete
  `WorkflowsDesignGitReconciliationFeature : WorkflowsDesignReconciliationFeature` (`[ShellFeature]`)
  binding from `CShells:Shells:{shell}:Features:{featureId}` with a `Role` (`Writer` | `Consumer`),
  mirroring the `ClrActivityReconciliation` precedent.
- **FR-015**: Git MUST NOT be registered as an `IWorkflowDefinitionStore`, MUST NOT be read on the
  runtime execution path, and MUST NOT introduce a Design→app or Design→runtime dependency.
- **FR-016**: Drafts MUST NOT be reconciled. An **optional** one-way WIP draft snapshot (to `drafts/`
  or a WIP branch) MAY be provided as a thin follow-on; it MUST NEVER be imported back.

### Key Entities *(include if feature involves data)*

- **GitWorkflowReconciliationSource** — `IWorkflowReconciliationSource` (`SourceKind = "git"`).
- **Export reconciler** — set-diff sweep making git's version files match the catalog (Writer only).
- **Version file** (`versions/{semver}.json`) — `indent(canonical StateSource)`; immutable; hashed.
- **`definition.json`** — mutable `{ name, description, deleted }`; latest-wins.
- **`WorkflowVersionReconciliationModel.ContentHash`** (new, optional) — canonical hash.
- **`WorkflowsDesignGitReconciliationFeature`** — CShells feature (repo/auth/role/export config).
- **`IGitClient` / `Elsa.Git`** (shared lib) — the single git stack.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: ✅ Met by `7275e0d8` — exactly one git wrapper (`Elsa.Git`) exists; EB rewired to it,
  module-internal wrapper deleted, EB tests 75/75 and arch guard 49/49 green.
- **SC-002**: The shared serializer emits byte-stable output for equal state across runs (proven by a
  determinism test), so equal state hashes identically.
- **SC-003**: N git version files reconcile into N `WorkflowDefinitionVersion` rows with correct
  SemVer + commit-time `SourceCreatedAt`; a rename/delete in `definition.json` propagates; a second
  pass adds zero rows.
- **SC-004**: The export reconciler makes git's file set equal the catalog's version set; a second run
  is a no-op; an export-then-import round-trip is a reconciliation no-op.
- **SC-005**: A simulated second writer produces a rejected ff-push or a loud import throw — never
  silent divergence.
- **SC-006**: No Design→app / Design→runtime dependency (structural guard); git is never queried
  during execution.

## Out of Scope / Non-Goals

- Git as a replacement operational store; git on the runtime read path.
- Multi-writer / git-first authoring (needs author-assigned versions — deferred).
- Branch-based collaborative draft authoring (separate ADR); reconciling drafts.
- The Extension Builder working-copy / per-user-branch model; source-deletion mirroring.
- Multi-tenant repo partitioning (v1 targets single/default tenant).

See [ADR 0034](../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md) for the
full decision log (D1–D11), alternatives, and constitution/arch-guard implications.
