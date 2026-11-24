# Suites

One row per suite file under `tests/` (spec 1235, data-model.md). Results: see data-model.md for the allowed values.

| Suite | Definitions | Elsa 3 result | Elsa 4 result | Last run |
|---|---|---|---|---|
| `branching/fork-parallel-tests.json` | branching-fork-parallel.json | pass | not run | 11 |
| `branching/if-then-else-tests.json` | branching-flows.json | pass | not run | 11 |
| `branching/implicit-tests.json` | branching-implicit.json | excluded (coordinator, 2026-11-24: re-evaluate on Elsa 4) | excluded | 4 |
| `branching/join-wait-all-active-tests.json` | branching-wait-all-active.json | excluded (coordinator, 2026-11-24: re-evaluate on Elsa 4) | excluded | 4 |
| `branching/join-wait-all-tests.json` | branching-wait-all.json | pass | not run | 11 |
| `branching/join-wait-any-tests.json` | branching-wait-any.json | pass | not run | 11 |
| `branching/switch-match-any-mode-tests.json` | branching-flows.json | pass | not run | 11 |
| `branching/switch-match-first-mode-tests.json` | branching-flows.json | pass | not run | 11 |
| `branching/switchflow-case-default-tests.json` | branching-flows.json | pass | not run | 11 |
| `branching/switchflow-case-fail-tests.json` | branching-flows.json | pass | not run | 11 |
| `composition/bulk-dispatch-workflow-channels-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | pass | not run | 11 |
| `composition/bulk-dispatch-workflow-handling-handling-await-completion-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | pass | not run | 11 |
| `composition/bulk-dispatch-workflow-handling-handling-correlation-id-function-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | pass | not run | 11 |
| `composition/bulk-dispatch-workflow-handling-handling-input-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | pass | not run | 11 |
| `composition/bulk-dispatch-workflow-handling-handling-on-completed-callbacks-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | pass | not run | 11 |
| `composition/dispatch-workflow-awaitCompletion-tests.json` | composition-dispatch-root-workflow.json, composition-dispatch-sub-workflow.json | pass | not run | 11 |
| `composition/dispatch-workflow-channels-tests.json` | composition-dispatch-root-workflow.json, composition-dispatch-sub-workflow.json | pass | not run | 11 |
| `composition/dispatch-workflow-handling-input-output-tests.json` | composition-dispatch-root-workflow.json, composition-dispatch-sub-workflow.json | pass | not run | 11 |
| `composition/execute-workflow-tests.json` | composition-execute-workflow-root.json, composition-execute-workflow-sub-workflow.json | pass | not run | 11 |
| `composition/workflow-as-activity-tests.json` | composition-workflow-as-activity-root.json, composition-workflow-as-activity-sub.json | pass | not run | 11 |
| `correlate/correlate-guid-tests.json` | correlate-guid.json | pass | not run | 11 |
| `events/basic-event-tests.json` | events-listener.json, events-publisher.json | pass | not run | 11 |
| `http/form-files-tests.json` | http-endpoints.json | pass | fail (definition refused: `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`) | 11, 12 |
| `http/headers-tests.json` | http-endpoints.json | pass | fail (definition refused: `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`) | 11, 12 |
| `http/multiple-supported-methods-tests.json` | http-endpoints.json | pass | fail (definition refused: `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`) | 11, 12 |
| `http/query-parameter-tests.json` | http-endpoints.json | pass | fail (definition refused: `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`) | 11, 12 |
| `http/request-body-tests.json` | http-endpoints.json | pass | fail (definition refused: `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`) | 11, 12 |
| `http/route-parameter-tests.json` | http-endpoints.json | pass | fail (definition refused: `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`) | 11, 12 |
| `javascript/array-methods-tests.json` | javascript-array-methods.json | pass | not run | 11 |
| `javascript/date-methods-tests.json` | javascript-date-methods.json | pass | not run | 11 |
| `javascript/elsa-functions-tests.json` | javascript-elsa-functions.json | pass | not run | 11 |
| `javascript/json-methods-tests.json` | javascript-json-methods.json | pass | not run | 11 |
| `javascript/object-methods-tests.json` | javascript-object-methods.json | pass | not run | 11 |
| `javascript/script-handler-functions-tests.json` | javascript-script-handler-functions.json | pass (base64 cases; `getConfiguration` cases removed, B14) | not run | 11 |
| `javascript/specialized-methods-tests.json` | javascript-specialized-methods-workflow.json | pass | not run | 11 |
| `logging/logging-tests.json` | logging-workflow.json | pass | not run | 11 |
| `performance/baseline-dispatch-tests.json` | performance-baseline-dispatch-sub.json, performance-baseline-dispatch.json | excluded | excluded | - |
| `performance/big-data-tests.json` | performance-bigdata.json | pass | not run | 11 |
| `performance/commit-after-every-activity-tests.json` | performance-commit-after-every-activity.json | pass | not run | 11 |
| `performance/concurrency-tests.json` | performance-concurrency.json, simple-write-hello-world.json | pass | not run | 11 |
| `single-outcome/single-outcome-tests.json` | single-outcome-activities.json | pass | not run | 11 |
| `storage-drivers/variables-tests.json` | storage-drivers-variables-root-workflow.json, storage-drivers-variables-sub-workflow.json | pass | not run | 11 |

## Notes

- `performance/baseline-dispatch-tests.json` and its two definitions are excluded from acceptance (spec Q13).
- **Excluded, to re-evaluate on Elsa 4**: `branching/implicit-tests.json` and `branching/join-wait-all-active-tests.json`,
  with their definitions `branching-implicit.json` and `branching-wait-all-active.json` (coordinator decision,
  2026-11-24). Both encode implicit-merge and `WaitAllActive` join behaviour that 3.6.3 does not show (baseline
  failures B1–B8). What Elsa 4 should do there is decided with the JOIN work (#4193); these suites, or Elsa 4-only
  workflows and suites written for that decision, come back then. They are not run on either engine until then.
  The acceptance set is now 39 suites and 32 definitions.
- `javascript/specialized-methods-tests.json` carries a `workflowDefinitionId` global of `c7576b41b8d5185e`, which is the logging workflow's id; the suite runs `javascript-specialized-methods-workflow.json` through its endpoint.
