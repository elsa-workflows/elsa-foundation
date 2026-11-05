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

T009 API request/view/endpoint/capability source work is implemented. The complete Runtime API suite passed 309 tests (zero failed/skipped) after the shared exact UTF-16 query/access binding helper and subject/profile/tenant controls were added. A further 35 focused Runtime history tests passed with that helper. API behavior coverage exercises unknown versions, safe metadata, missing/denied equivalence before provider reads, advancing empty windows, explicit uncaptured/unsupported/invalid states and native bookmark expiry/absence without lifecycle rewriting. Source-owner continuation and complete composed runtime journeys remain outstanding; T010 is not marked complete from fake-provider API boundary tests alone. Runtime EF implementation and Studio Timeline/journal/bookmark implementation are in progress in their respective existing modules. No provider, fresh-process, composed-host, independent UX or merge acceptance is claimed yet.

The API test project's locked restore passed. An initial no-restore invocation correctly refused missing assets; a later build encountered a temporary shared-source edit mismatch and was superseded by frozen-source verification. Neither is classified as a product pass.

Before UI edits, the complete Workflows package baseline passed 3,341 tests in 241 files using the frozen pnpm lockfile. This is baseline evidence only and must be repeated after implementation.

## Outstanding acceptance

All unfinished tasks remain unchecked in [tasks.md](../tasks.md), including actual source-owner semantic coverage, durable provider/restart/upgrade/race qualification, bounded history/access controls, affected whole suites, clean current-head composed captures at desktop/320px in both themes, downloaded evidence with independent inspection, exact-head review/CI, merge and resulting-main verification.
