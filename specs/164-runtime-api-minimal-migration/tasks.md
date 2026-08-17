# Runtime migration tasks

## Baseline

- [x] T001 Add the detached historical FastEndpoints capture runner.
- [x] T002 Capture and freeze HTTP/OpenAPI fixtures with provenance and hashes.
- [x] T003 Expand binding, error, not-found, and status cases from the actual FE host.

## Production

- [x] T004 Add Runtime-owned permission constants and contributor.
- [x] T005 Add owner-local source-generated JSON context.
- [x] T006 Map all 24 registrations with stable metadata and typed responses.
- [x] T007 Remove Runtime production FE registrations/reference while retaining oracle test references.
- [x] T008 Preserve route-over-body and content-type binding behavior.

## Verification and handoff

- [x] T009 Run the Runtime owner suite and composition/anonymous route tests.
- [x] T010 Add deep HTTP/OpenAPI comparer and explicit difference approvals.
- [x] T011 Complete one honest three-cycle collectible application-pipeline proof: every cycle maps routes, exercises middleware authorization/resource evaluation, succeeds through typed and body-bound routes, uses source-generated JSON, generates native OpenAPI, and disposes/unloads.
- [x] T012 Capture final post-W6 Runtime E2E evidence against rebuilt Workbench/fresh DB (Runtime GET 20/20 and write 10/10).
- [ ] T013 Refresh maps and run the final Architecture/build/format/diff gates (focused owner gates, maps, affected builds, scoped Runtime formatter, and diff checks pass; repository-wide formatter has unrelated baseline diagnostics and is not claimed green).
- [x] T014 Review the final migration recommendation and follow-up issues after the integration gates complete.
