# Validation guide: activity playback

## Prerequisites

Foundational dependency T003 must be delivered and qualified before product implementation/proof. The current activity browser alone does not qualify. Use only owned disposable Workbench/Studio stores, ports and test identities. No existing demo/personal host, external model call, timestamp editing or substituted history response.

## Supporting checks after implementation

From repository root:

```bash
pnpm --dir Studio install --frozen-lockfile
pnpm --dir Studio --filter @elsa-workflows/studio-workflows test --maxWorkers=1 --minWorkers=1
pnpm --dir Studio typecheck
pnpm --dir Studio lint
pnpm --dir Studio exec playwright test tests/browser/activity-playback.spec.ts --workers=1
python3 -m unittest discover -s tools/ux-acceptance -p 'test_*.py'
```

The browser file above is planned, not yet implemented. Reconcile whole-suite execution with discovery. Run the owner facet's complete API/provider suites and applicable architecture/map/public-contract gates; exact commands follow the reviewed owner implementation paths. Check `uptime` before interpreting timing-shaped failures. Do not bypass build-slot wrappers.

## Composed evidence matrix

Add these four named journeys to `Studio/tests/acceptance/journeys.spec.ts` and the runner inventory. Execute each in desktop-light/dark and narrow-light/dark:16 executions, no retries/skips. New narrow playback cases set320px without changing inherited journeys.

| Journey | Actual facts and required controls |
|---|---|
| Recorded path and transport | Actual branch/nested/loop/parallel and held-retry runs; exact IDs/lineage, forward/reverse, four paces, pause/manual inspection/backgrounding, end, reduced motion and keyboard. Preserve unsupported-edge truth. |
| Paged ordinal seeking | Exactly200 actual occurrences including composites; first/middle/last/older repeat, ten rapid seeks, page buffering, failed real read/retry and bounded request/identity manifests. |
| Live bounds and recorded waits | Actual Event wait/resume appends, frozen bound and explicit refresh; source-controlled clock witness creates a real52-minute recorded wait, while browser time controls cadence only. |
| Coverage and access boundaries | Actual owner-recorded gap/retention operation, coarse/unsupported coverage, unavailable pin, genuine metadata/payload grant differences and access revocation during a pending read. Never equate logout with all permission cases. |

Reuse real seeds from existing timeline/executable acceptance journeys and `e2e-tests/single-outcome/Test-ForEachLoop.ps1`, `branching/Test-ParallelFork.ps1`, `concurrency/Test-ParallelSuspendedBranches.ps1`. The actual held-retry, gap, clock and permission witnesses are required additions; current fixtures do not establish them.

After committing the tested source and registering the journeys:

```bash
python3 tools/ux-acceptance/run.py --output /tmp/studio-activity-playback-<unique-run>
```

Retain exact head/source/dirty metadata, identities/coverage/request manifest, named PNG/ARIA states and traces. Include normal, pending, failure/retry, gap/denied/unavailable and context-loss captures; all intended project/journeys must run. Download the current CI artifact and have an independent reviewer open the affected states and traces. A green machine report, a local absolute path or an illustrative concept is not final UX approval. Preserve failed/not-run states and rerun the affected full journey after fixes.


## Recorded prerequisite qualification

These supporting results do not establish T003 production/composed readiness. Exact commands, failure history, scope and local TRX hashes are in their receipts; they are not downloadable visual acceptance.

| Source | Actual complete project results | Receipt |
|---|---|---|
| S14a `43cdff392b` | Runtime4193, API399;0 fail/0 skip at clean head | [Current-subject/component receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6861895228) |
| S14b `0f68c9ae6f` | EF2086 with recorded source-equivalence caveat; clean strict native272;0 fail/0 skip | [Projection correction handoff](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6862730540) |
| Owner S11c `57fa2bfed3` | Clean EF2124;0 fail/0 skip after recorded compile correction | [Staging receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6862226651) |
| S14c `ccbc769989` | Clean EF2208 and strict native275;0 fail/0 skip | [Reader handoff](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6862759814) |
| S14c deferred correction `d79d214871` | EF2211 with recorded source-equivalence caveat; clean strict native275;0 fail/0 skip | [Deferred-read handoff](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6864077533) |
| S14d selected read adapter `da6b5e1895` | Clean complete EF2301, strict native278;0 fail/0 skip | [Selected-reader handoff](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6864442037) |
| Owner F `d644cf6b61` / `453c48cbf2` | Clean EF2192 at d644; clean strict native273 at owning test-import correction453;0 fail/0 skip | [Retention qualification](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6864444640) |

The earlier ccbc row includes the owning mapper correction; d79 supersedes its owned SQLite transaction boundary. The selected-adapter row adds actual PostgreSQL/SQL Server/MySQL adapter controls. Isolated EnsureCreated, context reopen and modeled retention do not qualify generated migrations, native checkpoint/retention owners, process restart, actual persisted IAM or the normal composed host. Final composition must also include the separate S14a authorization head. T003 and all dependent UI/composed/merge tasks remain unchecked until their actual gates pass.

Official four-provider generated migrations are retained locally at `1b2fae2049bc9050d9932c08e43391e53e4a2b28` on reviewed G base ce6. All40 historical migration blobs remain unchanged; all four pending-model checks and the scoped post-generation build passed. Their19,891 generated lines remain separate from authored source; publication/review and final integrated upgrade/restart qualification are still open. See research R7 and the owner handoff for scope.
