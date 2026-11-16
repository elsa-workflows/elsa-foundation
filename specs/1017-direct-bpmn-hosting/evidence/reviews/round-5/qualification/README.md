# Round 5 correction qualification

Executing source **04c2b53adf7b8eeda6cdf2c879f697b867fc5ad2** includes all four corrections from a50a31e3 and main d2f99623 (#3492). The [red/green correction ledger](../corrections/README.md) preserves the original failures, the incomplete startup-ordering repair and the final controls. This qualification is input to round 6; it does not change round 5's not-clean verdict. Five complete rounds have finished, none clean; one remains.

## Actual recovery and Studio

All four actual old-fault cases pass in the normal 41-case host suite. [Fault recovery receipts](fault-recovery/) retain eight closed Runtime stores, before/after physical truth, original interpreter/fault/pin identities and actual binary hashes. Every case survives another SIGKILL and cold restart. The pending-parent case consumes the real old native-parent ACK and cancels the remaining Event worker during that decision; it no longer needs another startup to discover that work. Original old inputs remain in their preceding capture bundles. No saved state, queue, receipt or authority was edited through SQL.

Both actual saved Studio families pass the unchanged verifier at this same source: four completed new Published/Test Runs, nine original-run observations, twelve whole coupled Runtime/Design stores, unchanged original 84/92 native material records, exact JSON/BPMN exports and saved authoring layout. All twelve screenshots were visually inspected. Published runs are `14n5AbpAkM2` and `14n5V64YeuR`; Test Run executions are `14n5FnnpdXf` and `14n5Y3YZHvd`, distinct from their transient records. Full new execution API views remain equal after real backend restarts. [Verification](studio-verification.json), [all 13 controls](studio-controls.json) and [cleanup](studio-cleanup.json) pass. Six owned child identities and tabs 26/27 are absent; user tabs 4/11 are retained.

All 104 frozen baseline assets remain byte-identical. The saved authoring/export and published runtime layout are verified; the existing transient Test Run layout-sidecar limit remains. This proves existing embedded Studio compatibility, not first-class direct-model Studio acceptance.

## Whole affected suites

| Suite | Passed | Skipped |
|---|---:|---:|
| BPMN | 492 | 0 |
| Runtime | 3083 | 0 |
| Runtime EF | 1790 | 0 |
| Architecture | 594 | 0 |
| Strict PostgreSQL / SQL Server / MySQL providers | 212 | 0 |
| Standard host | 41 | 0 |
| Publishing | 61 | 0 |
| Design | 655 | 0 |

The eight whole suites total **6928 passed, zero failed or skipped**. [Source/counts](gates/source-and-counts.json), compressed raw TRX/build/test logs and [discovery reconciliation](gates/discovery/reconciliation.json) retain the evidence. All method sets match; Runtime 3072→3083, Architecture 585→594 and Design 585→655 expand deferred theories, not skipped cases. Native-provider execution requires the real engines, with external connection overrides cleared. This does not imply non-SQLite actual upgraded-host recovery.

Locked Server restore, maps, all 123 project tier/shard guards, 52 affected frontend tests, the full frontend workspace build and the private Studio build pass. [Actual host product hashes](gates/host-products-after-all-runs.json) remain exactly equal before, after private Studio compilation and after every run. Studio compilation did not replace the binaries used by the running host suite.

Reproduce at the declared source by locked-restoring the Server graph, building the eight projects in [projects.json](gates/projects.json), then running each whole test project with `--no-build --no-restore` and a fresh TRX directory. Set `ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS=1` for the provider suite. `--list-tests` must reconcile with the executed method sets. Use `tools/bpmn/run_owned_studio_upgrade.py` with explicit newly built Workbench, Studio and DeliveryRecoveryRead products for both families; feed the resulting strict bundle to the unchanged Studio verifier and `FinalActualStudioUpgradeControls`. Historical paths and PIDs are provenance, not portable inputs. Closed fixture archives and declared source/product hashes are the reproduction boundaries.

The [public binary API audit](api-binary/README.md) passes twelve comparisons with resolved references, parameter-name checks and no suppressions. API shape does not resolve the owner version-magnitude decision.

Main subsequently gained frontend-only Switch authoring at d481d6a5 (#3495). Its affected editor/build qualification must accompany adoption before round 6; none of the results above is relabelled as execution at that later integration. T041/T043/T044 remain open for that integration. Owner magnitude/needs:decision, deferred GitGuardian 39189012, clean exact-head review, required checks, protected merge and resulting-main verification remain open. #2898/#2907/#2908/#2892 stay open/Partial; wider/upgraded-host ETL, first-class direct-model Studio, native fault policy #3242 and Orleans/task-runner deferrals retain their own acceptance.
