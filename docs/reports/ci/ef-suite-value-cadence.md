# EF container coverage: suite value and CI cadence

Decision spike: [#2027](https://github.com/elsa-workflows/elsa-foundation/issues/2027), 2026-09-25. This is an audit and recommendation; it does not change a test or gate. The suite manifest is [`tools/ci/ef-suites.json`](../../../tools/ci/ef-suites.json); the [PR selector report](ef-pr-suite-selection.md) explains why unrelated PRs can now select zero legs. This report asks a different question: how many separately scheduled checks are worth keeping?

## Recommendation

Keep the **18 test projects and their distinct assertions for now**, but do not treat 18 separate GitHub jobs as a permanent architecture. Continue to select affected suites on PRs and prove the full inventory on `main`. Pilot a small number of grouped jobs that run the selected project tests sequentially on shared runners, with strict native-provider mode and an explicit executed/skipped test summary. Compare that pilot against the existing matrix on the same commits before replacing the matrix. Preserve one stable aggregate check name through the migration. Do not move the only strict native-provider proof to the existing nightly job: nightly currently runs a single solution-filter test command without the matrix's required-provider variables.

This is a job-layout decision, not a recommendation to delete domain tests. Most suite names correspond to different persistence contracts. Consolidation can remove repeated checkout, SDK, restore, and runner startup costs, and make PR checks readable, but may increase the wall-clock critical path through serial execution or reveal fixture interference. Measure both outcomes before rollout.

## What the suites prove

The table describes the distinctive behavior visible in test source, rather than counting test projects as coverage. “Required native” means CI sets the manifest's `require` variable; “optional native” means a missing Docker/provider may skip live facts while the job succeeds. A single [full main run](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36112916320) reported **zero skipped tests** in all nine optional-native suites. That observation does not make future optional-provider skips fail the gate.

| Suite | Distinct contract worth retaining | Native-provider behavior | Median job min, 8 full main runs |
|---|---|---|---:|
| `activities-design` | Activity design schema CRUD, query, transactions, concurrency, token fencing | Required native matrix | 2.10 |
| `cli-acceptance` | Real persistence-script CLI, repeatable raw-ADO application, all enabled Workbench EF features under `Migrate:Policy=Validate` | Required PG/SQL Server/MySQL | 4.20 |
| `dashboard` | Run-health and portfolio queries across runtime/design stores | Required native matrix | 2.02 |
| `elsa3-import` | Import model persistence and atomic import commit/rollback | Required native matrix | 2.08 |
| `identity` | IAM/config CRUD, rollback, revisions, compare-and-swap, mismatch guards | Optional native, plus model checks | 1.59 |
| `migrations` | Discovered module contexts, install/validate/history, custom schema, ordinal collation | Required PG/SQL Server/MySQL | 3.47 |
| `mysql-feasibility` | MySQL migration drift, identity/collation, JSON updates, bulk counts, transactions, reconnect | Optional native; 11 of 12 facts can skip | 1.27 |
| `opentelemetry` | Signal roundtrip, restart/scope, retention, concurrency, provider-specific limits | Optional native, plus model checks | 1.60 |
| `publishing` | Publishing receipts, retry/idempotency, revision, rollback, tenant, upgrade behavior | Required native matrix plus SQLite cases | 2.63 |
| `runtime` | Runtime stores, queues, fences, schedules, checkpoint behavior | Required native matrix | 1.88 |
| `runtime-distributed` | Placement claims, command-stream leases, acknowledgement, rollback/reopen | Required native matrix | 1.76 |
| `secrets-mysql` | MySQL Secrets repository CRUD, normalized lookup, rollback, concurrency | Optional native; 1 of 3 facts can skip | 1.11 |
| `secrets-sqlserver` | SQL Server Secrets migration, repository, collation, concurrency, projection paging | Optional native; 2 of 4 facts can skip | 1.14 |
| `shared-persistence-fixture` | Named-target routing, history isolation, restart, partial opt-in, wrong-target refusal | Required PostgreSQL | 4.42 |
| `structured-logs` | Durable log append/query, transaction, compare-and-swap, trim | Optional native, plus model checks | 1.63 |
| `studio-preferences` | Durable preference revisions, compare-and-swap, MySQL history/registration | Optional native, plus model checks | 1.50 |
| `transaction-topology` | Borrower/owner transaction lifecycle, savepoints, rollback across contexts | Optional native; 3 of 16 facts can skip, 13 SQLite facts remain | 1.43 |
| `workflows-design` | W01–W05 design-store CRUD/query and scoped/global identity | Optional native | 1.67 |

Several tests share provider fixtures but not outcomes. `migrations` proves schema/upgrade behavior; provider-specific Secrets tests prove repository and projection behavior. `cli-acceptance` proves operator-produced scripts and a composed host, which a direct migration API test cannot. `shared-persistence-fixture` proves resource routing and restart, not just table creation. The always-run `Secrets EF composition` job explicitly starts PostgreSQL and proves one real-shell path, but does not replace SQL Server/MySQL Secrets behavior or the other module contracts. The fast `Build & test` lane builds all test projects but excludes Testcontainers projects from test execution.

## Cost and detection evidence

Eight recent successful full-main CI runs on 2026-09-25 ([first](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36088338526), [last](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36112916320)) each passed all 18 legs. Per run, their summed EF job duration was **36.68–38.02 runner-minutes**, median **37.33**. The first EF job start through the last EF job finish was **3.93–7.70 wall-clock minutes**, median **5.75**. The median delay from CI creation until the *last* EF leg started was **2.28 minutes**; runner scheduling can dominate a tiny suite. These are observed job timestamps, not billed minutes or the whole PR critical path. `cli-acceptance`, `shared-persistence-fixture`, and `migrations` account for about **12.1 of 37.3** per-suite median runner-minutes.

The recent PR sample illustrates the selector: [docs-only PR #2030](https://github.com/elsa-workflows/elsa-foundation/pull/2030) selected zero named EF legs, [a narrow Planning PR run](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36113138646) ran two, and [a broad PR run](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36111868384) ran all 18. Unknown/unowned source and shared build/EF changes deliberately fail closed to all 18, which explains the screenshot-shaped experience for some PRs. The broad [run 36109567590](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36109567590) had one EF leg start almost 14 minutes after run creation; it still passed. That is a queue tail, not a 14-minute test body.

Across the eight successful full-main runs, no EF leg failed. In a nearby [red main run](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36098626429), all 18 EF legs succeeded and `Build & test` failed. A superseded PR run was cancelled during a head update, not an EF regression. This small, recent sample is insufficient to call any suite redundant or to estimate a meaningful product-failure detection rate. A sampled full-main run's optional suites reported zero skipped tests, but source inspection found the skip modes above. The durable fix is to make a missing required provider fail, rather than infer coverage from a green job label.

The existing [scheduled integration workflow](../../../.github/workflows/integration.yml) builds the solution and tests the Testcontainers filter in one job. Twelve recent successful scheduled runs had job durations from **6.35 to 18.95 minutes** (ten were 6.35–10.33). This supports testing a grouped job, not claiming an apples-to-apples cost saving: nightly has different build/setup and does not set the nine matrix required-provider variables. Moving full proof only to nightly also delays detection until after a merge and the next schedule, potentially nearly a day.

The current [main rulesets](https://github.com/elsa-workflows/elsa-foundation/rules) require `Build & test` and `Generated maps fresh`; neither the 18 named EF legs nor `EF container suites` is presently a required status context. The aggregate still reports selected-suite outcomes and prevents a misleading green CI workflow result, but branch protection does not currently make it a merge block. This should be decided explicitly when the job layout is changed, rather than inferred from the word “gate” in workflow comments.

## Policy comparison and migration

| Policy | PR/main evidence | Likely cost and UX | Regression timing / risk |
|---|---|---|---|
| Current selection + 18 independent jobs | Affected legs on PR; all 18 on main; separate always-run Secrets host proof | Narrow/docs PRs are much smaller, but broad PRs show 18 rows and about 37 runner-minutes; parallel wave median 5.75 min | Best provider isolation and immediate main proof; optional-provider skips can masquerade as success |
| **Pilot: selected projects in a few grouped jobs** | Same affected-project selection and required-provider modes on PR; all projects on main; Secrets host proof stays | Fewer rows and less repeated job setup are plausible; measure actual runner-minutes and wall time on identical heads | Same theoretical detection point, but serial execution may delay results and shared runner/container state may affect tests |
| PR smoke + full nightly only | Small PR smoke; full projects on schedule | Few PR rows, but no demonstrated equivalent strict-provider nightly proof | Broad regressions can merge and remain undetected until nightly; not recommended as the sole full proof |
| Delete overlapping-looking suites | Fewer projects | Immediate check reduction but unknown savings relative to lost assertions | Loses domain-specific regressions; no deletion justified by this audit |

Implementation should proceed in two bounded steps:

1. [**Provider evidence hardening #2031**](https://github.com/elsa-workflows/elsa-foundation/issues/2031): for every native-provider fact, make CI-required mode fail on provider startup failure and assert that the intended provider cases executed. Keep local development allowed to skip when Docker is absent. Validate by deliberately making a required provider unavailable, then running the real CI mode.
2. [**Grouped-job pilot #2032**](https://github.com/elsa-workflows/elsa-foundation/issues/2032): retain the manifest and per-project selection, but run selected projects in a small number of stable groups on a trial workflow or otherwise non-required comparison. Preserve per-project test output and a machine-readable passed/failed/skipped summary. Run the old matrix and pilot on the same broad and narrow commits; compare total runner time, critical path, check count, provider execution, failure localization, and fixture isolation. Only then replace the matrix, keep the `EF container suites` aggregate context stable, update the selector/contract tests and documentation, and deliberately decide whether to add that aggregate to the main ruleset's required checks. Do not remove any named check from branch rules without first verifying the current ruleset; none is required today.

No suite is deleted and no cadence changes in this spike. A savings percentage or exact group count would be speculative until the pilot runs.
