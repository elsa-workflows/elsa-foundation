# T043: ordered control acceptance with one parent decision

The generic calling-child inbox accepts one exact admitted control stream after immutable source authorization. Nonterminal delivery uses command `DeliverChildControl=20`; terminal control enters through ordinary completed-child evaluation. Future ordinals buffer without activation. One callback receives only the contiguous prefix, and its cursor/removal/processed-child receipt commits with the consumer's returned private state, schedules and cancellation work on Defer, Complete, intentional Fault and Cancel. Same-call cancellation merges the inbox into the cancelled projection. Unexpected callback failure does not consume the prefix.

The [source and check receipt](control-inbox.json) records hashes of the qualified changed source/test/API files and the parent commit. The commit containing this receipt supplies its published candidate identity. This receipt does not restamp the historical T042 host/Studio evidence.

## Finite in-process host witnesses

Eleven real in-process host cases cover running nonterminal delivery and exact retry; empty terminal-success and ordinary-fault barriers delivered before an earlier notification; callback-free buffering and one eventual ordered prefix; returned defer/fault/cancel decisions with exact private-state/inbox atomicity; consuming control while cancelling the same call; a one-shot exact source read outage; reply loss after buffered terminal or consumed nonterminal acceptance; and late source delivery after a cancelled caller's history is deleted. That last case requires the called workflow specifically Cancelled and its source Delivered before deletion, then proves no new actor envelope or recreated invocation.

Review found that source I/O inside the ordinary leaf callback could turn a temporary read failure into permanent fault after trigger consumption. The named Core `ActivityResumeValidating` event now runs through the Sequential inline publisher before trigger claim or activation. DispatchWorkflow supplies its scoped exact-source handler; missing configured event infrastructure fails closed. Runtime unavailable work preserves its queue claim/fence contract and safe cause, while source conflicts refuse. The original activity constructor remains available.

An opted-in call can fail before child admission. Four real start-exhaustion witnesses cover ordinary/opted-in calls in TestRun and PublishedRun. Opted-in cases failed before the correction and pass with exact dispatch/generation-scoped failure receipt validation. They retain the ordinary `DispatchFailed` outcome and fixed diagnostics without inventing a child root, control batch or inbox. A forged DispatchFailed result against a successful admitted call refuses.

## Retention and refusal

Both existing providers refuse history deletion inside their deletion gates while linked control/replay responsibility needs it. A called run requires sticky caller deletion proof. A terminal caller requires terminal called work (or proven failure/cancellation before admission) and successful Delivered source rows; Pending and FailedFinal remain owed. Missing or inconsistent hinted-child dispatch authority retains both sides. Caller deletion precedes called history deletion. EF checks sticky incoming caller proof before acquiring the candidate write lock, avoiding a caller/child lock cycle; it does not acquire a second root lock. The supported terminal lifecycle is monotonic and its last source work commits before or with workflow terminal state. Before its final dependency read, EF also stages the candidate ownership revision: a racing activity-only accepted checkpoint must either appear in the guard or conflict deletion at final save. Refusal explicitly rolls back and clears that staged tombstone, with a later-save regression control.

Before any candidate write lock, EF uses finite same-scope header pages to capture hinted children without matching dispatch authority because parent identity is not currently an indexed workflow projection. This is a conservative rare-deletion traversal, without a throughput claim. Source rows and dispatch records remain outside run-owned history purge. A missing caller read alone never proves retirement: the notification handler requires provider-authenticated sticky tombstone proof after immutable source validation. Legacy/custom stores default to no proof. The coalescing wrapper delegates proof to its durable provider.

Constructed provider controls exercise running-child refusal, called-history retention, missing/mismatched authority, Pending/FailedFinal refusal, Delivered release, caller-first deletion and sticky proof. A direct throwing SQLite reader verifies contextual exception normalization and original cause. These controls establish provider retention behavior; they do not prove fresh-provider execution recovery with a buffered gap.

## Checks and mutations

| Check | Passed |
|---|---:|
| Whole DispatchWorkflow project | 335 |
| Whole Activities Runtime project | 664 |
| Whole Workflow Runtime project | 2,648 |
| Whole Runtime EF project | 1,022 |
| Deletion API integration | 21 |
| Publishing compiler controls | 147 |

All checks have zero failures. Earlier whole-EF failures came from manually composed test hosts omitting the newly required Events feature; those hosts now explicitly compose it. The failed run was stopped and the complete affected EF project rerun successfully. Existing NuGet/analyzer warnings are not a clean-warning claim.

Regression mutations produced real test failures: removing the pre-claim source event (1); dropping intentional-fault inbox contribution (1, with 2 other returned decisions still passing); allowing the terminal barrier to skip a missing earlier ordinal (2); removing either provider's deletion guard (2 each); and capturing deletion ownership too late (1). Every mutation was restored byte-identically before the passing checks. Generic command wire controls refuse terminal delivery, sibling redirection and foreign executable pins and retain the whole source/address on JSON round-trip. Startup refuses a missing exact-source event handler. The frozen authoring fingerprint remains byte-identical at the hash in the JSON receipt.

## Remaining acceptance

T044 still owns BPMN interpretation; T045 owns actual current-host/fresh-EF buffered-gap recovery, split/concurrent claims and called-control semantics. T024 local combined outcomes, T029 ordinary compensation, duplicate engine deletion, final current-host/Studio proof, complete-PR review, required exact-head CI and resulting-main verification remain open. Independent correctness, standards and acceptance component reviews are clean. They do not count as a full-PR round; the complete-PR loop remains at zero rounds. ADR0063 and production #2898/#2907 remain open.
