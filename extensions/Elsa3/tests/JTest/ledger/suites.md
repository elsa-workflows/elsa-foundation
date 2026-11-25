# Suites

One row per suite file under `tests/` (spec 1235, data-model.md). Results: see data-model.md for the allowed values.

| Suite | Definitions | Elsa 3 result | Elsa 4 result | Last run |
|---|---|---|---|---|
| `branching/fork-parallel-tests.json` | branching-fork-parallel.json | pass | blocked: JOIN (#4193; `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED`: WaitAny join whose branches can run at the same time) | 11, 29 |
| `branching/if-then-else-tests.json` | branching-flows.json | pass | pass | 11, 29 |
| `branching/implicit-tests.json` | branching-implicit.json | excluded (coordinator, 2026-11-24: re-evaluate on Elsa 4) | excluded | 4 |
| `branching/join-wait-all-active-tests.json` | branching-wait-all-active.json | excluded (coordinator, 2026-11-24: re-evaluate on Elsa 4) | excluded | 4 |
| `branching/join-wait-all-tests.json` | branching-wait-all.json | pass | blocked: JOIN (#4193; `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED`: WaitAll join where not every branch provably runs) | 11, 29 |
| `branching/join-wait-any-tests.json` | branching-wait-any.json | pass | blocked: JOIN (#4193; `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED`: WaitAny join whose branches can run at the same time) | 11, 29 |
| `branching/switch-match-any-mode-tests.json` | branching-flows.json | pass | blocked: #1152 (Switch mode set by an expression; the adjusted copy fixes `MatchFirst`, so only case 1 of 2 runs) | 11, 29 |
| `branching/switch-match-first-mode-tests.json` | branching-flows.json | pass | pass | 11, 29 |
| `branching/switchflow-case-default-tests.json` | branching-flows.json | pass | pass | 11, 29 |
| `branching/switchflow-case-fail-tests.json` | branching-flows.json | pass | pass | 11, 29 |
| `composition/bulk-dispatch-workflow-channels-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | pass | incompatible (`ELS3-ACTIVITY-NO-COUNTERPART`: BulkDispatchWorkflows) | 11, 29 |
| `composition/bulk-dispatch-workflow-handling-handling-await-completion-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | pass | incompatible (`ELS3-ACTIVITY-NO-COUNTERPART`: BulkDispatchWorkflows) | 11, 29 |
| `composition/bulk-dispatch-workflow-handling-handling-correlation-id-function-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | pass | incompatible (`ELS3-ACTIVITY-NO-COUNTERPART`: BulkDispatchWorkflows) | 11, 29 |
| `composition/bulk-dispatch-workflow-handling-handling-input-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | pass | incompatible (`ELS3-ACTIVITY-NO-COUNTERPART`: BulkDispatchWorkflows) | 11, 29 |
| `composition/bulk-dispatch-workflow-handling-handling-on-completed-callbacks-tests.json` | composition-bulk-dispatch-root-workflow.json, composition-bulk-dispatch-sub-workflow.json | pass | incompatible (`ELS3-ACTIVITY-NO-COUNTERPART`: BulkDispatchWorkflows) | 11, 29 |
| `composition/dispatch-workflow-awaitCompletion-tests.json` | composition-dispatch-root-workflow.json, composition-dispatch-sub-workflow.json | pass | blocked: #5818 (the child faults, so the root writes no response) and #1152 (Dispatch Workflow `Result` not carried over) | 11, 29 |
| `composition/dispatch-workflow-channels-tests.json` | composition-dispatch-root-workflow.json, composition-dispatch-sub-workflow.json | pass | blocked: #5818 (the child's 500 ms busy-wait exceeds the script memory limit; the awaited Dispatch ends `Faulted`); its output assertion is a value read (value capture, #5647) | 11, 29 |
| `composition/dispatch-workflow-handling-input-output-tests.json` | composition-dispatch-root-workflow.json, composition-dispatch-sub-workflow.json | pass | blocked: #5818 (the child faults, so the root writes no response) and #1152 (Dispatch Workflow `Result` not carried over) | 11, 29 |
| `composition/execute-workflow-tests.json` | composition-execute-workflow-root.json, composition-execute-workflow-sub-workflow.json | pass | blocked: #1152 (captured Execute Workflow result: `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`; the suite asserts the result) | 11, 29 |
| `composition/workflow-as-activity-tests.json` | composition-workflow-as-activity-root.json, composition-workflow-as-activity-sub.json | pass | blocked: #3023 (reusable workflow holds `SetOutput`) | 11, 29 |
| `correlate/correlate-guid-tests.json` | correlate-guid.json | pass | pass | 11, 29 |
| `events/basic-event-tests.json` | events-listener.json, events-publisher.json | pass | blocked: value capture (`output.EventListenerCorrelationIdentifier`, `output.Result`: Elsa 4 returns snapshot trees, no plain-JSON read) | 11, 29 |
| `http/form-files-tests.json` | http-endpoints.json | pass | blocked: #1152 (HttpEndpoint file upload; resource variables #3194); the adjusted copy drops the file reads, so the response holds no files | 11, 29 |
| `http/headers-tests.json` | http-endpoints.json | pass | blocked: #5803 (a header value read as a single string, `Host`, is refused where Elsa 4 expects `string[]`; fix in PR #5813); its activity-state header assertion is a value read (value capture, #5647) | 11, 29 |
| `http/multiple-supported-methods-tests.json` | http-endpoints.json | pass | pass | 11, 29 |
| `http/query-parameter-tests.json` | http-endpoints.json | pass | pass | 11, 29 |
| `http/request-body-tests.json` | http-endpoints.json | pass | pass (XML case per engine, suite-changes W2) | 11, 31 |
| `http/route-parameter-tests.json` | http-endpoints.json | pass | pass | 11, 29 |
| `javascript/array-methods-tests.json` | javascript-array-methods.json | pass | blocked: #5686 (publish fails with VF-STATE-002 on `Body`) | 11, 29 |
| `javascript/date-methods-tests.json` | javascript-date-methods.json | pass | incompatible (`VF-IMP-005`: date parsed from text without an offset) | 11, 29 |
| `javascript/elsa-functions-tests.json` | javascript-elsa-functions.json | pass | incompatible (`VF-IMP-005`: byte helpers) | 11, 29 |
| `javascript/json-methods-tests.json` | javascript-json-methods.json | pass | pass | 11, 29 |
| `javascript/object-methods-tests.json` | javascript-object-methods.json | pass | blocked: #5686 (publish fails with VF-STATE-002 on `Body`) | 11, 29 |
| `javascript/script-handler-functions-tests.json` | javascript-script-handler-functions.json | pass (base64 cases; `getConfiguration` cases removed, B14) | incompatible (`VF-IMP-005`: unknown host function `getConfiguration`) | 11, 29 |
| `javascript/specialized-methods-tests.json` | javascript-specialized-methods-workflow.json | pass | blocked: #5687 (a Promise result evaluates to `{}`) and #5818 (the top-level-await busy-wait exceeds the script memory limit and faults the workflow) | 11, 29 |
| `logging/logging-tests.json` | logging-workflow.json | pass | blocked: #5686 (publish fails with VF-STATE-002 on `Headers`) | 11, 29 |
| `performance/baseline-dispatch-tests.json` | performance-baseline-dispatch-sub.json, performance-baseline-dispatch.json | excluded | excluded | - |
| `performance/big-data-tests.json` | performance-bigdata.json | pass | blocked: value capture (`output.OutputData`, `activityState.Value`/`OutputValue`, `outputs.Result`: snapshot trees; 10 782 items also exceed the snapshot limits) | 11, 29 |
| `performance/commit-after-every-activity-tests.json` | performance-commit-after-every-activity.json | pass | blocked: #5697 (adjusted copy converts; publish fails with `ActivityDataSelection/VariableUnknown` on the loop-variable read) | 11, 29 |
| `performance/concurrency-tests.json` | performance-concurrency.json, simple-write-hello-world.json | pass | incompatible (`ELS3-ACTIVITY-NO-COUNTERPART`: BulkDispatchWorkflows) | 11, 29 |
| `single-outcome/single-outcome-tests.json` | single-outcome-activities.json | pass | incompatible (`ELS3-ACTIVITY-NO-COUNTERPART`: ParallelForEach) | 11, 29 |
| `storage-drivers/variables-tests.json` | storage-drivers-variables-root-workflow.json, storage-drivers-variables-sub-workflow.json | pass | blocked: #3023 (reusable workflow holds `SetOutput`) | 11, 29 |

## Notes

- `performance/baseline-dispatch-tests.json` and its two definitions are excluded from acceptance (spec Q13).
- **Excluded, to re-evaluate on Elsa 4**: `branching/implicit-tests.json` and `branching/join-wait-all-active-tests.json`,
  with their definitions `branching-implicit.json` and `branching-wait-all-active.json` (coordinator decision,
  2026-11-24). Both encode implicit-merge and `WaitAllActive` join behaviour that 3.6.3 does not show (baseline
  failures B1–B8). What Elsa 4 should do there is decided with the JOIN work (#4193); these suites, or Elsa 4-only
  workflows and suites written for that decision, come back then. They are not run on either engine until then.
  The acceptance set is now 39 suites and 32 definitions.
- `javascript/specialized-methods-tests.json` carries a `workflowDefinitionId` global of `c7576b41b8d5185e`, which is the logging workflow's id; the suite runs `javascript-specialized-methods-workflow.json` through its endpoint.
- Corpus defect for the write-back to the origin repository (T047): `branching/switchflow-case-default-tests.json` and
  `switchflow-case-fail-tests.json` list `{{ $.globals.activityIds.httpResponse_Final }}` among the activities that must not
  run, but the key is `writeHttpResponse_Final`, so the entry resolves to nothing. Elsa 3's journal filter ignored it, and
  the Elsa 4 template ignores it the same way; the suite is not changed here.
