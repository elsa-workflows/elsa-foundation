# Two-process HTTP recovery checkpoint

Scope: T013 in [spec 996](../spec.md), issue #3151, draft PR #3156.
The existing file-worker process harness now supports connector bindings and precise connector checkpoints.
This evidence concerns the current worker binary; the frozen predecessor compatibility task T003 remains open.

## What executes

Each case launches the real `Elsa.Etl.WorkerHost` executable twice, sharing the fixture's SQLite database
and immutable snapshot directory. The parent only migrates/admit-seeds the run and stops its own runtime
worker before either child starts. Child PID receipts, process exit and higher replacement ownership fences
are asserted. Only fixture-owned children are terminated. The real runtime worker, source transport,
transformation, retained route bodies, store transactions and acceptance all execute.

The optional connector-store capability is preserved through the fixture decorator. Existing file gates
continue to forward through their original adapter. HTTP bindings travel through the worker configuration;
each binding must match the configured scope. Gates publish an atomic journal after/before their named
transition and await the existing release marker. Only the first caller publishes, but **all matching
attempts wait** so a replacement attempt in the same process cannot bypass the checkpoint.

## Interruption matrix

`Two_http_worker_processes_recover_each_durable_boundary` covers:

| Predecessor stops at | Evidence before exit | Successor obligation |
|---|---|---|
| Published source page, before SQL capture | Verified page manifest/raw bytes exist; source attempt is still InFlight without a capture receipt; no attached page/batch | Recover original page and provenance, attach and execute without another GET |
| Applied batch reservation | Owed batch/quota exists; no retained output or route journal yet | Preserve descriptor/digest/quota and produce both routes |
| Persisted main acknowledgement | Main is acknowledged, errors not yet attempted | Skip main and deliver errors |
| Persisted errors acknowledgement | Both routes are acknowledged, input remains unaccepted | Skip both physical deliveries and accept once |
| Before batch acceptance | Both physical receipts and retained bodies exist; outstanding quota remains | Preserve work identity and settle progress/quota exactly once |

The replacement pauses before acceptance, allowing inspection of durable intermediate state. The test
compares original batch/body/digest where available, verifies page and original capture provenance,
checks zero prior accepted/rejected totals, and verifies exactly one GET and one POST per route.
Every POST must target exactly `/output` or `/errors`, with the retained body hash/length and effect key.
After release, one accepted row and one lookup-rejected row produce the expected business-error terminal,
zero outstanding quota, one settled batch and one owed terminal outbox. No native consumer is configured;
that outbox is not called consumed.

## Lost response and receiver semantics

Two additional cases use a real loopback receiver that applies the main effect and withholds its response
until the predecessor has exited. The retained attempt remains InFlight with no acknowledgement. The
successor records the old attempt as uncertain, allocates a new physical request and repeats the exact
body and logical effect key. It then completes the errors route and accepts internal progress once.

- A receiver keyed by logical effect identity records one main application despite two requests.
- An ordinary receiver records two main applications. This is an explicit demonstration of at-least-once
  external effects, not a claim that Elsa can roll back or deduplicate arbitrary receivers.

Both cases assert two main requests, one errors request, one GET, the same retained output JSON/digest,
original attempt history and unchanged outstanding quota before final acceptance. Internal totals settle
once regardless of whether the receiver deduplicates.

## Review and validation

Root review corrected a fixture barrier that would have allowed later matching attempts to pass through.
Independent review found and closed an assertion loophole accepting unexpected POST targets. Root then
strengthened the publication-only boundary to inspect the absent SQL capture, checked persisted route
acknowledgements directly and compared original cache provenance. An initial compile identified a missing
Core namespace import; it was corrected before execution.

The first focused run passed all **7 process cases**, zero failures/skips. A negative control changed the
transport header from the retained effect identity to the physical request identifier; both lost-response
receiver cases failed at their actual request-versus-retained-effect assertion. Production transport was
restored byte-for-byte before the final run. No production implementation or timeout changed.

Full restored-source ETL validation passed **936 tests**, architecture passed **584**, and a final
focused run of all **7 process cases** passed after the last boundary assertions were added. All had
zero failures/skips. Maps, solution filters, relative links and diff checks passed. Production transport
was restored to SHA-256 `8a62d3a78635485b46fb66312d90c866084049b644f8d7cf39b6c10cf1d1dba3`.

Three bounded review passes covered harness integration/barrier semantics, receiver and durable-state
assertions, and final evidence/diff validation. No actionable finding remains in this checkpoint.
T013 is complete; **13/22 tasks** in the HTTP leaf are complete. The PR remains an incomplete draft.

Commands:

```bash
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore --filter 'FullyQualifiedName~Two_http_worker_processes'
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters-check
git diff --check
```

The full leaf remains incomplete: T003 frozen predecessor resume; T014 corruption/attempt replacement;
T015 native cancellation/races; T017 policy/telemetry; T018 inspection; T019 real Workbench/browser
acceptance; T020–T022 final integration and delivery. Historical timeout causes remain unproved and main
CI tracker #2293 remains independent of this checkpoint. Task runner/pause stays a later program component.
