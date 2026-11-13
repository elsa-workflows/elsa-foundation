# Shared actor incarnation and exact invocation targeting

**Status:** proposed M0/#3820 and shared-kernel #4229 work unit, 13 November 2026. Review before prototype
implementation; this is not product architecture agreement, a new public API or a provider schema decision.
It advances [the terminal plan](terminal-receipts-plan.md)'s unresolved recreation prerequisite.

## Problem and chosen direction

The normal delete/recreate witness in [#4394](https://github.com/elsa-workflows/elsa-foundation/pull/4394)
reproduces the same local frame in two creations of one workflow actor ID. An old sender input is accepted
fresh into the new creation. I10 reports the revision/status regression before I22; neither the diagnostic nor
the subsequent receipt/commit-clock work fixes it. A partition epoch identifies an owner generation, not an
actor creation: takeover must preserve invocation identity while recreation must change it.

Propose an immutable **actor incarnation** owned by the shared durable actor boundary and persisted with its
existing row. The exact address is actor placement/ID + incarnation + model-local invocation occurrence.
The native model consumes that identity; StateMachine adds no private nonce, receipt actor or address registry.
Business-key lookup is a separate operation: resolve once at admission and retain the resolved incarnation.
A delayed pinned command must never resolve the key again against a replacement run.

| Alternative | Assessment |
|---|---|
| Prohibit actor-ID reuse permanently | Safe only with an enforced durable non-reuse record. Current Delete removes the row; naming guidance alone does not enforce it. Permanent tombstones add retention/index obligations and change the proposed delete/recreate behavior. |
| Sender input ID, frame counter, definition/version or current partition epoch | None identifies one actor creation. Input IDs are sender-controlled; counters reset; definitions repeat; ownership epochs change during a live run. |
| New immutable shared incarnation | Keeps actor-ID reuse and scopes all model occurrences to their original creation. Requires a common envelope, propagation and store validation rather than a StateMachine-only check. Preferred prototype direction. |

## Shared contract to implement and review

1. Mint an opaque unique creation identity at the durable owner before the first authored turn. Keep the same
   staged identity through that activation and persist it with the first accepted row, including failed-start
   Park. A lost acknowledgement reloads the committed identity. If no first commit landed, a replay may mint
   another identity: no post-commit invocation receipt for the abandoned candidate was accepted. The identity
   is not a wall-clock value, partition epoch, revision or resettable frame counter. The deterministic test
   generator and deployment generator must have an explicit freshness contract; neither is user input.
2. The row's incarnation is immutable for that creation. Every ordinary/model/control/metadata/terminal/Park
   write preserves it. Validate attempted replacement under the existing fence before writing anything.
   Models cannot replace the header by serializing their private state. Delete removes that creation; a later
   committed start receives a fresh identity. This adds no independently acknowledged write.
3. Make exact targeting common to transport and owner dispatch. A pinned message carries the target
   incarnation. After loading the row, reject a mismatch before controls, authored hydration, jobs, metadata
   or state mutation. Consume/refuse it through the normal fenced outcome and a request-correlated result
   where applicable. A missing target does not create a replacement unless an explicit start is authorized.
4. Separate creation requests, key-addressed admission and pinned run commands. An externally supplied start
   is not permission to overwrite a live creation. Same-start transport retries preserve their ordinary
   deduplication window and resolve uncertainty; this proposal creates no permanent exactly-once start
   promise after that window. Preserve the kernel owner's accepted key-resolution/pinning rule.
5. Propagate incarnation through generated sends, message IDs, timer/retry recalls, background-job ticket and
   completion identities, event subscriptions/deliveries, child starts and parent-finished notifications.
   A revision-derived message or child ID must not collide with one from a deleted creation. Record the
   parent's exact creation when starting a child; an old child's completion/termination cannot target the
   replacement parent. Review these surfaces together before claiming a shared identity solution.
6. Native registration exposes the committed actor incarnation with the exact frame occurrence. Admission,
   receipt query and retained retry carry that address. Resolve duplicate/conflict only within that creation;
   a stale address returns an explicit wrong-creation/unknown result without touching the new inbox or
   constructing its receiver. Re-entering one node within the same creation still requires a fresh frame.
7. Define legacy behavior explicitly. A saved row without the header is not silently assigned an attested
   historical identity. Preserve supported legacy receipt lookup/protection, refuse new qualified admission
   where identity is unavailable, and never interpret an omitted incarnation as the current qualified one.
   An older writer must not drop or reinterpret the header. Existing generic terminal late-message draining
   and opaque future-state handling remain required; a capability refusal is not permission to wedge them.
8. The first executable cut may qualify only the in-memory provider. Other providers must reject unsupported
   identity-bearing writes before their codecs can truncate the header/envelope. Existing legacy ordinary
   workflows may remain explicitly unqualified; no metadata consumer can promise exact identity there.
   Specify this capability boundary before enabling the consumer, and preserve the shared commit-clock and
   lease/fence rules. Actual provider/binary rollout remains a separate gate.

## Bounded proof and stopping conditions

Inventory the actual read/write/dispatch surfaces above before editing. Coordinate on existing #3820/#4229;
do not edit another owner's branch or present this plan as that owner's architectural agreement. Keep one
coherent prototype PR near the requested size limit, splitting additional provider proof when necessary.

The executable witness uses normal start, activity completion, retained receipt expiry, Delete and recreation,
not seeded successful receipts. Cross Balanced/Durable and root/nested use. Old admission/query/duplicate and
conflict attempts must not reach the new inbox, even when local frame names and sender IDs match. New correctly
addressed input still succeeds. Cold takeover preserves the original incarnation and receipt order/deadline;
same-node recreation must change it. Test missing-incarnation refusal explicitly.

Execute delayed old controls, timer/event/job completions and child-finished notifications after recreation.
The new creation must retain its waits, state, jobs and parent continuation. Include two actors with equal
local frame names and same-creation state re-entry. Preserve the existing I10/I22 oracle boundary: extend its
expectation only when it can distinguish actual incarnations; never whitelist the old regression wholesale.

Use isolated mutations for reused incarnation, ignored target-incarnation check and dropped propagation;
each must fail a named safety assertion. Test immutable-header refusal, before-apply and applied/lost-ack
creation, actual stale writes after takeover, legacy/unsupported capability and definition-free metadata.
Run affected complete prototype runners and relevant admission/clock/terminal/Statechart/lease regressions
through the normal build-slot wrapper, bounded as in the preceding plans. No service/cloud/old Azure grant
or full solution build is authorized by this research plan.

Stop short of claiming the prerequisite closed if only the native input envelope is guarded while shared
late commands or generated identities still alias. Record any counterexample and revise the contract first.
After independent source and raw-evidence acceptance, update the actual M0 technical plan and kernel finding;
product adoption still needs its applicable design agreement. Causal trigger cohorts, ordinary guard/phase
recovery, values/scopes, complete StateMachine/Studio and native-compensation composition remain separate work.
