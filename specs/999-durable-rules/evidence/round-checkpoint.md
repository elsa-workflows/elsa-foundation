# SQLite round acceptance and consumption checkpoint

This internal checkpoint completes the SQLite implementations and focused tests for work snapshots, accepted receipts, one-time consumption, cancellation races and lost-response replay (T007, T011 and T016–T018). It does not complete actual host composition, snapshot progress paging, delivery acknowledgement, packages, separate processes, additional providers, native composites or upgrade qualification. The whole #3181 claim remains active and the store is not yet registered as a complete public implementation.

## Result and transaction boundary

`ReadWorkAsync` returns a consistent bounded round input or pending receipt for the same live owner/fence, with a refreshed semantic revision and no mutation. It checks the run concurrency token after reading the related receipt and sampling database time. A consume/cancel/renew interleaving causes a fresh read before the projection or ownership is evaluated. The expected revision on an old token does not prevent the same owner from reading its new revision.

`AcceptRoundAsync` calls the Runtime complete-step validator outside a transaction, then rereads authoritative state even after Invalid/UnsupportedFormat. Only Equivalent may commit a new receipt. Exact immutable receipt content can replay an earlier acceptance before current ownership/revision checks, preserving the first physical attempt and returning only the original IDs/revision. It never returns successor work. A different receipt conflicts. New acceptance changes the pending-receipt pointer and model revision, but not committed facts or round count.

`ConsumeRoundAsync` uses the accepted common receipt without rerunning the old interpreter. A tracked transaction atomically applies canonical additions, appends fact history, marks the receipt consumed and records its immutable transition outcome. A no-addition round establishes success before the budget check; the witness succeeds with budget four and faults after committing its third adding round with budget three. Terminal state and an optional native delivery obligation are written in the same transaction. A direct invocation has no delivery obligation. A repeated consumption returns the original confirmation even after further rounds or cancellation.

Fresh writes check exact identity/fingerprint, current revision, owner/fence, input/pin and database-time expiry. The existing shared schema write gate, concurrency token and post-save live-clock check remain in force. Expiry after the writes rolls back the entire transaction, including receipts, facts/history, terminal and delivery fields. Cancellation preserves committed facts and excludes pending additions. The contract retains the mutation/check-point guarantee and does not promise commit before wall-clock expiry or exactly-once physical external effects.

Pending projection sizing now includes the actual receipt body and its escaped attempt/transition metadata. Versioned transition-ID derivation and historical replay are recorded in the [persistence contract](../contracts/persistence.md). No public API, dependency, migration or shared schema-gate change was needed.

## Review and corrective cases

Root integration and delegated read-only review corrected:

- A test compared append-only fact-history order with canonical current-fact order. History stays append-only; content comparisons use ordinal sorting while checking visibility revisions separately.
- Related-row validation could treat concurrent consumption as corrupt data. The three paths now verify the run snapshot before validating the joined receipt.
- Projection sizing included the receipt JSON but omitted its stored metadata. The bound now includes both, with an escaping regression.
- A renewal across the original expiry could return LeaseLost despite continuously valid renewed ownership. Its reproducer failed with LeaseLost; checking the snapshot before ownership fixes it.
- A coherently corrupted Running row at its round limit could consume once more and produce an unreadable terminal. Its reproducer returned Consumed; Running state now requires `CommittedRounds < MaxRounds` and refuses the corrupt row without writing it.
- Rollback snapshots initially covered only selected run fields. They now serialize every run field, receipt and fact-history row.

The initial compiler pass also caught use of an inaccessible internal Core validation helper. The provider now validates the two incoming digest strings locally without broadening the Core API or friend assemblies. Reusable test clock, save and read interceptors were extracted from the prior lease tests; no duplicate database setup was introduced. Final separate source review found no further concrete defect. Root ran the validation; the reviewing agent did not run tests.

## Executed behavioral evidence

The final restored focused suite passed **155 tests, zero failed or skipped**. It includes migrated SQLite writes, complete witness firings and facts, budget/closure, semantic-invalid receipts, first-attempt attribution, read-only revision refresh, historical confirmation replay, compatible recovery without the old interpreter, competing consumers, cancellation versus consumption, validator interleavings with expiry/reclaim/cancel, post-save rollback and consistent reads across consumption/renewal.

Four actual source-removal controls were run serially and restored in `finally`:

| Control | Observed result |
|---|---|
| Let budget exhaustion override closure at the exact boundary | Four-round witness failed; three-round budget case still passed |
| Disable complete-step equivalence validation | All five malformed/incomplete receipt cases failed, including omitted firing evidence |
| Disable the post-save lease-window check | Acceptance and consumption rollback cases both failed |
| Ignore the run-token recheck after related receipt reads | Work read, acceptance replay and consumption replay cases all failed |

The controls failed as behavioral assertions, not compiler failures. [Control receipts](round-controls.json) retain each source-restoration digest and result. Those digests identify the control boundary; the subsequent reviewed renewal-order and running-budget fixes changed the final source. The two new edge-case regressions each failed on the pre-fix source, and the full 155-test suite was rebuilt green afterward.

These tests use real migrated SQLite transactions. Deterministic expiry/interleaving cases replace the tagged database-clock query response, not the production clock implementation. Response-loss tests retry through fresh store/context instances against retained state. They do not claim separate-process, real network-loss or PostgreSQL recovery proof. Synthetic test pins do not establish installed NuGet/loaded-asset provenance. Native delivery persistence is not native continuation or proof-based acknowledgement.

The selected architecture/public-API/Rules EF-closure suite passed **86 tests, zero failed or skipped**. The final focused rebuild after snapshot-helper cleanup again passed **155 tests, zero failed or skipped**. Local maps were regenerated, the generator check and solution-filter check passed, and diff checks were clean. Under the current specs 257/259 map policy, generated maps remain ignored local artifacts; no generated file was force-added. Existing unrelated architecture dependency/analyzer warnings and separate main-red reports are not resolved by this checkpoint. Workbench E2E remains part of the pending real host/native composition acceptance, not a skipped test counted as passed.

## Commands and retained evidence

```sh
dotnet test Extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj --no-restore
# Four temporary source changes are individually built/tested and restored in finally.
python3 /tmp/3181-rounds-controls.py
# Rebuild/rerun the full focused suite after all controls and review fixes.
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --no-restore --filter 'FullyQualifiedName~ArchitectureGuardTests|FullyQualifiedName~PublicApiFilesGuardTests|FullyQualifiedName~Every_admitted_Rules_project_resolves_only_its_reviewed_EF_closure'
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- check
dotnet run --no-build --project tools/maps/Elsa.Maps.Generator -- solution-filters-check
git diff --check
```

Raw local logs: `/tmp/3181-rounds-first-tests.log` (initial compiler failure), `second-tests.log` (128 passed), `races-tests.log` (150 passed), `final-tests.log` (153 passed before the last two regression additions), `restored-tests.log` and `verified-tests.log` (155 passed each), `control-{fixed-point-precedence,complete-step-validation,expiry-rollback,consistent-read}.log`, `renew-read-before.log`, `budget-state-before.log`, `architecture.log`, and `maps-{all,check,solution-filters-check}.log`, all with the `/tmp/3181-rounds-` prefix. The executed results and source-control receipts are durable here; raw logs remain local.
