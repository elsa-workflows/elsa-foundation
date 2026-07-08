# Workflow Definitions Reconcile From And Export To Git

Status: proposed (2026-07-07; free-flow design. Sharpened through a grilling pass — the decisions
below (D1–D11) supersede the first draft's bidirectional/multi-environment framing.)

Program goal: `none/free-flow`. GitOps for workflow definitions is not owned by an existing bucket
(see [Groundwork Persistence Readiness](../program-goals/groundwork-persistence-readiness.md),
[Workspace Split Readiness](../program-goals/workspace-split-readiness.md)). Promote to a bucket only
if it grows into a mid-term coordination surface.

Git is a **reconciliation source and an export sink layered on the existing operational catalog**,
not a replacement backing store. Immutable workflow-definition *versions* flow between git and the
catalog; the catalog (EFCore / Groundwork) stays the single runtime read path and git is never on the
runtime hot path.

## Context

A 2026-07-07 feasibility pass accepted "git as a GitOps source/sink reconciled into the catalog" and
rejected "git as a replacement operational store" (git is a poor OLTP store: no cross-store txn,
per-session working copies for concurrency, no efficient `ListAsync(filter)`, commit latency vs draft
autosave). The reconciliation seam already exists and is empty:
[`IWorkflowReconciliationSource`](../../src/Elsa/Workflows/Design/Reconciliation/Contracts/IWorkflowReconciliationSource.cs)
names "git" in its doc, the abstract
[`WorkflowsDesignReconciliationFeature`](../../src/Elsa/Workflows/Design/Reconciliation/WorkflowsDesignReconciliationFeature.cs)
is built to be extended by source-variant features, and
[`specs/002`](../../specs/002-workflow-state-scope/spec.md) lists git as a trusted external source —
but no concrete git source exists yet, and the workflow reconciliation lifecycle is not wired into
any shell today ([`shells.baseline.json`](../../src/Apps/Elsa.Server/shells.baseline.json) enables
`ActivitiesDesignReconciliation` only).

**Do not justify this on version history.** Elsa already has an immutable SemVer version model
([`WorkflowDefinitionVersion`](../../src/Elsa/Workflows/Design/Persistence/Core/Entities/WorkflowDefinitionVersion.cs),
write-once `StateSource`). Git's incremental value is narrower and real: **PR/diff review** of
authored workflows, cross-environment **distribution/promotion**, an out-of-DB **canonical record**,
and portable/offline authoring.

**The reconciliation policy this must obey (Model X / FR-016, `specs/002`).** Lookup a candidate by
`(id, version)`; absent → create with immutable provenance; present + matching content hash →
skip-or-throw per config; present + **mismatched** hash → **throw** (same logical identity must mean
same content; a mismatch means the source is broken). Versions are never deleted.

This ADR was sharpened through a design grilling; the decisions are recorded as D1–D11 below.

## Decisions

### D1 — Two authorities, never a merge

"Source of truth" is retired as overloaded (the glossary already avoids it). Authority splits in two:

- **[Content authority](../glossary/elsa.md)** — the source (git). For a given `(definitionId,
  version)`, git's *canonical content* (see D3) is the version's content; the catalog never rewrites
  it.
- **[Retention authority](../glossary/elsa.md)** — the catalog. An append-only, write-once ledger a
  source can add to but never mutate or delete from (source disappearance is informational only).

Because a version is immutable, a fixed `(id, version)` is single-valued everywhere, so the two
authorities never contend. The only cross-authority event possible is "same `(id, version)`,
different canonical content" — which is definitionally a **broken source** (Model X throws), not a
conflict to merge. **There is no merge, ever.**

### D2 — Single-writer topology is a v1 invariant

Version numbers are **system-assigned per catalog**: `PromoteDraftToVersion` calls
`WorkflowVersionNumbering.NextMajor(localLatest)` (`= {localMajor+1}.0.0`), computed off the local
catalog's latest. (Note the live code contradiction: `WorkflowDefinitionVersion.cs:13` documents
`Version` as "author-controlled," but promotion ignores author choice and auto-assigns.) So
`(definitionId, version)` is content-stable **only within one catalog**: two environments each
promoting a different draft for the same definition both mint `v2.0.0` with different content, which
D1 would (wrongly) flag as a broken source.

Therefore **v1 assumes exactly one catalog that promotes-and-exports; every other environment imports
read-only** (`Export.Enabled = false`). Multi-writer / git-first authoring is **deferred**: it
requires **author-assigned version numbers** (aligning with the entity's own doc comment) plus a
uniqueness/monotonicity gate — a separate, larger change on Unit D's territory.

### D3 — Canonical serialization is content identity (hard prerequisite)

The feature exists for diff review, but `StateSource` is emitted by `payloadSerializer.Serialize`
with no `WriteIndented` — **compact, single-line JSON** with **unstable key order** (dictionaries in
enumeration order; the polymorphic converter injects a discriminator). Raw-blob-on-disk gives exact
hashing but unreviewable one-line diffs; naive pretty-printing gives reviewable diffs but unstable
hashes.

Resolution: **content identity is defined over a canonical serialization** (deterministic key
ordering + normalized formatting), not the raw stored blob. Concretely (D8), make the **shared
payload serializer deterministic** so `StateSource` *becomes* the canonical hash preimage; the git
file is `indent(StateSource)` — a pure whitespace transform, so `hash(strip_ws(gitfile)) ==
hash(StateSource)` exactly. A **deterministic canonical serializer is a hard prerequisite**: without
it, both the hash and the "no false conflicts" guarantee are unsound.

### D4 — Export is a reconciler, not an event

There is **no** promotion event and **no** `Elsa-Export` commit trailer. Export is the **mirror of
import**: an **export reconciler** that makes git's file set match the catalog's version set. For
each catalog version, ensure `versions/{semver}.json` exists; present → skip; absent → write +
commit. It is set-based, idempotent, and trigger-agnostic, and it captures *all* locally-authored
versions regardless of creation path (promote, submit, import-from-elsewhere).

**Loop-avoidance is structural**, needing no provenance: because versions are immutable, export only
writes files that are absent and import only upserts `(id, version)` absent from the catalog. A
git-sourced version is already a file → export skips it; a locally-authored version isn't a file yet
→ export writes it once, then skips forever. The two sweeps compose with no ping-pong.

### D5 — Split the on-disk model along the mutability seam

A `WorkflowDefinition` has *mutable* `Name`/`Description`/soft-delete that change without minting a
version. A metadata-update path has since landed (`WorkflowsVersionReconciler.UpdateDefinitionMetadata`,
PR #546), but it applies metadata from *every* version entry rather than from a single mutable source —
so rename/soft-delete propagate order-dependently (see the carried-over defect below). Split the layout
so a single mutable file, not per-version content, drives definition metadata:

```
{WorkflowsPath}/                       # default: "workflows"
  {definitionId}/
    definition.json                    # MUTABLE metadata: name, description, deleted flag — latest-wins
    versions/
      1.0.0.json                       # IMMUTABLE canonical content only; no name/description; hashed
      2.0.0.json
    drafts/                            # OPTIONAL, one-way WIP snapshots (D6) — never imported
```

- **`versions/{semver}.json`** = pure versioned canonical content (D3), immutable, content-authoritative.
- **`definition.json`** = mutable definition-level metadata (name, description, `deleted`), latest-wins,
  re-committed on change, **not** part of any version's content identity.
- The import reconciler's definition-metadata update path (already landed, PR #546) MUST be **narrowed
  to `definition.json`** (applied every pass) instead of per-version content. Soft-delete propagates as
  a **flag**, never a file deletion (consistent with retention authority).

> **Carried-over defect (spec 087 → fix in spec 085, FR-008a).** The generic prerequisite landed
> (`WorkflowsVersionReconciler.UpdateDefinitionMetadata`, PR #546) applies name/description from *every*
> incoming version entry, unconditionally and *before* the outdated-version skip — so a stale/older
> entry can overwrite current metadata, order-dependently (breaks "latest-wins"). The proper fix is this
> decision: `definition.json` is the **sole** metadata authority (not per-version), with a latest-only
> gate as defense-in-depth for any per-version fallback.

### D6 — Drafts stay out of reconciliation

A draft is mutable, has no stable content identity, is discarded routinely, and is
many-per-definition/multi-author — the exact opposite of a version on every axis D1–D3 rely on.
Round-tripping drafts through the version reconciler would re-open the rejected operational-store
proposal and violate single-writer. So:

- **v1: drafts do not cross the git boundary** (they stay operational-only).
- **Thin opt-in follow-on:** a **one-way WIP snapshot** — commit the current draft state to
  `drafts/` (or a WIP branch) on demand for backup / work-in-progress review. **Never imported back**;
  git is not the draft's store. Outside the reconciler.
- **Separate future ADR:** branch-based collaborative draft authoring (draft ≈ branch, edit
  round-trips through git, promote = merge). That is "Extension Builder for definitions" — the
  working-copy/branch/conflict model this unit scopes out.

### D7 — Single-writer is enforced by ff-only push + hash tripwire

The invariant has teeth without new machinery:

- **Gate — fast-forward-only push.** The writer pushes ff-only (never force). If a second writer
  already pushed, git *rejects* the non-ff push — git's native ref semantics enforce "one writer wins
  at the remote."
- **Tripwire — Model X hash mismatch.** A blocked second writer that pulls then holds a divergent
  `(id, version)` between git and its catalog; the next import **throws** at the reconciliation site.

A single-writer violation can therefore only surface as a **rejected push** or a **loud import
throw** — never silent divergence. An optional repo **claim file** (`writer.json` naming the
authoritative writer) is operator-friendly hardening, not load-bearing.

### D8 — Make the shared payload serializer deterministic

Rather than a git-only renderer kept eternally in sync, make the **shared** serializer deterministic
(sort dictionary keys; fix discriminator placement), so `StateSource` is the canonical hash preimage
and the git file is its indented form (D3). Determinism is a latent requirement anywhere Elsa hashes
serialized state (activity reconciliation already relies on a content hash), so it pays off
system-wide. **No migration / backward-compat work** — this is unreleased software; the serializer
simply becomes deterministic.

### D9 — One shared `Elsa.Git` library ✅ DONE

**Landed 2026-07-07 (commit `7275e0d8`, "refactor(git): extract shared Elsa.Git library from
ExtensionBuilder"), pending merge into this branch/main.** The shared **public `Elsa.Git`** library
now exists at `src/Elsa/Git/`, holding `IGitClient` + `GitClient` + `GitClientOptions` +
`AddGitClient()` (DI extension) together — a thin §2.17 mechanical utility (shells out to git,
`GIT_TERMINAL_PROMPT=0`, zero domain deps). Contract+impl in one lib (the strict `.Core`+impl split of
ADR 0033 is overkill for a ~100-line utility). It is a **true leaf project** (`net10.0`, only
`Microsoft.Extensions.DependencyInjection.Abstractions` + `Logging.Abstractions` — no Elsa coupling),
so any layer may reference it and the dependency-envelope guard stays clean.

`IGitClient` exposes exactly the three original methods (`RunAsync`, `RunOrDefault`,
`IsGitRepository`). DI consumers call `AddGitClient(...)` (registers `IGitClient` as a singleton);
consumers needing a per-repository executable (Extension Builder, whose git path is per
`ExtensionBuilderOptions`) construct `new GitClient(gitExecutable, logger)` directly. The Design-layer
git feature resolves `IGitClient` via `AddGitClient`.

**Correction to the framing above:** at design time the wrapper was reported as `internal sealed` in
the `Elsa.Server` app, but the Extension-Builder-to-module refactor had already relocated it to
`Elsa.Modularity.ExtensionBuilder`; this extraction pulled it from there into `Elsa.Git`. Either way
the "not referenceable from the Design layer" blocker is now gone.

### D10 — Sequence `Elsa.Git` behind the ExtensionBuilder module refactor ✅ RESOLVED

The ExtensionBuilder module refactor owned GitClient's relocation, so a parallel extraction would
have collided. This unit sequenced behind it and coordinated the landing spot; the refactor landed
GitClient in the shared public `Elsa.Git` lib (D9) rather than module-internal, and rewired the EB
module to `IGitClient` (deleting the old module-internal `GitClient`) — serving both consumers in one
move. **The coordination succeeded**: the GitOps unit's original FR-001 (extract the client) is
**obviated** — the Design.Reconciliation feature now only adds a `<ProjectReference>` to `Elsa.Git`.
Validation reported green (75/75 Extension Builder tests, 49/49 architecture guard).

### D11 — Asymmetric roles, two clone modes

The git↔catalog flow is **asymmetric by role**, a system property, not a per-node round-trip:

- **Writer**: authors in Studio → **exports** (D4). It **imports** at **bootstrap** (seed a fresh
  authoring catalog from an existing repo) and idempotently thereafter (re-seeing its own exports →
  skip). It does not author via git (that is the deferred git-first path, D2).
- **Consumer**: **imports** git → catalog, read-only; never exports.

Each role gets its own clone mode:

- **Writer clone** — a **persistent working copy** on the export branch: `fetch` + **ff-only**
  integrate (a non-ff divergence *is* the D7 violation signal → stop, never merge); holds local
  export commits until pushed (per `PushMode`); never `reset --hard`.
- **Consumer clone** — a **disposable mirror**: `fetch` + `reset --hard origin/{branch}`; read-only.

## How the pieces map to existing seams

- **Inbound source** = `GitWorkflowReconciliationSource : IWorkflowReconciliationSource`
  (`SourceKind = "git"`), read from the working clone, emitting `WorkflowVersionReconciliationModel`
  entries (`State = Published`, `SourceCreatedAt` from `git log -1 --format=%cI -- {path}` — the
  commit that introduced the immutable version file). The existing
  [`WorkflowVersionsReconcilingHandler`](../../src/Elsa/Workflows/Design/Reconciliation/Handlers/WorkflowVersionsReconcilingHandler.cs)
  turns each model into the entity pair — no custom handler.
- **`WorkflowVersionReconciliationModel` gains an optional `ContentHash`** (additive) so the source
  carries the canonical hash (D3) ahead of a persisted home; the reconciler enforces full Model X the
  moment FR-016a (Unit D) gives the entity a place to store it. FR-016a is a **soft** dependency.
- **Config** mirrors the `ClrActivityReconciliation` precedent: a concrete
  `WorkflowsDesignGitReconciliationFeature : WorkflowsDesignReconciliationFeature` (`[ShellFeature]`),
  binding from `CShells:Shells:{shell}:Features:{featureId}`:

```jsonc
"WorkflowsDesignGitReconciliation": {
  "RemoteUrl": "git@github.com:acme/workflows.git",
  "Branch": "main",
  "WorkflowsPath": "workflows",
  "LocalCachePath": "",              // defaults under the host data dir
  "Role": "Consumer",                // Writer | Consumer  (drives clone mode + export, D11)
  "CredentialsMode": "SshKey",       // SshKey | Token | HostDefault
  "Token": "",                        // [ManifestSetting(Secret=true)] — Token mode only
  "Export": { "PushMode": "Manual", "Branch": "", "Tag": true },   // honoured only when Role=Writer
  "Options": { "DuplicateHandling": "Skip" }   // inherited WorkflowVersionReconcilerOptions
}
```

- **Auth**: `GIT_TERMINAL_PROMPT=0` (already in `GitClient`); credentials out-of-band (SSH deploy key,
  credential helper, or a `Secret` token) — never on the command line.
- **Commit shape** (writer export): machine identity `Elsa Design <design@elsa.local>` set
  per-invocation with `-c user.name/-c user.email` (as Extension Builder does); message `Publish
  {name} v{version} ({definitionId})`; optional tag `wf/{definitionId}/v{version}`. **No** export
  trailer (D4).

## Scope boundaries and non-goals

- **NOT a replacement operational store.** Git is a source + export sink over the catalog; it never
  becomes an `IWorkflowDefinitionStore`, and it is never read on the runtime execution path.
- **NOT the Extension Builder git stack.** EB stores extension **.NET source**; this stores
  definition **JSON**. Reused: the **`GitClient` wrapper** (via `Elsa.Git`, D9) and EB's *safety
  conventions* (non-destructive ops, ff-only, preflight, machine identity) — **not** its working-copy
  / branch / collaboration model.
- **NOT multi-writer / git-first authoring** (D2), **NOT branch-based draft authoring** (D6), **NOT a
  source-deletion mirror** (D1/D5).

## Consequences

The empty reconciliation seam gets its first concrete workflow source; Elsa gains reviewable,
distributable, out-of-DB GitOps for workflow definitions with the runtime read path untouched and
loop/conflict handling reduced to structural properties (immutability + ff-only + hash). The costs
are three prerequisites (one already satisfied) and one behavior change:

1. **`Elsa.Git` extraction** — ✅ **done** (commit `7275e0d8`, D9/D10); the feature now just references it.
2. **Deterministic shared serializer** — the real weight of the "canonical" prerequisite (D3/D8).
3. **Import-reconciler definition-metadata update path** — new capability (D5).
4. Enabling the feature **activates the dormant workflow reconciliation lifecycle** for the first
   time — a composition-level change to validate.

## Open questions and sequencing

- **FR-016a (soft dependency).** Ship v1 on coarse `(id, version)` dedup; add the optional
  `ContentHash` now; enforce full Model X when Unit D allocates the persisted provenance/hash fields.
- **Multi-writer / git-first authoring (deferred).** Needs author-assigned versions + a
  uniqueness/monotonicity gate (D2). Separate unit.
- **Branch-based collaborative draft authoring (deferred).** Separate ADR (D6).
- **Multi-tenant repo layout.** `WorkflowDefinitionVersion` is a `TenantEntity`; per-repo vs
  per-tenant-subtree is deferred (v1 targets single/default tenant).
- **Constitution / arch-guard.** The git feature references only the Design reconciliation feature +
  `Elsa.Git`, no app/runtime deps (dependency-envelope guard). `Elsa.Git` is a §2.17 utility in its
  own low-level lib. The deterministic-serializer change must be validated against serialization
  snapshot tests.

## Follow-up

- Spec: [`specs/085-workflow-definition-gitops`](../../specs/085-workflow-definition-gitops/spec.md).
- Prerequisites: (1) `Elsa.Git` — ✅ done (commit `7275e0d8`); (2) deterministic shared
  payload serializer; (3) import-reconciler definition-metadata update path.
- Cross-references: `IWorkflowReconciliationSource`; `WorkflowsDesignReconciliationFeature`;
  [`specs/002` Model X / FR-016 / FR-016a](../../specs/002-workflow-state-scope/spec.md);
  `WorkflowDefinitionVersion`; glossary [Content authority / Retention authority](../glossary/elsa.md);
  Extension Builder ADRs 0001–0019 (git identity, safety envelope, branch model, conflict handling).
