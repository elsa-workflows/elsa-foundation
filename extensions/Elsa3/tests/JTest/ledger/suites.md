# Suites

One row per suite file under `tests/` (spec 1235, data-model.md). Results: see data-model.md for the allowed values.

| Suite | Definitions | Elsa 3 result | Elsa 4 result | Last run |
|---|---|---|---|---|
| `branching/fork-parallel-tests.json` | branching-fork-parallel.json | not run | not run | - |
| `branching/if-then-else-tests.json` | branching-flows.json | not run | not run | - |
| `branching/implicit-tests.json` | branching-implicit.json | not run | not run | - |
| `branching/join-wait-all-active-tests.json` | branching-wait-all-active.json | not run | not run | - |
| `branching/join-wait-all-tests.json` | branching-wait-all.json | not run | not run | - |
| `branching/join-wait-any-tests.json` | branching-wait-any.json | not run | not run | - |
| `branching/switch-match-any-mode-tests.json` | branching-flows.json | not run | not run | - |
| `branching/switch-match-first-mode-tests.json` | branching-flows.json | not run | not run | - |
| `branching/switchflow-case-default-tests.json` | branching-flows.json | not run | not run | - |
| `branching/switchflow-case-fail-tests.json` | branching-flows.json | not run | not run | - |
| `composition/bulk-dispatch-workflow-channels-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | not run | not run | - |
| `composition/bulk-dispatch-workflow-handling-handling-await-completion-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | not run | not run | - |
| `composition/bulk-dispatch-workflow-handling-handling-correlation-id-function-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | not run | not run | - |
| `composition/bulk-dispatch-workflow-handling-handling-input-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | not run | not run | - |
| `composition/bulk-dispatch-workflow-handling-handling-on-completed-callbacks-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | not run | not run | - |
| `composition/dispatch-workflow-awaitCompletion-tests.json` | composition-dispatch-root-workflow.json, composition-dispatch-sub-workflow.json | not run | not run | - |
| `composition/dispatch-workflow-channels-tests.json` | composition-dispatch-root-workflow.json, composition-dispatch-sub-workflow.json | not run | not run | - |
| `composition/dispatch-workflow-handling-input-output-tests.json` | composition-dispatch-root-workflow.json, composition-dispatch-sub-workflow.json | not run | not run | - |
| `composition/execute-workflow-tests.json` | composition-execute-workflow-root.json, composition-execute-workflow-sub-workflow.json | not run | not run | - |
| `composition/workflow-as-activity-tests.json` | composition-workflow-as-activity-root.json, composition-workflow-as-activity-sub.json | not run | not run | - |
| `correlate/correlate-guid-tests.json` | correlate-guid.json | not run | not run | - |
| `events/basic-event-tests.json` | events-listener.json, events-publisher.json | not run | not run | - |
| `http/form-files-tests.json` | http-endpoints.json | not run | not run | - |
| `http/headers-tests.json` | http-endpoints.json | not run | not run | - |
| `http/multiple-supported-methods-tests.json` | http-endpoints.json | not run | not run | - |
| `http/query-parameter-tests.json` | http-endpoints.json | not run | not run | - |
| `http/request-body-tests.json` | http-endpoints.json | not run | not run | - |
| `http/route-parameter-tests.json` | http-endpoints.json | not run | not run | - |
| `javascript/array-methods-tests.json` | javascript-array-methods.json | not run | not run | - |
| `javascript/date-methods-tests.json` | javascript-date-methods.json | not run | not run | - |
| `javascript/elsa-functions-tests.json` | javascript-elsa-functions.json | not run | not run | - |
| `javascript/json-methods-tests.json` | javascript-json-methods.json | not run | not run | - |
| `javascript/object-methods-tests.json` | javascript-object-methods.json | not run | not run | - |
| `javascript/script-handler-functions-tests.json` | javascript-script-handler-functions.json | not run | not run | - |
| `javascript/specialized-methods-tests.json` | javascript-specialized-methods-workflow.json | not run | not run | - |
| `logging/logging-tests.json` | logging-workflow.json | not run | not run | - |
| `performance/baseline-dispatch-tests.json` | performance-baseline-dispatch-sub.json, performance-baseline-dispatch.json | excluded | excluded | - |
| `performance/big-data-tests.json` | performance-bigdata.json | not run | not run | - |
| `performance/commit-after-every-activity-tests.json` | performance-commit-after-every-activity.json | not run | not run | - |
| `performance/concurrency-tests.json` | performance-concurrency.json, simple-write-hello-world.json | not run | not run | - |
| `single-outcome/single-outcome-tests.json` | single-outcome-activities.json | not run | not run | - |
| `storage-drivers/variables-tests.json` | storage-drivers-variables-root-workflow.json, storage-drivers-variables-sub-workflow.json | not run | not run | - |

## Notes

- `performance/baseline-dispatch-tests.json` and its two definitions are excluded from acceptance (spec Q13).
- `javascript/specialized-methods-tests.json` carries a `workflowDefinitionId` global of `c7576b41b8d5185e`, which is the logging workflow's id; the suite runs `javascript-specialized-methods-workflow.json` through its endpoint.
