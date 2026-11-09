# Final integrated HTTP ETL gate

Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151), PR [#3156](https://github.com/elsa-workflows/elsa-foundation/pull/3156), T020 and T021. This checkpoint completes local integration and review; exact-head CI and resulting-main delivery are separate gates.

## Integrated source and contracts

Main `4014274b6fc04e6714277d33eebfa8e0ed6a6cb6` was merged normally in `789434158`. The sole modify/delete conflict retained main's removal of committed dependency maps. Current maps live on `generated-maps`; the local generator's `check` validates the committed solution filters. No workflow, release, permission or deployment configuration was changed by this leaf.

Reviewed candidate `f5fccbe5b4c92a78ecbea90bb272eab54e4e5578` includes the full finite profile and the final fixes below. Public additions are in Unshipped baselines. README/extension catalogs describe versioned codecs, provider composition, scoped HTTP bindings, all five runtime endpoints, migration order, truthful diagnostics and upgrade limits. V1 file-only composition remains supported. Existing `IEtlRuntimeOperations` implementations need not implement connector inspection: its default reports the established provider-unavailable response while preserving the four old operations. A real TestServer test verifies existing-route success, denied connector inspection and authorized unavailable inspection.

Upgrade all ETL workers before v2 admission. SQLite migration is additive from the released v1 schema; early private draft connector databases require recreation. Old binaries must not consume newly admitted connector runs. Receipt time is optional for old callers/journals, retaining their conservative reconciliation rule.

## Review findings resolved

The two earlier bounded rounds are recorded in the [T010 receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3156#issuecomment-6315872091). [Round 3](https://github.com/elsa-workflows/elsa-foundation/pull/3156#issuecomment-6321708425) used separate correctness, acceptance and API/Studio/contract reviews plus root integration. It found:

- A timely destination acknowledgement could be rejected if database reconciliation followed attempt expiry. `df031ca8f` retains validated response receipt time separately from durable completion. Current ownership, fence and terminal checks remain required. Response status determines acknowledgement; optional body draining is bounded. A timely success can reconcile later; a late transient outcome cannot start a retry beyond the overall deadline. `f70f0a5cc` corrected a test's contradictory retry policy, and `f5fccbe5b` clarified response-body wording.
- Direct real-child-process proof was missing for error-route Retry-After after main-route success and source Retry-After across restart. `1f4c38094`, `3c2ebc59b` and `8b5aacec4` add those cases. Ownership is released before predecessor exit; the successor is ready before eligibility, so lease expiry cannot hide an early retry. Observed requests preserve body/effect identity and accounting without resending the successful main route.
- T020 separately repaired the legacy runtime-operations interface requirement (`fad9507c5` and `0129a02b2`). Root corrected the API analyzer notation and missing test import before verification.

Root's final pass over the integrated contracts, fixes, tests, live results and documentation found no remaining actionable issue. Final public round 4 identifies the exact documentation-inclusive head before merge. No review finding is being deferred to make the gate green.

## Executed local validation

Heavy commands ran serially on the shared machine. Paths below identify retained local logs; they are not committed payloads.

| Gate | Result | Local log |
|---|---|---|
| Full ETL: `dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj -p:RestoreLockedMode=true` | 1,009 passed; zero failed/skipped | `/tmp/3151-t021-final-etl.log` |
| Focused receipt/metadata/store/transport tests | 43 passed; zero failed/skipped | `/tmp/3151-t021-receipt-focused-fixed.log` |
| Direct source/destination Retry-After child-process cases | 2 passed; zero failed/skipped | `/tmp/3151-t021-process-retry.log` |
| Legacy runtime-operations TestServer case | 1 passed | `/tmp/3151-t020-api-compat-final.log` |
| Architecture: `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -p:RestoreLockedMode=true` | 584 passed; zero failed/skipped | `/tmp/3151-t021-confirmed-architecture.log` |
| Core-only: `dotnet test Elsa.Server.Core.slnf -c Release -p:RestoreLockedMode=true -p:WarningsNotAsErrors=NU1603` | 4,198 passed across 19 suites; zero failed; **2 skipped** | `/tmp/3151-t020-core.log` |
| ETL Studio component tests | 69 passed in eight files | `/tmp/3151-t020-studio-etl.log` |
| `pnpm --dir Studio exec tsc --noEmit -p ../Extensions/Etl/studio/Elsa.Studio.Etl/Client/tsconfig.json` | Passed | `/tmp/3151-t020-studio-etl-types-fixed.log` |
| `pnpm --dir Studio build`, Studio host build | Passed | `/tmp/3151-t020-studio-assets.log`, `/tmp/3151-t020-studio-host.log` |
| Rebuilt Workbench | Passed | `/tmp/3151-t021-confirmed-workbench.log` |
| `pwsh -NoProfile -File e2e-tests/etl/Test-FiniteHttpEtl.ps1` | All six connector/composite cases and delayed-source cancellation passed | `/tmp/3151-t021-confirmed-http-rest.log` |
| `pwsh -NoProfile -File e2e-tests/etl/Test-FiniteFileEtl.ps1` | Sequence and Flowchart passed | `/tmp/3151-t021-confirmed-file-rest.log` |
| `pwsh -NoProfile -File e2e-tests/etl/Test-FiniteFileEtlStudio.ps1 -Http` | 2 authenticated browser cases passed; zero failed/skipped/flaky | `/tmp/3151-t021-confirmed-http-browser.log` |
| Same Studio runner without `-Http` | Existing file journey passed; zero failed/skipped/flaky | `/tmp/3151-t021-confirmed-file-browser.log` |
| `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` | Committed solution filters match current graph | `/tmp/3151-t021-confirmed-filters.log` |

Full ETL ran on `f70f0a5cc`; the following `f5fccbe5b` only clarified XML/contract text. Architecture, rebuilt REST/browser and filter checks ran on `f5fccbe5b`. Core-only and Studio checks ran on the earlier integrated candidate; subsequent changes do not alter their production surface. Exact-head CI is required in addition to these local checks.

The two core skips were an opt-in SQLite stress test and a Linux-only `/proc/self/mem` case on macOS. They are not passes. Root inspected the current browser screenshots for partition progress and native cancellation; following work remains unexecuted after cancellation. Owned servers were stopped and private databases/authentication traces/screenshots retained locally.

Regression controls: restoring the old delivery transition failed both timely-receipt cases with `Invalid`; removing the interface default failed old-provider compilation with CS0535. Restored sources passed. Earlier partial failures remain recorded: a receipt test initially had an invalid retry-policy fixture (42 passed, one failed); the first final architecture pass rejected a stale Core assembly after an XML edit (583 passed, one failed), then passed after rebuilding Core; package-direct typecheck could not locate `tsc`, then passed using the workspace compiler. None is represented as a passing attempt.

## Upgrade requalification

The frozen predecessor/current successor runner was rerun after main integration on clean `f5fccbe5b4c92a78ecbea90bb272eab54e4e5578`:

```bash
python3 Extensions/Etl/tests/Shared/test-v1-native-upgrade.py --evidence-root /tmp/3151-v1-native-upgrade-integrated
```

Producer **1/1** and successor **1/1** passed, zero failures/skips. Root read both TRX counters, provenance and migration/integrity receipts. The predecessor had one runtime migration and the successor nine; runtime, workflow and offline design databases each returned `ok` before and after. The same owed batch and native Sequence completed under the successor. The runner verified unchanged archive hashes and removed its owned predecessor checkout. See [the detailed upgrade contract](v1-native-upgrade-checkpoint.md) for assertions and same-machine/path limits.

## Delivery boundary

This closes T020 local integration. T021 exact-head CI and T022 resulting-main verification/bookkeeping remain required. Main `4014274b6` CI passed, including backend/architecture/core/maps; scheduled-only EF jobs were skipped. Older main-red issue #2293 remains open because its cause has not been established. Later passing runs do not explain historical receipt/HTTP timeouts.

This is finite HTTP/CSV input to HTTP/JSONL output within native workflows. Parent continuous sources, SQL/RabbitMQ, BPMN integration and the future task runner/pause capability remain outstanding. Remote effects are at least once, collection-wide snapshots depend on the source API, and this leaf makes no general exactly-once or power-loss claim.
