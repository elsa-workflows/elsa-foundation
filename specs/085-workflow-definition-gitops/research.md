# Phase 0 Research — Workflow-Definition GitOps (085)

Consolidated design decisions. Each resolves an implementation unknown surfaced while mapping the
spec/ADR against the live seams. Format: **Decision · Rationale · Alternatives rejected**.

The spec (FR-001…FR-016) and [ADR 0034](../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md)
(D1–D11) own the *what* and *why-at-the-architecture-level*; this file resolves the *how* against the
concrete code that exists on `main` today.

---

## R1 — Project home & dependency envelope

**Decision.** New leaf project `src/Elsa/Workflows/Design/Reconciliation/Git/`
→ `Elsa.Workflows.Design.Reconciliation.Git.csproj`, mirroring the `Elsa.Activities.Design.Reconciliation.Clr`
precedent but nested under the workflow reconciliation domain root (same `Compile Remove="Git/**"`
glob treatment the base project already applies to `Core/**`). References only:
`Elsa.Workflows.Design.Reconciliation`, `Elsa.Workflows.Design.Reconciliation.Core`,
`Elsa.Workflows.Design.Core`, `Elsa.Workflows.Design.Persistence.Core`, `Elsa.Serialization.Core`,
`Elsa.Git`, `Elsa.Tasks.Core`, `CShells.Abstractions`, and the manifest-generator hint package.

**Rationale.** `Elsa.Git` is a true leaf (only DI/Logging abstractions), so referencing it keeps the
dependency-envelope guard clean (§E2.2, SC-006). No reference to `Elsa.Server`, the EB module, or any
`Elsa.Workflows.Runtime.*` — the source reads/writes files and upserts through the existing catalog
seam; git is never on the runtime path (FR-015).

**Alternatives rejected.** (a) Folding git into the base `Elsa.Workflows.Design.Reconciliation`
project — pollutes the base with a git dependency every reconciliation feature would inherit. (b) A
sibling top-level `Elsa.Workflows.Design.Git` — breaks the established "source-variant features nest
under the reconciliation root" convention (§2.5 inheritance seam).

## R2 — Feature shape: derive from the base, not a bare source feature

**Decision.** `WorkflowsDesignGitReconciliationFeature : WorkflowsDesignReconciliationFeature`
(`[ShellFeature(name: "WorkflowsDesignGitReconciliation")]`), overriding `Sources` to yield the git
source, and adding git/role/export config as bound `[ManifestSetting]` properties. `ConfigureServices`
calls `base.ConfigureServices(services)` (registers reconciler + options + universal handler), then
registers the git source, `IGitWorkspace`, the import startup task, and — only when `Role == Writer` —
the export reconciler + export startup task.

**Rationale.** FR-014 pins the base type. Unlike `ClrActivityReconciliationFeature` (a *standalone*
source feature that does **not** derive), the workflow git feature must also carry the reconciler
wiring; deriving from the base is exactly the sanctioned cross-feature structural coupling (§2.5). One
feature added to a shell fully activates GitOps.

**Alternatives rejected.** Bare `IShellFeature` + separate reconciliation feature — forces operators to
enable two features and re-derives the base's registrations by hand.

## R3 — Activating the dormant reconcile lifecycle

**Decision.** Register `services.AddScoped<IStartupTask, WorkflowsVersionReconcilerStartupTask>()` in
the **base** `WorkflowsDesignReconciliationFeature.ConfigureServices`. The export startup task is
git-specific and registered in the git feature (Writer only).

**Rationale.** The base registers the reconciler service + handler but **not** the startup task that
drives a pass — confirmed by grep: no `AddScoped<IStartupTask, …>` anywhere in the Reconciliation
project, matching the ADR's "the workflow reconciliation lifecycle is not wired into any shell." The
startup task *is* the lifecycle trigger and belongs with the reconciler registration, so any concrete
reconciliation feature (git today, file-system/CRM tomorrow) activates the pass. `TaskManager`
discovers `IStartupTask` from DI and runs it under a `[SingleNodeTask]` distributed lock — no extra
wiring. **Flagged for review:** this is a base-feature behavior change (dormant → active on enable);
it is the intended "composition-level change to validate" the ADR calls out (Consequence 4).

**Alternatives rejected.** Registering the import startup task only in the git feature — leaves the
base's lifecycle dormant for every future source and duplicates the registration per feature.

## R4 — Canonical content identity: round-trip, never naive whitespace-strip

**Decision.** Content identity = SHA-256 (hex) of the **compact** canonical serialization produced by
`IPayloadSerializer.Serialize(state)` (deterministic per spec 086 / #549). The on-disk file is the
**indented** form of that same string, produced by a pure-whitespace transform:
`JsonNode.Parse(compact)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true })`.

- **Export (write):** `state → Serialize → compact → JsonNode indent → file`.
- **Import (read):** `file → JsonNode.Parse → ToJsonString() (compact) → SHA-256` for `ContentHash`,
  **and** `IPayloadSerializer.Deserialize<WorkflowDefinitionState>(fileText)` for `State`.

**Rationale.** `hash(strip_ws(gitfile)) == hash(StateSource)` (ADR D3) holds only if "strip whitespace"
means *structural* re-serialization, not a byte-level `Regex.Replace(@"\s","")` — the latter corrupts
whitespace **inside** JSON string values (an activity display name "Send Email" would hash as
"SendEmail"). Parsing to `JsonNode` and re-emitting compact is the sound canonicalizer: it preserves
member order (the serializer already made it deterministic) and string contents, differing from the
compact form only in insignificant whitespace. `JsonNode` (not `ExpandoObject`) is the canonical
dynamic type per ADR 0035.

**Alternatives rejected.** (a) Byte-strip — unsound (above). (b) Hash the raw indented bytes —
whitespace-sensitive, so a formatter change breaks every hash. (c) A git-only serializer kept in sync
with the payload serializer — the exact eternal-sync trap D8 rejected.

## R5 — On-disk layout & the mutability split (D5)

**Decision.**
```
{WorkflowsPath}/{definitionId}/definition.json        # { name, description, deleted }  — mutable, latest-wins
{WorkflowsPath}/{definitionId}/versions/{semver}.json # indent(canonical State)          — immutable, hashed
```
`versions/*.json` carry **only** authored `WorkflowDefinitionState` (no name/description).
`definition.json` is the sole authority for name/description/`deleted`. `{semver}` is the raw
`Version` string (SemVer 2.0.0). Malformed/enoent `definition.json` → definition metadata defaults to
the version's own id and empty description with a diagnostic (non-fatal).

**Rationale.** Directly implements FR-004/FR-008 and D5. Keeping name/description out of version files
means a rename never mints a "different content" for an existing version (no false Model X conflict).

**Alternatives rejected.** Name/description embedded per version — the metadata-in-content coupling D5
explicitly splits.

## R6 — `SourceCreatedAt` from the introducing commit

**Decision.** `SourceCreatedAt = git log -1 --format=%cI -- {relativePathToVersionFile}` parsed as
`DateTimeOffset` (round-trip `O`/ISO-8601 with offset). Empty output (file not yet committed, e.g. a
freshly-written-then-imported working-tree file) → `null`.

**Rationale.** FR-005. `%cI` is strict-ISO committer date. Immutable version files never move, so
`-1` (newest touching the path) is the introducing commit. Uses `IGitClient.RunOrDefault` (read-only,
empty-on-failure) — no throw for the uncommitted edge.

**Alternatives rejected.** `%aI` (author date) — committer date is the catalog-relevant "when it
entered this history"; author date can predate cherry-picks/rebases.

## R7 — Working-copy management: `IGitWorkspace`, role-driven clone modes (D11)

**Decision.** A git-feature-internal `IGitWorkspace` (default `GitWorkspace`) owns the local clone and
exposes `Task<string> EnsureReadyAsync(ct)` returning the repo path, plus `RepositoryPath`. Called
lazily at the start of both the source's `Read` and the exporter's `Export` (mirroring the Clr source's
lazy-scan-per-call):

- **Absent clone dir** → `git clone --branch {Branch} --single-branch {RemoteUrl} {LocalCachePath}`.
- **Writer** → `git fetch origin {Branch}` then `git merge --ff-only origin/{Branch}`. A non-ff result
  (the D7 divergence signal) → the fetch/merge throws (`IGitClient.RunAsync` throws on non-zero);
  surfaced, **never** `reset --hard`, never a non-ff merge. Holds un-pushed local export commits.
- **Consumer** → `git fetch origin {Branch}` then `git reset --hard origin/{Branch}` (disposable mirror).

`LocalCachePath` defaults to `{hostDataDir}/gitops/{featureId-or-repo-hash}` when unset.

**Rationale.** FR-012/D11 verbatim. Lazy-ensure keeps the first reconcile pass self-seeding
(bootstrap, D11/edge-case) and re-runs pick up remote changes. `--single-branch` limits fetch cost.

**Alternatives rejected.** Ensuring the clone in a separate startup step ordered before reconcile —
adds cross-task ordering coupling; lazy-ensure inside `Read`/`Export` is simpler and matches the Clr
precedent.

## R8 — Credentials out-of-band, never on the command line (FR-013)

**Decision.** `CredentialsMode` ∈ { `SshKey`, `Token`, `HostDefault` }, applied once into the clone's
**git config** during `EnsureReadyAsync` (before any network op), so nothing secret rides argv:

- `SshKey` → `git config core.sshCommand "ssh -i {KeyPath} -o IdentitiesOnly=yes"` (`KeyPath` is a
  path, not a secret).
- `Token` → write a 0600 credentials file (`https://x-access-token:{token}@{host}`) under the cache
  dir and `git config credential.helper "store --file={path}"`; the `Token` property is
  `[ManifestSetting(Secret = true)]`.
- `HostDefault` → no-op; rely on the ambient credential helper / ssh-agent / deploy key.

`GIT_TERMINAL_PROMPT=0` (already in `GitClient`) guarantees fail-fast on any missing credential.

**Rationale.** FR-013 "never on the git command line." `IGitClient.RunAsync` exposes no per-call env
override, so `GIT_SSH_COMMAND`/header-via-argv are unavailable/undesirable; writing into the clone's
own `.git/config` is the portable, secret-safe path and needs **no** change to the shared `Elsa.Git`
lib (FR-001 "just add a ProjectReference").

**Alternatives rejected.** (a) `-c http.extraHeader="AUTHORIZATION: bearer …"` — token visible in
`ps`/argv. (b) Extending `IGitClient` with an env-bag — out of scope; the shared lib stays untouched.

## R9 — FR-008a: gate metadata apply to the newest version (the carried-over defect)

**Decision.** In `WorkflowsVersionReconciler.ReconcileVersion`, **move** the `UpdateDefinitionMetadata`
call to *after* the outdated-version skip. New order: find definition → compute `candidateSortKey` +
`FindLatestVersionAsync` → **if `latest is not null && CompareOrdinal(candidateSortKey, latest.SemVerSortKey) < 0` → skip (return, touching nothing)** → then, for the surviving newest-or-equal
candidate: create definition if absent, else `UpdateDefinitionMetadata`. A new test asserts an **older**
incoming entry (persisted `2.0.0`, incoming `1.0.0` + rename) leaves definition metadata unchanged.

**Rationale.** Passing the outdated-skip guarantees
`latest is null || CompareOrdinal(candidate, latest) >= 0` — exactly FR-008a's gate — so relocation
*is* the gate (no extra condition needed). Fixes the D5 "latest-wins" violation where a stale entry,
applied unconditionally before the skip (current line 60, before the check at 63–70), overwrites
current metadata. Existing tests stay green: `Metadata_update_does_not_add_or_alter_versions`
(incoming 2.0.0 > persisted 1.0.0) is the newest case and still applies.

**Alternatives rejected.** Adding a redundant explicit `>=` condition around the apply while leaving it
before the skip — duplicates the comparison and is easy to desync from the skip; relocation is DRY.

## R10 — Soft-delete propagation without bloating State (FR-008, US2 #4)

**Decision.** Thread a definition-level `deleted` flag through the reconciliation seam (never into
`WorkflowDefinitionState`):

1. `WorkflowVersionReconciliationModel` gains `bool Deleted = false` (additive).
2. `IWorkflowDefinition` (Design.Core) gains read-only `DateTimeOffset? DeletedAt { get; }` — a
   definition-level lifecycle timestamp, same category as the `Name`/`Description`/`CreatedAt` it
   already exposes (NOT authored content, so §E2.9 is untouched).
3. `IWorkflowDefinitionFactory.Create` gains `bool deleted = false`; the read-model sets
   `DeletedAt = deleted ? <stamped> : null`. The reconciler stamps the actual time (`TimeProvider`),
   so the factory carries a boolean intent and the entity carries the timestamp.
4. `WorkflowVersionsReconcilingHandler` passes `entry.Deleted` to the factory.
5. `UpdateDefinitionMetadata` widens its idempotent diff to reconcile `DeletedAt`: set when incoming
   is deleted and persisted is live; clear when incoming is live and persisted is soft-deleted
   (`definition.json` is latest-wins authority → un-delete is legal). Gated to newest (R9).
6. `WorkflowDefinition.From` reads `source.DeletedAt` off the interface for the fresh-create path.

Version rows are never deleted (retention authority).

**Rationale.** FR-008 requires soft-delete to "propagate as a flag." The facade already carries
definition-level metadata; a nullable `DeletedAt` is the minimal, category-correct addition and keeps
the flag out of `State`. The git source reads `definition.json.deleted` → `model.Deleted`.

**Alternatives rejected.** (a) `Deleted` on the facade as a bare bool — loses the audit timestamp the
entity already models (`DeletedAt`/`DeletedReason`). (b) Widening the domain event to carry
reconciliation models instead of entity pairs — a far larger change to the shared seam.

## R11 — Export reconciler: set-diff sweep, structural loop-avoidance (D4/FR-009)

**Decision.** `GitWorkflowExportReconciler.Export(ct)` (Writer only): `EnsureReadyAsync` →
`definitionStore.ListAsync(all)` → per definition `versionStore.ListByDefinitionAsync` → for each
version, if `versions/{semver}.json` **absent**, write `indent(canonical State)` (R4); write/update
`definition.json` when its `{name,description,deleted}` differs from disk. Stage only the explicit
files touched, commit per the D-config identity/message, optional tag `wf/{definitionId}/v{version}`.
`PushMode == Immediate` → `git push --ff-only origin {ExportBranch}` (refuse on divergence, no force);
`Manual` → local only. Present files are skipped (idempotent).

**Rationale.** FR-009/FR-010/FR-011/D4. Immutability + "write-only-if-absent" (export) composed with
"upsert-only-if-(id,version)-absent" (import) gives ping-pong-free round-trips with no trailer/event.
Staging only touched files honors the shared-worktree caution (never `git add -A`).

**Alternatives rejected.** A promotion domain event driving export — D4 rejects it; the set-diff sweep
captures all locally-authored versions regardless of creation path.

## R12 — Export trigger: a second `[SingleNodeTask]`, Writer-only

**Decision.** `GitWorkflowExportStartupTask : IStartupTask` (`[SingleNodeTask] [Order(3)]`), registered
only when `Role == Writer`, acquiring its own distributed lock and calling
`IGitWorkflowExporter.Export`. Ordered after the import task (`[Order(2)]`) so a bootstrap import
precedes the first export.

**Rationale.** Mirrors `WorkflowsVersionReconcilerStartupTask`. Single-node + lock keeps the single
writer honest even under multi-instance hosting. v1 triggers on startup; an on-demand endpoint is a
later thin follow-on (out of scope).

**Alternatives rejected.** A recurring background task — startup-pass parity with import is enough for
v1 and avoids a scheduling dependency.

## R13 — Model X hash tripwire without a persisted hash (FR-006, US4 #2/#3)

**Decision.** On the duplicate `(id, version)` path, the reconciler loads the existing version (its
`[NotMapped] State` is hydrated by the read-store loading handler), serializes both the incoming
`version.State` and the existing `State` via `IPayloadSerializer`, and compares. Mismatch → **log a
warning now** (`DuplicateHandling.Throw` still throws as today; a dedicated
hash-mismatch throw is deferred to when FR-016a persists the hash). `ContentHash` is added to
`WorkflowVersionReconciliationModel` (FR-007) and populated by the git source for its future
persisted home; the tripwire recomputes rather than depending on the facade carrying the hash.

**Rationale.** FR-006 "surfaced (logged now; throw once the hash persists)." The reconciler already has
`State` on both sides, so no `IWorkflowDefinitionVersion` contract change is needed for the tripwire —
only `IPayloadSerializer` + a by-definition version lookup are added to the reconciler. Keeps FR-016a a
genuine soft dependency.

**Alternatives rejected.** Adding `ContentHash` to `IWorkflowDefinitionVersion` to thread the source's
hash into the reconciler — an avoidable Design.Core contract change; recompute-from-State is
self-contained.

## R14 — Drafts stay out (D6/FR-016)

**Decision.** Nothing in the source or exporter reads or writes `drafts/` or any draft entity. The
optional one-way WIP snapshot is **not** built in v1 (noted as a thin follow-on).

**Rationale.** FR-016/D6. Keeps the reconciliation boundary clean.

---

## Open items carried into implementation (not blockers)

- **FR-016a (soft dep):** persisted provenance/content-hash fields are Unit D's allocation; v1 ships
  coarse `(id, version)` dedup + the recompute tripwire (R13). When they land, R13's warning becomes a
  throw and `ContentHash` gets a persisted home.
- **Multi-tenant repo partitioning:** v1 targets the single/default tenant (spec non-goal).
- **Base-feature activation (R3):** the QA gate must confirm no shell that only wanted the *base*
  registrations silently starts reconciling — mitigated because the base is abstract and only reached
  through a concrete `[ShellFeature]` an operator explicitly enables.
