# Qualification guide: run history

This is the intended executable qualification route; commands do not themselves claim a pass. Keep actual commands/results/source SHAs and failed/not-run states in tasks/evidence and the existing PR.

## Prerequisites and ownership

Use this feature's branch, the repository .NET 10 SDK and selected Node/pnpm versions. Read uptime/core count before interpreting timing-shaped failures; never bypass the dotnet build-slot wrapper. Preserve existing listeners, hosts and stores. Use owned temporary composed hosts/stores and frozen store copies for restart/upgrade controls.

Install Studio dependencies with pnpm --dir Studio install --frozen-lockfile when absent. Use repository migration tooling and locked restore. No external model calls, new production credentials or deployment are part of this proof.

## Runtime checks

Build/test the touched Runtime/Activities Runtime/API/Runtime EF projects, then affected whole suites. New tests must prove candidate order through coalescing, no-history fingerprint compatibility, capability refusal, initial/redelivery/stale bookmark distinctions, selected/sibling retirement, expiry without fake events and active/wait duration semantics.

Use existing EF checkpoint participant failure/marker interceptors to refuse history staging, lose acknowledgement after commit, race head creation/CAS, lose execution fence and replay from fresh contexts. Check runtime state, head/events/bookmark projections and replay marker together; rollback must expose none, replay must add no second semantic fact.

Run all persistence modes, including never-persisted Ephemeral completion recording off. Compare physical checkpoint counts/bounded query work with the known additional history participant work; do not infer cadence from displayed timestamps.

## Providers and upgrade

Generate additive migrations with bash tools/ef/generate-module-migrations.sh --runtime-incremental RunHistory; do not replace Runtime Initial. Qualify fresh and frozen pre-change upgrade on SQLite and supported PostgreSQL/SQL Server/MySQL provider fixtures, including Validate/schema-family metadata. Use owned provider databases only.

Run existing deletion and retention tests with history ownership/projections included. Qualify explicit purge/coverage boundaries and policy-safe phase/value references.

## API and long history

Exercise real capability discovery, runtime:read/structure authorization, tenant/environment/partition binding, exact links, tampered/mismatched cursors, independently denied payloads and no secret/capability leakage. Test unknown kinds, disabled/late/purged history, terminal state versus range settlement and failing/stale reads.

With 10,000 retained events and pages at most 100, seek a later bookmark through indexed identity, page within a stable watermark while appending, reconnect/catch up and verify no misses/duplicates. Bounded free-text windows report searched-through coverage instead of a full filtered count/false absence. These are correctness probes, not timing benchmarks.

## Studio and composed journey

Run the complete affected Workflows package tests, typecheck and build; run selected layout/selection/browser regressions and public-token CSS/layout lint. Extend the actual normal composed acceptance inventory, not only isolated mocked responses.

From a clean committed candidate, run python3 tools/ux-acceptance/run.py --output /tmp/studio-history-<unique-run> with a new output directory and the feature's added journey. Use normal Workbench/Studio composition, development seed identity and real user-task completion through normal UI/API. Suspend, stop the owned process, reopen its owned durable store in a new process and inspect the same event/handle identities and order.

Capture Timeline/Diagram, activity Journal/Bookmarks/Data, event and handle causation, waiting/completed/faulted/duplicate/expired/partial/loading/empty/denied/stale states, keyboard interaction and 320px/desktop in both themes. Never bless reference PNGs or old screenshots as current acceptance.

## Review and completion

Run required architecture/maps/API/catalog and selected CI gates. Give independent reviewers the exact candidate, scope/contract, source-bound running owned host and downloadable screenshots/traces/receipts; they must inspect the images. Keep failed/not-run states visible and fix/retest exact head. At most the governing review cap, then follow escalation rules if required.

Only after implementation/qualification and green reviewed exact-head checks may #3850 become ready and merge under the repository protocol. Verify resulting main and post-merge CI before closing #3849 and marking the delivery goal complete. No release/deployment is included.
