# Research: Post-Finalization Backfill

Inventory behind [spec.md](./spec.md). Everything here was read from the tree on 2026-09-27, with PR #2109's specs 180
to 183 merged in. Paths are repository-relative. Sections are cited by name, not line.

## The post-migration seam, and why it does not fit

| Piece | Where | What it does |
|---|---|---|
| Contract | `src/essentials/Persistence/EntityFramework/IEfPostMigrationAction.cs` | `Id`, `Kind`, `RequiredWhen`, `Audit`, `AuditAsync(DbContext)` (read-only), `RunAsync(DbContext)`. Its remarks: no dependency injection, because the `dotnet elsa persistence` worker has no shell container; `RunAsync` is reached only from `post-migrate`. |
| Declaration | `EfModuleAttribute.PostMigration` (`src/essentials/Persistence/EntityFramework/EfModuleAttribute.cs`) | Types with a public parameterless constructor, instantiated by `EfPostMigrationActions.Create`. |
| Audit at activation | `src/essentials/Persistence/EntityFramework/EfModuleMigrator.cs`, `EfPostMigrationActions.EnsureNotRequiredAsync` | Registered as a shell initializer at `LifecyclePhase.Prepare`. After applying or validating migrations it audits every declared action and throws `EfPostMigrationRequiredException` when one is required, so the shell does not activate. Spec 171, FR-056: under both migrate policies. |
| Enable-time guard | `src/essentials/Modularity/EntityFramework/EfPendingMigrationActivationGuard.cs` | Checks pending migrations, not actions; names `post-migrate` in its refusal for a module that declares actions. |
| The one instance | Secrets' projection reindex (spec 171, FR-057) | Rewrites rows a changed projection algorithm left stale, out of process. |

The deadlock, traced through these pieces: a backfill declared as an action would audit as required whenever a row
below the finalized version exists; `EfModuleMigrator` would then refuse the module at Prepare; spec 181 finalizes only
from a counted member with the module active (FR-005), or, on a single host, during that same Prepare-phase check
(FR-021); and the backfill may run only after finalization. Each waits on the next. Spec 180's Decisions (Q4) and #2116
record the same conclusion.

## Families, and where content-addressed rows live

Fifteen stamped families across three EF modules (spec 180's research, "Current state"). The ones holding rows written
once and rarely or never again:

- `RuntimeArtifactEfModule` (`src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeArtifactEfModule.cs`)
  stamps five tables: `elsa_runtime_workflow_executable`, `elsa_runtime_workflow_executable_coordination`,
  `elsa_runtime_executable_activity_template`, `elsa_runtime_executable_activity_template_hash_claim` and
  `elsa_runtime_workflow_executable_source_reference`. Executables and executable activity templates are
  content-addressed (ADR 0038); source references are per-publish facts and can be rewritten.
- `PublishingLedgerEfModule.ContentSchemaVersion` stamps activity-publication receipts and draft test runs, which are
  written once.
- Finished executions' runtime state (the other runtime families) is written for the last time when the execution
  ends.

Once spec 180's FR-026 stamps the ten unstamped EF modules and two Publishing tables in their 4.0 baselines (#2119),
they become families too, and each needs a rewriter (FR-004) before its first version bump.

**2026-09-28 note.** [#2119](https://github.com/elsa-workflows/elsa-foundation/issues/2119) (PR
[#2131](https://github.com/elsa-workflows/elsa-foundation/pull/2131)) landed after this inventory was read from the
tree: every EF module now stamps a schema family, taking the total from the fifteen named above to twenty-six.
Whether the ten-and-two named here still need the rewriter this section calls for, or already have one, should be
checked against what #2131 shipped before FR-004 is scoped.

## What the spec relies on from specs 180 to 183

| Need | Where it is specified |
|---|---|
| Read path: version check before content, integrity per stamped version, chain | spec 180, FR-006 to FR-012 |
| Write path: stamp the write version, never lower a stamp, check before overwrite, compare-and-set | spec 180, FR-013 to FR-018 |
| Opaque version labels, readable set, contiguity | spec 180, FR-004 and FR-005 |
| Identity-preserving upcasters; identity-changing formats sit alongside | spec 180, FR-020 and FR-028 |
| Retirement needs a scan, assigned here | spec 180, FR-024 |
| Finalization record, per database and family, in the 4.0 baseline | spec 181, FR-001 and FR-002 |
| Monotonic finalization | spec 181, FR-003 |
| Counted members; refresh interval (15 s) | spec 181, Terms; FR-010 |
| Refusal at activation and at enable time | spec 181, FR-015 and FR-016 (the latter narrowing spec 171's FR-069, amended with this spec) |
| Completeness requirement and the shared check | spec 182, FR-003 and FR-005 |
| Readability entry with database identity | spec 183, FR-019 |
| Expiry 30 s, skew 5 s | spec 183, FR-006 |

## Selecting rows by stamp

Stamp columns are named `SchemaVersion` and hold opaque labels (`"1.0.0"`, `"1"`). The chain's declaration lists the
versions before the finalized one, so a selection is `SchemaVersion IN (...)`, with no parsing. None of the stamped
tables' configurations under `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Configuration` or the
Publishing and Elsa 3 import equivalents declares an index on that column today, so every selection is a table scan.
Q27 asks whether to add one in the 4.0 baselines.

## The straggler window

Why a row below the finalized version can appear after finalization, in the spec's terms:

- A host that has not refreshed writes its observed version for up to one refresh interval (spec 181, User Story 1,
  scenario 3). The settle condition waits for every counted member to report having observed the new version.
- A member that lapsed from membership keeps writing at its last observed version (spec 181, FR-018 as narrowed by Q22).
  It still refreshes the finalization record from the module's own database, so it switches once it can reach that
  database. While it is counted, the settle condition waits for it; once it has expired, it is no longer counted.
- A writer that chose its write version and then stalled before committing, past the settle margin, can commit a row
  below the completion version. No lease closes this (spec 183, "Failure modes"). The audit is what makes it loud.

## Attention and status

- Spec 182's FR-010 adds an Attention contributor for dormancy and for spec 181's write refusals; FR-018's critical item
  belongs beside those.
- Spec 181's FR-022 defines the gate's status per family, printed by the CLI's status subcommand (FR-020); FR-021 of
  this spec adds the backfill's part.

## Noticed and left alone

- Spec 180's FR-014 says no "background task rewrites rows as a side effect". The backfill is a background task whose
  purpose is rewriting rows through the write path, and spec 180's own FR-024 and Q4 assign that work here. The
  wording could say so explicitly when spec 180 is next edited; this spec records the reading in its Decisions.
