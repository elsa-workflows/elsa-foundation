# Retained Rules deployment and admission-disabled rollback

T025/T026 are complete at this checkpoint. Two independently restored package consumers exercise
actual retained Rules work against one disposable PostgreSQL database; the paired
[native installation journey](native-installation-checkpoint.md) proves additive schema coexistence
with a suspended native workflow in SQLite. This does not close #3181 or its final integrated gates.

The [checkpoint receipt](retained-upgrade-checkpoint-receipt.json) links exact inputs, hashes, failed
probe attempts and review outcomes. The [qualification receipt](retained-upgrade-qualification.json)
retains the accepted receipt, admission results, successor/recovery snapshots and process evidence.
`fullAcceptancePassed` remains false.

## Package inputs and scope

Deployment A comes from immutable source `26c645ba4691ac0634ae3232f9bd3868f1f72ecd`; B comes from
`6f8a6f9d5c81794f1cf67b34143cb923f5c3e4a9`. Both use publish state
`eb3e12531b0cbd594374df1e49df0fadef9a50f3`. Each was packed as the actual six-module, 20-archive
closure, then independently restored into a four-root runtime-only consumer with isolated caches
and no ProjectReference libraries. Design packages are absent from those runtime consumers.
Both direct consumers pass success, fault, replay, incompatible-pin refusal and identity-check
removal/restoration controls before being used for retained work.

Both builds have the same computed NuGet version string. Their immutable source provenance,
package bytes, selected Runtime assembly hashes and loaded semantic pins differ. This proves
replacement by a real, incompatible-pin package deployment, not a conventional package version
increment, a published release or a schema-version-changing migration. Language and common
receipt/state schemas remain compatible. No version string or identity receipt was fabricated.

## Executed A to B to A boundary

1. A accepts a real worker receipt and is stopped at the observed accepted boundary, before common
   consumption. The database retains revision 2, zero committed rounds, the seed fact, original
   artifact and pin, and the accepted receipt's attempt/worker/fence attribution.
2. B starts with semantic workers suppressed and new admissions disabled. Exact admission replays
   the original invocation; a fresh invocation is unavailable and creates no run. Full retained
   state remains unchanged, and the loaded B pin differs from the stored A pin.
3. B's real worker consumes the already-accepted common receipt. The original pin and receipt
   attribution remain intact; revision advances to 3 and one committed round yields left/right/seed.
   A control using the original pin can claim and release the next round, proving eligible work
   exists. B's loaded pin cannot claim it, and no further semantic evaluation occurs.
4. A returns with workers suppressed and new admissions still disabled. It sees the unchanged
   post-B state, replays the existing invocation, and refuses a fresh invocation without a row.
5. A enables its actual worker while retaining the admission restriction. The original invocation
   completes all four rounds (ordinals 0–3), with unique receipts/attempts, the original accepted
   receipt and semantic hash, and final facts final/joined/left/right/seed. The terminal snapshot is
   stable. No destructive migration, replacement invocation or repinning occurs.

The coordinator requires distinct actual deployments and matching common schemas. It verifies
all trusted loaded assets against their deployment receipts before execution, then compares the
relative path sets and SHA-256 hashes of **all 58 regular files in each deployment** afterward.
This includes Rules assemblies, dependencies, probe binaries and receipts, and detects additions
and removals. The coordinator source hashes also remain unchanged. Five process commands have
the expected outcomes: one intentional stop at the accepted boundary, then four successful exits.
The owned PostgreSQL 16.15 container was removed successfully.

## Review and verification

Four bounded review/fix rounds completed with no remaining actionable finding in this checkpoint.
The review included root integration review and separate read-only reviews of native installation
and package evidence. Fixes were confined to probes, harnesses and documentation:

- Correct the four-root runtime consumer's closure assertion and a probe snapshot serialization
  compile error before producing the final consumers.
- Map canonical deployment-receipt pin fields explicitly to the public runtime DTO for comparison.
- Require original-pin work eligibility before accepting B's inability to claim as evidence.
- Expand the final deployment comparison from three probe files to the entire deployment; rerun
  the actual A/B/A witness against a fresh database. The final run passes all assertions.
- Preserve complete native snapshots while allowing an empty durable-value table: this workflow's
  variable is persisted inline in activity state. The corrected native journey and the existing
  four-scenario REST journey with terminal restart readback pass.

Failed probe attempts remain recorded and are not counted as passes. This checkpoint makes no
product-runtime change. Python syntax, PowerShell parsing and diff checks pass. Package consumer
builds and the actual process/REST journeys provide the affected executable validation.

## Remaining boundaries

This is same-machine package replacement on PostgreSQL plus a separate additive native installation
on SQLite. It does not qualify a multi-machine deployment or external exactly-once effects.
External effects remain at least once. The previous full Workbench result remains 61 passed / 1
failed, followed by an isolated 1/1 pass; the SQLite lock owner and causal explanation are unproven.
Final native acceptance audit, documentation, integrated compatibility/review/CI and merge gates
remain open. The broader architecture program remains in progress.
