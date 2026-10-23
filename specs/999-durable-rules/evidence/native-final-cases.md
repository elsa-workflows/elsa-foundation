# Final persisted native acceptance cases

The [receipt](native-final-cases-receipt.json) records the two additional cases found by the final
native audit. Both pass, and the complete Rules suite passes **278 tests, zero failures or skips**.
These are acceptance-test changes, not new production behavior. Full leaf acceptance remains open.

## Invalid input settles without a fabricated Rules run

The registered compiler publishes a Sequence containing `RunRules` with `MaxRounds = 0`. The real
persisted native runtime faults the child with `RulesNativeBoundary.InvalidRoundLimit`, consumes
its typed completion receipt, and closes its correlated resume attempt as a fault. The parent
faults and the following activity never starts. The Rules table stays empty, and both deliverable
and claimed native Start-intent queries are empty.

The Rules semantic completion identity belongs in the native deduplication key. The native
transport independently assigns its receipt ID; that ID correlates the resume attempt. The first
executable test incorrectly equated these IDs. Correcting the assertion did not change production
code. An earlier compilation attempt also lacked a dependency-injection namespace import.

## Consumed completion survives unavailable acknowledgment

A persisted Flowchart forks into Rules and a suspended Event. The Event keeps the parent Running
and its join held after Rules completes. The first real delivery executor dispatches the native
completion once. A forwarding proof-validator wrapper deliberately returns `Unavailable` on that
first call, without invoking its inner validator. Native completion is already durable, while the
Rules delivery remains unacknowledged with its owner and expiry retained.

After the ten-second lease expires according to the actual database clock, the same executor
reclaims the obligation. It obtains `AlreadyConsumed` proof and calls the real validator, which
returns `Equivalent`. The Rules store acknowledges delivery and clears its owner/expiry. There is
still only one bookmark dispatch and one native resume attempt. The consumed receipt, full
canonical terminal bytes and Rules model revision are unchanged. The sibling stays suspended,
the parent stays Running, and the join/final activity has not completed. No further delivery is
claimable.

This closes the consumed-child redelivery gap left by the [earlier retry case](native-recovery-acceptance-checkpoint.md),
whose final settlement used parent-terminal proof. The earlier canonical-completion comparison
removal/restoration control remains separate evidence; no new production safeguard is introduced here.

## Review and boundaries

Two bounded integration/review rounds ended clean after the test corrections, including a separate
read-only review. The source-built SQLite fixtures use a synthetic semantic pin. They do not prove
package provenance or a process restart; those journeys have their own evidence. The packaged REST
Flowchart and this held-sibling Flowchart remain distinct evidence surfaces. Final integrated
acceptance, compatibility, exact-head PR and resulting-main gates remain open. The historical
Workbench successor-acquisition timeout has no proven causal explanation. External effects remain
at least once.
