# Final JTest acceptance intake

Companion to [spec.md](spec.md). It records what the corpus contains, so the plan and tasks can rely on counts
instead of assumptions. Decisions are in the spec's *Spec decisions* table.

## Corpus at source commit `b1cde13`

| Part | Content |
|---|---|
| `workflow-definitions/` | 36 Elsa 3 definitions (schema `workflow-definition/v3.0.0`) |
| `tests/` | 42 suite files: branching 10, composition 10, javascript 7, http 6, performance 4, correlate 1, events 1, logging 1, single-outcome 1, storage-drivers 1 |
| `import-and-publish-workflows.json` | Elsa 3 setup suite that imports and publishes all 36 definitions (Elsa 3 baseline only) |
| `templates/` | 5 files with 17 templates: `elsa-authenticate`, definition import/publish/delete, workflow-instance get/search/not-exists, variable value, and eight `elsa-activity-instance-*` templates |
| `test-files/` | 5 request fixtures, one about 6 MB (`big-mock-data.json`) |
| `environments/dev.json` | Base URLs, credentials, correlation ID and date bound from environment variables; dispatch channels; dispatch wait time |
| `global-configuration.json` | Template search path |
| `pipelines/` | Original Azure pipelines (not used here); they pinned an Elsa 3.5.0 server image and `jtest` 0.1.3 |

Step kinds across suites and templates: 178 `use` (template calls), 98 `http`, 22 `wait` (fixed sleeps), 12 `assert`,
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
| `performance-*` (6 definitions) | 4 `performance/*` suites | No |
| `storage-drivers-variables-*` (2 definitions) | `storage-drivers/variables` | Root: yes |
| `simple-write-hello-world.json` | none | No |

"Yes" means the definition contains a join or wait construct, not that its suites are JOIN-blocked. Per FR-018 a
suite is blocked only when the join is what stops it on Elsa 4.

## Known starting points

- Earlier smoke runs of these 36 definitions on Elsa 4 imported 16 and published 11 with no workflow inputs; that is
  a converter measurement, not a suite result.
- Some JavaScript suites use classic Elsa host functions (`date-methods`, `elsa-functions`, `script-handler-functions`);
  the expression-capability work decides which have Elsa 4 equivalents, and the rest are candidate incompatibilities.
- An open defect makes two definitions fail to publish with a server error on Elsa 4; the plan accounts for it.
