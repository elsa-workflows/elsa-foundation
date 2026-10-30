# Round 4 correction qualification

All three retained round 4 findings have corrections. C1/S1 are fixed inee345b602; A1 is fixed in178154c133. The [actual old fault recovery receipt](../fault-recovery/README.md) preserves the original waiting-worker input before the writer change. Main4ab657f72 is integrated at **30dfb0a9b7ee46b59ef90e18b548162fa5a96c3f**, including the publishing startup and Studio toolbar changes. This receipt is an input to full-PR round 5, not a clean-review verdict or delivery clearance.

## Actual current-source compatibility

The [Studio verifier result](studio-verification.json) covers both actual saved-store families at30dfb0a9b: four completed new Published/Test Runs,12 whole coupled Runtime/Design stores, nine unchanged original-run observations,84/92 unchanged original native materials and12 visually inspected screenshots. Opening preserves exact JSON/layout and byte-exact BPMN XML; editing changes only the route label. Publication succeeds as3.0.0/4.0.0. Both full new execution views remain identical after each real backend restart, and Studio inspection works afterward. The new Test Run records and linked execution IDs are distinct. All six owned journey processes and both tabs are closed; user hosts/tabs remain untouched.

All [13 current-bundle controls](studio-controls.json) pass, zero skips. The first control invocation selected both test classes and correctly failed the expected-count postcondition after23 passing cases; its diagnostic log is retained. The final invocation explicitly selects `FinalActualStudioUpgradeControls` over the new bundle. No test or verifier was weakened. The existing transient-test layout-sidecar limit remains: saved authoring/export and published runtime layout are verified separately. This proves existing embedded Studio compatibility, not first-class direct-model Studio acceptance.

The three actual old-fault standard-host cases also pass at30dfb0a9b, zero skips, with another six closed Runtime stores under `current-main-legacy-host/`. Semantic fault, interpreter state, original pins/incarnation/authority, physical worker/result consumption and a second process loss remain verified. The source-bound case receipts bind every store to the actual binaries.

## Local gates and source boundaries

| Suite | Passed / skipped | Executing source |
|---|---|---|
| BPMN | 481 / 0 | f6d3c370e |
| Runtime | 3083 / 0 | f6d3c370e |
| Runtime EF | 1790 / 0 | f6d3c370e |
| Architecture | 594 / 0 | 30dfb0a9b |
| Strict native providers | 212 / 0 | 30dfb0a9b |
| Standard host, current-main repeat | 40 / 0 | 30dfb0a9b |

The completed six-suite set totals6200 passes, zero skips, at those declared sources. [Source equivalence](gates/source-equivalence.json) confirms the core production/unit paths are unchanged by the main integration; the integrated publishing startup and Studio receive fresh actual qualification. The current-main40-case host matrix also passes, zero skips, because the startup change runs over the other actual legacy Design stores. [Source/counts](gates/source-and-counts.json) preserves both completed host runs and the source boundaries. The final repeat takes19m6s under a recorded shared-machine load spike; it reports no failed case.

All six [discovery method sets reconcile](gates/discovery/reconciliation.json). Runtime3072 and Architecture585 discovery rows expand to3083/594 executed rows through deferred theories; those differences are not skips. Locked Server restore, frontend/Studio builds, maps and the123-project tier/shard checks pass. Strict PostgreSQL, SQL Server and MySQL execution uses no external connection override. The isolated current-source product builds preserve the earlier host run's actual product bytes.

The [complete published API shape audit](api-binary/README.md) passes10 comparisons over8 actual released packages, including every changed published assembly. Two new BPMN packages have no prior publish-state entry. This does not clear the owner's version-magnitude choice.

All104 frozen baseline assets remain unchanged. The backend/current Studio qualification at30dfb0a9b is complete. T041/T043/T044 stay open pending consolidation and the subsequently observed frontend-only main integration. Four full-PR rounds have completed, none clean; two remain. The existing owner API/version-magnitude/needs:decision, deferred GitGuardian39189012/scan, exact required CI, protected merge and resulting-main gates remain open. #2898/#2907/#2908/#2892 remain open/Partial. Non-SQLite upgraded-host recovery, wider/upgraded-host ETL and first-class direct-model Studio retain their own acceptance; #3242 and the Orleans/task-runner deferrals are unchanged.
