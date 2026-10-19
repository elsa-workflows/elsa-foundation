# Implementation Plan: Native hosting conformance

**Branch**: `agent/codex/3056-native-hosting-conformance` | **Date**: 2026-10-19 | **Spec**: [spec.md](spec.md)

## Summary

Qualify the existing built-in native host with real Workbench processes and production SQLite persistence. Reuse the published Sequence/Flowchart background-work witness, select hosting through composition, and add an original-engine-commit stale-owner scenario. This is evidence for the current provider boundary, not a new execution API or an Orleans implementation.

## Technical Context

- C# / .NET 10 (SDK pinned by `global.json`), xUnit, CShells and Nuplane.
- Existing non-packable Workbench fixture; real runtime EF stores, durable command transport and EF membership.
- One machine with isolated scenario directories and explicitly shared SQLite files/lock directory for clustered profiles. Start migrations sequentially. POSIX process suspension has explicit applicability.
- No throughput or latency target. Deadlines bound hangs and record diagnostics; lease expiry is observed separately from survivor acquisition.
- No new production package, wire contract, database schema or neutral public API is planned. A demonstrated production defect must be isolated, reviewed and validated within the claimed issue before inclusion.

## Constitution Check

Read framework v6.1.0, Elsa v6.0.2 and `docs/contributing/review-checklist.md`. Pre-design and post-design gates: pass after root and independent review. Review added explicit physical-commit selection, unchanged scheduler-claim evidence and disposal verification.

| Gate | Application |
|---|---|
| Framework §2.1, §2.3, §2.9; Elsa §E2.2 | Test-only dependencies stay in the non-packable fixture. No Core/Primitives/provider boundary changes or Runtime-to-Design dependency. Publishing remains REST; execution uses its pinned artifact. |
| Framework §2.5, §2.6.2, §2.11 | Test features remain public, non-sealed with virtual configuration. Explicit dependency ordering installs decorators after persistence modes. Replace exactly one descriptor; fail clearly if the expected seam is absent. Preserve lifetimes and disposal ownership. |
| Framework §2.23 | Preserve current assertions and coverage when extracting setup. Assertions observe processes, contracts and persisted outcomes, not source or documentation text. |
| Elsa §E2.6, §E5 | Preserve published artifacts and implementation availability. No package version changes; scenario Nuplane versions distinguish test binaries only. |
| Review/verification | Root integration review, independent review, focused process/runtime/EF tests, actual negative control, architecture guards and generated maps, then exact-head and post-merge CI. |

No exception is requested. Provisional framework §2.24 and Elsa §E2.8/§E2.9 are not ratified or expanded by this work.

## Project Structure

```text
specs/249-native-hosting-conformance/
  spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
Essentials/tests/Workbench/
  Tests/WorkbenchProcess.cs
  Tests/WorkbenchBackgroundActivityRecoveryTests.cs
  Tests/NativeHostingScenario.cs
  Tests/NativeHostingConformanceTests.cs
  Fixtures/BackgroundActivities/
    BackgroundProbe.cs, ReceiptDeliveryBarrier.cs
    NativeHostingWitness.cs
    BackgroundActivities.csproj, elsa-package.json
docs/reports/native-hosting-conformance.md
```

Use the existing Workbench project/fixture rather than creating another project. No external interface is introduced, so a `contracts/` artifact is unnecessary. Fixture shapes are in [data-model.md](data-model.md). The final report records evidence, not a new queue.

## Host profiles and fixture ownership

1. Extract narrowly shared package assembly, REST publish/start/state, body observations and diagnostics from the current recovery witness. Preserve its admission, body-loss, receipt-loss, cancellation and final-history assertions.
2. Assemble one unique-version fixture package per scenario and copy identical bytes to both host feeds. Do not create divergent package pins per node. Use the same published definition and activity implementation for all profiles.
3. The in-process profile removes distributed runtime features from authored test composition and retains the library default. The clustered profile explicitly enables the existing distributed runtime, EF placement/transport and durable EF membership with distinct host IDs.
4. Each process owns its content root, endpoint and Nuplane state. The scenario owns shared database, journal, lock and key-ring paths. Use absolute shared paths, sequential startup migrations and shared Data Protection configuration (or authenticate each host independently). Never reuse another session's resources.
5. Add owned-process identity, hard kill without restart, suspend and resume to `WorkbenchProcess`. Cleanup resumes a suspended process before graceful cleanup or kills it, waits for owned exit, and disposes shared paths only after both processes stop.
6. Keep process cases in the existing non-parallel collection. Use per-process/event journal records where physical duplicate work can overlap; logical identity and native final state remain authoritative.

## Actual stale-owner fence proof

The fixture is opt-in, configured for exact role and journal. It cannot synthesize a replacement checkpoint or report a fabricated heartbeat success.

1. A post-configuration feature depends explicitly on `WorkflowsRuntimeApi` (which installs persistence modes) and runtime EF. Decorate `CoalescingInner<IRuntimeCheckpointCommitStore>` so the original public coalescing path still calls the actual EF backend. Retain the captured holder for DI disposal. Keep this dependency in the non-packable fixture.
2. Select a fold-free Immediate/mandatory physical engine commit with an attached `ExpectedFence` and nonempty observable participants. Verify it reaches the inner store unchanged; do not mistake a buffered or synthetic flush for the selected boundary. Record execution/commit IDs and fingerprint, expected lease/owner/token, original scheduler claim owner/token when present, cancellation state and participant identities. Before entering EF, confirm that no accepted marker exists. Hold outside the EF transaction and root write lease.
3. Suspend the original owned process, stopping membership, placement and execution renewals together. Observe expiry and let the survivor's real runtime acquire a greater token through production EF ownership.
4. Decorate survivor acquisition only to pause **after** durable acquire and **before** returning to the drain. It must not claim/commit the held original work. Record the actual successor lease and valid expiry. Complete the stale-write attempt while that lease is active; an expired-lease rejection does not count.
5. Hold the original role's heartbeat call after resume until the captured commit finishes, preserving and ultimately delegating the real call. This prevents renewal cancellation from masking the fence; it neither renews stale ownership nor returns a fake result. Preserve the original commit object and original cancellation token throughout.
   The scheduler's consume-in-flight guard normally suppresses claim renewal during this call, but its dispatch deadline still applies. Keep the controlled suspension below the configured dispatch ceiling and assert the original claim owner/token are unchanged. If another renewal path would cancel the original dispatch, gate that test-only call without fabricating success. A changed claim, deadline or cancellation invalidates this proof.
6. Reconfirm no accepted marker, release the captured commit into real EF, and require `RuntimeStaleFencingTokenException` / `StaleToken` with presented T and persisted successor token >T. Require that the original token was not cancelled on delegation. Compare durable markers/participants before and after; no stale commit effect is permitted.
7. Release the original heartbeat barrier and survivor acquisition, then recover the same execution to one native final continuation. Retain both process identities and durable observations.
8. Temporarily bypass the actual EF fence stage for the focused negative control. The same original write must reach authoritative persistence and the test must fail on that evidence. If another guard prevents it, the control has not proven fence sensitivity: refine the selected boundary rather than counting an unrelated failure. Restore the safeguard and require the focused scenario to pass. Never commit the mutation.

## Evidence matrix

| Scenario | Profile/layer | Evidence required |
|---|---|---|
| Admission before worker start; executing-body loss; result durable before notification | In-process and clustered, actual processes/EF | Stable execution/invocation/attempt, changed PID, result not physically rerun after durable receipt |
| Both completion orders and terminal restart | Both profiles, native engine | Held peer prevents Sequence advance; one final logical activity and retained completed state |
| Owner loss with concurrently live survivor | Clustered, actual processes/EF membership/transport | Both identities, real survivor routing/recovery and same pinned artifact |
| Resumed stale original commit | Clustered POSIX, actual engine/EF | Unmarked original commit, uncancelled original token, successor token, exact transactional stale rejection, no partial state; negative-control failure/restored pass |
| Duplicate/conflicting admissions and receipts | Existing runtime and EF acceptance tests plus applicable process observations | Existing refusals and no duplicate logical continuation; clearly identify in-process contract tests |
| Cancellation and late result | Existing/selected process profiles and EF | Cancelled instance remains terminal; captured late receipt refused |
| Scope/capability/drain/incarnation/provider registration | Existing distributed and membership conformance at owning layers | Report modeled membership separately from production EF/runtime tests; no blanket actual-process claim |
| Graceful handoff | Clustered actual processes plus existing lifecycle tests | New admission avoids draining host; accepted work remains recoverable and implementations retained |

The report maps concrete test names/results to every row, including skips and missing coverage. Diagnose a failing existing witness before changing timing or semantics.

## Delivery and rollback

Implement fixture/profile setup first, then real takeover/fence controls, then integrated scenarios and evidence. Keep existing fixture users working at each step. For deployment rollback, stop new admission to the candidate provider, drain or explicitly recover accepted work with a readable compatible host, and retain published definitions, packages and durable state. Do not delete accepted instances or switch to a provider unable to read them. Orleans, other models, multi-machine partitions/clock skew and other databases remain unqualified here.

## Validation

See [quickstart.md](quickstart.md). Review the integrated diff and claims before commit, refresh changed maps including their manifest, run focused local validation through the shared build-slot wrapper, and leave broad CI to the normal PR gate. A runtime behavior fix additionally requires the relevant REST e2e suite against a rebuilt fresh-DB host. The new owned-process tests exercise real REST, persistence and runtime for this test-only qualification work.

This extends an existing fixture and production path. Preserve prior tests and add scenarios with fixture support; require actual runs and the negative control before acceptance. Elsa does not declare a global greenfield test cadence; this unit neither adds greenfield production code nor establishes a new cadence. Registration logic and captured-holder disposal receive focused behavioral coverage.
