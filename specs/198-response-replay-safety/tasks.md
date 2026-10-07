# Tasks: Response Replay Safety

**Input**: Design documents in `specs/198-response-replay-safety/` (`spec.md`, `plan.md`, `research.md`, `data-model.md`, and `quickstart.md`).

**Tests and cadence**: Follow the reviewed program proof order: establish the immutable External baseline and runnable test fixture first, then trial the activity declaration and verify the full contract, process-loss recovery, matched counts and causal revert. The child executable is test infrastructure for this existing behavior, not new application policy. Framework §2.21.2 does not require a separate owner approval for its test-writing cadence. Root owns this routine ordering under the delegated program authority. Preserve existing test continuity and run all required affected gates.

## Phase 1: Setup

**Purpose**: Preserve a genuine pre-candidate artifact for routine tests without fetching or building historical source in CI.

- [X] T001 Publish the selected `HttpEndpoint → SetVariable → WriteHttpResponse` workflow through the normal Workbench publication and closure-export path from the reviewed source-equivalent capture checkout at actual Git HEAD `d605dbe360621e649e1c947ab406928c91c4ee2f`, with baseline source reference `cd6e2a2a7edaf3ec774c5688e9adddeee6a09d2c`; retain the exact External closure bytes and provenance manifest at `tests/essentials/Activities/Http/IntegrationTests/Fixtures/ResponseReplayHost/Fixtures/pre-candidate-external-closure.json` and `tests/essentials/Activities/Http/IntegrationTests/Fixtures/ResponseReplayHost/Fixtures/pre-candidate-external-manifest.json`. Source equivalence, normal publication/export, valid HTTP request/response, identity, omitted-default profile resolution, and closure SHA-256 are retained in the manifest and private attempt logs.

## Phase 2: Foundational

**Purpose**: Prove the immutable baseline can execute through a real child host and establish test-owned process/database lifecycle before changing the candidate declaration.

- [X] T002 Add `tests/essentials/Activities/Http/IntegrationTests/Fixtures/ResponseReplayHost/ResponseReplayHost.csproj`, reference it as a build dependency from `tests/essentials/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj`, and add the initial parent process test in `tests/essentials/Activities/Http/IntegrationTests/ResponseReplaySafetyProcessTests.cs`; launch the already-built child DLL directly, load the unchanged External closure through the production reconciliation path, and prove the baseline response with owned SQLite, stable test keys, and deterministic child cleanup. Do not invoke nested `dotnet build`, `dotnet run`, or `dotnet test` from the parent test.

**Checkpoint**: The captured External artifact is immutable and executable on the candidate runtime through the child host; no candidate annotation has been added yet.

## Phase 3: User Story 1 — Publish and run a response workflow safely (Priority: P1)

**Goal**: Establish the complete existing response contract and prove normal publication pins the candidate only on a new artifact.

**Independent Test**: Publish and execute the primary workflow through the real design/compiler/publication path. Compare its synchronous HTTP status, headers, content type, body, committed instruction, and terminal output. Separately run the valid REST companion with the existing `WorkflowRequest.content` shape and verify its API admission envelope and committed result without treating that envelope as the activity HTTP response.

- [ ] T003 [US1] Temporarily declare the candidate `ReplaySafe` profile only on `WriteHttpResponse` in `src/essentials/Activities/Http/Activities/WriteHttpResponse.cs`; record the pre-change and candidate source SHA-256 values and keep the change conditional on all later proof gates.
- [ ] T004 [P] [US1] Cover current accepted StatusCode, Body, ContentType, and Headers binding families, defaults, unsupported/refused shapes, secret refusals, pure-expression requirements, and supported external-reference cases in `tests/essentials/Activities/Http/Tests/WriteHttpResponseExecutionTests.cs`; derive each field/source pair from the existing contract rather than adding unsupported cases.
- [ ] T005 [P] [US1] Verify the response header dictionary and arrays remain value-stable when mutated only after the serialized/projected `ActivityCompletion` boundary in `tests/essentials/Activities/Http/Tests/WriteHttpResponseLiveWriteTests.cs`; do not assert a deep copy at `ExecuteAsync` return.
- [ ] T006 [P] [US1] Verify newly compiled/published response nodes resolve the candidate profile while Immediate remains the default, HttpEndpoint remains External, and the captured artifact retains its original pinned profile and hash in `tests/essentials/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs` and `tests/essentials/Activities/Http/IntegrationTests/HttpEndpointCheckpointPolicyEquivalenceTests.cs`.
- [ ] T007 [US1] Extend `tests/essentials/Activities/Http/IntegrationTests/ResponseReplaySafetyProcessTests.cs` to publish the candidate through the normal API/compiler path and run the primary synchronous sequence plus the valid REST companion; assert the executed artifact/profile/hash, compare only the documented profile and publication-identity differences against the immutable External artifact, and verify response instruction and terminal output.

**Checkpoint**: If the accepted input contract, publication identity, or pinned-baseline proof fails, restore `WriteHttpResponse` to External and record the failed condition; do not proceed to candidate replay or savings claims.

## Phase 4: User Story 2 — Recover the response after an ungraceful interruption (Priority: P1)

**Goal**: Prove replay from the specified unflushed Coalesced window using normal durable recovery and no request resend.

**Independent Test**: The parent independently confirms the durable HttpEndpoint trigger/request boundary and required recovery work. The child then attests that the matching response completion is buffered in an active applicable Coalescing session while the parent confirms that completion is absent from SQLite. Hard-kill the process, restart with the same database and keys, wait for the existing attempt lease to become eligible, and verify normal resumption commits equivalent response values and terminal output for the same execution and request.

- [ ] T008 [US2] Add a test-only outer `IRuntimeCheckpointCommitStore` barrier around the registered Coalesced store in `tests/essentials/Activities/Http/IntegrationTests/Fixtures/ResponseReplayHost/ResponseReplayCommitGate.cs`; correlate the trigger-delivered HttpEndpoint claim and response completion, and send a child IPC attestation after the response commit returns using the existing session accessor and public overlay APIs. Do not add a production hook or bypass the inner coalescing store.
- [ ] T009 [US2] Extend `tests/essentials/Activities/Http/IntegrationTests/ResponseReplaySafetyProcessTests.cs` to validate the exact durable request payload identity, trigger delivery, claim, recovery work, and lease before acknowledging the child; after the response-stage attestation, independently verify response completion is absent from SQLite, hard-kill without disposal, restart the same owned state, wait within a bounded deadline for lease eligibility and normal resumption, and assert the same execution/request reaches equivalent committed instruction and terminal output with no resend or duplicate execution. Keep Immediate as a separate correctness control.

**Checkpoint**: If either the child overlay attestation or the independent durable-state checks are absent, if recovery work is missing, or if replay fails, restore External and record the precise failed guard; graceful disposal is not crash evidence.

## Phase 5: User Story 3 — Make a bounded, evidence-backed classification decision (Priority: P2)

**Goal**: Compare only the classification change on the selected workflow and record observed boundary counts, including zero.

**Independent Test**: Run the immutable External artifact and newly published ReplaySafe artifact under the same candidate runtime/provider, workflow, cadence, fusion, host settings, and equivalent fresh database copies. Verify each intended artifact is active and pinned, caches begin in equivalent cold state, and count windows extend through response buffering, segment flush, delivery, and independently confirmed terminal/settled state. Report activity claims, checkpoint flushes, dispatches, and database command attempts separately; separate background/resumption work and exclude parent observer queries from the counted window.

- [ ] T010 [US3] Add child-side command and checkpoint observation in `tests/essentials/Activities/Http/IntegrationTests/Fixtures/ResponseReplayHost/ResponseReplayObservation.cs` with parent boundary-window assertions in `tests/essentials/Activities/Http/IntegrationTests/ResponseReplaySafetyProcessTests.cs`; record failed and repeated database command attempts and distinguish request/drain counts from background/resumption counts without adding a reusable performance harness or timing assertions.
- [ ] T011 [US3] Add the matched Coalesced External-versus-candidate comparison to `tests/essentials/Activities/Http/IntegrationTests/ResponseReplaySafetyProcessTests.cs`; prepare equivalent owned database copies, use fresh candidate processes with equivalent cache initialization and no publication inside the measured processes, verify the active execution artifact/profile/hash, and count through terminal state with no execution-correlated scheduler/outbox work remaining. Keep the Immediate run separate from savings attribution and report zero deltas as zero.
- [ ] T012 [US3] Perform the causal mutation bite by temporarily reverting only the profile declaration in `src/essentials/Activities/Http/Activities/WriteHttpResponse.cs` to External, rebuild and rerun the focused profile/count proof with the same fixture and settings, verify External resolution and corresponding claim/flush behavior, then restore the accepted candidate bytes exactly (or leave External if an earlier proof failed); retain both source hashes and results.
- [ ] T013 [US3] Record the decision and evidence in `specs/198-response-replay-safety/evidence/decision.md`, including source and artifact hashes/profiles, provider/settings, cold-cache state, observed counts and failures, recovery results, mutation result, limits, and whether the candidate is retained or retired; make no gain claim from pre-M2 counts, timing, or unobserved values.

## Phase 6: Polish and Cross-Cutting Gates

**Purpose**: Run affected verification, preserve the conditional decision, and check the final scope.

- [ ] T014 Run the complete affected publisher/compiler, HTTP activity, and HTTP integration test projects at `tests/essentials/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj`, `tests/essentials/Activities/Http/Tests/Elsa.Activities.Http.Tests.csproj`, and `tests/essentials/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj`; execute sequentially through the build-slot wrapper.
- [ ] T015 Run the affected runtime, resumption, and EF persistence suites in `tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`, `tests/essentials/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj`, and `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj`; execute sequentially through the build-slot wrapper.
- [ ] T016 Rebuild Workbench and run `e2e-tests/http/Test-HttpMethods.ps1` using the owned content-root/process procedure in `specs/198-response-replay-safety/quickstart.md`; retain isolated databases and logs on failure.
- [ ] T017 Run `tests/essentials/Architecture/Elsa.Architecture.Tests.csproj` and `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`; refresh maps only if the checker finds a real generated-map difference.
- [ ] T018 Review the final diff and evidence at `specs/198-response-replay-safety/evidence/decision.md`; ensure the only production change is the conditional `WriteHttpResponse` declaration when every proof passes, otherwise ensure it is restored to External, and confirm no default, mandatory checkpoint, HttpEndpoint classification, old pinned artifact, production hook, or user-facing option changed.

## Dependencies and Execution Order

### Phase dependencies

- Setup T001 creates the immutable baseline fixture. Foundational T002 depends on T001 and proves that fixture executes before candidate classification is introduced.
- User Story 1 begins only after T002. T003 precedes its contract/profile checks. T004, T005, and T006 touch separate test files and may proceed in parallel; T007 integrates their passing contract with real publication and HTTP execution.
- User Story 2 depends on the complete User Story 1 publication and contract gate. T008 establishes the child barrier/attestation; T009 proves the independent durable boundary and actual hard-kill/recovery sequence.
- User Story 3 depends on successful publication, contract, and crash/replay proofs. T010 enables the bounded counts, T011 compares matched artifacts, T012 supplies the causal mutation bite, and T013 records the disposition. If an earlier proof fails, retain External and record no-change rather than continuing as though the candidate passed.
- Polish gates follow the selected disposition. Keep build/test commands sequential on the shared machine; do not nest dotnet commands inside the parent test.

### Parallel opportunities

- After T003, T004 (execution contract), T005 (projected header snapshot), and T006 (compiler/policy profile guards) are independent changes in separate files.
- The baseline capture and test-host work are deliberately ordered because the host vertical slice must load and execute the retained real artifact; the candidate declaration is later still.
- No process, database, measurement, or build gate is marked parallel because these checks share owned runtime state or build capacity.

## Implementation Strategy

1. Capture the immutable External artifact and complete the child-host baseline vertical slice before changing the activity declaration.
2. Treat the annotation as a temporary candidate. Complete User Story 1 independently and stop/revert on any input, publication, or pinned-artifact failure.
3. Complete the actual hard-process-loss proof for User Story 2; graceful shutdown or an in-process exception does not satisfy it.
4. Only after both safety stories pass, run the matched Coalesced comparison and mutation bite for User Story 3. Zero reduction is a valid no-change result.
5. Run the affected project and Workbench gates, architecture and maps checks, and review the final evidence/disposition. Immediate remains a separate correctness/default control throughout.
