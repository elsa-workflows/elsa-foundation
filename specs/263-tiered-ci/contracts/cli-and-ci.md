# Contracts: tier tooling, workflows and check contexts

## Check contexts

| Context | Workflow | Required | Reports |
|---|---|---|---|
| `Build & test` | `ci.yml` | yes, unchanged name | Aggregate of `Plan tiers`, every `PR shard (n)`, `Architecture guards` and `Core-only build & test`. Success when all of them succeeded, or when the diff is documentation-only (spec 257). |
| `Generated maps fresh` | `ci.yml` | yes, unchanged | Gains `test-tiers-check` beside `ef-suites-self-test`. |
| `Full tier` | `ci-full.yml` | **yes, new** (added to ruleset `main-2` by an admin) | Aggregate of the full-tier plan, integration shards and EF container shards. On a draft with no manifest change it is not reported; see the next row. |
| `Full tier (not run: draft)` | `ci-full.yml` | no, and never required | The same aggregate job, named this way when the full tier did not run on a draft (research R8). It leaves the required `Full tier` missing on that head. |
| `PR shard (n)`, `Integration shard (n)`, `EF container shard (n)`, `Plan tiers`, `Architecture guards`, `Core-only build & test` | as above | no | per job |

The job names `Build & test`, `Generated maps fresh` and `Full tier` are load-bearing: branch protection matches them by job name. They are top-level jobs of their workflows, never jobs inside a reusable workflow, so the context carries no `caller / callee` prefix.

## Triggers

| Workflow | Triggers | Concurrency group |
|---|---|---|
| `ci.yml` | `pull_request` (default types) on `main`; `push` to `main`; `schedule` (unchanged cron); `workflow_dispatch` with input `tier` | unchanged (`ci-pr-<n>`, `ci-push-<ref>`, `ci-run-<id>`) |
| `ci-full.yml` | `pull_request` types `[opened, reopened, synchronize, ready_for_review]` on `main` | `ci-full-pr-<n>`, cancel in progress |
| `full-tier.yml` | `workflow_call` with inputs `containers: boolean`, `run: boolean` | inherited from the caller |
| `ci-flake-record.yml` | `workflow_run` on `[CI, CI full tier]`, types `[completed]` | `ci-flake-record`, no cancel |
| `studio-ci.yml` | `pull_request` on `main` with the `paths:` list from research R10; `push` to `main` with no filter | unchanged |

**`workflow_dispatch` input of `ci.yml`:**

```yaml
tier:
  description: Which tier to run
  type: choice
  options: [full, pr]
  default: full
```

**No label.** The full tier has no label trigger (FR-008). The input name `tier` and the value `full` are the two literals that `test_tiers.py check-docs` compares with `docs/contributing/merging.md#ci-tiers` (FR-025).

## tools/ci/test_tiers.py

```text
test_tiers.py select --tier pr|full|push|nightly [--manifest tools/ci/test-tiers.json]
                     [--github-output <file>] [--summary <file>]
test_tiers.py check-docs
test_tiers.py --self-test
```

- **`select`** writes these step outputs:
  - `pr_matrix`, `integration_matrix` and `container_matrix`, each JSON `{"include":[{"shard":…,"projects":[…],"filter":…}]}`;
  - `pr_count`, `integration_count` and `container_count`.

  A split unit carries its `--filter` expression. A rest group carries the negated filter of its siblings. `--tier pr` honours `integrationLeavesPrTier`.
  - Exit 1 when the JSON fails to parse.
  - Exit 1 when the switch is being set to `true` without `Full tier` in ruleset `main-2`.
- **`check-docs`**:
  - Exit 1 when `docs/contributing/merging.md` has no `## CI tiers` section.
  - Exit 1 when that section does not name `-f tier=full` and the two tier names.
  - Exit 1 when any of the four skills (`.claude/skills/{elsa-auto-review-merge,elsa-quality-fix,speckit-implement,elsa-add-unit-tests}/SKILL.md`) lacks a link to `docs/contributing/merging.md#ci-tiers`.
  - Exit 1 when the literals in `ci.yml`'s dispatch input differ from the documented ones.
  - It runs in `Plan tiers`, including on documentation-only PRs, because it reads only documentation and workflow files.
- **`--self-test`**: built-in cases for selection (split, rest negation, the switch on and off) and for the docs check (each missing link, each mismatched literal). Exits 1 on any miss.

## tools/ci/test_results.py

```text
test_results.py failures <trx-dir>                       # prints FQNs of failed tests, one per line
test_results.py retry-filter <trx-dir>                   # prints a dotnet --filter for those FQNs, or nothing
test_results.py evaluate <shard-dir> --manifest <file> --shard <name> --head <sha>
                         [--summary <file>] [--outcomes <file>]
test_results.py check-quarantine --manifest <file> [--base <sha>]   # needs GH_TOKEN with issues: read
test_results.py --self-test
```

- **`evaluate`** reads `<shard-dir>/try1/*.trx` and, when present, `<shard-dir>/try2/*.trx`, and writes `test-outcomes.json` (data-model).
  - Exit 1 when a test that is not quarantined failed in try1 and also failed, or did not run, in try2.
  - Exit 1 when the test host aborted.
  - Otherwise exit 0.
- **`check-quarantine`**: exit 1 on an entry whose issue is missing or closed. The message names the entry, its issue and the fix: remove the entry, or reopen the issue if the test is still flaky.
  - With `--base <sha>` (PR runs), it checks only the entries that are new or changed compared with the manifest at `<sha>`. Entries that are unchanged or removed are not checked.
  - Without `--base` (push to `main`, nightly), it checks every entry.
- **Exit 2** on bad arguments, for every subcommand.

## Elsa.Maps.Generator

| Command | Change |
|---|---|
| `test-tiers-check` | **New.** It checks, and exits 1 naming the project, the manifest path and both tiers:<ul><li>every test project is in the manifest exactly once, and every entry exists (FR-016);</li><li>Testcontainers (direct or through the test-project reference closure) or a `dotnet` assembly launch (`ProcessStartInfo`/`Process.Start` whose file name is `"dotnet"`, `DOTNET_HOST_PATH` or a `DotnetMuxer()` helper, in the project's sources or those of a referenced test or fixture project) means the project must be integration tier: always for Testcontainers, and for a `dotnet` launch when the project's `medianMinutes` is 2 or more (FR-017);</li><li>split classes exist and collections are not split;</li><li>shard coverage is complete.</li></ul> |
| `test-tiers-self-test` | **New.** Fixture trees for each failure above. |
| `ef-suites-select`, `ef-suites-self-test` | Read `containerSuites` from `tools/ci/test-tiers.json` instead of `tools/ci/ef-suites.json`. Output unchanged. |
| `rebalance <trx-dir> [--write]` | **New, maintainer tool.** It reads TRX files from a full run, recomputes `medianMinutes` and the per-class durations of split projects, and proposes `shards` and `splits` by greedy bin packing (longest first) within `shardTargetMinutes`, never splitting a collection. `--write` updates the manifest for review; CI never runs it. |

## Shard job steps (PR and integration)

1. checkout; setup-dotnet; NuGet cache;
2. `bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603` (unchanged);
3. `dotnet build Elsa.Server.slnx -c Release --no-restore -p:WarningsNotAsErrors=NU1603` (research R4);
4. for each project in the shard: `dotnet test <project> -c Release --no-build --logger trx --results-directory $RUNNER_TEMP/r/try1 --blame-hang-timeout 10m --blame-hang-dump-type none [--filter <unit filter>]`;
5. when `test_results.py retry-filter` prints a filter: one retry of those tests into `try2`;
6. `test_results.py evaluate …`; upload `test-outcomes.json` as artifact `test-outcomes-<shard>` (retention 7 days).

Step 4 runs the shard's projects in one `dotnet test` invocation through a generated `.slnf` when no project in it is split, so in-runner project parallelism is kept, as today.

## EF container shard steps

1. checkout; setup-dotnet; NuGet cache;
2. restore and build the shard's container projects once;
3. for each suite: arm only that suite's `require` variable in the step's own `env`, never `GITHUB_ENV`, so modes do not leak across suites; `dotnet test <project> --no-build` with the same TRX, retry and evaluate steps;
4. the Secrets EF composition proof keeps its PostgreSQL service and steps, in the shard that the manifest names.
