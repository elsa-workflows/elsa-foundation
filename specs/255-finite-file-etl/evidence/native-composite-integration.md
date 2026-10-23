# Composite checkpoint: integration with compensation M2

Date: 2026-10-20. Normal merge of main `f08e053060e94425349116d176ef4499bdd7e11a` into ETL checkpoint `4b9cb5194c4663f111a91cc64ce65387438be52c`, within the existing #3109 / draft #3111 lane. No rewrite or separate product implementation.

## Preserved behavior

Main #3113/#3117 add operation keys, retry successors, compensation state v2, uncertain outcomes, handler retries and intervention. Main #3115 fixes Event publication/resumption. The two conflict resolutions preserve these behaviors alongside the new immutable child scheduling metadata:

- `ActivityChildFaultedContext` retains main's optional fault-code/category constructor. Its additive eight-argument overload also snapshots `ChildSchedulingMetadata`; the original signature delegates with an empty metadata snapshot.
- The parent handler passes the committed child's fault code, category **and** scheduling provenance. Completion passes committed child metadata too. No command metadata is merged.
- CompensationScope keeps main's full state-v2 retry/uncertainty implementation, with both completion/fault metadata reads using the callback snapshot.
- Main's direct uncertainty/upcast tests now supply child metadata on the callback, separately from the delivery command. Their assertions remain intact. The initial integrated run passed 60 and failed 6 because those direct test adapters still used the old command-only pattern; correcting all three sites passed the complete suite.
- The package README now reflects main's operation-key, uncertain-outcome and bounded retry behavior; it does not promise one physical handler call.

## Final integrated verification

| Gate | Executed result |
|---|---|
| Real SQLite ETL Sequence/Flowchart/CompensationScope/later-fault journeys | 4 passed |
| Full Release ETL | 239 passed |
| Full Release compensation, including state upgrade/restart/operator retry | 66 passed |
| Full Release Activity Runtime, including Event and retry changes | 569 passed |
| Full Release architecture | 579 passed |
| CI-shaped locked Release / isolated Debug restores | Passed |
| Regenerated committed dependency-map and solution-filter checks | Passed |
| Diff and conflict-marker review | Passed |

All executed test cases had zero skips. Commands are the same targeted suite/restore/map/filter commands in the [composite checkpoint](native-composite-checkpoint.md), run against this integrated tree. A full solution test run, rebuilt Workbench ETL HTTP journey, browser product acceptance and two-OS-process recovery were not run here. CI on the new merge head is required; earlier head73's still-running backend CI and passed Studio checks are not a substitute.

The [integrated regression receipt](composite-integration-regressions.json) proves both intents against final source:

1. Restore the old compensation command-metadata lookup: 2 repeated-delivery cases fail (2 still pass); exact source restoration passes all 4.
2. Drop the landed fault-category propagation while retaining child metadata: the real runtime lost-response case fails, leaving the reservation; exact restoration passes that case.

Root integration review/fix loop: three passes, ending with no remaining actionable integration finding. Worker review supplied input; root reviewed the resolution and ran QA. This does not create an independent-approval gate. The product PR remains draft at 16/36 checked tasks; T020 terminal outcomes/cancellation and the remaining materialization/recovery/API/Studio gates are still open. The [original checkpoint's evidence limits](native-composite-checkpoint.md#evidence-limits-and-next-work) continue to apply. Historical receipt/HTTP timeout causes remain unproven, and external effects remain at least once.

## Final integrated source hashes

```json
{
  "Essentials/src/Activities/Compensation/Activities/CompensationScope.cs": "f4e18dddf251c200be6f0739141340461c1716930692464f2677131daa33c671",
  "Essentials/src/Activities/Runtime/Core/Models/ActivityChildFaultedContext.cs": "b5473bded8dcae78cc9c108c69af9d5ec8ec21cf906eb975b4b87e45a3e27535",
  "Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs": "45a70627b480c15019c2d098c90cde52ee82f5681ad24074bb94f9f4b933d008",
  "Essentials/tests/Activities/Compensation/Tests/CompensationScopeUncertaintyTests.cs": "bc64de99c10b031e72d14f2ecfdc9dcca864d7a85f71b9fdfc30622dfd537841"
}
```
