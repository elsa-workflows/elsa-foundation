# Implementation Plan: Response Replay Safety

**Branch**: claude/runtime-db-response-2400 | **Date**: 2026-10-08 | **Spec**: [spec.md](spec.md)

**Input**: [Response Replay Safety specification](spec.md)

## Summary

Evaluate the existing ReplaySafe profile for WriteHttpResponse without changing Immediate defaults, HttpEndpoint’s External profile, supported inputs, persistence policy, or existing artifact interpretation. The only production candidate is a WriteHttpResponse profile declaration, conditional on complete-contract coverage, normal publication and pinned-artifact proof, and a hard-process-loss replay proof. Compare the prior published External artifact with the newly published candidate artifact under the same candidate runtime and Coalesced settings. Retain External if a proof fails; if the matched boundary counts show zero reduction, record zero and allow a no-change disposition.

The crash proof needs a narrow test-only child HTTP host and an outer decorator around the existing Coalesced commit store. Pause after a durable trigger/request boundary, then after a response completion returns from the Coalesced store into an active segment. The child attests its matching in-memory segment over IPC; the parent independently checks durable state before killing the child. Recovery uses the same owned SQLite file and keys, with no request resend.

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: Elsa workflow publishing/runtime, HTTP activities and ASP.NET Core; EF Core SQLite for an isolated process-recovery fixture; existing xUnit test projects.

**Storage**: Existing durable runtime stores. The process proof uses a unique file-backed SQLite database only; no schema or persistence contract change.

**Testing**: Focused publisher/compiler and HTTP activity tests, a process-level HTTP integration test, affected runtime/resumption and EF persistence suites, rebuilt Workbench HTTP end-to-end coverage, Architecture tests, and generated-map freshness.

**Target Platform**: Supported Elsa .NET host. The process-loss proof runs on the CI-supported test platform; the child process and database are owned and cleaned up by the test fixture.

**Project Type**: Runtime/activity libraries with publishing, integration, and backend end-to-end tests.

**Performance Goals**: Report actual activity-claim, checkpoint-flush, dispatch, and database-command deltas for one bounded matched Coalesced comparison. No timing target or timing CI.

**Constraints**: Preserve the full WriteHttpResponse binding/default/header/result contract, secret refusal and pure-expression requirements; Immediate remains default; HttpEndpoint stays External; no user option, durability policy, production test switch, provider-wide claim, transport exactly-once guarantee, or generic measurement harness. Do not infer current savings from pre-M2 counts.

**Scale/Scope**: One activity profile candidate, one published primary workflow plus REST companion, one isolated process-loss scenario, one selected durable provider, and one bounded command-count comparison.

## Constitution Check

**Before design**: Pass. The candidate changes only author-declared checkpoint profile metadata. It adds no Runtime-to-Design dependency (§E2.2), does not allow workflow source to redefine runtime semantics or weaken mandatory boundaries (§E2.6), and preserves durable-state recovery as the source of truth (ADR 0031). Correctness evidence covers publication, serialization/materialization, process loss, and replay; source-only or graceful-disposal tests are insufficient (framework §2.21). Existing stable contracts and artifacts remain compatible.

**After design**: Pass conditionally. Keep process controls and command observation in test-only projects. The child host must use supported publication/runtime composition and a durable provider, with no production hook or configuration switch. Add no persistent entity. If the fixture cannot prove request identity, buffered completion, preserved recovery work, and real replay, keep WriteHttpResponse External. A zero count reduction permits no change.

## Design Decisions

1. Trial only the activity profile declaration after this design/tasks review. Accept it for delivery only after every acceptance proof passes; a failed proof restores External. Do not alter Coalesced policy or Immediate default.
2. Create one immutable pre-candidate External artifact fixture by publishing the selected workflow through the normal publisher at source revision cd6e2a2a7edaf3ec774c5688e9adddeee6a09d2c and exporting its `WorkflowArtifactClosure` JSON. Store exact bytes as pre-candidate-external-closure.json and provenance as pre-candidate-external-manifest.json under tests/essentials/Activities/Http/IntegrationTests/Fixtures/ResponseReplayHost/Fixtures/. The manifest contains source revision, design/version identity, artifact ID/version/hash, resolved External profile, and fixture-byte SHA-256. Capture is a one-time controlled procedure; routine CI must not fetch or build historical Git revisions. The candidate host loads the unchanged fixture through the existing `JsonWorkflowArtifactReconciliationFeature` and its production reader/reconciler, verifies its hash/profile, and publishes a new candidate through the normal API/compiler path into the same test-owned database. If the loader cannot retain/execute the published bytes and pinned profile without rewriting them, stop the classification proof. Execute both artifacts on the same candidate runtime, host, provider, workload, cadence, and fusion settings. Compare the authored graph and pinned behavioral content, allowing only the selected profile change and the enumerated artifact/design version, hash and publication-provenance fields necessarily changed by republishing; do not claim the JSON bytes are otherwise identical without checking that comparison.
3. Keep Immediate as a separate correctness/default control, not the savings comparator.
4. Use a test-only outer IRuntimeCheckpointCommitStore wrapper that delegates to the registered Coalesced store. It must not replace or bypass the inner Coalesced implementation. Pause only for the identified HttpEndpoint claim and WriteHttpResponse completion.
5. Add one small child-host executable at tests/essentials/Activities/Http/IntegrationTests/Fixtures/ResponseReplayHost/ResponseReplayHost.csproj, with its parent test in tests/essentials/Activities/Http/IntegrationTests/ResponseReplaySafetyProcessTests.cs. The current HTTP fixture uses TestServer and hand-saved executables; it cannot prove normal publication plus OS process loss. WorkerOidcHost demonstrates process control but is an OIDC/CShell host without HTTP workflow/publishing composition. Do not grow either unrelated fixture. Reference the executable from Elsa.Activities.Http.IntegrationTests.csproj so the test build produces it, and launch the already-built DLL directly from the parent process; do not invoke nested dotnet build/run/test commands from a queued dotnet test. Keep it test-only and include it in maps/architecture validation.
6. Keep rebuilt Workbench E2E at e2e-tests/http/Test-HttpMethods.ps1 as the integration gate for real submit/publish/request behavior; the test child host supplies the controlled crash barrier unavailable in the current Workbench lifecycle.
7. No public contract doc is needed: there is no external interface change. Existing behavior is specified by the feature spec and ADRs 0020, 0031, 0032, 0045, and 0073.

## Crash-Proof Sequence

1. Start the child with a unique SQLite file, stable test signing keys, Coalesced settings, normal HTTP and publishing modules, and the test-only outer store wrapper. The parent owns the database directory and child lifetime.
2. Publish through the normal API/compiler path. Record the candidate profile and executable identity. Retain and verify the older pre-candidate External artifact in the same database.
3. Submit the primary synchronous request. On return from the durable External HttpEndpoint ActivityAttemptClaimed commit, the child signals the parent and waits. The parent uses a separate database context to verify the exact execution, trigger delivery, request payload identity, claim, and durable recovery work required by normal resumption; checkpoint name alone is insufficient.
4. Continue through deterministic SetVariable (“Alice Smith”) into WriteHttpResponse. After its completion returns from the Coalesced store, the child reads IRuntimeCoalescingSessionAccessor.Current and sends a correlated IPC attestation: execution and artifact identities, trigger-delivery/claim/work-item IDs, response node and activity-execution IDs, session execution ID, IsActive/AppliesTo/HasBufferedChanges/HopCount, the matching completion from TryGetActivity or GetActivityUpserts, and a digest of its serialized completion. These are existing public test-visible session APIs ([IRuntimeCoalescingSessionAccessor.cs](../../src/essentials/Workflows/Runtime/Contracts/IRuntimeCoalescingSessionAccessor.cs), lines 13–19; [RuntimeCoalescingSession.cs](../../src/essentials/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingSession.cs), lines 72–102, 319–362). The parent treats overlay state as child attestation, not as an independent memory read. Separately, the parent queries SQLite to verify the exact durable trigger/request/claim, recovery work and lease state, and absence of the matching response completion. If either overlay attestation or durable checks fail, do not kill or count the run as proof.
5. Kill the child without graceful disposal. Preserve database, keys, artifact fixture, and published candidate. Restart the candidate host with normal resumption services and the same lease configuration. Wait for the existing attempt lease to become eligible and for the normal resumption pump to redrive; use a bounded deadline, and distinguish lease waiting from missing recovery work. Do not resend the HTTP request or invoke an internal replay helper.
6. Verify the same workflow execution and trigger/request reach terminal state, with no new execution, and commit equivalent response instruction values and terminal output. Compare status, headers, content type, body, and deterministic result; ignore attempt IDs and timestamps. The interrupted socket is outside the guarantee.

## Matched Count Window

Each measured request must resolve to exactly the intended artifact. Use the existing activation coordinator/reconciliation ownership rules to select one active HTTP trigger for the shared route in each isolated test database; never activate competing old/candidate routes and infer the chosen artifact from the response. Verify the execution's actual artifact ID/hash/profile before accepting its count window. Loading the historical closure and publishing the candidate can require separate definition identities and activation ownership; record those setup/provenance differences explicitly, outside measurement, without editing either published artifact.

Prepare the immutable External fixture and candidate publication in an owned seed database, then stop the setup host before creating equivalent test copies. Use fresh candidate-host processes against those copies; perform no publication in either measured process. Verify equivalent cache initialization and start command capture only after identical host startup; use identical request inputs. Capture from just before the trigger request through both response-buffered and segment-flushed boundaries, response delivery, and an independent database read confirming terminal state and no remaining execution-correlated scheduler/outbox work. Count activity claims, checkpoint flushes, dispatches, and EF database command attempts separately, including failed and repeated commands. Correlate request-owned work and report background/resumption work in a separate bucket; do not silently drop it. Parent verification reads happen outside the command window. Exclude identical host startup, migrations, fixture loading/publication, parent reads, and cleanup from both runs. Report raw counts and deltas; timing and historical pre-M2 totals are not gain evidence. Run Immediate separately for correctness/default only.

After the matched candidate comparison, perform a one-line local mutation bite by reverting only the WriteHttpResponse profile declaration to External. Rebuild and rerun the focused publication/count proof with the same fixture and settings; verify the resolved profile and relevant claim/flush behavior return to the External control. Restore the candidate declaration byte-for-byte and retain both source hashes and results. This is a bounded causal check, not a runtime option or permanent mutable test hook. If there is no observable boundary reduction, report zero and retain the no-change outcome.

## Project Structure

### Planning artifacts

    specs/198-response-replay-safety/
    ├── plan.md
    ├── research.md
    ├── data-model.md
    └── quickstart.md

### Affected source and verification areas

    src/essentials/Activities/Http/                 # candidate activity declaration only
    tests/essentials/Workflows/Publishing/Api/Tests/WorkflowExecutableCompilerTests.cs
    tests/essentials/Activities/Http/Tests/WriteHttpResponseExecutionTests.cs
    tests/essentials/Activities/Http/Tests/WriteHttpResponseLiveWriteTests.cs
    tests/essentials/Activities/Http/IntegrationTests/
      ├── HttpEndpointSyncResponseEndToEndTests.cs
      ├── HttpEndpointCheckpointPolicyEquivalenceTests.cs
      ├── ResponseReplaySafetyProcessTests.cs
      └── Fixtures/ResponseReplayHost/ResponseReplayHost.csproj
    tests/essentials/Workflows/Runtime/Tests/        # checkpoint behavior guards
    tests/essentials/Workflows/Runtime/Resumption/Tests/
    tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/
    e2e-tests/http/Test-HttpMethods.ps1              # rebuilt Workbench publication/request acceptance
    docs/maps/                                       # freshness check; regenerate only for real generated-map differences

**Structure Decision**: Keep production scope to the HTTP response activity profile declaration. Put process launch, barriers, isolated database ownership, and command counts in the HTTP test area. A child executable is necessary because TestServer and graceful in-process disposal cannot establish OS process loss. WorkerOidcHost is not reused because it lacks HTTP activity publication and runtime execution. Add no public contract or production test switch.

## Complexity Tracking

No constitution violation is planned. One test-only child executable is the smallest isolated process boundary for hard termination with normal publisher/runtime composition; an in-process fixture cannot prove it.
