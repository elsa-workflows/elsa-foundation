# Validation Guide: Response Replay Safety

Run after implementation is authorized. Use the build-slot wrapper and test-owned databases, child processes, and signing keys. No command in the parent test may invoke a nested dotnet build/run/test process.

## One-time baseline artifact capture

The current fixture was captured through normal Workbench design/submit/publish and executable-export APIs at `11c852be19a24fd173edb77ab2cdbfc0aa977c42`, after integrating #2517. It runs HttpEndpoint → SetVariable → WriteHttpResponse with the response profile External. The [capture receipt](evidence/current-format-publication.md) pins the exact source, binaries, new publication identities, HTTP result and export bytes. Routine CI uses `current-format-external-closure.json` and `current-format-external-manifest.json` under `tests/essentials/Activities/Http/IntegrationTests/Fixtures/ResponseReplayHost/Fixtures/`; it does not fetch or rebuild a historical checkout.

The original `pre-candidate-external-*.json` files remain unchanged as historical evidence from `d605dbe360621e649e1c947ab406928c91c4ee2f`. They are no longer current-runtime import inputs. The owner confirmed Foundation is unreleased and unused, so those exports require no compatibility importer or migration. Preserve their original manifests and databases; do not relabel their source-equivalence or response evidence as a new capture.

## Focused tests

The HTTP integration project must reference ResponseReplayHost.csproj as a build dependency. Building/running the test project builds that child through the project reference. ResponseReplaySafetyProcessTests.cs launches the already-compiled child DLL directly through ProcessStartInfo and the configured dotnet host. Do not start nested dotnet build/run/test commands from the test process.

Run publisher/compiler and activity tests for profile resolution, artifact identity, inputs, defaults, refusals, and snapshots:

    dotnet test tests/essentials/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
    dotnet test tests/essentials/Activities/Http/Tests/Elsa.Activities.Http.Tests.csproj
    dotnet test tests/essentials/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj

Relevant existing files include WorkflowExecutableCompilerTests.cs, WriteHttpResponseExecutionTests.cs, WriteHttpResponseLiveWriteTests.cs, HttpEndpointSyncResponseEndToEndTests.cs, and HttpEndpointCheckpointPolicyEquivalenceTests.cs. The process test loads the exact immutable External fixture without profile/content edits, publishes the candidate through the normal API/compiler into that same isolated database, verifies both profile/hash identities, then executes both artifacts under the same candidate runtime.

Cover all four inputs: StatusCode, Body, ContentType, and Headers. For each, verify current literal/object/default forms where applicable, every binding family accepted by that input definition, and current unsupported/refused shapes. Across accepted field/source pairs include request values, variables, causally prior activity results, and canonical expressions that satisfy binding-pure-v1 where supported. Preserve existing secret-binding refusals for the shapes they reject; do not treat unsupported field/source pairs as failed proofs. Exercise stable external-reference replay only for a supported binding/provider shape that actually uses the reference. Include nonpositive/default status behavior. Assert header values at the serialized/projected ActivityCompletion boundary, then mutate both the original header dictionary and its arrays and verify the committed snapshot remains unchanged; do not assume deep-copy behavior at WriteHttpResponse.ExecuteAsync return.

## Crash and recovery acceptance

Use the primary sequence: HttpEndpoint trigger, deterministic SetVariable producing Alice Smith, and WriteHttpResponse. Include the valid REST companion with WorkflowRequest.content. Compare synchronous HTTP status, headers, content type, and body with the committed response instruction. Validate the REST admission envelope under its own contract and inspect the committed instruction and terminal result.

The test-only outer store wrapper delegates to the registered Coalesced store. Pause after the durable External HttpEndpoint activity claim returns from its inner commit. The parent independently queries SQLite for exact execution, original request and trigger seeds, activity claim, ready durable invoke backstop, and execution-owner lease/fence matching the child. The selected Coalesced path keeps the durable work item unclaimed (null owner/claim/visibility timestamps and token zero), while dispatch proceeds in memory. Continue only after those checks. Later pause after the matching response completion returns from the Coalesced store. The child sends a correlated IPC attestation containing artifact/execution/claim/work-item IDs, response node/activity IDs, active session identity, IsActive/AppliesTo/HasBufferedChanges/HopCount, matching buffered completion identity and value digest. The parent treats this as child-reported memory evidence and separately reads SQLite to verify the response completion is absent. There is no acknowledgement that releases the response barrier.

Hard-kill the child without graceful disposal and recheck the durable backstop, original request and owner fence. For this stale-owner case, wait under a bounded deadline for the persisted execution-owner lease to expire, then restart the already-built candidate host against the same database and keys and await normal resumption. The queue entry is already discoverable because its visibility deadline is null; the wait is a test condition, not a scheduler-claim requirement. Do not resend the HTTP request or edit lease timestamps. Verify the same original request, correlation and trigger seeds survive, the same execution reaches terminal state with no new execution, and committed response values and terminal output equal the uninterrupted control. Lease-wait timeout, missing recovery work and redrive failure remain distinct failed proof outcomes. A lost socket is outside the guarantee.

## Matched count and causal checks

Before counting, select only the intended active HTTP trigger using the existing activation coordinator and ownership rules in each isolated database. Do not leave competing old/candidate triggers on the same route. Assert the actual execution's artifact ID/hash/profile equals the intended one; report necessary definition identity and activation provenance differences separately from behavioral differences.

Prepare both artifacts in an owned seed database, stop the setup host and create equivalent test copies. Start fresh candidate-host processes without publication in either measured process. Verify equal cache initialization, provider/settings, workflow input, Coalesced cadence, fusion, host and runtime binary. The behavioral comparison isolates the profile change while enumerating necessary identity/version/hash/publication-provenance differences. Start command capture after identical host startup. Measure from just before the request through response-buffered and segment-flushed boundaries, response delivery, and an independent database confirmation of terminal state with no execution-correlated scheduler/outbox work remaining.

Report activity claims, checkpoint flushes, dispatches, and EF command attempts separately, including failures and repeats. Correlate request/drain work and separately report background/resumption work. Parent DB observer queries are outside the counted window. Do not exclude relevant background commands. Record raw values and deltas, including zero; do not use elapsed time or pre-M2 totals as evidence.

After the candidate run, perform a one-line source mutation that reverts only the WriteHttpResponse profile declaration to External. Rebuild and repeat the focused profile/count check with the same fixture/settings; verify External profile resolution and the corresponding claim/flush behavior. Restore the candidate declaration byte-for-byte and retain source hashes/results. This is a bounded causal bite, not a runtime option. A zero reduction permits no-change.

## Related gates

Run the affected runtime, resumption, and EF persistence suites:

    dotnet test tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
    dotnet test tests/essentials/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj
    dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj

For the rebuilt Workbench E2E gate, build once and use the existing owned-server helper from a PowerShell session at the repository root. A single `ConnectionStrings__Elsa` override does not isolate the other configured SQLite stores (for example diagnostics); the helper instead creates a separate content root and working directory for every relative database file. Confirm the committed SQLite configuration is selected and remove inherited persistence/configuration overrides that would redirect any target outside that owned root before launch.

    dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj
    . ./e2e-tests/_ServerLifecycle.ps1
    $responseProofRoot = New-ElsaContentRoot -Prefix 'elsa-response-replay'
    $responseProofBaseUrl = Resolve-ElsaServerBaseUrl
    $keepResponseProof = $true
    try {
        Start-OwnedElsaServer -BaseUrl $responseProofBaseUrl -ContentRoot $responseProofRoot
        pwsh -NoProfile -File ./e2e-tests/http/Test-HttpMethods.ps1 -BaseUrl $responseProofBaseUrl
        if ($LASTEXITCODE -ne 0) { throw 'HTTP methods proof failed.' }
        $keepResponseProof = $false
    } finally {
        Remove-OwnedElsaServer -ContentRoot $responseProofRoot -KeepContentRoot:$keepResponseProof
    }

The helper launches the already-built DLL directly, selects a free loopback port, waits for `/health/ready`, stops only its owned process, and retains failure logs/data when requested. Do not delete or reuse any existing developer database. On Windows use the documented `powershell -NoProfile -ExecutionPolicy Bypass -File` form for the test script; see [E2E lifecycle rules](../../e2e-tests/README.md#running-these-tests--read-this-agents-included).

Run Architecture and map freshness checks:

    dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj
    dotnet run --project tools/maps/Elsa.Maps.Generator -- check

Include the child-host project and reference in map validation. Regenerate maps only if the checker reports a real generated-map difference. No solution-wide test run, timing CI, provider matrix, or global performance harness is planned.
