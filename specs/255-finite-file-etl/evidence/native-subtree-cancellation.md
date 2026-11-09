# Native subtree cancellation and placed resume targets

T022 proves native-initiated whole-workflow and activity-boundary subtree cancellation for the finite ETL slice. This is a checkpoint in draft PR #3111; it does not complete the product journey or program.

## Executed journey

The SQLite/native fixture covers Sequence, Flowchart and CompensationScope in each ordering:

- After the actual native bookmark and ETL admission, cancel either the whole workflow or the enclosing reusable Activity Graph boundary through the real actor command and scheduler. The subtree command targets the placed boundary's own execution scope; assertions verify the Graph → composite → RunEtl parent chain. No scope is injected into an ordinary composite.
- Delay the production admission call after the bookmark/start intent commits. Cancel through the same native command, deliver the committed cancellation-intent kind through the real outbox processor, and require a cancelled/no-bindings tombstone before releasing admission. The delayed real admission leaves the entire persisted run unchanged, with no preparation or bindings.

All twelve cancellation cases require the cancelled ETL activity/composite, absent following siblings and bookmark, no consumed result receipt, no accepted output manifest, and a cancelled ETL result. The real ETL worker acknowledges `ProvenParentTerminal`; the tree/bookmark/manifest assertions repeat after acknowledgement. Subtree cancellation leaves the outer Sequence and workflow running, in accordance with existing native semantics. Sequence's structural `Defer` does not change its status to `Waiting`.

Three additional positive controls require normal ETL completion to return through the placed composite and Graph boundary into the surrounding Sequence and its following sibling. The original direct-composite terminal matrix remains separate; the Graph wrapper declares only `Done`, so these controls do not qualify propagation of every business outcome through arbitrary reusable contracts.

## Canonical boundary inputs and limits

The fixture uses actual CLR-scanned contracts/catalog factories, the source-owned activity publisher and EF publication/template/source-reference stores, the production Graph compiler and template placer, then the production executable metadata enricher. It asserts that the generated resume target is genuinely different from the node/local composed ID. The wrapper publication itself is an explicitly documented in-memory overlay over the real publication reader. The resulting parent executable uses the existing harness publication path. This proves runtime boundary semantics; it does not prove the full persisted wrapper/workflow authoring, picker, publishing API, or Studio lifecycle. Only the flat native structures used by these fixtures are re-authored against current scanned versions; historical pin compatibility is not claimed.

## Corrections and compatibility

Normal source publication exposed a missing `ResumeTarget` declaration on `RunEtl.ResumeAsync`. The retained native failure state recorded the missing `etl-completed` target before ETL admission. After that declaration was added, placement exposed the bridge's assumption that all targets use `node:local` identities. Fresh activations now resolve and validate the actual target from the pinned executable and retain it in private state. Registration, bookmark, result and invocation comparisons remain exact. Cancellation recovers that same address after a legitimate consumed receipt payload is retired.

The existing pure target lookup moved to Runtime Core; the scheduler's prior helper remains a forwarding compatibility surface. Tests cover global-ID precedence (ownership remains a caller check), unique opaque local lookup, missing/ambiguous targets, argument guards, and unchanged ambiguity diagnostics through both entry points. ETL activation separately rejects missing/foreign-node/wrong-handler targets. Earlier direct-invocation private requests with a missing/null field retain the composed-ID reader fallback. Previously unsupported opaque placed snapshots cannot be reconstructed this way; no rolling downgrade or deployed migration qualification is asserted. See [data model](../data-model.md).

## Regression controls and validation

The [target regression receipt](native-placed-target-regression.json) records separate temporary removal of the declaration and the frozen target field. Each makes all three placed positive controls fail; byte-exact source restoration passes. The earlier [parent-cleanup regression](native-parent-cleanup-regression.json) independently proves native cancellation cleanup. These are concrete regression controls, not explanations of historical timing failures.

Local validation: **273 ETL, 2,436 workflow-runtime and 579 architecture tests passed**, with zero failures/skips. The final 28 native composite cases additionally exercise the explicit opaque-target, retired terminal state/receipt payload, and persisted-invocation assertions. Locked restore, committed dependency map, solution filters and diff checks pass; existing package versions did not change. Root self-review used five bounded passes, with focused worker input reviewed and integrated by root. PR checks remain separate exact-head evidence. Commands:

```sh
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore
dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore
dotnet restore Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --locked-mode -p:WarningsNotAsErrors=NU1603
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
bash tools/solution-filters/generate-solution-filters.sh --check
git diff --check
```

T022 completion does not complete final materialization, live-worker permits, two-process recovery, API/Studio/HTTP/browser acceptance, or the other unchecked tasks. Physical effects remain at least once; cancellation neither terminates a physical worker by proof nor reverses prior output. Historical receipt/HTTP timeout causes remain unproven. Orleans remains deferred; no actor provider is selected.
