# Actual saved-broadcast upgrade

Executing host and checked-reader source: `facffc859ce01a2d4e3fe85be52bcb5b5b285998`. The actual [pre-writer matrix](../../baseline/manifest.json) supplies four combined Runtime/Design stores written by `bf83b23b4b3987d6ac3b7c31df98ee9133156763`. Original captures remain unchanged. The production Workbench contains no recovery probe; its normal resumption pump reclaims each original source under fencing token 2 after its natural deadline. No row or clock is edited in the original or upgraded positive runs.

| Actual cut | Recovered keyed starts | Recovered saved callbacks | Required boundary |
|---|---:|---:|---|
| Before first selection | 2 | 4 | Include the listeners/binding created after publisher commit; exclude waits created by those starts |
| After cohort installation | 1 | 2 | Preserve the installed decision and leave the later listener untouched |
| After one callback and re-arm | 1 | 2 | Preserve the accepted callback/attempt, its new physical wait and the already parked keyed start; apply only the remaining callback |
| Installed empty cohort | 0 | 0 | Preserve the nonnull empty decision despite later matches |

Actual checked EF observations prove source fingerprint/payload, full pins, selected bookmark identities/generations, completed selected callbacks, exact keyed starts and excluded/re-armed waits. A second process preserves the complete acknowledged source record and checked recipient boundary. This is stronger than sender acknowledgment alone. The [process receipt](legacy-broadcast/process.json) retains actual HTTP views, checked proofs, twelve process identities, source-bound binaries and eight consistent upgraded snapshots. Each store retains all five original executable payloads/incarnations and its applied migration history. Both actual published BPMN descriptors match the [frozen authoring fingerprint](frozen-contract.json).

Normal Events complete the parked keyed starts. The generic HTTP stimulus endpoint retains its existing `Ambiguous` result when an excluded workflow has two matching waits. Those explicit refusals and the original infinite-loop fixtures retire through the existing native `CancelWorkflow` command. The receipt records the exact cancellation targets and terminal plan. Across the four independent cuts, eleven executions finish Completed and five retire Cancelled; all sixteen have no remaining bookmarks or incidents. A third process preserves every final API view. This does not introduce or claim a generic HTTP broadcast operation.

[Qualification](qualification.json): exact-source whole hosting8/8; capture/upgrade verifier37/37 with the original baseline supplied, zero skips; maps/tier checks pass. Eight new copied-evidence controls refuse a dropped empty decision, omission of a delivery-time listener, expanded installed cohort, repeated accepted callback, changed physical re-arm generation, physically undelivered source, noncompleted physical callback and physically nonterminal final workflow after assets/receipts are rehashed. Physical checks follow the existing adapters: text activity status, raw bookmark identities with Revision as generation, and encoded workflow/outbox identities. Portable verification was corrected against those actual layouts after the host journey passed.

```sh
python3 tools/bpmn/qualify-hosting.py resume-upgrade --family broadcast \
  --manifest specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json \
  --output /tmp/elsa-bpmn-broadcast-upgrade
python3 tools/bpmn/qualify-hosting.py verify-upgrade \
  --manifest specs/1017-direct-bpmn-hosting/evidence/upgrade/broadcast/manifest.json
```

This advances T030 for legacy native/BpmnProcess broadcasts. It does not prove new direct-model broadcast dispatch or settlement, real native/ETL bindings, other providers or clustered placement. Legacy controls, active multi-instance and actual Studio remain open before pipeline integration; final qualification must be repeated after affected delivery changes. T030, #2898, #2907 and Program#2892 remain partial; full-PR review rounds are zero.
