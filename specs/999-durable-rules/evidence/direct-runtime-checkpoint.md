# Direct runtime, bounded inspection and delivery-store checkpoint

This internal checkpoint completes T008, T010, T012 and T013 for real feature/service composition and migrated SQLite. The Rules-side portion of T029 is implemented; actual native bookmark dispatch and durable parent proof remain pending. No installed-package, separate-process, additional-provider, native-composite or upgrade qualification is claimed. The #3181 whole-leaf claim remains active.

## Implemented boundary

`IRuleInvocationFactory` freezes canonical inputs, the omitted round budget and a verified deployment pin before returning an immutable envelope. Callers retain that envelope for retries. Runtime identity reads use configured trusted receipt/deps paths beneath the physical host root, bounded JSON and file reads, the selected deps runtime target and hashes of actual loaded assemblies. Required Rules packages must match their corresponding default-load-context assemblies. Missing or incompatible identity is Unavailable; no downloader, dynamic loader or repinning fallback exists.

The `RulesRuntime` feature uses the existing Tasks lifecycle and GenericHost hosting. Its singleton worker starts once, waits for the appropriate startup boundary, runs a bounded number of scoped attempts, and drains its real execution task on shutdown. Cancellation of shell activation does not cancel the worker lifetime. Durable store claims remain the authority for work. Each attempt accepts and then consumes through a fresh authoritative work read; shutdown leaves unfinished leases recoverable. New admissions may be disabled while retained replay and recovery remain available.

`RulesRuntimeEntityFrameworkCore` registers the real SQLite context factory, migration lifecycle and complete store through the existing EF module binding/schema gate. Both pooled and non-pooled factories are exercised. Explicit host service replacements survive either registration order; the default reader follows the selected store instead of silently resolving another implementation.

Snapshot progress is bounded and stable across later acceptance/consumption. The cursor binds the full run scope/fingerprint and retained snapshot revision, but grants no authorization. Queries select bounded headers before loading selected bodies. Unsupported or corrupt earlier claim candidates remain unchanged and do not hide later healthy work. Common accepted receipts can be consumed without the original interpreter; subsequent evaluation still requires its original compatible pin.

Delivery uses a separate database-time lease and immutable full envelope. New acknowledgement requires a configured durable-proof validator and matching canonical destination/result content. Validation occurs outside the write transaction and is followed by a fresh durable-winner read, including negative/unavailable validation outcomes. Exact retained acknowledgement replays without revalidating the old parent. New writes retain ownership/fence/revision checks and post-save expiry rollback. A direct run has no delivery obligation. Physical external effects remain at least once.

## Review and corrective evidence

Root integration and bounded delegated reviews corrected loaded-package/assembly attribution, normal empty project deps hashes, shell-first lifecycle handling, override-to-reader registration, and corrupt-candidate starvation. Test setup was reduced to shared host/database helpers while each delivery test retains its own database; a class-wide store fixture had allowed global claims to consume another test's rows. Initial compiler/API tracking errors and a test-local shadow were corrected before final validation.

The starvation reproducer ran against pre-fix source: corrupt run JSON, corrupt pending receipt and missing pending receipt each returned Unavailable instead of claiming the later healthy run. All three failed as behavioral assertions. The corrected loop skips those unusable candidates, performs no repair writes and retains Unavailable when no usable candidate exists. The final suite includes all three regressions.

Three actual source-removal controls ran serially and were restored:

| Removed behavior | Observed result |
|---|---|
| Hide consumption occurring after a retained progress snapshot | Snapshot stability regression failed |
| Require durable proof validation before new acknowledgement | Proof-required regression failed |
| Compare the loaded asset digest with the deployment receipt | Loaded-file mismatch regression failed |

Each control failed one intended test, with zero passes/skips, rather than failing compilation. [Control receipts](direct-runtime-controls.json) preserve restoration hashes and results. The full focused suite was rebuilt after restoration and the final review fixes. Final scoped source review found no further concrete defect.

## Executed validation and limits

- **191 Rules tests passed, zero failed/skipped.** These include real migrated SQLite and GenericHost feature composition; default/explicit budgets, success/fault/refusal/invalid/unavailable outcomes; immutable replay with admissions disabled; shell-first startup and activation cancellation; pooled/non-pooled factories; replacement registration order; snapshot paging; delivery expiry/reclaim/rollback and concurrent acknowledgement replay.
- **86 selected architecture, public API and Rules EF-closure checks passed, zero failed/skipped.** Existing unrelated analyzer/dependency warnings remain outside this checkpoint.
- Dependency restore passed. Local maps regenerated, map/solution-filter checks passed, and generated findings showed zero runtime-to-design direct reference signals. Current specs 257/259 keep generated maps local/ignored; no generated snapshot was force-added.
- Diff/whitespace review passed. The change is confined to Rules and spec 999.

Composition tests inject synthetic identity, and identity-reader tests use synthetic receipt/deps metadata over actual loaded assembly files. Neither substitutes for an isolated NuGet consumer. Delivery tests use a synthetic proof validator and therefore do not establish real native parent continuation. Deterministic database-clock/interleaving fixtures supplement real SQLite transactions; other engines and independent processes remain unqualified. Workbench REST acceptance remains pending with the native adapter, not a skipped check counted as passed. Historical unrelated timeout causes remain unproved.

## Commands and retained evidence

```sh
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj --no-restore
python3 /tmp/3181-composition-controls.py
# Rebuild the full focused suite after restoring all temporary controls.
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore --filter 'FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~PublicApiFilesGuardTests|FullyQualifiedName~Every_admitted_Rules_project_resolves_only_its_reviewed_EF_closure'
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- solution-filters-check
git diff --check
```

Raw local logs use `/tmp/3181-composition-`: `final-tests.log`, `architecture.log`, `restore.log`, `claim-before.log`, `control-snapshot-consumption.log`, `control-durable-proof-required.log`, `control-loaded-asset-digest.log`, and `maps-{all,check,solution-filters-check}.log`. Executed results and control receipts are durable here; raw logs remain local. Next qualification work is actual packages, additional providers/process recovery, persisted native composites and upgrade/rollback, followed by the reviewed PR and resulting-main gates.
