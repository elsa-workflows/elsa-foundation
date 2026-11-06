# Typed processor first-run evidence

6 November 2026; frozen source `5ed3d1e7865c6752d89b48071c5c58fb40466c4f`, tree `08068863a07c252f5a0182a92b26450b50e05d61`.

The first Azure phase remains **FAILED_OR_INCOMPLETE**, exit 1 at `mutation-oracle`. Its runner incorrectly rejected xUnit's error-level log entry for the exact expected mutation failure. The original result is retained without changing its verdict.

| Actual raw result | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Processor/context/JSON cases | 55 | 0 | 0 |
| Drop typed finalization at completion | 7 | 1 expected | 0 |

The sole failed test is `Exact_prepared_plan_and_actual_scoped_tuple_reach_only_atomic_completion`: `Assert.Same` expected the prepared finalization and received null. The raw TRX records no test error, timeout or abort. Its single error-level RunInfo is xUnit's matching `[FAIL]` log; the original full journal identifies the runner's unconditional rejection at line 156. Independent raw inspection confirmed this classification defect and the exact one-call mutation.

[azure-originals.tar.gz](azure-originals.tar.gz) contains all 84 original files, including complete logs/TRX, immutable input packet, original fixture/provenance, mutation and restored source facts, journal, primary failure, cleanup and independent post-stop source proof. All 83 manifest hashes and the archive size/hash were verified after download; [manifest.json](manifest.json) records the scope and fingerprints. The original source/fixture bytes and clean head/tree were restored, and only the owned unit was stopped; its cgroup and recorded process identities are absent.

R1 did not run the later restored test, EF projection, complete projects, generation or model checks. R2 classifies only the exact expected xUnit failure after checking its method, assertion and counters; nine lightweight parser controls reject unrelated failures. It resumes the unrun stages on the same source without repeating the 55 tests or actual mutation. Neither this component nor its parser correction establishes business Message acceptance or provider capability.
