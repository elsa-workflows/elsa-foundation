# Connector runtime inspection checkpoint

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), draft PR
[#3156](https://github.com/elsa-workflows/elsa-foundation/pull/3156). T018 is complete; **17/22 tasks** are complete.
The HTTP leaf is not ready to merge.

## Retained-state inspection

`IEtlConnectorRuntimeInspector` is an optional read-only provider capability. It does not require design
services or a running worker. Each provider call uses a consistent retained-state snapshot; separate HTTP
requests can observe later progress. It does not recheck a receiver or retained body file.

The runtime API preserves the existing file-only partition endpoint and adds a separately advertised
`etl-run-connector-partitions` capability. Connector partitions contain a typed file range or HTTP page/item
position, source exhaustion, recorded claim and expiry, source retry summary, owed output receipts, and
settled route-body counts. Unknown HTTP totals remain unknown. Pagination binds cursors to the caller's
scope, run, page size and endpoint kind and respects the provider's page cap.

Run details identify artifact format independently of admitted connector pins. A rejection before admission
therefore keeps its terminal diagnostic visible without inventing binding or partition progress. A missing
inspector capability is an explicit unavailable response. An admitted connector run cannot silently fall back
to file offsets.

An HTTP acknowledgement or file publication remains owed until internal batch acceptance. Accepted-effect
counts count settled route bodies, not records, attempts, or physical receiver executions. A fully filtered
batch contributes no route bodies. Cancellation preserves retained responsibility and does not undo effects.

## Storage and compatibility

An additive SQLite migration adds nullable main/error route-kind metadata and a covering index to retained
batches. It backfills from existing canonical output documents without rewriting their bytes, hashes, output
budgets, or logical schema version. Output retention writes the scalar kinds together with the retained plan;
subsequent plan reads validate their agreement. Inspection aggregates settled scalar rows in SQL and reads
only a bounded page of current positions and owed batches, rather than materializing all historical output
JSON. Aggregate query work still grows with retained batch history; this is not a constant-time claim.

The migration is forward-only, consistent with the existing connector migrations; restoring a verified
backup is the rollback route. SQLite is the qualified provider. This checkpoint does not qualify another
provider or a frozen predecessor worker; authentic predecessor resume remains T003.

## Executable evidence

- Runtime-only API composition, tenant/run/cursor isolation, page caps, unsupported capabilities, CSV ranges,
  optional lookup absence, and visible rejection before admission.
- Persisted HTTP capture and partial-page progress, retained route bodies, HTTP acknowledgement and JSONL
  publication before acceptance, then accepted counts and replay without double counting.
- Provider retry state, expired recorded claims, exhausted HTTP positions, cancellation with owed output,
  scalar/output consistency, and migration preservation.
- Studio endpoint choice, connector paging, missing-capability handling, HTTP unknown totals, source capture,
  receipt-versus-acceptance wording, and rejection diagnostics. Existing file inspection remains covered.

These API tests exercise the real HTTP endpoint stack and SQLite provider in-process. Studio tests use API
fixtures. Rebuilt Workbench and actual-browser acceptance remain T019; no live operator journey is claimed here.

Four bounded review passes plus root integration closed pagination cursor-kind support, visible pre-admission
rejection, receipt-versus-acceptance wording, indexed settled counts, historical migration fixture projections,
and custom-provider coherence validation. The final provider checks reject contradictory retry deadlines,
source capture statuses, missing acknowledgement journals and effect/phase combinations with a fixed diagnostic.
Root review found no remaining actionable issue after those corrections.

Removing the settled-batch predicate from both aggregation queries made all three selected acceptance and
cancellation regressions fail. The provider file was restored byte-for-byte before the full validation run.
A later cleanup removed an unused local only; final affected tests cover the final source.

| Gate | Result |
|---|---|
| Full ETL suite | 986 passed, 0 failed, 0 skipped |
| Final affected API/provider suite | 33 passed, 0 failed, 0 skipped |
| Studio tests | 69 passed |
| Studio typecheck and build | Passed |
| Architecture suite | 584 passed, 0 failed, 0 skipped |
| Settled-count regression control | 3 expected failures; source restored exactly |
| Generated maps and solution filters | Passed; no generated map changes |
| Relative links and diff | Passed |

Commands: `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --no-restore`,
`dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore`,
`pnpm --dir Studio --filter @elsa-workflows/studio-etl typecheck` (also `test` and `build`),
and `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, then `check` and `solution-filters-check`.
The final affected test filter is `FullyQualifiedName~EtlRuntimeApiHttpTests|FullyQualifiedName~Connector_inspection_`.
The full ETL run preceded six final malformed-provider cases and their private validation refinements;
all affected tests were rebuilt and passed afterward. The first architecture run correctly rejected links
to the unstaged evidence file; staging it and rerunning the full suite passed.

Exact-head CI, integration with newer main (currently conflicting), rebuilt Workbench/browser acceptance,
frozen predecessor-worker resume and final resulting-main checks remain delivery gates. External effects
remain at least once. No historical timeout cause is inferred from these passing runs.
