# T028: runtime-only ETL inspection and cancellation

Scope: #3109 / draft PR #3111. This checkpoint adds the runtime API; it does not complete Studio, integrated ETL Workbench/browser acceptance or the program.

## Contract and composition

Four NativeEndpoints operations expose scoped run lists/details, partition progress and idempotent cancellation.
The feature contributes `elsa.api.etl-runtime` plus read/cancel permissions. Its safe DTOs expose the immutable
selection, counts, aliases, diagnostics, retained claim metadata and owed logical batch ranges. They omit raw
records, paths, handles, output keys, invocation payloads, worker identities and native completion addresses.
Claim metadata is not proof that a worker is alive or stopped. Cancellation does not reverse physical effects.

Review against US4 found that alias-only detail could not explain the input identities pinned to a resumed run.
Detail now adds source/lookup binding revisions and expected digests, plus the prepared snapshot digest/length.
These come from the retained run, never today's host bindings. Expected and last-verified snapshot identities
are distinct, with null snapshot fields before preparation. Inspection does not re-read files or certify their
current integrity. HTTP/SQLite coverage checks distinct source/lookup identities before/after preparation and
retains cross-tenant refusal and path/handle/object-key non-disclosure.

Cancellation loads the exact scoped retained invocation, then uses the existing store transition. A missing
run returns 404 without manufacturing an invocation or tombstone. Retrying cancellation returns the same
terminal result. A previously completed/business-error/faulted result remains authoritative and is returned
with `cancellationAccepted: false`. The route owns the run ID. The endpoint accepts no body model, and the
shared pre-binding envelope guard bounds even ignored request content to 4096 bytes.

Inspection requires no design compiler/database, file binding service or ETL worker. Source review found that
the runtime persistence feature's old `EtlRuntime` dependency implicitly activated compute. That dependency
was removed: persistence can now stand alone; execution hosts explicitly select `EtlRuntime` (the native ETL
feature already does). A production feature-discovery/dependency-resolver test checks the resulting closure;
the actual SQLite/HTTP fixture independently checks service absence.

`IEtlRuntimeStore.MaximumPageSize` exposes its positive per-call bound. The API can return its advertised
1..200 HTTP page by composing bounded keyset reads, retaining at most 201 rows including lookahead. This
corrects the default provider's cap of 100, which otherwise rejects a valid API request for 200. Existing
manifest materialization also consumes the explicit capability instead of probing through argument errors.
No schema or persisted format changes are introduced.

Common transport helpers moved into internal `Elsa.Etl.Api` infrastructure. Design and runtime API assemblies
remain separately composed. Keys are registered by API owner so one feature cannot silently select the
other feature's cursor key. Existing authoring cursor JSON is preserved when the optional runtime boundary
is absent. `Runtime/Elsa.Etl.Runtime` is the implementation's name-derived project location now that Runtime
contains an API sibling; its assembly identity is preserved.

## Evidence boundaries

The HTTP fixture uses TestServer, the production Foundation Identity authorization pipeline and actual
SQLite runtime migrations/store transitions. Test-only authentication and partition middleware represent
trusted host selection. It does not run ETL file processing or native workflow execution to seed every API
state, and it is not the separately owed Workbench/browser proof. Tests inspect durable state after refused
or repeated cancellation and check actual shell dependency resolution separately from manual service setup.

## Validation

The initial focused HTTP/cursor/composition run passed 30 tests. Removing only the cursor resource-ID
comparison produced a precise HTTP failure: a partition cursor reused for another run returned OK instead
of BadRequest. [Hashes and regression receipt](runtime-cursor-regression.json) record byte-exact restoration.
The restored baseline ETL suite passed **344 tests, zero failures/skips**. The encoded-order and retained-input
identity cases were added afterward; the final rebuilt suite now passes **346 tests, zero failures/skips**
(`dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore -p:WarningsNotAsErrors=NU1603`).
The initial architecture run passed 582 checks and rejected the runtime API assembly as older than the last
input-identity source edit. The final Release rebuild and complete rerun passed **583 architecture checks,
zero failures/skips** (`dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
--no-restore -c Release -p:WarningsNotAsErrors=NU1603`). Locked CI Release/isolated-Debug restores passed.
Final map regeneration and `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` passed, as did
`bash tools/solution-filters/generate-solution-filters.sh --check`. The final source review retained the cursor
resource comparison, bounded page composition and runtime-only dependency closure; diff/link checks passed.
T028 is complete locally; changed-head CI and the remaining product acceptance are separate gates. Historical receipt/HTTP
timeout causes remain unproved; external effects remain at least once. The future task-runner/pause follow-up
remains outside scope.
