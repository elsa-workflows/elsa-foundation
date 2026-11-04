# Handoff: #2908 ETL delivery, from spec 1203 onward (2026-11-04)

Written by a Claude session that ran out of credits. Everything below is source-bound to branch
`agent/claude/2908-etl-runtime-providers`; verify against the code before acting.

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

The registration still refuses server engines with the message "spec 1204". The `etl-ef` provider suite passed 696/696 at `a93f1d045`, and four bite-proofs bite (`evidence/bite-proofs.json`).

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

1. **Spec 1204, runtime concurrency on the server engines:**
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
