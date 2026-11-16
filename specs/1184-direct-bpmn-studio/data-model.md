# Data model

Use existing durable aggregates/stores; no new database table.

| Entity | Identity/fields | Validation/relationship |
|---|---|---|
| Authored process | Existing definition, draft/version, root BpmnProcess/element IDs | Exact immutable supported root for direct publish. Native child identity keeps its meaning. |
| Direct binding | Element property `elsa.direct.binding`; formatVersion1; human with claimRequired, or nativeService with full pin/sourceRef/business-error/fault policy | Strict version/kind/size/shape at publication. Reject incompatible child/listener intent. Exact JSON survives save/version/XML. |
| Published process | Existing model pin/incarnation and Published sourceRef; source version/label/layout | Stable authored ProcessId; immutable source version; runtime uses material, not Design. Changed content/source conflicts under same operation. |
| Catalog page | Items/opaque continuation; protected initial source clock | One raw page, scoped authorization, empty filtered page permitted, no total/snapshot promise. |
| Inspection | Execution/revision/protocol/pin/Authority/lifecycle; source/graph/current work/attempts/incidents/settlement | Authorized persisted projection; no synthetic native parent or invented history. |
| Human claim | Opaque ID, exact invocation/attempt, normalized claimant, accepted input, current/obsolete | Model frame15; CAS selects one owner; no timed lease. Authority is not claimant. Obsolete work cannot complete. |
| Service attempt/incident | Logical invocation, physical attempt/native run, budget/fault/outcome/retry | Preserve original failures; one accepted retry yields one replacement attempt. |
| ETL link | Existing RunId and result-address workflow/activity execution IDs | Authorized server identity projection; backend/tenant scoped; pipeline token/worker pin remain authoritative. |

## Transitions

Authoring: draft → save/reopen → promote exact state → explicit direct publication. Ordinary native publication remains available.

Execution: pending admission → admitted/running → wait or held service failure → semantic terminal with possible settlement → fully settled. Pending is not Running. Retain accepted input receipts.

Claim-enabled work: unclaimed → one accepted claim → exact completion or obsolescence by interruption/cancel. Same operation replays; changed payload, wrong attempt or foreign claim is refused without semantic progression. Claim survives browser/host loss without reassignment.

Legacy material/state needs neither claims nor payload rewrite. New frame/profile fences older readers. Admission-off preserves recovery of accepted claim-bearing work.

## Retention and privacy

Keep existing source/dependency, original incident, receipt and downstream-obligation retention. Do not expose foreign-resource counts or connector values in navigation, or put claimant in Authority. No new expiration/reassignment task.
