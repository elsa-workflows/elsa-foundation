# Tasks: ETL runtime concurrency

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md)

Cadence: risk-based implementation with paired focused behavior tests, then red/green mutation controls for guards. No blanket TDD or performance measurement. Complete projects and API/architecture gates run on the stable candidate; repeat only for a relevant change or failure.

## Setup and foundations

- [x] T001 Verify the existing schemas and review spec → plan in `specs/1206-etl-runtime-concurrency/`; preserve source-bound parent #3803.
- [x] T002 Add the public `EtlStoreException` contract in `Extensions/Etl/src/Core/Runtime/EtlStoreException.cs`, track it in `Core/PublicAPI.Unshipped.txt` and document storage failures on all three runtime store interfaces.
- [x] T003 Add the private operation boundary under `Extensions/Etl/src/Persistence/EntityFrameworkCore/Stores/`: transaction/cancellation preflight, one EfWriteRetry budget, per-attempt cancellation and cleanup, final ETL failure mapping; preserve schema refusal and existing validation/corruption handling.

## US2 — authoritative concurrent transitions (prerequisite to activation)

Independent test: separate contexts on one real engine contend and preserve scoped authority, exact accounting, immutable input snapshots and one result/outbox identity.

- [x] T004 [US2] Split and wrap the v1 operations in `Stores/EfEtlRuntimeStore.cs`, retaining bounded effects capture outside retry and replacing the old two-attempt claim loop.
- [x] T005 [US2] Wrap connector and inspection operations in `Stores/EfEtlConnector*.cs`, retain admission pins across retries and use an unretried private connector-run read inside inspection transactions. Physical connector I/O stays outside this boundary.
- [x] T006 [US2] Add boundary/snapshot/cleanup controls to the existing shared `EtlRuntimeStoreContract` partial in `Extensions/Etl/tests/Shared/RuntimeStore/EtlRuntimeRetryContract.cs`, inherited by existing SQLite/provider subclasses: raw/wrapped/exhausted/nonretryable failures through all three ports, cancellation, caller/ambient/enlisted refusal before mutation, preserved corruption/schema refusals, input mutation after first attempt and successful reuse after failure.
- [x] T007 [US2] Add a multi-save preparation rollback control in `Shared/RuntimeStore/EtlRuntimeRetryContract.cs`: classified failure after a real successful save and before commit; retry reconstructs one graph; exhaustion retains no partial persisted/tracked preparation.
- [x] T008 [US2] Add overlapping native-provider races in `Extensions/Etl/tests/Persistence/ProviderTests/EtlRuntimeConcurrencyTests.cs` for admission/replay/conflict, preparation/partition authority, reservation budget, final acceptance/cancellation and delivery. Require a native classified conflict and record retry removal/restoration for the admission race.
- [x] T009 [US2] Add separate continuous-compaction controls in `ProviderTests/EtlRuntimeConcurrencyTests.cs`, staging failure around set-based writes rather than the first-save barrier; verify exact compacted counters, predecessor/request/batch integrity, one live claim and rollback-restored history on exhaustion.
- [x] T010 [US2] Run a four-writer, 32-run correctness soak on each server engine in `ProviderTests/EtlRuntimeConcurrencyTests.cs`; assert exact final/outstanding counters, unique terminal/outbox identities and stale-fence refusal. Record results and unproven guards under this spec's `evidence/`.

## US1 — ordinary host activation and restart readback

Independent test: one owned Workbench per server engine uses both ETL stores, publishes/runs a two-partition pipeline from native Flowchart, produces expected physical records, completes once and reads the same identities after host restart.

- [x] T011 [US1] After concurrent transition controls pass, replace the runtime refusal with `Binding.Select` in `DependencyInjection/EtlRuntimeEntityFrameworkCoreRegistration.cs`, add server entries to `AssemblyInfo.cs`, update feature/project manifest descriptions and prove four-engine/default/runtime-only/optional-capability composition in `EtlRuntimeProviderTests.cs` and `Extensions/Etl/tests/Tests/`.
- [x] T012 [US1] Add `specs/1206-etl-runtime-concurrency/evidence/runtime_store_workbench_journey.py` from the 1202 controller, using generated credentials, owned resource cleanup, safe ephemeral ports and verified product-source/hash receipts.
- [x] T013 [US1] Build intended Workbench products and run both-store journeys on all three engines; capture pins, partition/run views, exact output, one native completion and retained post-restart state in `specs/1206-etl-runtime-concurrency/evidence/`. Do not classify completed-state readback as an old-writer or mid-work crash proof.

The T013 receipt retains its historical executing source. Requalification after integration
uses the same controller, with manual `CI` `suite=etl-host` available when local capacity is
unallocated. Preparing that route does not mark fresh host qualification as passed.

## Qualification and delivery

- [x] T014 Run complete `Elsa.Etl.Tests` and required `Elsa.Etl.Persistence.ProviderTests` projects; reconcile discovery/execution and record sources/counts plus deliberate failures/skips separately in `specs/1206-etl-runtime-concurrency/evidence/README.md`.
- [x] T015 Run locked graph restore, `Elsa.Architecture.Tests`, generator solution-filter/test-tier checks and new public API/binary/computed-version checks; record receipts under `evidence/`. Commit filters only if changed; no literal package versions.
- [x] T016 Update `Extensions/Etl/README.md`, persistence README and `Core/EXTENSION_POINTS.md` with actual support, failure behavior, compatibility and remaining proof boundaries; set this spec's terminal status in its implementation PR.
- [ ] T017 Run independent complete-diff correctness/standards/acceptance review on the draft to convergence, post round and gate comments, mark ready/request review, and retain all exact-head required checks, unmerged-base and human-hold gates.
- [ ] T018 After base-first protected delivery, verify the resulting main with affected source-bound checks and update #2908, #2892 and Project #62. Keep the feature/program In Progress until remaining acceptance exists; continue #3789 off main next.

## Dependencies and parallel opportunities

T001 → T002–003 → T004–005 → T006–010 → T011–013 → T014–018. Both stories are P1; concurrency is the explicit prerequisite to ordinary server activation. Proof-harness drafting can be reviewed independently of finished Core documentation, but source edits and local builds remain sequential on the shared machine. No extra implementation agent or new task issue is required.

## Incremental strategy

First establish one retryable logical operation with native red/green evidence, then cover all existing operations and rollback boundaries. Enable providers only after that evidence. Reuse unchanged products for harness-only repairs while retaining their real product source, and batch broad qualification/review on a stable candidate. Later transforms, full recovery/stale-writer acceptance, BPMN and Studio remain separate ordered slices.
