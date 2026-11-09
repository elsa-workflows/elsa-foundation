# Native Rules delivery proof checkpoint

This checkpoint extends the [intent/retry boundary](native-retry-checkpoint.md). It implements the result-delivery executor and native proof validator as internal components. It does not register or host them, and does not complete T027–T032 or claim persisted Sequence/Flowchart, REST, package-host or upgrade acceptance.

## Delivery responsibility

`RulesNativeDeliveryExecution` claims an existing Rules delivery lease and reads the frozen terminal and invocation view. A fresh native persistence scope checks the exact workflow, tenant, partition, executable pin, activity, compiled resume target and bookmark registration. If the native wait is still live, the executor dispatches the complete frozen result through the ordinary bookmark dispatcher and inspects again in another scope. Dispatcher status never permits acknowledgement. Missing proof leaves the obligation intact; lease expiry is the existing retry path. There is no second mailbox, queue or cross-store transaction.

Before acknowledgement commits, `RulesNativeDeliveryProofValidator` independently opens another native scope using the invocation retained by the Rules store. It compares the exact candidate evidence with a fresh inspection. The Rules store then rechecks its own frozen envelope, ownership and lease. Provider failures become unavailable validation and retain responsibility. Requested cancellation propagates.

## Native proof

- A live wait requires the retained private request and full invocation fingerprint, exact address metadata, bookmark and trigger registration. A consumed in-flight receipt is insufficient and is not dispatched again.
- Successful/refused consumption requires one matching consumed receipt, the exact final ended resume attempt, the native completion's invocation/attempt/contract identity and outcome, and the complete result compared canonically after native JSON deserialization. Inline and external result envelopes are supported. Altered facts remain a mismatch even if their digest is recomputed.
- The completed native handler retires private state and may retire the consumed trigger payload. Address metadata and the final completion/attempt/receipt remain usable. In that retired state, semantic invocation equivalence is established transitively: `RunRules.ResumeAsync` reconstructs the full frozen invocation and verifies its fingerprint against the immutable Rules row before producing the exact native terminal outcome. The inspector does not claim a separately retained semantic fingerprint after private-state retirement.
- Fault consumption requires the exact fault code and Rules terminal digest marker in normalized fault metadata. A cancelled child alone never proves parent termination. A terminal-parent proof binds the actual persisted workflow identity, tenant, partition, executable pin, terminal status and completion timestamp; it does not alter the model result.
- The retained-history ceilings follow the native activation claimer's existing 32-attempt/32-delivery compaction limits. Nonoverlapping prior retries are accepted. Compiled resume target IDs are opaque and must not be equated with the static handler key.

## Review corrections

The first review found that the completion projector and final attempt capture their timestamps independently. Equality across those two clock reads would strand legitimate completions. The inspector now checks the result projection falls within the final attempt and that the activity completion matches that attempt's end. Tests use different projection/end timestamps. Review also corrected the opaque-target assumption and required the existing native contract identity.

The first compile exposed an ambiguous transition enum and immutable-record construction errors in the new fixture; these were corrected. The initial executable tests also exposed two fixture mistakes: private-state JSON must use native default casing, and refusal settlement requires an actual refusal validator decision. Neither production assertions nor store refusal rules were weakened.

## Evidence boundary

The focused proof tests construct native state and exercise the real inspector/validator. Executor tests use a migrated SQLite Rules database, real Rules leases, settlement and acknowledgement, the real native validator, and controlled native stores/dispatcher. They verify separate disposed operation scopes, unavailable reads before and during acknowledgement validation, reclaim/recovery, full-content mismatch, terminal-parent proof, and proof changing between observation and acknowledgement. Native stores in this fixture are doubles; these are not full persisted composite or process-restart acceptance tests.

The source-removal control removes only the successful-completion canonical-content comparison. Both the direct altered-result test and the SQLite-backed executor altered-result test fail. The exact source is restored before the complete Rules suite runs. This component control does not complete T032's native REST/package-host proof-before-acknowledgement gate.

## Verification and next step

The first architecture run flagged the new evidence document as untracked and a Rules assembly as older than a subsequent comment-only source edit. The intended files were staged and the assembly rebuilt before repeating the unchanged guards; no assertions or scan scope were weakened.

Final intact validation passed: **258 Rules tests and 585 architecture tests**, zero failures/skips. Local map generation and the committed solution-filter check passed. Both findings reports were inspected: no runtime-to-design signals or missing catalog links. Generated documentation maps remain ignored and unstaged. Three bounded review rounds ended clean for this component scope. Existing analyzer/package warnings remain visible; none were suppressed.

See the [machine-readable receipt](native-delivery-receipt.json) for final commands, counts and source/log hashes. T027–T032 remain unchecked. The next integration slice is delivery worker hosting and normal feature registration, followed by real published Sequence and Flowchart journeys, REST acceptance and six-module package/upgrade qualification. General task-runner and Orleans implementation remain outside this slice. External effects remain at least once.
