# Round2 corrections and actual journey qualification

Executing production/host/test source `e3ec219fcc3a93c949d2669fc585bd1e79152abe`; released BPMN0.5.0. All six round2 findings have corrections and verification. The live native-parent compare remains mandatory; the initial child may be TerminalPendingSettlement when its first evaluation already completes. Retained start replay compares the original caller/source/payload/pin identity before requiring a live source for new admission. Provider errors reuse the existing Core unavailable exception, preserving cause/context/cancellation; human inspection reuses the owned reader. Refusals identify their process/element/type. The three new CLR names are shortened without changing wire/family/checkpoint identifiers.

## Red then green

At source0d9ba3856 with only new uncommitted regressions (production unchanged from reviewedbbb21), three BPMN and three EF cases fail for the six expected observations: retired replay404, raw persisted JSON, missing refusal context, and raw provider failures on each read. The actual standard-host Root case also fails after process loss/restart with the Running-only guard. After the fixes, all six focused cases pass in development. Raw commands/TRXs/logs retain these labels; they are not exact committed-source qualification.

At committed executing sourcee3ec219fc, four actual authenticated start→end journeys complete directly and through Root, Sequence, Flowchart and CompensationScope. Each native invocation first retains its real typed wait/owed start, loses its owned host, and starts the model after restart. The first receipt is revision1/TerminalPendingSettlement with the native result owed; the exact parent consumes it, source ACKs settle revision2, and a third process retains the same result/pins/whole execution view. Each native successor completes where present.16 actual whole SQLite stores in8 coupled Runtime/Design pairs,12 stopped hosts/recorded137 exits, complete API/physical observations and current product hashes are retained. The read-only verifier passes all four cases and rejects24 copied semantic-observation controls. These controls validate the evidence, not substitute for the actual hosts or the original failing production behavior.

## Whole-project gates

| Project | Passed / executed | Skipped |
|---|---:|---:|
| BPMN |465/465|0|
| Runtime |3078/3078|0|
| Actual hosting |37/37|0|
| Runtime EF |1782/1782|0|
| Architecture |594/594|0|
| Native-provider project |209/209|0|

Total6165 passes. Raw discovery reconciles every executed method; existing runtime-only theory expansion explains3067→3078 and585→594. No discovered method is missing. The native-provider suite requires test-owned engines and clears all three connection overrides: PostgreSQL135, SQL Server36 and MySQL35 cases, plus three registration/SQLite-plan cases. This provider/transaction proof remains distinct from actual SQLite host upgrades or ETL source/destination connectors. Maps and123-project tiers pass.

The whole37-host suite repeats actual saved legacy families and both Design histories; earlier portable legacy/Design repair and Studio receipts keep their sources. Only the16 new immediate-completion stores are duplicated here; other repeat-run store payloads stay local, with no portability claim for those duplicates. All104 frozen assets are unchanged. The source-boundary ledger confirms the legacy adapter, authored/UI paths and shared Design/EF migration repair are unchanged from actual Studio source4f46; native helper/container changes are CLR-only. The new direct-model API/driver and non-null model-parent condition do not invalidate those existing Studio journeys. No new current-source browser run is claimed; first-class direct-model Studio remains open.

## Remaining delivery

The fresh API audit has Core180 additions/0 removals, Runtime4/0 and four manually reviewed shared-EF declarations/0 removals against actual published records. The extension-container rename affects only an unpublished addition. No new public exception is introduced. Calculator succeeds with Core4.0.56/Runtime4.0.70/sharedEF4.0.33 proposals; magnitude clearance and binary ApiCompat are not claimed. The existing owner card remains unanswered.

Two full-PR rounds of6 completed; round3 is next, with no clean-loop or merge clearance yet. T042 magnitude/needs:decision, deferred GitGuardian39189012/scan, final reviewed-head required CI, protected merge and resulting-main verification remain open. #2898/#2907/#2908/program remain open/Partial; upgraded-host/wider ETL, direct-model Studio and other approved program scope remain due. #3242 and Orleans/task-runner deferrals are unchanged. Temporary producer paths in raw receipts/scripts are historical provenance; reproduction uses the repository host tests, fresh evidence roots and current standard assemblies.
