# Finite-file implementation checkpoint — 20 October 2026

This is component and design-persistence evidence for #3109, not ETL/native/Studio acceptance. The branch remains a draft implementation lane.

## Reviewed corrections

The self-review loop preserved saved artifact versions instead of replacing them with the current version; bounded the complete artifact envelope and forged-artifact deserialization; retained exact string/whitespace lookup semantics; recompiled publication requests rather than trusting self-hashed JSON; kept genuine storage failures distinct from revision conflicts; corrected SQLite configuration-name versus provider-name comparison; and corrected two test-fixture mistakes without attributing them to storage behavior.

Bounded worker reviews supplemented root integration/review. They are not external GitHub approval. No independent approval is required by the owner's standing delegation; platform protections remain in force.

## File publication finding and safeguard proof

An integrated test observed two different-content writers both reporting successful `File.Move(..., overwrite: false)` publication to one destination. The .NET 10.0.8 [Unix implementation](https://github.com/dotnet/runtime/blob/v10.0.8/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Unix.cs#L193-L214) checks destination existence before a replacing rename, so that API does not establish the required concurrent create-only boundary. Retained logs: `etl-primitives-green.log` (78 passed/1 failed) and `chunk-race-diagnostic.log` under `/tmp/elsa-program-20261020-reprioritization/`.

The feature now uses atomic hard-link creation on Unix, with no copy fallback, and no-replace move on Windows. The staging file is already flushed, closed and hashed before publication; cleanup removes only its own staging name after the writer unwinds.

The attempted mutation back to `File.Move(false)` passed four cases on that later run. It did **not** establish a deterministic regression bite; the earlier observed race and source mechanism remain separate evidence. Removing create-only behavior by allowing replacement failed all four concurrent cases; restoration passed all four. The latter is the deterministic safeguard-removal proof, not a claim that the intermittent reproduction fails on every run.

For `Extensions/Etl/src/Runtime/Output/FileChunkStore.cs`:

- Original/restored SHA-256: `37d737cfe1c76e8bf2ea6b0b58eeaf3c990fcda0cab4825c35418bc7e6324320`.
- Replacement-allowed mutation SHA-256: `f52a22118039a218af5c014e0f356bc0690e1dff2d70c9d1d3cc6e13953554df`.
- Command: `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore --filter FullyQualifiedName~Concurrent_identical_writers --logger 'console;verbosity=normal'`.
- Receipts: `chunk-no-replace-proof.json`, `chunk-no-replace-mutated.log`, `chunk-no-replace-restored.log` in the same temporary evidence directory.

## Verification boundary

Root's integrated component/design-store run passed 108 tests with zero failures/skips. It includes a real plain host AutoMigrate → publish → stop → separate Validate host → reload journey and SQLite model/migration consistency. Parsing, transforms, preparation, scopes/pins and output effects are covered. Focused preparation passed 16/16; parser-with-offsets passed 15/15. Parser tests were observed failing against its empty implementation; no red-first execution is claimed for the preparation suite.

The root checkpoint review ran four review/fix passes and stopped with no actionable finding in the included component/design-store boundary. Final local gates:

- ETL tests: **108 passed, 0 failed/skipped**, after extracting shared design/host pipeline test setup.
- Complete architecture suite: **579 passed, 0 failed/skipped**. This includes the new exact EF package admission, real feature composition, all runtime-library loading, schema stamps, internal visibility and solution layout.
- New-project isolated Debug locked restore passed; normal project/test restores supplied Release/default assets. All five new projects have tracked lock files; the architecture test's lock changes contain the four intended ETL project references.
- Deliberate map refresh and maps freshness check passed. Reviewed changes add four source projects, one test project, two design features and the ETL domain/spec task state; there are no new map findings.
- Source and staged diff checks passed. Existing unrelated warnings in the broader dependency tree are not described as newly introduced ETL warnings.

Initial architecture runs identified wiring/freshness gaps while the worktree was changing; those failed runs are not passes. In particular the stale dependency dataset was regenerated before the final 579/579 run. Final exact-commit CI and the later product journey remain separate gates.

No durable ETL run/partition acceptance, native invocation/continuation, actual two-worker recovery, API/Studio or browser qualification exists yet. Partial snapshot/staging retention, platform/filesystem durability and live worker memory accounting remain explicit operational boundaries. Parent #2908 and Program #2892 remain open; Orleans remains deferred and Proto.Actor is not selected.

## Integration with main61abce1

Merged main `61abce1dbb0c010817c44ca28b22ed031bc52803` normally into the existing implementation branch after GitHub reported a conflict and did not start new-head CI. Explicit conflicts were the active-spec selector and generated maps. Kept spec255 selected for this branch and regenerated maps from both sets of source; all native compensation #3089 and Connection creation #3105 behavior is retained. No ETL production source changed during integration.

Integrated local validation: **108 ETL tests, 19 bookmark-boundary tests and 579 architecture tests passed**, zero failures/skips. Maps freshness, reviewed generated findings, solution-filter validation and diff checks passed. The now-landed CompensationScope is added to the pending native child-completion proof; this does not promise automatic reversal of ETL effects. Current-head GitHub CI and the remaining product acceptance are still owed. Historical main-red issue #2293 remains with its existing owner and cause investigation.

## CI dependency correction

CI run `39414984392` on `2ed6b3b8c9656da9f422c2eaa7271343fda7d4d4` failed during locked restore, before compilation or tests. Adding a central `Microsoft.Extensions.Hosting` package pin for the ETL host test unintentionally promoted an existing probe's transitive Hosting dependency, invalidating its lock (`NU1004`). The earlier focused restores did not include that tool project. Generated maps passed, but the downstream architecture/core jobs were skipped; they are not passing evidence.

Removed the new central Hosting pin and the ETL test's corresponding PackageReference. The test now uses `Microsoft.AspNetCore.App` as a test-only FrameworkReference, following the existing Secrets host-test pattern. No probe source or lock changed, and this does not resume deferred Orleans work. The ETL test lock drops the unnecessary Hosting implementation package closure; production ETL dependencies are unchanged.

The correction passed the complete CI-shaped locked Release and isolated Debug restores (`bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603`), all **108 ETL tests with zero failures/skips**, and regenerated maps freshness. Logs are `host-framework-locked-restore-verified.log`, `host-framework-tests.log`, and `host-framework-maps-check.log` under `/tmp/elsa-program-20261020-reprioritization/`. Fresh CI on the corrective head remains required; the failed run is not repaired by a local pass.

The corrective checkpoint also normally merges main `5a08c2148`, which stops tracking the active-feature pointer. Resolved the modify/delete conflict by retaining main's deletion; spec255 remains selected in the ignored local pointer. The complete architecture suite then passed **579/579, zero failures/skips**. Two root correction/integration review passes found no remaining actionable finding in this dependency-only delta. The central package file and the probe source/lock are identical to integrated main. Runtime contracts and scope compatibility remain subsequent work, not evidence from this checkpoint.
