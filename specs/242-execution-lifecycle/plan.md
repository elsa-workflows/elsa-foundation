# Delivery and evidence — execution lifecycle compatibility

Branch: `agent/codex/2905-execution-lifecycle`. Source baseline: `9a1c5c5317f34da544464f6e9ea55c2059918edf`.

## Implementation

Extend the existing EF background-activity integration fixture with a shared BPMN child graph and Sequence/Flowchart parent wrappers. The Flowchart wrapper includes an independent sibling and a join. Share publishing, partition, worker barrier and query setup. Preserve native assertions. Add only the needed BPMN project reference and restore its lock graph. Keep the existing checkpoint transaction and model-private state.

Primary paths:
- `Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/NativeBackgroundActivityFlowchartEfTests.cs`
- `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/BackgroundActivityResultApplication.cs`
- `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs`
- `Essentials/src/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointBackgroundActivityParticipantStaging.cs`

The first path is the existing integration-test anchor; production paths explain the authority under test, not a plan to rewrite them. `BackgroundActivityWorkCheckpointAtomicityTests` covers participant rollback and stale fences. `WorkbenchBackgroundActivityRecoveryTests` remains the existing native process-restart proof.

## Gates

- Existing native and added BPMN integration cases; affected runtime/BPMN suites if a production defect requires changes.
- A meaningful negative control on BPMN completion/cancellation semantics, followed by restoration and passing tests.
- Independent review of graph semantics, durable state assertions and unchanged native behavior.
- Lock consistency, generated-map freshness, architecture guards and diff review; CI handles the wider build.
- Review of the exact PR head and post-merge verification before marking this unit delivered.

No performance measurement, new domain root, public contract, persisted schema migration, Runtime-to-Design dependency or alternate first-party persistence provider is introduced. Existing test policy and constitution boundaries apply; draft constitutional sections are not used as ratified authority. The owner's 18 October instruction permits a concise contract and executable proof instead of redundant Spec Kit artifacts.

## Progress

- [x] Refresh source, claims and program authorization.
- [x] Identify the existing checkpoint authority and second-consumer coverage gap.
- [x] Write the bounded contract and compatibility obligations.
- [x] Add BPMN integration cases and share fixture setup where useful.
- [x] Run affected validation and negative control.
- [ ] Independent review, reviewed PR and post-merge evidence.

## Results

The focused `NativeBackgroundActivityFlowchartEfTests` class passes 19/19, including the unchanged native cases and new BPMN matrices. Independent review of the test/project/lock diff found no remaining actionable findings. The complete affected EF project passed 986/986 (matching 986 discovered cases), the architecture suite passed 578/578, and generated-map freshness passed. Both suites had zero failures/skips. Locked Release and isolated Debug project-graph restores passed. The join negative control changed parallel-join `All` to `Any`: all six new completion cases failed at the waiting-join assertion. Restoring the exact production source made all six pass. No production diff remains. PR/post-merge gates are still pending; this is not yet a delivery or direct BPMN hosting claim.

The restart fixture allocates fresh activity IDs after host recreation, preserving persisted IDs and full artifact pins. Terminal BPMN private state is intentionally cleared by the existing parent-completion implementation, so the join assertion is made while waiting and terminal completion is asserted through persisted activity/workflow state and the native successor count. The fault tests preserve the existing opt-in parent fault-routing distinction described in the spec.

Validation commands (from the worktree root):

```sh
bash tools/architecture/restore-ci-project-graph.sh --locked-mode
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --no-build --no-restore --list-tests
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --no-build --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- check
```

The focused class and mutation/restoration runs built the affected project with `--no-restore`; their filters selected the class or `Published_bpmn_process_waits_for_internal_and_outer_joins_across_host_restart`. Local logs/TRX are retained under `/tmp/elsa-2960`; the PR evidence comment is the durable result record. No HTTP behavior, production implementation or provider-container code changed, so a rebuilt-server e2e or container run was not added to this test-only slice.
