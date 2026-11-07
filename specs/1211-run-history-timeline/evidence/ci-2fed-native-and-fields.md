# Published 2fed composed UX and Fields75 diagnosis

This receipt records source-bound observations, not whole-feature acceptance.
Independent review remains round 3/6. The unpublished follow-up is `21ff0619f4e1f713d1230d6c31f3faa8da702316`, tree `e84438847b396dd138e8fafbb86491b7df458e77`.

## Actual composed run

[CI42141991943](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42141991943),
[composed job127675412946](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42141991943/job/127675412946),
tested merge `21239aec17995b5566f51ffad33443bfa387762b`, published head `2fedfa596cc039a5e0c840eba4457bf149409dee`, dirty false.

- Actual complete runner: **42 passed**, comprising 40 primary and 2 diagnostic journeys.
- Both native Playwright reports: zero unexpected, flaky, skipped or retry cases.
- **416 primary +36 diagnostic PNG/ARIA pairs**; all required 20 Timeline frames exist in each desktop light/dark and dedicated 320px light/dark project.
- All four Timeline journeys retain actual owned-process restart receipts. Diagnostic projects cover repeated/parallel executions, long-history exact targets/new events, retention, passive native bookmark expiry, historical faults/resolution, metadata/payload access and disabled recording.
- Runner explicitly states `visualApproval: requires independent review`. Author opening selected images is not independent acceptance.

Downloadable [artifact13477180207](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42141991943/artifacts/13477180207):
`studio-ux-21239aec17995b5566f51ffad33443bfa387762b-1`, 1,107,871,790 bytes,
SHA-256 `88c89d37068ebdcdb981807ff65df36517744ceb2c5c8e24d2419cef11827232`.
Downloaded once via the author CLI after the connector rejected its size; all 2,840 ZIP members passed CRC validation. Original raw reports, traces and private fixture evidence remain in that archive. No browser/build/test was launched on the Mac to inspect it.

The same source's frontend job passed **3,485 Workflows tests /249 files**, plus the surrounding workspace packages, lint, typechecking and build. Browser shard 1 passed. Shard 2 and queued backend gates were still incomplete when this receipt was written. This is not an all-green CI claim.

## Actual Studio .NET failure

[Job127675412950](https://github.com/elsa-workflows/elsa-foundation/actions/runs/42141991943/job/127675412950) passed **727 tests /0 skipped** and packed the Studio projects, including Decisions. It then failed the strict package inventory because `Extensions/Decisions/studio/Elsa.Studio.Decisions/Elsa.Studio.Decisions.csproj` was absent from the canonical identity list.

Full decoded job log retained locally: 1,338,771 bytes, SHA-256 `10ee6ccc643c115daebe8752ef79254057361614752bc36911151c2f14ee96d1`. The earlier CLI log-download failure is not evidence of successful retrieval; this complete log came from the connector and was retained as passive data.

Follow-up `21ff0619f` adds that actual packable project as an addition after consolidation. The frozen historical consolidation manifest and all inventory/package/asset assertions remain unchanged. Decisions already suppresses dependency packing, so the entry does not claim the optional hosting dependency-metadata contract. Qualification is pending; the original job remains failed.

## Reproduced Fields75 shuffled failure

Original tested merge `e21ca49047d37190a4f572aeeadb7c2d3aa8ad53`, tree `b9b4f41c2b7f7a167e9437d96703bb3729c6c973`, from run42126308467/job127600207878, was recreated in a separate clean Azure worktree with fresh frozen dependencies. Only synchronous context ordinals/call stacks were added to the existing failing mock, and the trace was emitted immediately before its original assertion. Removing the trace restores every original byte/assertion; no wait, retry, skip or assertion was changed.

The actual original seed **1** run reproduced **3,413 passed /1 failed /0 pending in245 files**. Seeds11/29 were not run in this diagnostic. Wrapper exit0 means diagnostic collection completed; the test process exited1.

The raw call trace, before the unchanged pending-activity assertion, records:

1. `write5`, context1, from `prepareAttachedExecution` called by the previous `moveScopeOccurrence`.
2. `denied-execution`, context2, from the current incident-summary load.
3. `mismatch-execution`, context2, from that current load.
4. `transient-execution`, context2, from that current load.

The previous context's extra inspection consumes the mock's first transient failure. The current-context transient lookup then succeeds before the test requests retry. This supports a disposed occurrence-navigation cause; the earlier late initial-metadata-loader hypothesis is superseded by the actual trace.

Original complete archive: 242,159 bytes, SHA-256 `596618b911dd395d1fa432826a4bf3c01642587c6a3917cf6410aa0567edfee2`. Full native JSON/log, call stacks, restored original sources, dependency/source preparation and process receipts are retained. The source was restored clean; finite C06 ownership sampling verified closure without claiming complete ancestry.

Follow-up `21ff0619f` guards current navigation after scope resolution, child-page resolution, dynamic import and preparation, before starting another request. A deferred child-page/unmount regression asserts the existing signal is aborted and no additional inspection, browse or payload call starts. It preserves all original retry/denial/mismatch assertions. Fresh source preparation on Azure passed; negative/restored regression, inventory controls and current shuffled orders remain unqualified at this receipt.

## Remaining acceptance

Canonical #3938's one-line short-editor QA setup is faithfully integrated at `1dd531667`; it is not a production geometry change or current-source browser proof. Final source-bound regression/full suites, provider/migration gates, independent actual UX acceptance, exact-head required CI/review, protected merge and resulting-main verification remain open. No release/deployment or parked-program work is included.
