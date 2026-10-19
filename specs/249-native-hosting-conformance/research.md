# Source decisions

Baseline: main `553f35e37ebd5073079ecbfff94f1bed4ff26e04`. No unresolved design clarification remains. Implementation may expose a defect; observations, not this plan, decide whether a provider qualifies.

## Reuse the native witness

**Decision:** Extend the existing Workbench background-work fixture and share setup, preserving assertions.
**Rationale:** `Essentials/tests/Workbench/Tests/WorkbenchBackgroundActivityRecoveryTests.cs` already publishes Sequence → Flowchart → two background bodies → final activity through REST and proves stable logical identity, durable receipts, cancellation and restart. Its processes are sequential; a concurrently live survivor is missing evidence.
**Alternative:** A synthetic workflow/actor test would duplicate setup and miss real hosting.

## Select composition, keep semantics

**Decision:** Explicit in-process and clustered profiles, one package pin/definition per scenario.
**Rationale:** `WorkflowsRuntimeDistributedFeature` selects routing/placement and uses cluster identity; ownership and checkpoint fencing remain runtime concerns. `WorkbenchDataProtectionTests` documents shared membership and lock settings. Stock Workbench chooses distributed features, so it is not itself proof of the library default.
**Alternative:** Changing activity code or installing a second membership provider would not test substitution.

## Observe the original physical commit

**Decision:** Post-configure the existing coalescing inner checkpoint store in the test package.
**Rationale:** `RuntimeCheckpointCommitter` attaches `ExpectedFence`; `EfRuntimeCheckpointCommitStore` checks replay first, then stages the fence before participants inside its transaction. `ResponseReplayObservationFeature` supplies a precedent. `WorkflowsRuntimeApiFeature` installs persistence modes in post-configuration; explicit dependency ordering is required. Retain the captured holder's disposal ownership rather than discarding it.
**Alternative:** Policy interception is too early for the physical commit; a synthetic stale request or replayed marker would not prove the original engine write.

## Prevent cancellation from masking rejection

**Decision:** Suspend the original process, gate real successor acquisition, and hold original ownership heartbeat only for the measured stale commit.
**Rationale:** `WorkflowDrainOrchestrator` renews ownership in a fresh operation scope and cancels the drain when renewal loses ownership. The unchanged commit/token must reach `RuntimeExecutionFenceValidator` and report `StaleToken`, rather than cancellation or expiry. Holding outside a transaction permits real EF acquisition.
**Alternative:** Replacing cancellation tokens, returning fake renewal success, directly changing persisted ownership, or using only a fake clock weakens the process proof.

## Evidence has layers

**Decision:** Reuse distributed placement/EF and membership conformance tests for their actual contracts; identify layers in the report.
**Rationale:** Scope isolation, runnability, drain/incarnation and duplicate registration already have owning tests. The membership runtime tier includes modeled execution and cannot replace actual Workbench/EF results.
**Alternative:** Repeating every lower-layer test in an expensive process matrix adds noise without strengthening their assertions.
