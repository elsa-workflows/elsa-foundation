# Research: Response Replay Safety

## Decisions

### Change the activity profile, not checkpoint policy

The compiler defaults an unmarked CLR activity to External ([ExecutableNodeCompiler.cs](../../src/essentials/Workflows/Publishing/Services/ExecutableNodeCompiler.cs), lines 365–367). The Coalesced policy treats the pre-activation ActivityAttemptClaimed checkpoint as Immediate for External or missing profile and Deferred for ReplaySafe; mandatory checkpoint status remains enforced ([CoalescingRuntimeCheckpointPersistencePolicy.cs](../../src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointPersistencePolicy.cs), lines 50–71). The candidate is only the WriteHttpResponse declaration after complete proof. No policy/default change is needed or authorized.

### Verify the active overlay after the store returns

With an applicable active session and a segment under its cap, the commit store buffers a commit before returning ([CoalescingRuntimeCheckpointCommitStore.cs](../../src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointCommitStore.cs), lines 38–67). Without such a session, or when a boundary/cap applies, a commit can reach durable storage. Quiescence later folds and flushes the segment ([RuntimeCoalescingDrainScopeFactory.cs](../../src/essentials/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingDrainScopeFactory.cs), lines 53–75).

The child can attest overlay state through existing public APIs: [IRuntimeCoalescingSessionAccessor.Current](../../src/essentials/Workflows/Runtime/Contracts/IRuntimeCoalescingSessionAccessor.cs), lines 13–19, and [RuntimeCoalescingSession](../../src/essentials/Workflows/Runtime/Services/Coalescing/RuntimeCoalescingSession.cs), lines 72–102 and 319–362, expose execution ownership, active/buffer state, hop count, and activity upserts. The paused child sends a correlated test-only IPC attestation with execution/artifact, trigger/claim/work-item, response node/activity IDs, session identity, matching buffered completion identity and serialized-value digest. Attribute in-memory evidence to the child attestation; the parent cannot independently read child memory. It separately reads SQLite to verify the durable request boundary/recovery work and absence of the response completion. Deferred alone is not a crash-window proof.

### The durable trigger claim is the request boundary

ActivityAttemptActivationClaimer commits activity state, attempt claim, trigger delivery, and identity metadata in one checkpoint commit ([ActivityAttemptActivationClaimer.cs](../../src/essentials/Activities/Runtime/Services/ActivityAttemptActivationClaimer.cs), lines 279–391). The barrier selects the trigger-delivered HttpEndpoint node and verifies execution, trigger/request payload identity, claim, and recovery work from durable state. Matching only the checkpoint name could accept an unrelated claim.

### Existing fixtures do not prove process loss and publication together

HttpEndpointHostFixture configures isolated SQLite and Coalesced mode ([HttpEndpointHostFixture.cs](../../tests/essentials/Activities/Http/IntegrationTests/HttpEndpointHostFixture.cs), lines 103–159), but always uses TestServer (lines 161–180), and its response helper stores a hand-built executable (lines 479–505, 842–857). Its teardown is graceful. It remains useful for contract and in-process response assertions, not hard process loss or normal publication.

WorkerOidcHostFixture and its executable demonstrate redirected-stdin/stdout process control and kill/restart ([WorkerOidcHostFixture.cs](../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcHostFixture.cs), lines 111–130, 231–392). Its executable composes OIDC identity and CShell worker services, not HTTP activity and normal publishing ([WorkerOidcHost.csproj](../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Fixtures/WorkerOidcHost/WorkerOidcHost.csproj)). Reusing it would widen unrelated test infrastructure. A small test-only HTTP child host is narrower.

### Use normal resumption after lease eligibility

The normal resumption pump invokes the sweep with configured lease settings ([RuntimeResumptionPumpTask.cs](../../src/essentials/Workflows/Runtime/Resumption/RuntimeResumptionPumpTask.cs), lines 151–170); RuntimeResumptionService discovers and re-drives eligible executions ([RuntimeResumptionService.cs](../../src/essentials/Workflows/Runtime/Services/Recovery/RuntimeResumptionService.cs), lines 86–115, 236–355, 528–560). After process kill, wait for the existing attempt lease to become eligible under the test’s existing lease configuration and then for the normal pump to redrive. Use a bounded deadline, distinguish lease waiting from missing recovery work, and do not resend the request or call an internal replay helper.

### Preserve one real baseline artifact without historical CI builds

Capture one pre-candidate External artifact at source revision cd6e2a2a7edaf3ec774c5688e9adddeee6a09d2c through the normal publisher and executable-export endpoint. Retain exact bytes as pre-candidate-external-closure.json and provenance as pre-candidate-external-manifest.json under the process-host test fixtures; include source revision, design/version identity, artifact ID/version/hash, resolved External profile, and fixture-byte SHA-256. The permanent test loads those unchanged bytes through the existing `JsonWorkflowArtifactReconciliationFeature` and its production reader/reconciler, verifies pinned profile/hash, and publishes the candidate normally through the current API/compiler into the same isolated database. Execute both under the same candidate runtime. Routine CI must not fetch or build historical Git revisions. Do not synthesize External by editing candidate JSON or metadata. If the captured bytes cannot be loaded and executed without rewriting them, stop the classification proof.

The existing JSON export boundary is [WorkflowsPublishingApi.HandleExportExecutableClosureAsync](../../src/essentials/Workflows/Publishing/Api/WorkflowsPublishingApi.cs). Mount its immutable `WorkflowArtifactClosure` with [JsonWorkflowArtifactReconciliationFeature](../../src/essentials/Workflows/Runtime/Reconciliation/JsonWorkflowArtifactReconciliationFeature.cs), [JsonWorkflowArtifactClosureReader](../../src/essentials/Workflows/Runtime/Reconciliation/Services/JsonWorkflowArtifactClosureReader.cs) and [WorkflowArtifactReconciler](../../src/essentials/Workflows/Runtime/Reconciliation/Services/WorkflowArtifactReconciler.cs). Compose the required trigger and locking features; reconciliation validates and activates through the existing coordinator. [WorkflowArtifactExportImportExecutionTests](../../tests/essentials/Workflows/Publishing/Tests/WorkflowArtifactExportImportExecutionTests.cs) supplies an existing export/import/execution example, not evidence that this new HTTP fixture has passed.

### Match cache/setup state and count through a settled terminal boundary

The matched artifacts preserve authored graph and behavioral content except the selected profile; enumerate necessary identity/version/hash/publication-provenance differences separately. They run on the same candidate runtime, host, provider, workload, cadence, fusion, and request. Prepare both artifacts in an owned seed database, stop the setup host, and create equivalent fresh copies for fresh candidate processes. Perform no publication in either measured process; verify equivalent cache initialization before starting capture after identical host startup. Count through response buffering, segment flush, delivery, and an independent database confirmation of terminal state with no execution-correlated scheduler/outbox work remaining. Report correlated request/drain work separately from background/resumption work; do not silently drop background work. Parent observer reads remain outside the count window. Include failed and repeated database command attempts. A zero reduction is valid; pre-M2 totals and timing are not gain evidence.

After the matched comparison, perform a one-line local mutation bite by reverting only the candidate profile declaration to External. Rebuild and rerun the focused profile/count check with identical fixture and settings; verify the resolved profile and relevant claim/flush behavior return to the External control. Restore candidate source byte-for-byte and retain both source hashes/results. This is a bounded causal check, not a runtime option. If no boundary reduction is observed, report zero and allow no change.

## Alternatives considered

- A compiler/unit test alone cannot prove normal publication pinning, real HTTP delivery, or process-loss replay.
- The current TestServer fixture is in-process and disposes gracefully.
- Reusing WorkerOidcHost would require unrelated HTTP and publishing composition.
- Workbench process lifecycle proves real submit/publish/request behavior but has no controlled post-buffer barrier; retain its E2E test and use a small child host for controlled crash.
- Deferred alone is insufficient because the commit store may pass through without an applicable active session or flush at a boundary/cap.
- Changing global cadence or defaults conflicts with the specification and ADR 0032.

## Evidence boundaries

Source establishes profile resolution, immediate/deferred policy, buffer/flush paths, public overlay observation, and resumption entry points. These code references were read from the assigned source at cd6e2a2; the spec commit at affc3f9 changed no src, tests, or e2e-tests files. Process behavior, complete input coverage, immutable baseline fixture provenance, candidate publication profile/hash, command deltas, and replay equivalence remain unverified until tests run. This plan predicts no gain.
