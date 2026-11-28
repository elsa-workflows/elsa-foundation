# Definition dispositions

One row per definition (spec 1235, data-model.md). Buckets stay `unclassified` until decided; the two baseline-dispatch definitions are `excluded` (spec Q13).

| Definition | Id / version | Bucket | Reconciliation | Adjusted copy | Incompatibility | Suites affected | JOIN |
|---|---|---|---|---|---|---|---|
| `branching-flows.json` | `123751d0e5707622` v1 | unclassified | original refused: `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`; adjusted copy published and routes correctly since #5789 was fixed (run 29) | `adjusted/branching-flows.json`: `WriteHttpResponse3` statusCode → literal `ExpectationFailed` (417, the only value the suites send; approved, coordinator 2026-11-24); `SwitchACtivity1` mode → literal `MatchFirst` (`ELS3-ACTIVITY-UNSUPPORTED-MODE`, found after the first fix; approved, coordinator 2026-11-24). 3.6.3: 4/5 suites pass, `switch-match-any` fails as expected | - | `branching/if-then-else-tests.json`, `branching/switch-match-any-mode-tests.json`, `branching/switch-match-first-mode-tests.json`, `branching/switchflow-case-default-tests.json`, `branching/switchflow-case-fail-tests.json` | contains join, not blocking (the WaitAny join converts) |
| `branching-fork-parallel.json` | `1827851ae82eae1096` v1 | unclassified | refused: `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED` | - | - | `branching/fork-parallel-tests.json` | blocked: WaitAny join whose branches can run at the same time (`ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED`) |
| `branching-implicit.json` | `1fd212d023250c3f` v1 | excluded (re-evaluate on Elsa 4) | - | - | - | `branching/implicit-tests.json` | none |
| `branching-wait-all-active.json` | `33427851d00eai876` v1 | excluded (re-evaluate on Elsa 4) | - | - | - | `branching/join-wait-all-active-tests.json` | contains join or fork (not yet assessed) |
| `branching-wait-all.json` | `7c3751d00f707633` v1 | unclassified | refused: `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED` | - | - | `branching/join-wait-all-tests.json` | blocked: WaitAll join where not every branch provably runs (`ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED`) |
| `branching-wait-any.json` | `16253948d00ea5633` v1 | unclassified | refused: `ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED` | - | - | `branching/join-wait-any-tests.json` | blocked: WaitAny join whose branches can run at the same time (`ELS3-ACTIVITY-GRAPH-JOIN-UNSUPPORTED`) |
| `composition-bulk-dispatch-root-workflow.json` | `ccb613931f8b60a` v1 | incompatible | refused: `ELS3-ACTIVITY-NO-COUNTERPART` | - | `ELS3-ACTIVITY-NO-COUNTERPART`: BulkDispatchWorkflows; not yet implemented (#1152, unported activities); reconsider when Elsa 4 has a counterpart; coordinator 2026-11-24 | `composition/bulk-dispatch-workflow-channels-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-await-completion-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-correlation-id-function-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-input-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-on-completed-callbacks-tests.json` | contains join or fork (not yet assessed) |
| `composition-bulk-dispatch-sub-workflow.json` | `32aa8cdf445d8276` v1 | unclassified | published | - | - | `composition/bulk-dispatch-workflow-channels-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-await-completion-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-correlation-id-function-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-input-tests.json`, `composition/bulk-dispatch-workflow-handling-handling-on-completed-callbacks-tests.json` | none |
| `composition-dispatch-root-workflow.json` | `ddd44c7b8d434a3b` v1 | unclassified | original refused: `ELS3-ACTIVITY-REMOVED-INPUT-BOUND`; adjusted copy published since #5790 was fixed (run 29) | `adjusted/composition-dispatch-root-workflow.json`: `channelName` cleared (approved, coordinator 2026-11-24); the `result` capture, removed on 2026-11-24 while Elsa 4 could not carry it, is restored since spec 1245 was implemented (#5999): it converts to the awaited Dispatch's `Output`. 3.6.3: `channels` passes; `awaitCompletion` and `handling-input-output` fail on the dispatched output as expected | - | `composition/dispatch-workflow-awaitCompletion-tests.json`, `composition/dispatch-workflow-channels-tests.json`, `composition/dispatch-workflow-handling-input-output-tests.json` | none |
| `composition-dispatch-sub-workflow.json` | `52d1bea42e264b2` v1 | unclassified | published; its 500 ms busy-wait runs since #5818 was fixed (run 37) | - | - | `composition/dispatch-workflow-awaitCompletion-tests.json`, `composition/dispatch-workflow-channels-tests.json`, `composition/dispatch-workflow-handling-input-output-tests.json` | none |
| `composition-execute-workflow-root.json` | `cd108818fa8414ac` v1 | convert-with-adjustments | original refused: the child id is an expression; with a literal id, `VF-IMP-006` (`return newGuid();;`) and `VF-IMP-005` (start time parsed from text). Adjusted copy published (run 39); its run faults with VF-BIND-001 (#5894) | `adjusted/composition-execute-workflow-root.json`: literal child id `23004d23f02c0e7f`, `return newGuid();`, start time as `Date.now()` and the elapsed time as `Date.now() - getStartTime()` (approved, coordinator 2026-11-28). 3.6.3: the suite passes with the copy (3/3) | - | `composition/execute-workflow-tests.json` | none |
| `composition-execute-workflow-sub-workflow.json` | `23004d23f02c0e7f` v1 | unclassified | published | - | - | `composition/execute-workflow-tests.json` | none |
| `composition-workflow-as-activity-root.json` | `b51b573695a207d3` v1 | unclassified | refused: `ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY` | - | - | `composition/workflow-as-activity-tests.json` | none |
| `composition-workflow-as-activity-sub.json` | `61a8539ad084c337` v1 | unclassified | refused: `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED` | - | - | `composition/workflow-as-activity-tests.json` | none |
| `correlate-guid.json` | `18276fa5f694bf4c` v1 | convert-as-is | published | - | - | `correlate/correlate-guid-tests.json` | none |
| `events-listener.json` | `c6fae2759f0fb6e8` v1 | unclassified | published | - | - | `events/basic-event-tests.json` | none |
| `events-publisher.json` | `5b805c0f98b60b08` v1 | unclassified | published | - | - | `events/basic-event-tests.json` | none |
| `http-endpoints.json` | `12758460d794cb4f` v1 | unclassified | original refused: `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`; adjusted copy published since #5790 was fixed (run 29) | `adjusted/http-endpoints.json`: `WriteHttpResponse2` contentType → literal `application/json` (approved, coordinator 2026-11-24; the suite asserts status codes only, a non-JSON request now gets a JSON body); also approved, coordinator 2026-11-24: the `FormFiles` variable and `files` capture removed and the file-reading script returns `[]` (`ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE`, `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE`), `WriteHttpResponse9` headers read through `getHttpResponseHeaders()` (`VF-IMP-008`). 3.6.3: 4/6 suites pass; `form-files` and `headers` fail as expected | - | `http/form-files-tests.json`, `http/headers-tests.json`, `http/multiple-supported-methods-tests.json`, `http/query-parameter-tests.json`, `http/request-body-tests.json`, `http/route-parameter-tests.json` | none |
| `javascript-array-methods.json` | `bbd827061c3d471e` v1 | unclassified | published since #5686 was fixed (run 36); its response script changes `ArrayVariable` and faults with VF-BIND-001 (#5894) | - | - | `javascript/array-methods-tests.json` | none |
| `javascript-date-methods.json` | `8d0a63b4d0aef52` v1 | convert-with-adjustments | original refused: `VF-IMP-005` (date parsed from text without a provable offset); adjusted copy published (run 34) | `adjusted/javascript-date-methods.json` (made compatible): `WriteHttpResponse2` parses the ISO 8601 text with its explicit offset and builds the instant from `Date.UTC(...)` minus that offset, instead of `new Date(text)`; text without an offset is no longer accepted (approved, coordinator 2026-11-26). 3.6.3: the suite passes (run 33) | - | `javascript/date-methods-tests.json` | none |
| `javascript-elsa-functions.json` | `40419e7edde048b6a983` v1 | convert-with-adjustments | original refused: `VF-IMP-005` (byte helpers); adjusted copy published and its suite passes (run 37) | `adjusted/javascript-elsa-functions.json` (made compatible): the `parseBytes` branch encodes the text to UTF-8 bytes and decodes it back with language syntax (`codePointAt`, bit operations, `String.fromCodePoint`) instead of `bytesFromString`/`bytesToString`, which Elsa 4 does not have (approved, coordinator 2026-11-26). 3.6.3: the suite passes (run 33) | - | `javascript/elsa-functions-tests.json` | none |
| `javascript-json-methods.json` | `491af4a441029cfa` v1 | convert-as-is | published | - | - | `javascript/json-methods-tests.json` | none |
| `javascript-object-methods.json` | `f274bc894f698a66` v1 | unclassified | published since #5686 was fixed (run 36); its response script changes `ObjectVariable` and faults with VF-BIND-001 (#5894) | - | - | `javascript/object-methods-tests.json` | none |
| `javascript-script-handler-functions.json` | `8942389234hiog234` v1 | convert-with-adjustments | original refused: `VF-IMP-005` (unknown host function `getConfiguration`); adjusted copy published (run 34) | `adjusted/javascript-script-handler-functions.json` (made compatible): the branch for method `getConfiguration` returns `null` instead of calling that host function, which is not part of elsa-core; the decisions and the base64 branches are unchanged, and no suite case calls the branch (its cases were removed in T017). Approved, coordinator 2026-11-26 ("Branch returns null"). 3.6.3: the suite passes (run 33) | - | `javascript/script-handler-functions-tests.json` | none |
| `javascript-specialized-methods-workflow.json` | `7d56ad4719654` v1 | unclassified | published | - | - | `javascript/specialized-methods-tests.json` | none |
| `logging-workflow.json` | `c7576b41b8d5185e` v1 | unclassified | published since #5686 was fixed (run 36); its response script changes `HttpResponseBody` and faults with VF-BIND-001 (#5894) | - | - | `logging/logging-tests.json` | none |
| `performance-baseline-dispatch-sub.json` | `e3ec3010946b27c1` v1 | excluded | - | - | - | `performance/baseline-dispatch-tests.json` | none |
| `performance-baseline-dispatch.json` | `e3b73ccc31170394` v1 | excluded | - | - | - | `performance/baseline-dispatch-tests.json` | none |
| `performance-bigdata.json` | `e1e1f7b3526f2a7a` v1 | unclassified | published | - | - | `performance/big-data-tests.json` | none |
| `performance-commit-after-every-activity.json` | `e3ddcece1187babf` v1 | unclassified | original refused: `ELS3-UNTYPED-RESULT`; adjusted copy converts, publish fails: `ActivityDataSelection/VariableUnknown` (#5697) | `adjusted/performance-commit-after-every-activity.json`: `WriteLine1` text → JavaScript `return String(getCurrentIterationValue());`, the converter's guidance (approved, coordinator 2026-11-24). 3.6.3: the suite passes | - | `performance/commit-after-every-activity-tests.json` | none |
| `performance-concurrency.json` | `f4e543514bd28e61` v1 | incompatible | refused: `ELS3-ACTIVITY-NO-COUNTERPART` | - | `ELS3-ACTIVITY-NO-COUNTERPART`: BulkDispatchWorkflows; not yet implemented (#1152, unported activities); reconsider when Elsa 4 has a counterpart; coordinator 2026-11-24 | `performance/concurrency-tests.json` | none |
| `performance-minimal-baseline.json` | `4be9f82f570019c7` v1 | convert-with-adjustments | original refused: `VF-IMP-005` (date parsed from text); adjusted copy published (run 34) | `adjusted/performance-minimal-baseline.json` (made compatible): `StartTime` holds `Date.now()` and the output is `Date.now() - getStartTime()`, the same elapsed milliseconds without parsing date text (approved, coordinator 2026-11-26) | - | none (no suite) | none |
| `simple-write-hello-world.json` | `e1248e15b33efa5a` v1 | unclassified | published | - | - | `performance/concurrency-tests.json` | none |
| `single-outcome-activities.json` | `b82759f221414c088f` v1 | incompatible | refused: `ELS3-ACTIVITY-NO-COUNTERPART` | - | `ELS3-ACTIVITY-NO-COUNTERPART`: ParallelForEach; not yet implemented (#1152, unported activities); reconsider when Elsa 4 has a counterpart; coordinator 2026-11-24 | `single-outcome/single-outcome-tests.json` | none |
| `storage-drivers-variables-root-workflow.json` | `775ed70076c8d703` v2 | unclassified | refused: `ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY` | - | - | `storage-drivers/variables-tests.json` | not assessed: the import stops first on #3023 |
| `storage-drivers-variables-sub-workflow.json` | `bd94124913202141` v1 | unclassified | refused: `ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED` | - | - | `storage-drivers/variables-tests.json` | none |

## Notes

- `performance-minimal-baseline.json` is imported by the setup suite but no suite or other definition references it.
- `storage-drivers-variables-root-workflow.json` is patched to version 1 in T017 (spec Q14).
- `branching-implicit.json` and `branching-wait-all-active.json` are excluded from acceptance, to re-evaluate on Elsa 4
  with the JOIN work (#4193); coordinator decision 2026-11-24, see `suites.md` Notes.
- `javascript-script-handler-functions.json`: `base64Encode`/`base64Decode` (host functions outside elsa-core) are
  replaced by the Elsa 3 natives `stringToBase64`/`stringFromBase64`, which Elsa 4 has as built-ins and the converter
  keeps as written (coordinator-approved correction, 2026-11-24). `getConfiguration`
  stays in the definition: it has no native equivalent (baseline-failures B14). Its suite cases are removed; the definition
  itself still calls it, so the Elsa 4 conversion refuses it (`VF-IMP-005`); bucketed `incompatible` by the coordinator
  on 2026-11-24 (T034).
- **Adjusted copies (T036).** Each copy under `adjusted/` keeps its original's file name, id and version and differs
  only by the changes in its row (`diff workflow-definitions/<file> adjusted/<file>`). The coordinator approved the first
  change of each on 2026-11-24 (content type, status code, channel name). The converter reports one error at a time, so
  each fix showed the next refusal; those further changes follow the converter's own guidance and were approved by the coordinator on 2026-11-24 ("Accept them");
  each row names them. A copy that removes something a suite needs (form files, the dispatched output, `MatchAny`) leaves that
  suite blocked on the tracked gap (#1152) instead of being split or worked around. All four copies now convert; three
  fail at publish on #5686 and one on #5697, so their buckets stay `unclassified` until those are fixed (T034).
  After the initiative merge of run 25 (8c32d2de21) the HTTP header/query part of #5686 no longer stops them:
  `branching-flows` publishes and then routes wrongly (#5789); `http-endpoints`, `logging` and the dispatch root stop
  on #5790; the `Body` case of #5686 still blocks array-methods and object-methods.
- **Still `unclassified`, with the reason**: the definitions behind a blocked suite (`suites.md`): #5686 (6 definitions),
  #5697, #5687, #1152 (`composition-execute-workflow-root.json`), #3023 (workflow-as-activity and storage-drivers pairs),
  JOIN #4193 (fork-parallel, wait-all, wait-any), value capture #5647 (events pair, bigdata). Sub-workflows that publish
  (`composition-*-sub-workflow.json`, `simple-write-hello-world.json`) stay `unclassified` with their root workflow.
- **Made compatible (2026-11-26).** The coordinator reversed the `incompatible` bucket for the four `VF-IMP-005`
  definitions: they are examples of a definition that must be changed to convert. Three copies are approved and in
  `adjusted/` (date-methods, minimal-baseline, elsa-functions); the fourth, `javascript-script-handler-functions.json`,
  was approved the same day with its `getConfiguration` branch returning `null`.

## Migration diagnostics for the adjustments

Rule (coordinator, 2026-11-27): an Elsa 3 pattern the suites show to be incompatible with Elsa 4 gets a small
compatible adjustment here, and the converter must give the original pattern a warning or refusal with guidance that
the adjustment follows. Audit of every adjustment so far:

| Original pattern | Converter diagnostic on the original | Adjustment follows the guidance |
|---|---|---|
| Response content type computed by an expression (`http-endpoints`) | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`: set ContentType to the literal `application/json` | yes |
| Status code from an expression (`branching-flows`) | `ELS3-ACTIVITY-VALUE-OUTSIDE-CONTRACT`: set an explicit positive status | yes |
| Switch mode from an expression (`branching-flows`) | `ELS3-ACTIVITY-UNSUPPORTED-MODE`: set the mode literally or split the switch | yes (mode fixed; MatchAny case tracked in #1152) |
| `channelName` bound (`composition-dispatch-root-workflow`) | `ELS3-ACTIVITY-REMOVED-INPUT-BOUND`: clear the input | yes |
| Dispatch `result` captured | none needed since spec 1245 (#5999): converts to the awaited Dispatch's `Output` | resolved, capture restored |
| Form-file variable and `files` capture (`http-endpoints`) | `ELS3-ACTIVITY-VARIABLE-VALUE-UNCONVERTIBLE` (#3194), `ELS3-ACTIVITY-OUTPUT-NOT-REPRESENTABLE` | yes (form-files suite tracked in #1152) |
| `Object` variable bound to the typed Headers input (`http-endpoints`) | `VF-IMP-008`: convert explicitly in JavaScript | yes |
| Loop value read into a String input (`performance-commit-after-every-activity`) | `ELS3-UNTYPED-RESULT`: convert with `String(...)` | yes |
| Date parsed from text (`javascript-date-methods`, `performance-minimal-baseline`) | `VF-IMP-005`: rewrite with UTC methods or an explicit offset, or set the date interpretation | yes |
| Unknown host function `getConfiguration` (`javascript-script-handler-functions`) | `VF-IMP-005`: replace with language syntax or move into an activity | yes |
| Byte helpers `bytesFromString`/`bytesToString` (`javascript-elsa-functions`) | `VF-IMP-005`: move the conversion into an activity | partly: no Elsa 4 activity converts text to bytes; the copy uses plain language syntax, which the guidance does not mention |
| XML request body (W2, `http/request-body`) | `ELS3-BEHAVIOUR-FORM-POST-NOT-PARSED`: a text body becomes a string | covered |
| Input expression changes a variable it read (`javascript-array-methods`, `javascript-object-methods`, `logging-workflow`, `composition-execute-workflow-root`) | none: faults at run time with VF-BIND-001 | gap, #5894; copies wait for its guidance |
| Execute Workflow child id from an expression (`composition-execute-workflow-root`) | refused: the dispatch target must be literal | yes |
| Script ending in an empty statement, `return newGuid();;` (`composition-execute-workflow-root`) | `VF-IMP-006`, whose guidance (replace computed ambient access) does not say to end the script with a return | partly: guidance wording to file |
| Script result is a Promise (`javascript-specialized-methods-workflow`) | none: Elsa 4 yields `{}` silently | gap, #5687 |
| Time-based busy-wait (`composition-dispatch-sub-workflow`, `javascript-specialized-methods-workflow`) | none needed since #5818 was fixed: the script allocation budget defaults to 512 MiB and both waits run | resolved (#5992) |
