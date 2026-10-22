# Coalescing review repair

[Code Review round 1](https://github.com/elsa-workflows/elsa-foundation/pull/3192#pullrequestreview-5612948623) found that the bookmark-resume durable-claim boundary deactivated checkpoint coalescing for the remainder of every affected drain. This repair starts a fresh coalescing segment after a successful durable resume and delivery of its continuation outbox. It preserves the current ownership lease, total cycle budget, wait deadline and original request limits. Deferred, paused, faulted, empty and terminal outcomes do not restart a segment. A thrown drain discards its current unflushed segment.

The old overlay remains inactive while all durable continuation batches are delivered. Starting a replacement earlier would hide later durable batches behind the new overlay. Distinct durably delivered outbox IDs count as progress only during this handoff, including another worker's deliveries confirmed by lookup. Repeated IDs cannot reset the progress limit. Delivery retains the existing rule that newly emitted work can advance a following scheduler cycle even when the preceding cycle reached its item cap.

## Regression and removal evidence

- On the pre-repair runtime, the first regression set executed 12 tests: five failed and seven passed. Failures covered post-resume coalescing and the missing safe deferral explanation.
- Removing delivery of the remaining handoff batches failed an executed regression: the second emitted schedule item was absent. Restoration passed.
- Removing the preserved handoff-delivery signal failed the one-item request regression: the emitted continuation did not execute. Restoration passed.
- Final self-review found a progress-accounting edge when another worker completed a continuation. A new test performs real durable outbox claim, dispatch and completion transitions, while returning `Superseded` to the drain. With two continuation rows, batch size one and no-progress cap one, it failed with `DrainCycleLimitExceededException` before the correction and passed afterward. Assertions verify both coalesced suffix items, Delivered outbox rows and an empty durable queue/outbox.

[Command records, counters and hashes](coalescing-review-repair.json) preserve the failing and passing evidence. Removal tests failed assertions, not compilation.

## Local verification

The final correction passed the full Workflow Runtime suite (2,546 tests) and Rules suite (278), with zero failures or skips. Before the final external-delivery accounting correction, Runtime EF passed 1,003 and Architecture passed 585 with zero failures or skips; that correction changes no persistence provider, public API or dependency closure. The rebuilt Workbench and REST-driven restart and HTTP Flowchart results, map check and diff check are recorded in the receipt. These source-built checks do not relabel older package/provider acceptance as executed on this corrected runtime.

The restart journey uses a real process stop/start and verifies Event-to-Sequence completion with persisted values and no repeated preceding activity. HTTP Flowchart exercises the real server route. No timeout, assertion, cycle limit or persistence safeguard was weakened.

## Contract documentation and diagnostics

ADR 0088 and the constitution sync-impact text now acknowledge the shared runtime changes: Deferred enum members, the unavailable-work exception, `StoppedOnDeferral`, `UnavailableRetryDelay`, retained scheduler work, and initial/redelivery trigger-claim checkpoints that no longer consume that source. The runtime extension catalog describes the handoff progress rule.

`WorkflowExecutionCommandProcessResult.FromDrain` carries the handler's safe deferred-work summary in its existing reason field, without setting a fault flag. This is a drain diagnostic: the accepted command still has durable retry work. It is distinct from admission refusal; the actor dispatch status and idempotency treatment are unchanged. This does not claim a new external diagnostic UI.

The existing `999-durable-rules` path is retained in this repair. Its numeric collision with `999-studio-runs-task-first` remains a non-blocking catalog note; the full paths and immutable evidence links distinguish the records. No global spec renumbering is claimed.

## Review and delivery status

Self-review round 6 found the external-delivery accounting edge and two documentation omissions. Their focused repairs and red/green evidence are recorded above; no seventh formal self-review or new clean full round is claimed. The active Code Review changes-requested review requires fresh-head reassessment. Required new-head CI and resulting-main verification remain pending. This receipt is not merge authorization or program completion.

Historical receipt/HTTP/Workbench timeouts retain their prior causal uncertainty. Physical external effects remain at least once.
