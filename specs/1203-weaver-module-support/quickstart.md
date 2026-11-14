# Validation guide: generic effects and first owner leaves

This is the future implementation gate, not a report that these cases passed. Put source-bound evidence on each leaf issue/PR and transition summaries on #3443/#3428. The plan PR itself needs documentation checks and independent review; it does not run or claim these product proofs.

## Prerequisites and ownership

Use .NET 10 at `/usr/local/share/dotnet/dotnet`, Python 3, the repository-pinned Node/pnpm, Chromium, and the supported container runtime for the provider matrix. Respect the build-slot wrapper if installed; do not bypass it by using the binary directly when the wrapper owns the slot. Run native qualification at low load with `nice -n 10`. Check `uptime` before interpreting timing failures.

Every native run owns its processes, database/cache/output roots and chosen ports. Inspect bindings; never assume 5095 is free or reuse the owner's authenticated host/state. Use synthetic credentials/canaries generated at run time, never developer secrets. Do not activate a publisher or publish packages remotely.

## Component suites

After implementation, run each affected project whole and compare discovered/executed counts. Run affected API/serializer, current-authority, receipt-integrity, context/disclosure and lifecycle tests together, including both default and replacement executors.

```sh
/usr/local/share/dotnet/dotnet test Extensions/Weaver/tests/Tests/Elsa.Weaver.Tests.csproj -c Release --list-tests
/usr/local/share/dotnet/dotnet test Extensions/Weaver/tests/Tests/Elsa.Weaver.Tests.csproj -c Release
/usr/local/share/dotnet/dotnet test Extensions/Weaver/tests/Persistence/Tests/Elsa.Weaver.Persistence.Tests.csproj -c Release --list-tests
/usr/local/share/dotnet/dotnet test Extensions/Weaver/tests/Persistence/Tests/Elsa.Weaver.Persistence.Tests.csproj -c Release
ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX=1 /usr/local/share/dotnet/dotnet test Extensions/Weaver/tests/Persistence/ProviderTests/Elsa.Weaver.Persistence.ProviderTests.csproj -c Release
python3 -m unittest discover -s tools/studio/Weaver.Conformance.Probe -p 'test_*.py'
```

With a build-slot wrapper, submit the equivalent commands through that wrapper, which must target the required binary. The provider flag makes unavailable Docker a failure rather than a successful skip. Retain actual passes, skips, expected-negative causes and retries separately. Extend owning Secrets/Connections whole suites as their leaves land; this plan does not make them part of leaf 0 implementation.

From `Studio/`, use the existing web suite and scripts:

```sh
pnpm --filter @elsa-workflows/studio-web test
pnpm typecheck
pnpm lint
```

Run the architecture/map checks when implementation changes their inputs:

```sh
bash tools/architecture/restore-ci-project-graph.sh
/usr/local/share/dotnet/dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj
/usr/local/share/dotnet/dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

## Leaf 0 behavior matrix

1. Toggle all three host classes off/on, including combinations, after preparation/approval and before explicit recovered execution. Test descriptor-raised and preparation-raised Admin, legacy pending/approved Admin with byte-identical old digest, and accepted receipts retained after policy changes. Approval calls no module/principal. A veto before dispatch invokes the owner zero times.
2. Exercise all principal registration orderings in the policy contract, distinct request scopes/current principal identity, missing/unauthenticated/mismatched actor/tenant, missing/duplicate owning authorization and selected backend isolation. Direct tools and context providers must fail closed even when bypassing default facades.
3. Perform an owner-backed no-persistence action. Independently read its invocation counter/result and unchanged resource revision. Test failed business verdict versus could-not-run refusal, every wrong status/scope/declaration/target/evidence combination, and a lying custom executor. Duplicate/late/recovered delivery never calls the owner twice.
4. Put generated canaries in every typed owner code/message/result/diagnostic path. Verify fixed replacement and unavailable classification in HTTP, SSE, provider payload, generic review/results and audit. Include malformed code, reserved code, dotted out-of-set code, forged host/event/custom-executor flag, explicit empty set and undeclared compatibility. Module fixtures separately prove opaque allowlists.
5. Compose memory and EF variations, neither/both/wrong selection, direct AddWeaverStores consumer context, old two-argument/explicit-null custom or EF factories refusing effects composition, declared custom variation, custom specialization with omitted/static declarations, root registration copy and no EF observation. Compare actual catalog, Attention, capabilities and tool/read acceptance while held, unobserved, finalized and newly available without restart.
6. Hydrate old/new mixed row kinds, protected corruption and mismatched projected columns; prove canonical disclosure bytes and legacy digests unchanged. Test audit-owner/counter and owned summary/count projections. A foreign unreadable row cannot hydrate, settle or disclose schema state.
7. Hold Weaver at 1.0: representable legacy writes work, new behavior refuses before effect. Finalize 1.1, read old rows and keep the old counter readable without restamping. Place a barrier between stamp selection and save: only the safe metadata attempt retries, with fresh entities/stamp; domain invocation count stays one. Test paused/pending-only session writes and direct consumer contexts through the shared interceptor.
8. Inventory available, disabled, failed, partially registered, removed and unknown modules in normal Studio and a selected backend. Deselect structured logging, toggle ExtensionBuilder, remove a nested contributed source and reload the independent package. Facts retain side/host/generation; unknown route is route-only. Search projection/provider output for canary credentials, URLs, paths and failure text.

Use barriers and deterministic faults rather than sleeps for race assertions. For behavior changes, retain a failing pre-fix or mutation run and the corrected pass. Invalid cases must fail for the intended cause, not a startup/locator failure.

## Real composed and independent package journeys

Run the complete normal composed Studio/backend browser gate and the complete existing qualifier, extended with leaf 0 cases on both actual adapters. A standalone diagnostic or passing mock is insufficient. Keep normal workflow validation, atomic working-state batches, whole-batch undo, explicit Save/Publish and read-only/source-owned behavior intact.

From committed final source with a new absolute output directory outside the checkout and a unique local version:

```sh
nice -n 10 python3 tools/studio/Weaver.Conformance.Probe/qualify.py \
  --output /absolute/new-owned-weaver3443-proof \
  --version 4.0.0-weaver3443.local1
```

Use [the existing witness guide](../../tools/studio/Weaver.Conformance.Probe/README.md) and current `evidence.witness_manifest()` as authority for the original 38, current-authority, autonomy, workflow and restart inventories. Preserve them all; do not freeze an obsolete total count or substitute `qualify_autonomy.py`. Retain immutable product/SDK/module archive identities, consumer source/frontend invocation controls, resource readback, host exit/cache results and source SHA. No Weaver core/host/provider domain additions may be needed to install the external resource.

This fixture is deterministic provider-protocol evidence through actual adapters. Label component, protocol and live-provider evidence separately. No live-provider success is claimed unless an authorized real provider run establishes it; provider purchase/credential extraction remains outside scope.

## Leaves 1–3 interval proof

Use the [plan's PD3 table](plan.md#pd3-non-atomic-intervals) and [owner contract](contracts/owner-operations.md). Prove atomic own-resource expected revisions, connection delete/recreate at reused version, frozen cleanup-set identity and new PKCE/grant after review refusing before effects, tested exact secret version/resolved connection identity, post-test metadata conflict, rebind cleanup warning and native #3779 best-effort behavior versus strict Weaver preflight.

For secret revoke/delete, compare the bounded observed consumers again at execute, then create a binding behind a barrier after that comparison. The review warning and receipt must remain observation-attributed; neither the secret CAS nor a truncated advisory answer proves unused-at-deletion. Fault after actual value removal and before metadata save must yield uncertainty/no replay.

## Delivery gate

Fresh correctness and standards reviewers independently inspect the exact head until no P1/P2 findings survive. Preserve draft CI. Record PublicAPI/binary/behavioral compatibility and the approved pre-GA semantic magnitudes; no absent mechanical magnitude gate is called passed. Merge only with required checks green on that reviewed head and no hold. Verify resulting/descendant main, source-bind its evidence, then reconcile the owning issue and Project #65. A plan, prototype, isolated test or issue closure is not Program completion.
