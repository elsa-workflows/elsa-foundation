# Definition dispositions

One row per definition (spec 1235, data-model.md). Buckets stay `unclassified` until decided; the two baseline-dispatch definitions are `excluded` (spec Q13).

| Definition | Id / version | Bucket | Reconciliation | Adjusted copy | Incompatibility | Suites affected | JOIN |
|---|---|---|---|---|---|---|---|
| `branching-flows.json` | `123751d0e5707622` v1 | unclassified | - | - | - | `branching/if-then-else-tests.json`, `branching/switch-match-any-mode-tests.json`, `branching/switch-match-first-mode-tests.json`, `branching/switchflow-case-default-tests.json`, `branching/switchflow-case-fail-tests.json` | contains join or fork (not yet assessed) |
| `branching-fork-parallel.json` | `1827851ae82eae1096` v1 | unclassified | - | - | - | `branching/fork-parallel-tests.json` | contains join or fork (not yet assessed) |
| `branching-implicit.json` | `1fd212d023250c3f` v1 | excluded (re-evaluate on Elsa 4) | - | - | - | `branching/implicit-tests.json` | none |
| `branching-wait-all-active.json` | `33427851d00eai876` v1 | excluded (re-evaluate on Elsa 4) | - | - | - | `branching/join-wait-all-active-tests.json` | contains join or fork (not yet assessed) |
| `branching-wait-all.json` | `7c3751d00f707633` v1 | unclassified | - | - | - | `branching/join-wait-all-tests.json` | contains join or fork (not yet assessed) |
| `branching-wait-any.json` | `16253948d00ea5633` v1 | unclassified | - | - | - | `branching/join-wait-any-tests.json` | contains join or fork (not yet assessed) |
| `composition-bulk-dispatch-root-workflow.json` | `ccb613931f8b60a` v1 | unclassified | - | - | - | `composition/bulk-dispatch-workflow-channels-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-await-completion-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-correlation-id-function-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-input-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-on-completed-callbacks-tests.json` | contains join or fork (not yet assessed) |
| `composition-bulk-dispatch-sub-workflow.json` | `32aa8cdf445d8276` v1 | unclassified | - | - | - | `composition/bulk-dispatch-workflow-channels-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-await-completion-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-correlation-id-function-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-input-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-on-completed-callbacks-tests.json` | none |
| `composition-dispatch-root-workflow.json` | `ddd44c7b8d434a3b` v1 | unclassified | - | - | - | `composition/dispatch-workflow-awaitCompletion-tests.json`, `composition/dispatch-workflow-channels-tests.json`, `composition/dispatch-workflow-handling-input-output-tests.json` | none |
| `composition-dispatch-sub-workflow.json` | `52d1bea42e264b2` v1 | unclassified | - | - | - | `composition/dispatch-workflow-awaitCompletion-tests.json`, `composition/dispatch-workflow-channels-tests.json`, `composition/dispatch-workflow-handling-input-output-tests.json` | none |
| `composition-execute-workflow-root.json` | `cd108818fa8414ac` v1 | unclassified | - | - | - | `composition/execute-workflow-tests.json` | none |
| `composition-execute-workflow-sub-workflow.json` | `23004d23f02c0e7f` v1 | unclassified | - | - | - | `composition/execute-workflow-tests.json` | none |
| `composition-workflow-as-activity-root.json` | `b51b573695a207d3` v1 | unclassified | - | - | - | `composition/workflow-as-activity-tests.json` | none |
| `composition-workflow-as-activity-sub.json` | `61a8539ad084c337` v1 | unclassified | - | - | - | `composition/workflow-as-activity-tests.json` | none |
| `correlate-guid.json` | `18276fa5f694bf4c` v1 | unclassified | - | - | - | `correlate/correlate-guid-tests.json` | none |
| `events-listener.json` | `c6fae2759f0fb6e8` v1 | unclassified | - | - | - | `events/basic-event-tests.json` | none |
| `events-publisher.json` | `5b805c0f98b60b08` v1 | unclassified | - | - | - | `events/basic-event-tests.json` | none |
| `http-endpoints.json` | `12758460d794cb4f` v1 | unclassified | - | - | - | `http/form-files-tests.json`, `http/headers-tests.json`, `http/multiple-supported-methods-tests.json`, `http/query-parameter-tests.json`, `http/request-body-tests.json`, `http/route-parameter-tests.json` | none |
| `javascript-array-methods.json` | `bbd827061c3d471e` v1 | unclassified | - | - | - | `javascript/array-methods-tests.json` | none |
| `javascript-date-methods.json` | `8d0a63b4d0aef52` v1 | unclassified | - | - | - | `javascript/date-methods-tests.json` | none |
| `javascript-elsa-functions.json` | `40419e7edde048b6a983` v1 | unclassified | - | - | - | `javascript/elsa-functions-tests.json` | none |
| `javascript-json-methods.json` | `491af4a441029cfa` v1 | unclassified | - | - | - | `javascript/json-methods-tests.json` | none |
| `javascript-object-methods.json` | `f274bc894f698a66` v1 | unclassified | - | - | - | `javascript/object-methods-tests.json` | none |
| `javascript-script-handler-functions.json` | `8942389234hiog234` v1 | unclassified | - | - | - | `javascript/script-handler-functions-tests.json` | none |
| `javascript-specialized-methods-workflow.json` | `7d56ad4719654` v1 | unclassified | - | - | - | `javascript/specialized-methods-tests.json` | none |
| `logging-workflow.json` | `c7576b41b8d5185e` v1 | unclassified | - | - | - | `logging/logging-tests.json` | none |
| `performance-baseline-dispatch-sub.json` | `e3ec3010946b27c1` v1 | excluded | - | - | - | `performance/baseline-dispatch-tests.json` | none |
| `performance-baseline-dispatch.json` | `e3b73ccc31170394` v1 | excluded | - | - | - | `performance/baseline-dispatch-tests.json` | none |
| `performance-bigdata.json` | `e1e1f7b3526f2a7a` v1 | unclassified | - | - | - | `performance/big-data-tests.json` | none |
| `performance-commit-after-every-activity.json` | `e3ddcece1187babf` v1 | unclassified | - | - | - | `performance/commit-after-every-activity-tests.json` | none |
| `performance-concurrency.json` | `f4e543514bd28e61` v1 | unclassified | - | - | - | `performance/concurrency-tests.json` | none |
| `performance-minimal-baseline.json` | `4be9f82f570019c7` v1 | unclassified | - | - | - | none (no suite) | none |
| `simple-write-hello-world.json` | `e1248e15b33efa5a` v1 | unclassified | - | - | - | `performance/concurrency-tests.json` | none |
| `single-outcome-activities.json` | `b82759f221414c088f` v1 | unclassified | - | - | - | `single-outcome/single-outcome-tests.json` | none |
| `storage-drivers-variables-root-workflow.json` | `775ed70076c8d703` v2 | unclassified | - | - | - | `storage-drivers/variables-tests.json` | contains join or fork (not yet assessed) |
| `storage-drivers-variables-sub-workflow.json` | `bd94124913202141` v1 | unclassified | - | - | - | `storage-drivers/variables-tests.json` | none |

## Notes

- `performance-minimal-baseline.json` is imported by the setup suite but no suite or other definition references it.
- `storage-drivers-variables-root-workflow.json` is patched to version 1 in T017 (spec Q14).
- `branching-implicit.json` and `branching-wait-all-active.json` are excluded from acceptance, to re-evaluate on Elsa 4
  with the JOIN work (#4193); coordinator decision 2026-11-24, see `suites.md` Notes.
- `javascript-script-handler-functions.json`: `base64Encode`/`base64Decode` (Nexxbiz Executor V3 host functions) are
  replaced by the Elsa 3 natives `stringToBase64`/`stringFromBase64`, which Elsa 4 has as built-ins and the converter
  keeps as written (coordinator-approved correction, 2026-11-24; to apply in the Nexxbiz workflows too). `getConfiguration`
  stays in the definition: it has no native equivalent (baseline-failures B14). Its suite cases are removed; the definition
  itself still calls it, so the Elsa 4 conversion refuses it (`VF-IMP-005`) until an adjusted copy without that branch is
  approved (T036) or Nexxbiz replaces the call.
