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
existing row. Use a monotonically increasing creation generation and retain its high-water mark after Delete.
The exact address is actor placement/ID + creation generation + model-local invocation occurrence.
The native model consumes that identity; StateMachine adds no private nonce, receipt actor or address registry.
Business-key lookup is a separate operation: resolve once at admission and retain the resolved incarnation.
A delayed pinned command must never resolve the key again against a replacement run.

| Alternative | Assessment |
|---|---|
| Prohibit actor-ID reuse permanently | Safe with an enforced durable non-reuse record, but denies the currently proposed recreation behavior. Naming guidance alone does not enforce it. |
| Sender input ID, frame counter, definition/version or current partition epoch | None identifies one actor creation. Input IDs are sender-controlled; counters reset; definitions repeat; ownership epochs change during a live run. |
| Target-owner random incarnation alone | Distinguishes committed runs, but a parent cannot pin cancellation before the child's first commit without a separate start/acknowledgement protocol. A caller-supplied random token alone also cannot reject replay of that retired start after its deduplication window. |
| Monotonic shared incarnation with retained high-water mark | Keeps actor-ID reuse and permits a parent to pin a fresh child's first generation in its start/cancel intents. Requires a minimal lifecycle tombstone and common propagation/store validation. Preferred prototype direction, with the retention cost below. |

The tombstone is a deliberate change to physical deletion, requiring shared-kernel review. Delete clears the
workflow state, definition payload, waits, jobs and ordinary history under their existing rules, but keeps the
actor ID and retired creation generation in a minimal lifecycle record. It retains no receipt/business payload.
That high-water mark cannot be purged while the same actor ID remains reusable: otherwise an arbitrarily late
old start or command can alias again. Complete erasure requires retiring/rotating that address namespace, not
silently resetting its generation. This is a storage/retention tradeoff, not free identity metadata.

## Shared contract to implement and review

1. An explicit creation request names the next generation, expected predecessor and immutable start intent.
   A fresh globally unique actor ID begins at generation1; explicit recreation reads the retired high-water
   mark and requests its successor. The first start commit checks that predecessor/generation under the
   ordinary fence, writes the running row and start identity, and runs no authored work a second time for
   an already committed creation. A lost acknowledgement reloads the committed identity; before-apply replay
   retains its requested generation. A replay of a retired start cannot create a fresh successor implicitly.
   Competing starts cannot overwrite one another; define duplicate/conflict results for their start intents.
   Refuse exhaustion rather than wrap. This counter outlives deletion; it is not a partition epoch, workflow
   revision, wall clock or model-local frame counter.
2. The row's incarnation is immutable for that creation. Every ordinary/model/control/metadata/terminal/Park
   write preserves it. Validate attempted replacement under the existing fence before writing anything.
   Models cannot replace the header by serializing their private state. Delete retires that creation and
   preserves its high-water mark; a later explicitly authorized start advances it. The first prototype must
   prove both lifecycle transitions through existing fenced commits, without a second receipt authority.
3. Make exact targeting common to transport and owner dispatch. A pinned message carries the target
   incarnation. After loading the row, reject a mismatch before controls, authored hydration, jobs, metadata
   or state mutation. Consume/refuse it through the normal fenced outcome and a request-correlated result
   where applicable. A missing target does not create a replacement unless an explicit start is authorized.
4. Separate creation requests, key-addressed admission and pinned run commands. An externally supplied start
   is not permission to overwrite a live creation. Same-start transport retries preserve their ordinary
   deduplication window and resolve uncertainty. The retained generation/start-intent check separately prevents
   a retired creation being resurrected by that old request; it does not promise exactly-once external effects.
   Preserve the kernel owner's accepted key-resolution/pinning rule.
5. Propagate incarnation through generated sends, message IDs, timer/retry recalls, background-job ticket and
   completion identities, event subscriptions/deliveries, child starts and parent-finished notifications.
   A revision-derived message or child ID must not collide with one from a deleted creation. Record the
   parent's exact creation when starting a child; an old child's completion/termination cannot target the
   replacement parent. A parent mints a fresh child actor ID including its own creation and operation identity,
   and commits that child's generation1/start intent with both its start message and its child registry entry.
   Cancellation before the child's first commit uses that same exact address, ordered after the start as today;
   it never looks up a newer child incarnation. Uncertain child creation retries the same start intent. Explicit
   reuse of a retired child ID must advance its generation, so delayed parent cancellation cannot hit it.
   Review both directions and all these surfaces together before claiming a shared identity solution.
6. Native registration exposes the committed actor incarnation with the exact frame occurrence. Admission,
   receipt query and retained retry carry that address. Resolve duplicate/conflict only within that creation;
   a stale address returns an explicit wrong-creation/unknown result without touching the new inbox or
   constructing its receiver. Re-entering one node within the same creation still requires a fresh frame.
7. Define legacy behavior explicitly. A saved row without the header is not silently assigned an attested
   historical identity. Preserve supported legacy receipt lookup/protection, refuse new qualified admission
   where identity is unavailable, and never interpret an omitted incarnation as the current qualified one.
   An older writer must not drop or reinterpret the header/tombstone: specify a fenced minimum-protocol or
   exclusive-upgrade boundary before claiming mixed-kernel safety; model versioning alone cannot provide it.
   Existing generic terminal late-message draining
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

Execute delayed old starts/controls, timer/event/job completions and child-finished notifications after recreation.
The new creation must retain its waits, state, jobs and parent continuation. Include two actors with equal
local frame names and same-creation state re-entry. Terminate a parent before its child's first commit, cut
that child creation before/after application, then recreate the child ID and execute the delayed old parent
termination. Prove initial start/cancel consistency and isolation of the replacement. Preserve the existing I10/I22 oracle boundary: extend its
expectation only when it can distinguish actual incarnations; never whitelist the old regression wholesale.

Use isolated mutations for reused incarnation, ignored target-incarnation check and dropped propagation;
each must fail a named safety assertion. Test immutable-header refusal, before-apply and applied/lost-ack
creation, generation exhaustion, retired-start replay, competing start intents, actual stale writes after
takeover, legacy/unsupported capability and definition-free metadata. Deletion/erasure tests must distinguish
cleared workflow content from retained lifecycle metadata and must not describe the tombstone as full erasure.
Run affected complete prototype runners and relevant admission/clock/terminal/Statechart/lease regressions
through the normal build-slot wrapper, bounded as in the preceding plans. No service/cloud/old Azure grant
or full solution build is authorized by this research plan.

Stop short of claiming the prerequisite closed if only the native input envelope is guarded while shared
late commands or generated identities still alias. Record any counterexample and revise the contract first.
After independent source and raw-evidence acceptance, update the actual M0 technical plan and kernel finding;
product adoption still needs its applicable design agreement. Causal trigger cohorts, ordinary guard/phase
recovery, values/scopes, complete StateMachine/Studio and native-compensation composition remain separate work.
