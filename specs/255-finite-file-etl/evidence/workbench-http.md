# Rebuilt Workbench HTTP acceptance

Executed 2026-10-20 for #3109 / draft #3111, on the working tree following `b47e59b332ea4823e7cf86e5b52029ddc43fa226`. This checkpoint proves T031, not the Studio browser journey or final delivery gate.

## Commands and result

```sh
dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj --no-restore -p:WarningsNotAsErrors=NU1603
pwsh -NoProfile -File e2e-tests/etl/Test-FiniteFileEtl.ps1
```

The rebuild exited zero with 11 warnings and no errors. The HTTP harness exited zero after starting an owned Workbench on a free loopback port with fresh SQLite stores and private ETL feature configuration. It authenticated through the ordinary endpoint, read the binding catalog, created/validated/published `finite-file-f7113f1f839f` version 1, then published and executed native Sequence and Flowchart parents through the normal APIs.

Both parents completed `RunEtl` and their following `WriteLine`. Each ETL run reported 2 accepted, 1 rejected and 1 filtered record across two exhaustive, completed partitions. Assertions verified final partition cursors, no outstanding work, retained source/lookup digests and byte lengths, two accepted-output chunks and one safe error chunk, and the actual JSONL records. Row rejection produced the expected `businessError`; Flowchart explicitly routed that outcome to its successor. The API does not expose manifest entries, so this is not an HTTP manifest assertion.

Run IDs:

- Sequence: `etl-fdf0f773c73a7d33435e849837db320b9053cb9f91e0c8f1e42b882c7fc879a2`
- Flowchart: `etl-2b1655bad90ac82cc5b6171efbccb1873bc3d4b4b86bcc92c04e2bcf592f3327`

The harness stopped only its owned process (34677); an independent process query confirmed it had exited. The successful fixture was removed by the harness. Local diagnostic logs are retained at `/tmp/elsa-program-20261020-reprioritization/etl-workbench-tenant-build.log` and `/tmp/elsa-program-20261020-reprioritization/etl-workbench-http-restored.log`.

## Defect exposed and corrected

The first execution failed: HTTP publication used tenant `default`, but the native start dispatch omitted tenant identity. The resulting null-tenant ETL invocation correctly could not use the default-tenant file bindings. Retained SQLite state showed `etl.binding-unavailable`, no partitions and an uncollected terminal result while the parent was suspended. That failed fixture and its logs were retained.

`WorkflowExecutionStartService` now propagates the trusted normalized HTTP tenant independently of persistence partition. It preserves the existing two-argument embedded construction. Invalid or ambiguous HTTP identity is refused before artifact lookup or dispatch; absent validator registration cannot silently select embedded behavior. No binding scopes were broadened to make the test pass.

Focused start-service/API-composition validation passed 41 tests with zero failures or skips. Removing tenant propagation caused the new dispatch assertion to fail with expected `tenant-from-identity` versus null. Byte-exact restoration passed the same 41 tests. The actual rebuilt HTTP pass above then verified this correction through persistence, workers, result delivery and parent continuation.

The full affected native runtime regression also passed after this correction: `dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603`, **2,443 passed, zero failed/skipped**. It respected the shared build-slot queue; the retained log is `/tmp/elsa-program-20261020-reprioritization/native-start-full-runtime.log`.

The separate Runtime API suite also passed: `dotnet test Essentials/tests/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603`, **180 passed, zero failed/skipped** (`native-start-api-regression.log` in the same local diagnostic directory).

## Limits

Studio browser acceptance, remaining recovery/provider matrix checks, fresh architecture/maps/lock gates, final self-review and exact-head CI remain required. This evidence does not complete the leaf or its parents. External effects remain at least once; historical receipt/HTTP timeout causes remain unproved. Task-runner and pause semantics remain future scope.

## Studio publication-review scope regression

The later Studio journey reached a different path from the direct HTTP publication above. It could author,
publish and select an exact ETL version, but native publication review could not resolve it. Both native
preflight endpoints created `WorkflowExecutableCompileRequest` without its tenant; the actual publication
handler already supplied the caller tenant. ETL's exact scoped lookup correctly refused the null-tenant query.

The focused HTTP regression covers the saved-version and unsaved-snapshot routes under both `tenant-a` and
`tenant-b`. The first test version changed a historical authentication handler with the same short class name
as the current handler. Its initial four null-tenant failures did not establish the intended two-tenant proof;
a first full run exposed the fixture mistake (808 passed, four failed with actual `capture-tenant`). Root moved
the test-only tenant-header support to the active fixture and reverted the unused historical change. The corrected
fixture then proved four exact null-tenant failures after removing only the two propagation arguments.
Byte-exact restoration (SHA-256 checked) passed the full Publishing API suite: **812 passed, zero failures/skips**.

Both endpoints supply the existing `PublicationRequestTenant` resolution to compilation;
snapshot review reuses that same value for its review token. No ETL lookup fallback, binding broadening,
public wire-contract change or new identity policy is added. The [rebuilt-host browser journey](studio-browser.md) subsequently passed ordinary publication review, exact selection, native execution and continuation.

The scoped regression command was:

```bash
dotnet test Essentials/tests/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603 --filter FullyQualifiedName~Preflight_compiles_in_the_authenticated_tenant_scope_without_publication_writes
```

The restored full run used the same command without the filter. The restored source SHA-256 values are
`3ab2e1e93ddeb4dfdf6ead57a18b6a3bdfaeaf3810c81c9032605d9f33096922` for the version preflight endpoint and
`7fc8eb1a06f6c2606a6547f91c6897617c02e5beeb7cb51855c9944198c48b77` for snapshot preflight.

### Test Run boundary

This acceptance targets persisted published executables. Studio's transient Test Run path is not qualified
by it. A source-only follow-up remains: the three compile-request constructions in
`StartWorkflowTestRunRequestHandler` also omit `TenantId`. Those paths have their own run/admission semantics;
no browser or runtime proof of scoped ETL Test Run support is claimed, and they are not folded into the
publication-review repair without a separate bounded regression.
