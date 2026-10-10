# Authoring-only acceptance rehearsal

These are implementation acceptance steps, not executed results. The existing fixtures currently describe the old combined dependency; running them before implementation does not prove the new boundary. Use the [proof matrix](contracts/acceptance-proof-matrix.md) and [surface contract](contracts/authoring-surface.md).

1. Implement the reviewed support/narrow API/EF boundary after official task generation. Use an isolated Authoring host configuration that replaces Workbench baseline full Publishing/Runtime/Resumption selections; adding a feature to the existing combined baseline is insufficient.
2. Supply the existing authentication substrate, actual Read/Manage grants, one centrally named SQLite resource and required private cursor keys through disposable private configuration. Keep secrets and connection values out of diagnostics and public artifacts.
3. Build only affected projects through the normal shared-machine build-slot wrapper. Rebuild Workbench before process proof and use a fresh database after rebuild; record selected source heads.
4. Run the existing targeted Authoring fixtures after extending them to the actual authenticated persisted publication/restart/absence journey:

```bash
dotnet test tests/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.csproj --filter FullyQualifiedName~AuthoringCompositionHostEvidenceTests
dotnet test tests/essentials/Workbench/Tests/Elsa.Workbench.Tests.csproj --filter FullyQualifiedName~AuthoringFixtureHostEvidenceTests
```

5. Run affected Publishing API/Runtime EF ownership/activation fixtures and existing full combined test-run controls. Perform one actual old-dependency/full-mapper bypass control: it must fail the narrow absence assertion; restore and rerun the focused proof.
6. Connect the updated current Studio client to the rebuilt authenticated host. Persist/publish, read slot/artifacts, download/decode the actual closure, select/export a retained history publication through its precise advertised relation and exercise all three run controls. Prove no version-only fallback when precise export is absent/refused. Verify no execution requests when corresponding relations are absent; repeat successful existing execution controls against the combined host. Use real supported login if the browser actor requires it, not an injected token masquerading as user proof.
7. Execute the applicable backend e2e suite against rebuilt host per `e2e-tests/README.md`. Reconcile stale tests against fresh main instead of treating a failure as an automatic product defect.
8. Before profile publication, use actual public CLI plan/accept/generate with the new reviewed immutable profile/catalog and private resource inputs. Start a fresh intended host from the exact generated file and repeat authenticated host/client/restart/absence proof. Preserve old pins/bytes; do not infer this handoff from a manually assembled fixture.
9. Run architecture and maps freshness, review the exact diff and publish exact-head actor results, provider/host limits, skip counts and review state. No new EF suite, provider matrix or cadence is needed.

A successful specification publication means this contract/proof plan is reviewed. Backend delivery, actual Studio integration and released profile each require their corresponding actor evidence; none is already established by this document.
