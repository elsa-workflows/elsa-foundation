# T004 partial checkpoint: connector model and compiler

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), 21 October 2026.

## Delivered in this checkpoint

The additive `EtlPipelineV2` declares CSV or HTTP JSON input, optional typed CSV lookup, independent JSONL or HTTP output routes, explicit pagination and finite payload/request policy. Endpoints and credentials remain host bindings. `IEtlArtifactCompilerV2` is a separate capability; existing v1 compiler implementations do not acquire a new required member. The built-in design feature resolves both capabilities to one scoped compiler.

The v2 codec bounds actual serialized bytes and JSON depth, rejects duplicate properties recursively, rejects unknown members and discriminator/enum spellings, and writes deterministic typed property order. Authored discriminator placement need not be first. The compiler validates identities, aliases, JSON Pointer syntax, scalar references, limits and enclosing budgets, then snapshots canonical bytes with format-specific artifact identity. Shared schema/mapping/filter helpers preserve the existing v1 codec and identity constants.

## Review and test history

- Initial compilation exposed missing public API declarations; exact analyzer declarations were added without suppression.
- Compiler test-first run against the temporary unimplemented compiler: 22 failures and one passing registration test. The stub was replaced with the implementation.
- First combined run: 29 failures and 38 passes. Required JSON metadata on the getter-only shared filter property was invalid. A v2-only strict filter converter fixed it without changing v1 serialization.
- Corrected combined selection: **68 passed, zero failed or skipped**. It includes v1 and v2 compiler tests, the v2 codec, retained v1 fixture replay, and the existing persisted native Sequence/Flowchart journeys.
- Two controlled regressions removed the compiler finite-limit validation and reader duplicate-property validation. Both targeted tests failed at the intended assertions: invalid limits produced an artifact, and duplicate input did not throw. Both source files were restored byte-for-byte before subsequent validation.

- Restored full ETL suite: **420 passed, zero failed or skipped** (1 minute 37 seconds).
- Final bounded worker/root review found that explicit null required members and null collection entries were still accepted by the public codec. Both new reader/writer regression tests failed before the fix. V2 nullable annotation enforcement plus explicit collection-entry validation corrected it; the final affected selection passed **70/70**, zero failures or skips. The worker reviewed the final fix read-only and found no remaining actionable codec/compiler issue. The full-suite result precedes this narrow fix; the 70-test selection verifies the final codec change.

Validation commands:

```sh
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --logger 'console;verbosity=minimal'
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --filter 'FullyQualifiedName~EtlArtifactCompiler|FullyQualifiedName~EtlArtifactV2JsonTests|FullyQualifiedName~EtlV1CompatibilityFixtureTests|FullyQualifiedName~EtlPublishedWorkflowJourneyTests' --logger 'console;verbosity=minimal'
```

Architecture full run: **583 passed, one failed, zero skipped**. The failure was the compiled-assembly freshness guard: root changed only an `EtlArtifact` API comment after Core had built. Rebuilding and rerunning that guard passed **1/1**, zero failures/skips. No runtime defect is inferred from the earlier freshness failure. The combined evidence covers all 584 architecture checks; this is not a claim of a second full-suite run. Generated dependency-map and solution-filter freshness checks both passed without generated changes. Diff and evidence-link checks passed.

```sh
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --logger 'console;verbosity=minimal'
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --filter 'FullyQualifiedName~InternalsVisibleTo_grants_stay_within_the_granting_domain' --logger 'console;verbosity=minimal'
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters-check
git diff --check
``` Logs are retained locally under `/tmp/elsa-program-20261020-reprioritization/3151-v2-*`; filenames are not verdicts (the first-green log contains a failed run).

## Outstanding scope

T004 remains unchecked. Dual runtime readers, immutable native pin dispatch and v2 definition persistence are still to be integrated. Compilation alone does not establish host or persistence-provider support. No HTTP requests, durable retry/effect storage, worker recovery, new native continuation, API or Studio acceptance is claimed by these tests. T003 still owes its full old-worker/surrounding-native resume journey; its retained store replay is described separately.

Durable task-runner and pause semantics remain the program's recorded follow-up. These data contracts select no scheduler or actor provider and claim no workflow/queue pause capability.
