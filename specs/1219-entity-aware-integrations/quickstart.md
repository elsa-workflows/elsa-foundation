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

Prove format-2 startup, publication, classification guards and same-store reopen for each affected provider. Prove a schema-1 reader/activation profile refuses format 2 and the new build refuses unsupported schema-1 material. Keep old fixture files unchanged. No old-database upgrade or imported format-1 replay is a passed result.

## Evidence sequence

| Step | Observe and retain |
|---|---|
| Model and admission (C01–C02) | Zero/default/unknown decisions unavailable; exact current-scope alias accepted; same-type fake/wrong scope/unattributed or weakened private candidate refused with zero authoritative Design/Runtime writes on both routes. |
| Persistence/replay (C03–C05/C11) | Actual relational publication and reopen; preserved tenant/requirement participants; unchanged behavior/template identities; exact format-2 replay, changed-requirement conflict, unsupported receipt refusal; fork/clone/migration inheritance and private header protection. |
| HTTP projections (C06–C08) | Allowed and denied users in two tenants/two sources; all palette/list/detail/construct/options/dependency/diff/fork/availability/review/test/receipt routes; zero private sentinels/count leaks and zero denied provider calls. Policy removal/outage survives reopen as protected/unavailable. |
| Complete pagination (C08) | Authorized totals and continuation across several pages, revocation invalidating scope, exact 256 non-public or undecidable keys and 257-key overflow unavailable. No successful truncated count. |
| Assistant identity/history (C09–C10) | 450-character private ID sharing a 256-character public prefix; zero-rendered-row counts with revoked omitted contributors; escaped 1 MiB/10,000-reference overflow and forged caller snapshot; new snapshot retains exact ID, old uncertified snapshot requires fresh session; revoked owner/history with SDK-retained text refuses before model/tool calls, including between passes in one stream. |
| Ordinary host compatibility (C12) | Actual public CLR/intrinsic catalog; submit/publish/run a public workflow, stop/reopen the exact owned host/store, inspect retained execution/connection/catalog and run again with no incidents. |
| Composed experience (C12) | Allowed/denied/unavailable catalog states and historical fresh-session recovery in the actual Studio host, keyboard/focus and existing themes/layouts where affected. Preserve saved authoring state and inspect current-commit downloadable evidence independently. |

Use existing focused xUnit projects/classes plus the owning behavior at risk. The production task list will name exact commands after the reviewed contracts and implementation files exist. Do not manufacture a green command by filtering out new refusal/upgrade cases.

## Review and closeout

Record passed, failed, skipped, cancelled, unavailable and not-run separately. Fix and re-run failures attributable to the change. Complete required architecture/composition/maps/CI, independent correctness/security/product review, and resulting-main qualification.

C01–C12 may close the catalog unit only after every applicable outcome is observed. Source-private production enablement still requires complete deployed-reader/Runtime-inspection coverage and actual source/workload policy. M1/provider SDK, entity Studio authoring, User Tasks, source-schema/concurrency/uncertain-write cases, durable event start/resume and business projections remain full-feature obligations.
