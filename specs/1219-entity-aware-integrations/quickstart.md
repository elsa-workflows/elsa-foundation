# Catalog unit qualification guide

**Status:** required future validation, not runnable product instructions or passing evidence.
**Scope:** the approved catalog unit in [plan.md](plan.md), using [data-model.md](data-model.md) and [the catalog contract](contracts/catalog-disclosure.md). Do not report the full entity feature delivered from these checks.

## Prerequisites and ownership

Use an isolated implementation worktree and an owned fresh format-2 catalog database. Check the live main-red tracker, current branch/claim and required review state before integration. Use the normal PATH dotnet/build queue; inspect machine load before diagnosing timing failures. Keep existing hosts/stores untouched.

The ordinary owned host composes Disclosure, its lifecycle/Events dependencies, actual stores and the existing Design/Publishing APIs explicitly. A separate protected fixture composes two scoped source registrations under one provider and a current-identity test policy. This is a synthetic security fixture, not a production entity provider or credential profile.

Record exact source commit, build/provenance, provider and store paths, host PID/port and feature configuration. Keep credentials/configuration/databases private; retain only allowlisted source/results/screenshots/traces. Stop only a verified owned process and retain cleanup receipts.

## Migration profile

The implementation uses the repository's pre-GA fresh-store policy. From the isolated worktree, the existing filter regenerates only ActivitiesDesign's four provider Initials:

    bash tools/ef/generate-module-migrations.sh 'ActivitiesDesign(Sqlite|SqlServer|PostgreSql|MySql)DbContext'

This is an implementation command, not an instruction to run it against any existing database. Build the changed model/tooling through the shared queue, inspect all generated migrations/Designers/snapshots, and create fresh qualification stores. The WorkflowsDesign incremental flag is unrelated.

Prove format-2 startup, publication, classification guards and same-store reopen for each affected provider. Prove a schema-1 reader/activation profile refuses format 2 and the new build refuses unsupported schema-1 material. Keep old fixture files unchanged. No old-database upgrade or imported ActivitiesDesign format-1 replay is a passed result. PublishingLedger activity-publication receipt members/stamp 1 and flat PublishingSnapshotReview remain unchanged; no standalone old-ledger refusal follows from the catalog stamp.

## Evidence sequence

| Step | Observe and retain |
|---|---|
| Model and admission (C01–C02) | Zero/default/unknown decisions unavailable; exact current-scope alias accepted; same-type fake/wrong scope/unattributed or weakened private candidate refused with zero authoritative Design/Runtime writes on both routes. |
| Persistence/replay (C03–C05/C11) | Actual relational publication and reopen; preserved tenant/requirement participants; unchanged behavior/template identities; exact catalog format-2 replay, changed-requirement conflict, unsupported catalog receipt refusal; applied/non-applied foreign receipt current authorization, old review-token hash-domain conflict, changed/deleted draft diagnostic refusal and commit-side fingerprint agreement; fork/clone/migration inheritance and private header protection. |
| HTTP projections (C06–C08) | Allowed and denied users in two tenants/two sources; all palette/list/detail/construct/options/expression-context/search/completion/hover/validation/dependency/diff/fork/availability/review/test/receipt routes, including catalog revocation while workflow-read permission and an earlier expression context revision remain; zero private sentinels/count leaks and zero denied provider calls. Policy removal/outage survives reopen as protected/unavailable. |
| Complete pagination (C08) | Authorized totals and continuation across several pages, revocation invalidating scope, exact 256 non-public or undecidable keys and 257-key overflow unavailable. No successful truncated count. |
| Assistant identity/history (C09–C10) | 450-character private ID sharing a 256-character public prefix; zero-rendered-row counts with revoked omitted contributors; escaped 1 MiB/10,000-reference overflow and forged caller snapshot; new snapshot retains exact ID, old uncertified snapshot requires fresh session; revoked owner/history with SDK-retained text refuses before model/tool calls, including between passes in one stream. |
| Ordinary host compatibility (C12) | Actual public CLR/intrinsic catalog; submit/publish/run a public workflow, stop/reopen the exact owned host/store, inspect retained execution/connection/catalog and run again with no incidents. |
| Composed experience (C12) | Allowed/denied/unavailable catalog states and historical fresh-session recovery in the actual Studio host, keyboard/focus and existing themes/layouts where affected. Preserve saved authoring state and inspect current-commit downloadable evidence independently. |

Use existing focused xUnit projects/classes plus the owning behavior at risk. The production task list will name exact commands after the reviewed contracts and implementation files exist. Do not manufacture a green command by filtering out new refusal/upgrade cases.

## Review and closeout

Record passed, failed, skipped, cancelled, unavailable and not-run separately. Fix and re-run failures attributable to the change. Complete required architecture/composition/maps/CI, independent correctness/security/product review, and resulting-main qualification.

C01–C12 may close the catalog unit only after every applicable outcome is observed. Source-private production enablement still requires complete deployed-reader/Runtime-inspection coverage and actual source/workload policy. M1/provider SDK, entity Studio authoring, User Tasks, source-schema/concurrency/uncertain-write cases, durable event start/resume and business projections remain full-feature obligations.

## Foundation execution ledger — 6 November 2026

The first coding batch is isolated in `/tmp/elsa-entity-3608-catalog`, branch
`agent/codex/3608-catalog-disclosure`, based on reviewed plan head `87225a83bb4af00f4aa5eadaecd53c373359ff90`
and merged ownership fix `cdfc79a0c3c181b0782719437cab5466d961690f`. The existing #3608 claim remains
the winning claim. The Ready backlog exceeds its cap, so no new issue or Ready promotion is created.
The final implementation head, review and CI receipts belong on its PR; this ledger records scoped evidence.

- The independently composed Disclosure project builds, with its public surface tracked. C01's 19
  focused cases pass. The affected Activities Design project passes **806/806**, zero skips.
- Three temporary causal mutations fail their intended tests: accepting unknown classification as
  Public, bypassing exact producer binding identity, and publishing the registry before final DI
  validation. All mutated source bytes were restored; the final post-restoration and API-tracked runs
  pass 806/806. The architecture suite passes 617/617; locked Release/Debug graph restores, maps
  generation/check and the 222-file catalog link check pass. Required hosted CI remains pending.
- Initial failures remain visible: one test-construction compile error was fixed; the first whole
  project run was 800/802 because a serialization fixture contained Undefined JSON and a duplicate
  replacement test called its intentional unsupported stub. Both test defects were fixed before the
  806-case passing run. The initial architecture run was 614/617: three guards correctly caught the new project
  missing from the architecture test host; its full project reference/lock was added before the
  617-case passing run. No production failure was hidden or relabeled.
- Independent correctness/security, standards and acceptance review is clean at
  `c649dd17d6323c3ecd4ac881bac226d522f66e54`. Standards findings were resolved with Core-only API
  tracking (2,518 existing baseline entries and 255 additions) and complete owning catalogs. Test
  discovery lists 783 entries; two dynamic Theories expand into 25 executed cases. Every listed method
  ran, accounting for the 806 executed cases without omitted/skipped tests.
- Allowlisted local evidence is under `/tmp/entity-3608-catalog-foundation-results` and
  `/tmp/entity-3608-catalog-foundation-mutations`. This component batch starts no HTTP host and changes
  no store/schema. The ownership repair's separate resulting-main SQLite/global HTTP evidence is on
  [#3919](https://github.com/elsa-workflows/elsa-foundation/pull/3919#issuecomment-6677079165).

This batch covers values, policy/producer composition and standalone admission only. Existing catalog
writers/readers, retained lineage, format-2 persistence, complete counts/history and source profiles are
not yet integrated; C02's before-all-writes routes and every remaining C01–C12 outcome remain incomplete.
No private-source production profile is enabled. The full-feature checklist retains its open gates;
the owner's explicit catalog decision authorizes this reviewed scoped batch independently.
