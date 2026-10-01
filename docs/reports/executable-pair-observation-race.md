# Executable pair observation race

Owner: [Code Reality And Test Maturity](../program-goals/code-reality-and-test-maturity.md).
Issue: [#2282](https://github.com/elsa-workflows/elsa-foundation/issues/2282).
This correction clears a delivery dependency of [Runtime Composition #1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959); it does not expand that program's configuration contract.

## Finding and causal proof

At main `a9f7d2c439f968f5499949ba98e231778bf7440c`, `EfWorkflowExecutableStore` reads the artifact and its coordination row in separate statements. Both rows are inserted or deleted together, but a writer can commit between the reader's statements. A partial observation does not establish partial persisted state.

Root-reviewed regressions in the existing runtime test projects force a second connection to commit a complete winner immediately before the contender's coordination query executes. Unchanged production code fails with `InvalidDataException` at `SaveBatchAsync:52` in both file-backed SQLite/WAL and disposable PostgreSQL 16: one executed failure and zero skipped in each case. A first PostgreSQL invocation used an incorrect filter and matched no tests; only the subsequent executed test is evidence.

Two additional SQLite cases seed a complete pair, then delete and recreate it between the contender's artifact and coordination reads. Unchanged code reports mismatched incarnations through both idempotent save and root-write lease acquisition: two executed failures, zero skipped. The first entity is tracked, so rereading without clearing tracking cannot recover the successor artifact.

This demonstrates the same failure mechanism as the [resulting-main publishing failure](https://github.com/elsa-workflows/elsa-foundation/actions/runs/36906977249/job/110520181869), which reported 154 passed, one failed, zero skipped. It does not reconstruct every historical scheduling interleaving.

## Correction contract

Use one shared pair-observation helper at the save preflight, coordination loader and transactional delete recheck. On exactly one missing row or differing incarnations, clear tracking and reread both rows once. Leave absent/partial/incarnation/content/schema classification with the existing callers. Do not retry malformed payloads, swallow `InvalidDataException`, or retry whole publishing operations.

Stable partial or mismatched persisted pairs remain refused. A recreated successor still cannot be deleted using a stale expected incarnation. The save batch adds new rows only after all preflight observations, so tracker clearing cannot discard pending inserts. Coordination transitions and delete rechecks already begin with cleared tracking.

The reread is bounded recovery from an inconsistent observation, not a universal snapshot or unlimited contention guarantee. Repeated incoherence still follows the existing refusal. No transaction isolation, schema, dependency, project count, provider matrix or CI cadence changes are required.

## Verification status

Root verification on corrected source:

| Evidence | Actual outcome |
|---|---|
| Runtime artifact suite, including new interleavings and retained corruption/ABA controls | 65 passed, zero failed/skipped |
| Complete affected runtime EF unit project | 825 passed, zero failed/skipped |
| Existing PostgreSQL artifact smoke class, including forced winner interleaving | Three passed, zero failed/skipped |
| Unchanged publishing EF project, including real PostgreSQL racing publication | 155 passed, zero failed/skipped |
| Removal of only the reread's tracker clear | Both recreation cases fail with incarnation mismatch; source restored and complete runtime suite rerun green |
| Architecture after locked Release and isolated Debug graph prerequisite restore | 634 passed, zero failed/skipped |
| Deliberate maps refresh and check | Only test inventory changed; manifest and findings reports byte-identical; check green |
| Rebuilt Workbench, isolated fresh SQLite databases, HTTP submit/publish/execute/observe | Completed, one activity, zero incidents |
| Rebuilt Workbench publishing lifecycle HTTP script after stale assertion reconciliation | 24/24 passed |

The additional publishing lifecycle HTTP script initially reported 23/24 on both corrected source and a freshly rebuilt unchanged baseline (authoring head `e55fe63447423faa616327f0957ec25a18cd015b`, tracked tree identical to main a9). Its failed assertion compared `activePublicationId` on a runtime-owned slot view that exposes `activeActivationId`; it therefore compared two nulls. The script now requires an active runtime identity before unpublish and a null publication identity in the unpublish result. The subject and retirement objective are preserved. A fresh isolated Workbench rerun passed 24/24, and the complete workflow journey passed again. Both owned servers were terminated after observation.

The baseline's first build ended with MSBuild child-node exits; diagnostics were no longer present. A subsequent ordinary serial build passed, so no source-cause claim is made for that infrastructure failure. The first focused corrected lease case also had an overly strict new empty-tracker assertion: successful leases retain unchanged entities. That new assertion was corrected to verify unchanged state; no production cleanup semantics were changed.

Bounded independent source review found no concrete correction regression. External PR review and resulting-main gates remain required; a later green retry alone is not a repair claim.
