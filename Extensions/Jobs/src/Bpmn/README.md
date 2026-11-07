# Jobs BPMN integration

`JobsBpmnFeature` is optional. It combines `JobsWorkflowsRuntime` and `ActivitiesBpmnHosting`;
the standalone Jobs client, worker and persistence packages do not reference it.

An awaited `RunJob` root in a closed, pinned native service worker belongs to the actual BPMN
model root. Its definition gate uses the process's exact ordinal `ProcessId`, shared across material
versions. Raw model publication therefore chooses that logical identity explicitly; authored direct
projection already freezes the authored definition ID there. Jobs identities must fit 256 characters;
they are refused rather than truncated. The material version, complete pin and incarnation stay
separate. The first supported service policy is explicit `FinalFault` with one native worker attempt;
Jobs continues to own its physical handler retries.

The bridge proves an accepted exact root input and retained native terminal source before retiring
Jobs delivery. A matching rejected transport receipt or helper completion alone cannot do that.
Positive exact-root terminality can settle delivery without inventing a native consumption receipt.
The existing Jobs continuation grant is checked before live BPMN progression, including a consumed
cancelled JobResult. Native/root cancellation settlement and existing input replay retain their own
runtime paths. No worker reconciliation intent waits for root acknowledgement.

Before the existing BPMN native-result source becomes Delivered, an optional delivery contribution asks
the native Jobs authority to confirm the selected result's durable acknowledgement or positive proof that
the worker never admitted a job. Failed reads or unsettled responsibility keep that same source owed.
Its native history therefore remains available through the provider's required-source deletion fence.
Rejected input at a live root cannot acknowledge a selected Jobs result; exact root terminality permits cleanup.

This source slice does not add generic BPMN input, variable or output mapping. A genuine direct
BPMN witness uses a useful literal worker input and retains the typed native result in its root input.
Current-source execution and model acceptance remain pending.

## Cross-domain contributions

`JobsBpmnAssociationSource` contributes to Jobs' native `IJobWorkflowAssociationSource` aggregation.
`JobsBpmnResultValidator` contributes to BPMN's `IBpmnNativeWorkResultValidator` fan-in immediately
before live native-result advancement. Both use public owner projections; neither reads private
BPMN documents or writes a second model result ledger.
`JobsBpmnResultDeliveryContributor` consumes the public native settlement authority from the existing
BPMN result-delivery source; it adds no independent worker ACK intent.

Independent and detached service submissions use a closed pinned `SubmitJob` worker. Its typed
`PendingAdmission` completion is a durable model handoff rather than a Jobs acceptance receipt.
Before live service-result progression, the optional adapter requires the native submission inspector
to prove the exact canonical protected origin, request and returned JobId. A missing or conflicting
admission remains owed. These submissions have no selected-result wait or continuation grant.

An independent submission keeps the genuine BPMN root as its lifecycle owner after the helper
completes. The public model consumer supplies exact Active/Completed/Faulted/Cancelled observations;
terminal observations require its retained terminal receipt. Explicit cancellation propagates to
independent work, while detached work retains its association without inheriting cancellation.
The existing result delivery contributor settles only the submission admission responsibility, so
the owning root can complete before the separate independent-lifetime responsibility retires.
A rejected transport receipt alone does not release that responsibility. A completed helper whose
submission was cancelled before admission can settle only with positive root-terminal proof.
