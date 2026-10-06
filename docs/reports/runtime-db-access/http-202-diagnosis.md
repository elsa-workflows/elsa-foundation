# Synchronous HTTP 202 diagnosis (T03)

**Status:** bounded source/capture diagnosis prepared for root and independent review; PR integration remains pending. The historical valid-input 202 is unreproduced and unrepaired. No runtime correction is implemented.

## What 202 establishes

Current [HttpEndpointMiddleware](../../../src/essentials/Activities/Http/Middleware/HttpEndpointMiddleware.cs#L229) selects synchronous dispatch options, routes the stimulus and drains inline. A dispatch exception follows the fault/timeout handler, no dispatched start or resume returns 404, and an already-written response is preserved. Otherwise the middleware attempts committed response-instruction delivery for the dispatched execution IDs when the endpoint is synchronous and its delivery adapter is available. If that delivery does not occur, [the fallback](../../../src/essentials/Activities/Http/Middleware/HttpEndpointMiddleware.cs#L302) returns HTTP 202 with `started` and `resumed` IDs.

[HttpResponseInstructionDelivery.TryDeliverAsync](../../../src/essentials/Activities/Http/Services/HttpResponseInstructionDelivery.cs#L26) reads the dispatched executions' activity states, selects completed states with an inline typed response-instruction result, and writes the selected instruction to the live HTTP response. [StimulusRouter](../../../src/essentials/Workflows/Runtime/Services/Triggers/StimulusRouter.cs#L73) snapshots waiting resumes before starting matching triggers, then dispatches the resumes.

These source mechanisms explain the branch. They do not identify why the historical valid-body request reached it. In particular, 202 does not prove successful computation, terminal completion, a fault, a waiting bookmark, or the absence of a response instruction at a later inspection time. There is no blanket lifecycle or HTTP fault-policy change in this diagnosis.

## Positive control actually captured

The corrected pre-fix Coalesced reference in [PR #2436](https://github.com/elsa-workflows/elsa-foundation/pull/2436) is a separate representative request, not a replay of the historical anomaly. The captured binary was built at `5d28bd0cd3d76004988a01b0f0abd7af3af313d6`; its relevant runtime source equals main `43b3ef51882105916380ac8e539b49ad7b8a37b6`. The manual HTTP script ran at `3c6bd3da83c49ea84c9823c8d0181b46c468f881`. Source review at `b3f55bef32576011d39b93d664949a0cb705fb3b` confirmed the middleware, delivery adapter and router are unchanged.

| Evidence | Observed result |
|---|---|
| Fixture/input | Sequence / synchronous HttpEndpoint / SetVariable / WriteHttpResponse; `firstName=Alice`, `lastName=Smith` |
| Request identity | Trace `e5acc2e4ccc5b19b7771ec7d3ec1cd97`, request span `4d76eb7a0bb77d49` |
| Execution/artifact | `147eTn6gBLT` / `artifact-bc9dfcd40653` |
| Client response | HTTP 200, text `Alice Smith`; request finished `2026-10-06T01:22:04.462Z` |
| Effective settings | Coalesced, maximum segment checkpoints 50, boundary-level inspection |
| Response activity | `WriteHttpResponse` invocation `147eUJaXm71` Completed at `01:22:04.007994Z`, committed at `01:22:04.008028Z` |
| Workflow readback | Completed at `01:22:04.035813Z`; no incidents |
| Final drain | Span `0f4bb4288d7ea5fc`, stop reason `WorkflowTerminated`; ended at `01:22:04.257538Z` |
| Terminal checkpoint inside that drain | Span `c6e5bbe358b0defd`, WorkflowCompleted, Immediate persistence decision; ended at `01:22:04.249953Z` |

Root reviewed the private metadata, instance detail and normal diagnostics API export; the bounded helper independently read those small projections and reconciled source and curated evidence. The [accounting census](evidence/coalesced-rest-census-2026-10-06.json) retains request/execution identities and the command-unit qualifications. Instance detail shows response activity completion and commit timestamps but does not expose the persisted response-instruction payload itself. The transport response proves the correct body reached the client on this request. It is not a timing baseline or a proof that the historical intermittent anomaly is resolved.

The failed direct REST start is also separate: its API admitted the start with HTTP 200, but bounded readbacks stayed Running with a blocking SchedulerWorkPoisoned input-expression incident. The valid ordinary REST companion completed with `Alice Smith`. Neither result explains the historical HttpEndpoint 202. A malformed-input fault control remains outside successful-request samples and is still required by integrated verification.

## Owned residual and next capture

T17 (#2412) owns integrated response correctness and the bounded valid-input sampling from T01; T18 (#2413) owns the final finding response. They must retain the historical valid-input 202 as unresolved unless a matching defect is reproduced and corrected. The missing original transform/export is not a prerequisite for these representative controls.

For any valid-input 202 in the representative runs, retain:

1. Exact response status/body, request traceparent, source/build/script/artifact identity, payload shape and effective endpoint response mode.
2. The dispatched started/resumed execution IDs and request-owned delivery-adapter availability. Readback after the response is explicitly later evidence.
3. Drain stop reason/result, immediate execution/activity states and incidents, terminal checkpoint state, and typed response-instruction presence/deliverability at the fallback decision where the existing diagnostics expose it. If that decision-time field is unavailable, record the gap rather than infer it from later completion.
4. Separate request-response and settled-work boundaries. Preserve malformed-input controls and unexpected valid-input 202s outside successful timing samples; report their counts explicitly.

A reproduced unresolved valid-input defect requires a separately claimed correction leaf, the official reviewed specification/plan/tasks, before-fix proof, and a native blocker on T17 before T03 closes. This bounded spike has not established such a defect, so it creates no claimed causal repair or new correction leaf. Original anomalous-request identities and decision-time instruction evidence are unavailable; that residual stays owned by T17/T18 and the final response packet.
