# Final JTest acceptance intake

Companion to [spec.md](spec.md). It records what the corpus contains, so the plan and tasks can rely on counts
instead of assumptions. Decisions are in the spec's *Spec decisions* table.

## Corpus at source commit `b1cde13`

| Part | Content |
|---|---|
| `workflow-definitions/` | 36 Elsa 3 definitions (schema `workflow-definition/v3.0.0`) |
| `tests/` | 42 suite files: branching 10, composition 10, javascript 7, http 6, performance 4, correlate 1, events 1, logging 1, single-outcome 1, storage-drivers 1 |
| `import-and-publish-workflows.json` | Elsa 3 setup suite that imports and publishes 34 definitions (Elsa 3 baseline only); it omits `performance-baseline-dispatch.json` and `performance-baseline-dispatch-sub.json`, which `performance/baseline-dispatch-tests.json` needs |
| `templates/` | 5 files with 18 templates; suites call 13 of them (see [contracts/templates.md](contracts/templates.md)); one is an internal helper; four serve only the setup suite or nothing |
| `test-files/` | 5 request fixtures, one about 6 MB (`big-mock-data.json`) |
| `environments/dev.json` | Base URLs, credentials, correlation ID and date bound from environment variables; dispatch channels; dispatch wait time |
| `global-configuration.json` | Template search path |
| `pipelines/` | Original Azure pipelines (not used here); they pinned an Elsa 3.5.0 server image and `jtest` 0.1.3 (0.1.2 in the dev pipeline) |

Step kinds across suites, templates and the setup suite: 178 `use` (170 in suites), 98 `http`, 22 `wait` (fixed
sleeps: 15 in suites, 7 in templates), 12 `assert`,
plus loops. Assertion operators: 358 `equals`, 115 `exists`, 22 `length`, 18 `notexists`, and a few numeric
comparisons. Performance suites assert completion counts, not timings. No secret values and no external URLs.

## Suites by definition

| Definition | Suites | Contains a join or wait construct |
|---|---|---|
| `http-endpoints.json` | 6 `http/*` suites | No |
| `single-outcome-activities.json` | `single-outcome` | No |
| `branching-flows.json` | `if-then-else`, `switch-match-any-mode`, `switch-match-first-mode`, `switchflow-case-default`, `switchflow-case-fail` | Yes |
| `branching-fork-parallel.json`, `branching-wait-all.json`, `branching-wait-all-active.json`, `branching-wait-any.json` | `fork-parallel`, `join-wait-all`, `join-wait-all-active`, `join-wait-any` | Yes |
| `branching-implicit.json` | `implicit` | Implicit convergence; check |
| `composition-*` (8 definitions, root and sub workflows) | 10 `composition/*` suites | Bulk-dispatch root: yes |
| `javascript-*` (7 definitions) | 7 `javascript/*` suites | No |
| `correlate-guid.json`, `events-publisher.json`/`events-listener.json`, `logging-workflow.json` | one suite each | No |
| `performance-*` (6 definitions) | 4 `performance/*` suites; `performance-minimal-baseline.json` has no suite | No |
| `simple-write-hello-world.json` | `performance/concurrency` (bulk-dispatched by `performance-concurrency.json`) | No |
| `storage-drivers-variables-*` (2 definitions) | `storage-drivers/variables` | Root: yes |

"Yes" means the definition contains a join or wait construct, not that its suites are JOIN-blocked. Per FR-018 a
suite is blocked only when the join is what stops it on Elsa 4.

## Known starting points

- Some JavaScript suites use classic Elsa host functions (`date-methods`, `elsa-functions`, `script-handler-functions`);
  the expression-capability work decides which have Elsa 4 equivalents, and the rest are candidate incompatibilities.
- The setup suite misses two definitions (above). That case is excluded from acceptance (spec Q13): the two
  definitions and `performance/baseline-dispatch-tests.json` are copied but neither run nor bucketed.
- `storage-drivers-variables-root-workflow.json` is version 2 (all others are 1). The Elsa 3 import numbers it 1 and
  `storage-drivers/variables` expects 1; Elsa 4 keeps 2. It is patched to version 1 in the copy (spec Q14).
- `performance/big-data-tests.json` asserts on counts inside a 6 MB value; Elsa 4 captures it only as a bounded
  snapshot, so the suite is expected to be `blocked: value capture` (spec FR-024).
- The `events` suite and three `performance` suites call the Elsa 3 API directly instead of through a template and
  assert on `workflowState.status`; they need new templates in the engine-neutral refactor.
