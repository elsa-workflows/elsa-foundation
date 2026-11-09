# Persisted parent publication and runtime-only execution

This checkpoint covers the compiler/publication part of T018 under [#3109](https://github.com/elsa-workflows/elsa-foundation/issues/3109), in draft [#3111](https://github.com/elsa-workflows/elsa-foundation/pull/3111). T018 remains unchecked: the ETL-owned paginated picker, catalog acceptance and API/Studio product journey are still owed.

## Proof boundary

`EtlPublishedWorkflowJourneyTests.Published_parent_keeps_its_ETL_version_after_edit_and_runs_without_design_services` runs for native Sequence and Flowchart. Its authoring fixture uses the production CLR scanner/catalog factories and source-version publisher for the composite, `RunEtl` and production `WriteLine` sibling. Real SQLite stores retain the ETL draft and published v1, workflow draft, promoted workflow version, publication activation, executable and source reference. `IRequestSender` dispatches the ordinary `PublishWorkflow` request; the normal compiler's event/source fan-in supplies the embedded ETL artifact. Neither a hand-built executable, `Harness.PublishAsync`, direct metadata-enricher invocation nor copied publication serves as this evidence.

After parent publication, the fixture edits the ETL draft to change its output field from `name` to `renamed`, publishes ETL v2 and rereads the persisted parent workflow version. Its selection remains the full v1 hash/version. It then disposes the entire authoring provider.

The test takes the owned ETL design SQLite database offline and creates a separate runtime-only provider over the same native runtime database and new ETL runtime database. This is provider separation in one test process, not a separate-process crash proof. The runtime composition has no ETL definition store, artifact compiler, design context, compilation sources or workflow executable compiler. It reads the saved parent executable and v1 envelope, then starts the real live source reference through `IWorkflowStartDispatcher` in the explicitly bound persistence partition. No executable is rewritten or republished for runtime.

Before the ETL worker starts, the test observes the real suspended child, frozen v1 invocation and committed bookmark and verifies that the following sibling has not run. The production ETL worker and native resumption sweep then complete the workflow. Every compiled node has one completed persisted invocation; the child's actual typed consumed receipt matches its frozen registration/provider/type, its bookmark is removed, private state/payload are retired, and the terminal delivery is acknowledged. The accepted manifest contains one actual main output chunk with `{"name":"Ada"}` and one accepted record, demonstrating v1 behavior after v2 publication. These are durable continuation assertions, not an exactly-once claim about console or external effects.

## Composition details found during integration

- Manual feature setup must include publishing's declared `WorkflowsRuntimeTriggers` dependency and the production mediator implementation.
- Runtime EF selects the recurring schedule projection store. The authoring fixture composes `WorkflowsRuntimeRecurringTriggers` so its real indexer prepares even this workflow's empty schedule projection before atomic activation. Without it, publication explicitly failed with a missing prepared recurring-schedule projection. No schedule row was fabricated; runtime-only execution omits this authoring feature.
- The generic harness start helper creates an unbound DI scope. A partition-owned publication is invisible there even if the start request names that partition. This fixture binds the ordinary persistence operation scope before invoking the production dispatcher, as a transport must. It does not copy the artifact or reference into another partition.
- Shared catalog construction and host file binding fixtures keep the earlier native composite journeys and this publication proof aligned.

## Validation

The two focused cases passed. Omitting the production `EtlPublishedArtifactPinSource` registration then made both fail the explicit persisted compilation-contribution assertion before native start; neither compilation failure nor timeout was used as the oracle. Source restoration is byte-exact, recorded with hashes in [the regression receipt](persisted-parent-publication-regression.json). The complete affected ETL suite then passed **299 tests, zero failed/skipped**.

The complete architecture guard passed **579 tests, zero failed/skipped**. The CI restore script passed both Release and isolated Debug graphs in locked mode. The dependency map was regenerated, its four new test-reference edges reviewed and its freshness check passed. Solution filters were generated/checked; ignored findings were reviewed (72 catalogs indexed, none missing/unlinked, no direct runtime-to-design reference signal). Diff checks passed. Three root self-review passes ended clean after addressing fixture composition, trusted start scope, teardown/proof boundaries and shared arrange code. No production source, public API or persisted schema changed in this checkpoint.

Commands executed from the active worktree:

```text
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603
bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
bash tools/solution-filters/generate-solution-filters.sh
bash tools/solution-filters/generate-solution-filters.sh --check
git diff --check
```

Map/filter generators ran serially. Bounded worker review/exploration was input to root integration and QA, not an independent approval gate. The earlier native composite and process interruption evidence retains its original provenance; this checkpoint does not imply ETL HTTP/browser, provider/schema matrix or complete product acceptance.
