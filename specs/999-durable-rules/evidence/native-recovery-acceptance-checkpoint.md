# Persisted native retry and acknowledgment checkpoint

This checkpoint extends the [package/Workbench checkpoint](package-workbench-checkpoint.md) with two real SQLite native-runtime acceptance cases. It completes T032's source removal control alongside the preceding packaged REST execution. It does not close #3181, qualify retained upgrades, or make the earlier full Workbench suite green. The [receipt](native-recovery-acceptance-receipt.json) records source hashes, commands, logs and outcomes.

## Retry through the native scheduler

`RulesPersistedNativeRetryTests` executes the registered compiler's Sequence artifact through the real persisted native runtime. A forwarding reader returns exactly one `Unavailable` for the expected Rules invocation, armed immediately before the real bookmark dispatcher runs. Delivery preflight reads remain available. The failure occurs inside `RunRules.ResumeAsync`.

The native claim checkpoint deliberately retains a consumed trigger receipt and its open resume attempt. It does not roll that receipt back. Fresh persistence scopes observe the same queued `ResumeBookmark` work ID, a Running parent and child, no following activity, and no poison record. The Rules model revision and canonical terminal result remain unchanged. A controlled runtime clock verifies that the queued execution is hidden immediately before its retry boundary and claimable at that boundary; the actual resumption service then redrives it.

After retry, the same trigger delivery has one interrupted resume attempt and one completed resume attempt. The full native result equals the retained Rules result, the following Sequence activity has exactly one completed attempt, and the original scheduler work is gone. The parent completes. After the separate Rules delivery lease expires using database time, the real delivery worker settles the obligation without another bookmark dispatch. This final settlement uses parent-terminal proof; it is not evidence of the inspector's consumed-child branch. The fixture preserves the existing 60-second default and overrides it to one second only for this case.

## Fresh completion proof before acknowledgment

`Store_revalidates_real_consumed_completion_before_acknowledging_delivery` uses the existing persisted Flowchart with a genuinely suspended Event sibling. That sibling keeps the parent Running, so parent-terminal settlement cannot hide a missing child-result check.

The test claims the real Rules delivery, dispatches its actual bookmark result, and obtains `AlreadyConsumed` proof from the persisted completed activity. It then changes only that activity's completion payload through the actual activity store: the replacement is a valid terminal-result envelope with different facts and a matching recomputed facts digest. This is deliberate test corruption of a real completed record, not a normal workflow operation or a fabricated native history.

The actual Rules store rechecks the earlier proof in a fresh native scope and returns `Invalid`. Its delivery remains `Ready`. Readback confirms the altered completion is present. Restoring the original activity permits the same proof and live lease to acknowledge the obligation; no further delivery is claimable. The Event remains suspended and the join remains waiting throughout. Withholding acknowledgment does not claim to reverse or block a native continuation that already completed.

## Executed control and review

The seven persisted Sequence, Flowchart and retry cases passed. Root integration corrected reader-interface usage, structural result comparison and test assertion idioms. Removing only the canonical terminal-bytes comparison from `MatchesSuccessfulCompletionAsync` made the new persisted proof test fail specifically with **expected `Invalid`, actual `Acknowledged`**. The production source was restored byte-for-byte and rebuilt; the full Rules suite then passed **276 tests, zero failures or skips**. Two bounded review rounds, including final cross-review, ended with no actionable issue in either acceptance path.

These changes add tests and a fixture lease option; no production behavior is changed. The prior rebuilt architecture run passed 585 tests and the generated solution-filter check passed, as recorded in the package checkpoint. Those checks are not presented as a new run for this test-only checkpoint.

## Remaining limits

The retry case uses source-built services with the fixture's synthetic semantic pin. It is not a process-restart or packaged unavailable-retry proof; actual package provenance and REST execution are separately recorded in the preceding checkpoint. Native admission/refusal component proofs and cancellation journeys retain their own evidence boundaries. Retained native schema coexistence, Rules deployment upgrade and admission-disabled rollback remain pending, as do the final integrated compatibility/review/CI gates. The full Workbench suite's earlier 61-pass/1-failure result remains distinct from its passing isolated rerun; its SQLite lock cause is unproven. External effects remain at least once.
