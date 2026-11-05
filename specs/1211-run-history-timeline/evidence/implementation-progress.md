# Implementation and qualification progress

Status: **In progress**. This is a work log, not product acceptance. Final qualification must identify a clean committed candidate, downloadable composed artifacts and independent review. The selected design PNG remains reference only.

## Shared foundation

T004–T008 implemented: compatible envelope/policy/batch/query contracts, null-omitted new admission policy, logical source and complete candidate replay identities, cloning/folding/fingerprint compatibility, selected writer/reader qualification, semantic capture before coalescing and atomic in-memory head/event/bookmark staging. Legacy absent-policy queued/run work stays uncaptured; required runs refuse missing participants even after capture removal or a wrong-scope writer. History alone does not defeat the never-persisted Ephemeral completion exception.

Independent source review found and prompted corrections for event-only bookmark replay fingerprints, exact-clock value matching and unknown ancestry being mistaken for a root. Additional source guards prevent later enrichment from adding uncaptured evidence and prevent required-policy bypass through a different scope. The regressions exercise these failure paths; a final mutation/revert control remains part of T046–T047.

Root verification on 2026-11-05, normal analyzers/public API checks enabled:

```text
dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore \
  --filter 'FullyQualifiedName~RuntimeHistory|FullyQualifiedName~RuntimeWorkflowExecutionStartDispatch|FullyQualifiedName~RuntimeCheckpointCommit|FullyQualifiedName~RuntimeCheckpointCoalescing|FullyQualifiedName~WorkflowExecutionDeletion' \
  --logger 'console;verbosity=minimal'
Passed: 344; failed: 0; skipped: 0.
```

This result covers the final source-proof and required-policy scope guards. The external local log is `history-foundation-root-verified.log` in this chat's owned visualization directory; it is not downloadable CI acceptance evidence. Existing RS0026 optional-overload warnings remain visible; no analyzer errors were suppressed for this run.

## API, durable providers and Studio

T009 API request/view/endpoint/capability source work is implemented. The complete Runtime API suite passed 309 tests (zero failed/skipped) after the shared exact UTF-16 query/access binding helper and subject/profile/tenant controls were added. A further 35 focused Runtime history tests passed with that helper. API behavior coverage exercises unknown versions, safe metadata, missing/denied equivalence before provider reads, advancing empty windows, explicit uncaptured/unsupported/invalid states and native bookmark expiry/absence without lifecycle rewriting. Source-owner continuation and complete composed runtime journeys remain outstanding; At that first checkpoint T010 remained open because fake-provider API boundary tests alone were insufficient; its later native-source receipt is recorded below. Runtime EF implementation and Studio Timeline/journal/bookmark implementation are in progress in their respective existing modules. No provider, fresh-process, composed-host, independent UX or merge acceptance is claimed yet.

The API test project's locked restore passed. An initial no-restore invocation correctly refused missing assets; a later build encountered a temporary shared-source edit mismatch and was superseded by frozen-source verification. Neither is classified as a product pass.

Before UI edits, the complete Workflows package baseline passed 3,341 tests in 241 files using the frozen pnpm lockfile. This is baseline evidence only and must be repeated after implementation.

## Outstanding acceptance

All unfinished tasks remain unchecked in [tasks.md](../tasks.md), including actual source-owner semantic coverage, durable provider/restart/upgrade/race qualification, bounded history/access controls, affected whole suites, clean current-head composed captures at desktop/320px in both themes, downloaded evidence with independent inspection, exact-head review/CI, merge and resulting-main verification.

## Subsequent integration work, not final acceptance

The first native-source API journey now exercises the real capture enricher and atomic in-memory checkpoint writer through authorized HTTP reads: start, wait, shared run/activity/exact event identities, bookmark creation/eligibility and subsequent activity fault. The complete normal Runtime API suite passed 311 tests, zero failed/skipped, including this journey and explicit disabled-at-admission provenance. This completed T010's source-bound API slice. Current capture/source settlement fields and additional pending-source controls were added afterwards and await the next coherent runtime build; the earlier 311 result is not represented as qualification of those changes.

Normal EF history/timer qualification passed 17 focused tests, then 25 in an owned detached Release snapshot with strict DI order/custom-factory controls, head allocation/replay/acknowledgement controls and an additive SQLite pre-history native run/timer/marker upgrade witness. Canonical ignored test evidence was restored using the repository's immutable hash-verified hydrator after an initial missing-input copy error. Compiler/test-only editing errors were corrected before these passing runs; no analyzer disabling or skip was used. Broader EF/provider qualification remains in progress.

The canonical migration generator produced additive RunHistory migrations, designers and snapshots for all four providers: five internal history tables, three defaulted timer projections and bounded indexes. The existing Initial chain remains. Generation used an owned frozen detached source snapshot; before copying back only the 12 generated files, the worker verified current EF model/declaration inputs still matched the snapshot. The local manifest/receipt is supporting provenance; final committed-source migration/provider controls and downloadable CI evidence remain required.

Studio's complete Workflows suite passed 3,373 tests across 245 files at an intermediate snapshot. Subsequent focused checks cover mobile Diagram transition, live unloaded ranges, exact value/causal navigation and current coverage. An intervening test-harness edit produced a failed suite; it was restored and the current focused delta passed 65/65 with zero failed suites. A final whole-package receipt is running against the frozen Studio source. Earlier serial browser reports recorded 4/4 and 5/5 reconnect/selection and layout/BPMN passes. A subsequent attempt reused an existing development server and is invalid evidence; ownership/source provenance of the earlier reports is being reassessed and strict fresh-private-port reruns will supersede them. None establishes actual composed history acceptance.

The owned composed runner now has a native User Task→Fault inspection/restart journey, targeted 320px projects, exact artifact inventory and private supervised restart protocol. Runner/unit and TypeScript checks pass; actual hosts/journeys and visual review have not run. Normal Workbench/base/docker composition now opts into the production history feature; existing personal/shared hosts and stores have not been restarted or migrated by this task.

## Published-head CI boundary correction

CI run 41944079949 at de7fae568d8b7e2d9d7c36fe2e6b77484dfa43e4 failed the existing continuation/history invariant (Core-only and PR shard 3), service composition qualification and the endpoint manifest comparison. The first two are substantive implementation errors, not waived tests. HistoryBatches is being moved from RuntimeCheckpointStateChangeSet to a separately coalesced RuntimeCheckpointCommit write sidecar; the invariant remains unchanged. Policy/writer/reader qualification moves from service construction to actual admission. The manifest must be generated from the composed host and reviewed for the four new permission-owned history routes. Corrective normal reruns are pending.

Subsequent local supporting qualification: Studio reports 3,383/3,383 Workflows tests, typecheck/build/budgets and nine strict owned-port browser cases, with source identity receipts. These supersede the invalid reused-server attempt; native composed history acceptance remains unrun. EF reports 30 focused Release tests, all four migration controls and three real PostgreSQL/SQL Server/MySQL provider cases without skips in an owned detached snapshot. Broader EF qualification covered 1,911 cases, including a separately corrected and rerun immutable pre-history SQLite upgrade witness. Migration generation was repeated to eliminate retained timer-index renames; final migration controls preserve the old chain and retained schema. Sidecar-reference changes still require affected reruns. These local receipts are supporting evidence, not final clean-head downloadable acceptance.

## Corrective local rerun

Normal root-owned corrective qualification passed 212 selected Runtime tests, including the unchanged continuation/history invariant, separate coalescing-sidecar boundary/quiescence controls and lazy optional/disabled resolution controls. Normal API qualification passed 314/314 tests, zero failed/skipped, including real native capture→HTTP wait/journal/bookmark/fault, honest promised-source pending/unsupported coverage and safe infrastructure503. Its new unsupported-source fixture initially left the default capable timer provider registered; the negative fixture now explicitly selects a valid legacy provider with no history capability. Those failed fixture attempts are recorded here as failures, not product passes.

Both previously failing Architecture checks now pass: every feature resolves its registered services and the representative host's endpoint manifest remains stable and permission-owned. The manifest was captured from that actual composed host using temporary diagnostic output, then the diagnostic source was restored exactly. Review found exactly four new Runtime-owned GET history routes, all with Runtime read permission; no existing route changed or disappeared. An initial normal Architecture build found an uncompiled factory-return initializer in the new Activities continuation handoff. It was replaced by a snapshotting clone method; subsequent normal Architecture build/test passed. No assertion, permission gate or analyzer was weakened.

Local logs: /tmp/1211-corrective-runtime.log, /tmp/1211-corrective-api.log and /tmp/1211-corrective-architecture.log. This is supporting uncommitted-source qualification, not final composed or merged-head acceptance. Required remaining owner coverage, no-record timer-source discharge, native UX and exact-head independent review/CI remain open.
