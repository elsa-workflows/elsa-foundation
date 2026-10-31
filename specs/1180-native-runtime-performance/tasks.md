# Tasks

- [x] T1: Add zero-input read-budget, snapshot, custom-materializer and cancellation controls in `Essentials/tests/Workflows/Runtime/Tests`; capture baseline failures.
- [x] T2: Implement guarded demand-free snapshot materialization in `RuntimeActivityInputSnapshotMaterializer`.
- [x] T3: Reuse invariant preparation in `RuntimeActivityInputMaterializer`; verify fingerprints, isolation, concurrent invocations and validation.
- [x] T4: Index existing coalescing overlays; verify pagination, replacement/deletion and session/flush freshness.
- [x] T5: Evaluate and implement safe EF setup reuse if its lifetime and configuration boundaries can be proved; keep pooling opt-in.
- [x] T6: Run affected suites, rebuilt backend e2e and repository gates; retain causal negative controls.
- [x] T7: Run controlled before/after performance comparisons; record results and limitations, review and commit the completed work.
