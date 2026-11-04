# Handoff: #2908 ETL delivery, from spec 1203 onward (2026-11-04)

## Codex continuation checkpoint — 4 November

Live GitHub state overrides older statuses below; refresh the existing stack before acting. Part of #2908.

- Parent PR3737 is ready at `7190843f0` after clean full review 11; schema PR3803 is ready at `7afdb3456` after clean full review 6. Historical 1325/807/616 receipts retain their actual sources. Neither PR is merged; required CI and GitGuardian remain gates.
- Runtime concurrency successor spec1206/PR3804 adopts main `9daaa3206` through that stack. Its clean production candidate `a48ec7b0d` passes full local qualification, genuine native contention controls, and all 36 actual both-store host/restart checks on PostgreSQL, SQLServer and MySQL. Native persistence remains SQLite. Current counts, discovery, binary/version results and exact raw receipt hashes are in [1206 evidence](../1206-etl-runtime-concurrency/evidence/README.md). T001–T016 are complete; complete-diff implementation review and protected delivery/resulting-main verification remain T017–T018.
- Completed-state restart is not mid-work/old-writer recovery or Studio acceptance. Main's separate fresh-ActivitiesDesign legacy profile does not prove ordinary upgraded authoring/Studio compatibility. Old producers and frozen evidence are retained; this stack removes no writer or migration.
- Continue the original next-slice order after this stack: #3789 off main, transforms/admission, recovery/stale-writer proofs, BPMN and Studio. Keep #2908/#2892/Project #62 In Progress / Partial and retain the existing claim, Codex App identity, standing continuation authorization and all merge gates. Runtime preparation retention requested by #3519/T066 is queued on #2892, outside PR3804; it does not reorder this ETL sequence.



Written by a Claude session that ran out of credits. Everything below is source-bound to branch
`agent/claude/2908-etl-runtime-providers`; verify against the code before acting.

## Codex continuation (2026-11-04)

The owner transferred this continuation to Codex. Claim: https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6625913689. Continue the same runtime-provider branch; all writes and commits in this continuation use the Codex App identity.

- The six-PR stack has merged through main `73040cd7b`. Parent #3737 is reconciled by ordinary merges with #3734's final pre-squash head `6860f6b80`, then that main; pushed parent head `e4dde3cdf` now has only spec 1202 in its diff against main. Never push to #3705–#3734.
- Runtime branch adopts that parent. Its compaction constraints are rendered per engine; additive generated `ContinuousCompaction` migrations preserve the existing server Initial migrations and all SQLite migrations. All 33 SQLite expressions match the reconciled base.
- Code candidate `b56db13f51d2609c1cf57b54cddc5db13184fd66`: per-run container passwords, shared skippable runtime contracts, corrected aggregate specification, and four incoming fixture caller adaptations.
- Required-provider whole suite passed **801/801, zero failed/skipped**, executing inputs at `6e74eb3ca`. The later four fixture caller edits are only in SQLite-suite files excluded from ProviderTests. The fresh collation mutation at `6e74eb3ca` failed three model-agreement cases; restoring passed all three. Final ETL **1325/1325**, architecture **616/616**, locked graph restores and both generator checks pass at `b56db13f5`; see `evidence/qualification.json` and the evidence README. Final PR-head required CI remains separate.
- Preparation review round 1 found the optional-provider skip defect and aggregate-spec mismatch; corrected in `6e74eb3ca`. Round 2 found incoming helper callers after main reconciliation; corrected in `cdb654ea7` and `b56db13f5`. The final exact-head review and PR-ready/review-request steps follow these receipts; use the PR round comments for their final outcome.
- Parent #3737's GitGuardian check holds historical test-container incidents 39471203 / 39471729. Source was inspected: disposable-container fixture use and cleanup; current credentials generated per run. Local browser use is disabled in Work settings, so Codex cannot triage the dashboard. No scan or merge gate was bypassed. The owner has the first action card; handle the second individually.
- Project #62 items #2908 and #2892 remain **In Progress / Partial**. No parent acceptance checkbox changes.

The original handoff below is historical; use this continuation and the final PR receipts for newer source-bound results.

## Standing goal

Deliver #2908 (Distributed ETL) end to end, slice by slice. Each slice gets:
- a Speckit spec, plan and tasks under `specs/<n>-<name>/`;
- real-host, browser and process evidence plus bite-proofs (mutate a guard, show a named test fails, restore, show it passes) in `specs/<n>/evidence/`;
- the `elsa-auto-review-merge` loop (`.claude/skills/elsa-auto-review-merge`, `docs/skills/catalog.md#auto-review-loop-then-merge`);
- gate evidence posted as a PR comment, then `gh pr ready <n>`, then a comment `[Claude] @elsa-workflows-bot review`;
- #2908, #2892 and Project #62 updated with source-bound status. Project #62's Status stays **In Progress**, never Done.

## Rules that bite

- Commit identity: `git -c user.name='elsa-workflows-claude[bot]' -c user.email='342797135+elsa-workflows-claude[bot]@users.noreply.github.com' commit ...`. Never change the repo's git config. A Codex agent should use its own identity per `AGENTS.md`.
- Never use a closing keyword before an issue number, even negated ("does not close #2908" auto-closes it). Write "Part of #2908".
- Never force-push, and never bypass branch protection or required checks.
- Ports 5095 and 4179 are the owner's; use ephemeral ports.
- Committed scripts contain no secret literals; generate container secrets per run.
- Don't cancel a draft CI run after `gh pr ready`, because that blocks the merge.
- Re-merge `main` into a PR that adds project references before merging it.

## PR stack (state at handoff)

`main` ← #3695 ← #3699 ← #3705 ← #3717 ← #3725 ← #3734 (`agent/claude/2908-rabbitmq-source`) ← **#3737** (`agent/claude/2908-etl-design-providers`, spec 1202, ready at `8f6c3b349`) ← **spec 1203** (this branch, no PR yet).

Another session was merging #3695 → #3734 in order and asked that nobody push to #3705–#3734. When #3734 squash-merges, #3737's base becomes `main`. Then merge #3734's final head into #3737, merge `main` into #3737, and push. Don't force-push.

## Spec 1203: the runtime store's schema on all four EF engines (this branch)

Docs: [spec.md](spec.md), [plan.md](plan.md), [tasks.md](tasks.md) (all checked) and [evidence/README.md](evidence/README.md).

Done, at `a93f1d045`:
- server runtime contexts for SQL Server, PostgreSQL and MySQL (`EtlRuntimeDbContext.cs`);
- `EtlRuntimeConstraintDialect`;
- the `RunKeyOf` seam, with 64-character hashed run keys on the server engines;
- generated internal `Initial` migrations under `Migrations/EtlRuntime/{SqlServer,PostgreSql,MySql}`;
- the runtime-store tests turned into contracts (`Extensions/Etl/tests/Shared/RuntimeStore`), with server subclasses in `Elsa.Etl.Persistence.ProviderTests`;
- two store reads restructured so no query runs inside an open reader.

The registration still refuses server engines with the message "spec 1206". The `etl-ef` provider suite passed 696/696 at `a93f1d045`, and four bite-proofs bite (`evidence/bite-proofs.json`).

Committed with this handoff but **not yet verified**:
- the README bullets;
- the evidence README;
- the task checkboxes.

Remaining for 1203:
1. Run the suites and record the counts in `evidence/README.md` under "Suites":
   - `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj`;
   - `ELSA_ETL_EF_REQUIRE_NATIVE_PROVIDERS=1 dotnet test Extensions/Etl/tests/Persistence/ProviderTests` (needs Docker, about 10 minutes);
   - `bash tools/architecture/restore-ci-project-graph.sh`, then `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj`. The EF dependency guard may need its ProviderTests closure updated, because ProviderTests now references `Elsa.Etl.Runtime`;
   - `dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- solution-filters-check`, and the same with `test-tiers-check`.
2. Add the collation bite named "(to add)" in the evidence README. Record it in `bite-proofs.json` with its source revision, or move it to "Not bite-proven".
3. Open a draft PR with base `agent/claude/2908-etl-design-providers`. The body says "Part of #2908". Then run the review loop, post the gate evidence, run `gh pr ready`, and request review.

## Next slices (in order)

1. **Spec 1206, runtime concurrency on the server engines:**
   - wrap each runtime transition in `EfWriteRetry` as one unit (the pattern is in the 1202 design store, `EfEtlDefinitionStore` `PublishRetry`);
   - race tests and a soak on all three engines;
   - lift the registration guard (`DependencyInjection/EtlRuntimeEntityFrameworkCoreRegistration.cs`) via `Binding.Select`;
   - add the server `[EfModule]` entries and the manifest text;
   - a Workbench journey with both stores on each engine (the model is `specs/1202-*/evidence/design_store_workbench_journey.py`).
2. **#3789, a small PR off `main` after #3734 lands:**
   - the Studio SQL source key follows the wrong column mid-rename (`keyAfterColumnEdit`);
   - null `EtlSqlDestination.Key` entries pass `ValidateCollectionEntries`;
   - judge whether the Workbench `shells.json` should enable the SQL features.
3. Transform chains with admission budgets.
4. Recovery and stale-writer proofs.
5. BPMN invocation and Studio.

## Gotchas learned

- Server engines run one command per connection at a time. A query inside an open `await foreach` reader fails on SQL Server, PostgreSQL and MySQL, though SQLite allows it.
- Run provider tests with `[SkippableFact]` / `[SkippableTheory]` and one database per test (`EtlNativeProviderFixture`).
- On macOS there is no `timeout` command; run long suites in the background.
