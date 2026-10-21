# Live-attempt capacity and lease replacement (T021)

## Contract and implementation

The process-local budget belongs to the actual preparation or partition attempt, not its database lease. Records, input bytes, lookup bytes and output bytes are logical allowances, not a heap/RSS measurement. Expiring/replacing a lease cannot release them. Each successor must acquire its own allowance while reusing the durable owed batch rather than charging that reservation twice.

`EtlExecution` now owns its permit outside the processing `try` and disposes it after explicit heartbeat disposal and, for partitions, asynchronous claim cleanup. This conservatively includes the final cleanup awaits in the approved live-attempt boundary. Previously the inner `await using` released capacity as the processing body exited, before those awaits. Claim cleanup remains bounded/best effort; failure retains recoverable durable work. No actor/provider, persisted schema or public API changes.

## Real execution proof

`EtlRuntimeJourneyTests.AttemptReplacement.cs` uses the existing runtime-only host, physical fixture files, production preparation/processor/create-only publisher and SQLite store. A test-only forwarding store holds calls **before** delegating, outside database transactions; it never substitutes a lease, terminal result, reservation or accepted manifest.

The partition case performs these causal steps:

1. Admit and prepare a real run. Pause the first old heartbeat renewal; the execution body's separate renewal still reaches SQLite. Pause old acceptance after physical output publication, with the real owed descriptor/quota and an empty accepted manifest.
2. Advance the trusted test clock and claim that same partition under a larger fence. Confirm unchanged cursor/owed reservation. The old output is physically readable but remains unaccepted.
3. Release the delayed renewal to the real store. Its ownership failure cancels the old attempt. Hold the non-immediately-cancellable acceptance operation; independently probe all four process-local allowances.
4. Resume old acceptance with the real old fence, independently of caller cancellation, and require `LeaseLost`. Hold asynchronous old claim cleanup and probe all four allowances again.
5. Complete old unwind. Execute the successor under its real replacement lease. Pause its acceptance and confirm its own four allowances are held, quota/owed descriptor remain unchanged and the manifest is still empty. Its batch/counts/effect equal the old deterministic physical output.
6. Accept once. Require success, one accepted record, zero outstanding quota, exactly one matching manifest entry and expected route bytes. Full capacity becomes available after the successor finishes.

A separate real preparation case holds heartbeat shutdown after preparation commits, then checks the same four allowances until the actual preparation task unwinds.

The injected **singleton** budget has two attempt slots and resource capacity equal to one pipeline attempt. Each probe requests only one dimension while another task slot remains free. Thus a slot limit or another held resource cannot hide an early record/byte release. `AcquireAsync` reaches its wait synchronously; immediate completion is the assertion, with no sleep or timed non-progress inference. Probe cancellation does not free another attempt's capacity. Both execution scopes resolve the same singleton. Gates are released and tasks joined in `finally`.

This proves a real successor after old unwind obtains its own allowance. It does not claim a second `ExecutePartitionAsync` was itself queued while the old body was paused; the direct competing budget probes establish that capacity condition. Existing budget tests cover waiter scheduling and cancellation.

## Validation and limits

Both initial real-execution cases failed the original early-disposal implementation at the capacity assertion, then passed with retained ownership. Review strengthened the probes to isolate all four dimensions and removed implicit heartbeat disposal after permit disposal. Restoring the original source under the final tests again failed **2/2**; byte-exact restoration passed **287 ETL tests**, zero failures/skips. [Source hashes and regression receipt](live-attempt-regression.json).

The full architecture gate passed **579 tests**, zero failures/skips. Its initial run passed578/failed1 because the README's new evidence target had not been staged; staging the actual file and rerunning the full gate resolved it. Dependency-map, solution-filter and diff checks passed. Root self-review completed three bounded passes, ending with no actionable finding; worker input was reviewed/integrated by root and was not an approval requirement. No dependency or schema changes. Prior native/workflow-runtime/REST/Studio validation retains its separate checkpoint provenance; this operation did not rerun the rebuilt-server/browser product gate, which remains owed with API/Studio composition. Fresh exact-head CI is reported separately in the public checkpoint receipt.

```sh
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore --filter 'FullyQualifiedName~Real_replaced_attempt|FullyQualifiedName~Real_preparation_retains'
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
bash tools/solution-filters/generate-solution-filters.sh --check
git diff --check
```

This is same-process actual executor/SQLite replacement evidence. Separate-process crash/interruption acceptance, complete persisted authoring/picker/publication, API/Studio/HTTP/browser acceptance and the remaining program remain unfinished. Historical native receipt/HTTP timeouts are not explained by this test. Physical effects remain at least once; only accepted manifest effects are authoritative. PR #3111 remains draft.
