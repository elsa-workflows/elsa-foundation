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
test_tiers.py select --tier pr|full|push|nightly [--manifest tools/ci/test-tiers.json] [--base <sha> --head <sha>]
                     [--github-output <file>] [--summary <file>]
test_tiers.py check-workflows
test_tiers.py check-docs
test_tiers.py --self-test
```

- **`select`** writes these step outputs:
  - `pr_matrix` and `integration_matrix`, each JSON `{"include":[{"shard":…,"whole":[<project>…],"split":[{"project":…,"group":…,"filter":…}]}]}`;
  - `container_matrix`, JSON `{"include":[{"shard":…,"suites":[{"suite":…,"project":…,"require":…}],"secrets":<bool>,"secretsProject":…}]}`;
  - `pr_count`, `integration_count` and `container_count`.

  A split unit carries its `--filter` expression. A rest group carries the negated filter of its siblings. `--tier pr` honours `integrationLeavesPrTier`.
  - Exit 1 when the JSON fails to parse.
  - Exit 1 when the switch is being set to `true` without `Full tier` in ruleset `main-2`.
- **`check-workflows`** (T022): exit 1 when `ci.yml` has no single `Build & test` job that runs with `if: ${{ !cancelled() }}` and needs `plan-tiers`, `pr-shard`, `architecture-guards` and `core-only`; when `ci-full.yml` has no single `Full tier` job that runs with `!cancelled()` and fails on a failed plan; or when either workflow has a `labeled` trigger (comments excluded). A text scan, because the runners carry no YAML library. It runs in `Plan tiers`.
- **`check-docs`**:
  - Exit 1 when `docs/contributing/merging.md` has no `## CI tiers` section.
  - Exit 1 when that section does not name `-f tier=full` and the two tier names.
  - Exit 1 when any of the four skills (`.claude/skills/{elsa-auto-review-merge,elsa-quality-fix,speckit-implement,elsa-add-unit-tests}/SKILL.md`) lacks a link to `docs/contributing/merging.md#ci-tiers`.
  - Exit 1 when the literals in `ci.yml`'s dispatch input differ from the documented ones.
  - It runs in `Plan tiers`, including on documentation-only PRs, because it reads only documentation and workflow files.
- **`--self-test`**: built-in cases for selection (split, rest negation, the switch on and off, the refusals), for `check-workflows` (a skippable aggregate, a missing need, a `labeled` trigger, a comment that only mentions one) and for `check-docs` (each missing link, each mismatched literal). Exits 1 on any miss.

## tools/ci/test_results.py

```text
test_results.py failures <trx-dir>                       # prints FQNs of failed tests, one per line
test_results.py retry-filter <trx-dir>                   # prints a dotnet --filter for those FQNs, or nothing
test_results.py evaluate <shard-dir> --manifest <file> --shard <name> --head <sha>
                         [--summary <file>] [--outcomes <file>]
test_results.py check-quarantine --manifest <file> [--base <sha>]   # needs GH_TOKEN with issues: read
test_results.py record-flakes <artifacts-dir> --run-url <url> --body <file>
test_results.py --self-test
```

- **`evaluate`** reads `<shard-dir>/runs.json` and, per run, `try1/<index>/*.trx` and, when present, `try2/<index>/*.trx`, and writes `test-outcomes.json` (data-model).
  - Exit 1 when a test that is not quarantined failed in try1 and also failed, or did not run, in try2.
  - Exit 1 when a test host aborted (an `Aborted` run summary, a non-zero aborted counter, or an Error-level run message about an abort or crash), or a run wrote no TRX.
  - Exit 1 when `slowTestMode` is `fail` and a PR-tier test is slower than `slowTestSeconds` and not on the allow-list.
  - Otherwise exit 0.
- **`check-quarantine`**: exit 1 on an entry whose issue is missing or closed. The message names the entry, its issue and the fix: remove the entry, or reopen the issue if the test is still flaky.
  - With `--base <sha>` (PR runs), it checks only the entries that are new or changed compared with the manifest at `<sha>`. Entries that are unchanged or removed are not checked.
  - Without `--base` (push to `main`, nightly), it checks every entry.
  - Exit 2 when the `--base` commit is not in the clone. Only "the manifest is not in that commit" counts as an absent file (every entry new); any other failure to read it exits 2, so a shallow clone can never silently check the whole list.
- **`record-flakes`**: reads every `test-outcomes.json` under the directory and writes, when any test passed on retry, a comment body listing each test and its shard. Names that do not match `^[A-Za-z_][A-Za-z0-9_.+]*$`, shards that do not match `^[a-z0-9][a-z0-9-]{0,63}$`, SHAs that are not 40 hex characters and unreadable files are dropped and logged, because the artifacts may come from a fork. Writes nothing when no test passed on retry.
- **Exit 2** on bad arguments, for every subcommand.

## Elsa.Maps.Generator

| Command | Change |
|---|---|
| `test-tiers-check` | **New.** It checks, and exits 1 naming the project, the manifest path and both tiers:<ul><li>every test project is in the manifest exactly once, and every entry exists (FR-016);</li><li>Testcontainers (direct or through the test-project reference closure) or a `dotnet` assembly launch (`ProcessStartInfo`/`Process.Start` whose file name is `"dotnet"`, `DOTNET_HOST_PATH` or a `DotnetMuxer()` helper, in the project's sources or those of a referenced test or fixture project) means the project must be integration tier: always for Testcontainers, and for a `dotnet` launch when the project's `medianMinutes` is 2 or more (FR-017);</li><li>split classes exist and collections are not split;</li><li>shard coverage is complete.</li></ul> |
| `test-tiers-self-test` | **New.** Fixture facts for each failure above, cases for the `dotnet`-launch and class/collection scanners, and the committed manifest against the tree. |
| `ef-suites-select`, `ef-suites-self-test` | Read `containerSuites` from `tools/ci/test-tiers.json` instead of `tools/ci/ef-suites.json`. Output unchanged. |
| `test-tiers-rebalance <trx-dir> [--write]` | **New, maintainer tool.** It reads the TRX files of one or more runs (CI uploads them as `trx-<shard>` artifacts), reports each project's median wall time and the slowest classes of split projects, and proposes a PR-tier packing by longest-first bin packing. `--write` records only `medianMinutes`, as the median over every run given; one run is too noisy, so pass several. The shard grouping stays a reviewed hand edit. CI never runs it. |

## Shard job steps (PR and integration)

1. checkout; setup-dotnet; NuGet cache;
2. `bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603` (unchanged);
3. `dotnet build Elsa.Server.slnx -c Release --no-restore -p:WarningsNotAsErrors=NU1603` (research R4);
4. `python3 tools/ci/run_shard.py --entry <matrix entry> --out $RUNNER_TEMP/shard --head <sha>`, which:
   - runs each unit (a whole project, or one class group with its filter) as its own `dotnet test <project> -c Release --no-build --logger trx --results-directory <out>/try1/<index> --blame-hang-timeout 10m --blame-hang-dump-type none [--filter <unit filter>]`, up to the runner's core count at a time;
   - re-runs each unit's failed tests once, with `FullyQualifiedName=` filters, into `try2/<index>`;
   - calls `test_results.py evaluate`, which decides the shard;
5. upload `test-outcomes.json` as artifact `test-outcomes-<shard>`, and the TRX files as `trx-<shard>` (both retention 7 days).

Each unit is its own process with its own results directory, instead of one `.slnf` invocation. VSTest names TRX files by user, machine and second, so runs that share a directory can overwrite each other, and the retry must trace each failure back to its unit. The pool keeps the same project parallelism `dotnet test` gives a solution.

## EF container shard steps

1. checkout; setup-dotnet; NuGet cache; PostgreSQL when the shard holds the Secrets EF composition proof;
2. restore (locked) and build each of the shard's container projects once;
3. `run_shard.py --parallel 1`: each suite is one run with only its own `require` variable set, in that process's environment only, never `GITHUB_ENV`, so modes do not leak across suites; the Secrets proof is one more run with its connection string. Same TRX, retry and evaluate steps; the connection string is kept out of the recorded `runs.json`.
