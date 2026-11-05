# Tasks: Custom Studio Host Guide and Program Exit

**Input**: [spec.md](spec.md), [plan.md](plan.md) and accepted #3366 handoffs.
**Cadence**: risk-based actual consumer verification after the small sample; no documentation-text tests or new production API.

## Phase 1: Setup

- [x] T001 Verify healthy main/prerequisites/exclusive claim, reserve the monotonic spec number, run the mandatory branch hook once and review spec/checklist/plan in `specs/1016-studio-host-guide/`.

## Phase 2: US1 - Runnable Package Host

**Independent test**: exact sample restores/publishes outside the repository and serves the standard browser and selected module from actual candidates with frontend tools denied.

- [x] T002 [US1] Add the small public-API sample in `tools/studio/PackageHost.Sample/` with explicit candidate version/source inputs, Dashboard selection and application-owned configuration; no private infrastructure or repository project references.
- [x] T003 [US1] Write the complete acquisition/original-archive selection/preview-feed/restore/publish/run recipe in `Studio/docs/custom-hosting.md` and route the sample README to it.
- [x] T004 [US1] Execute the exact isolated sample against genuine candidate packages; verify locked package graph, DLL/assets/runtime/browser observations, hidden source/feed/cache and zero consumer frontend-tool invocations; record actual provenance/cleanup in `specs/1016-studio-host-guide/validation.md`.

## Phase 3: US2 - Supported Migration Choices

**Independent test**: each migration and optional-capability statement traces to the accepted public contract/source and qualified cases.

- [x] T005 [US2] Complete auth/backend/storage/module/custom-frontend/themes/Nuplane/native-retention/reload-diagnostic migration guidance in `Studio/docs/custom-hosting.md`; update stale package/Studio/tool README routing and the contract's implementation-status note, preserving accepted limits.

## Phase 4: US3 - Evidence and Record Reconciliation

**Independent test**: reports/ledger are discoverable and completed records are reconciled from receipts; remaining parents stay open until final delivery.

- [x] T006 [US3] Index the four reports in `docs/reports/README.md`, add M0–M3 delivery evidence to `docs/program-goals/studio-hosting-package-reuse.md`, reconcile completed spec task records and five stale completed issue records, and prepare only this program's registry completion update.

## Phase 5: Review and Delivery

- [x] T007 Check new links and sample inputs, run required architecture graph/whole guards/discovery and maps, and record risk-based verification in `specs/1016-studio-host-guide/validation.md` without unrelated test expansion.
- [x] T008 Prepare the three-lens review and delivery-gate handoff, set terminal status/remove checklist in this PR, and record the frozen sample/architecture/link evidence in `validation.md`. Final review and hosted execution are live PR gates below.
- [x] T009 Prepare the final M0–M3 ledger, current issue links and post-merge verification/closure instructions below. Actual main execution and final native closure are recorded on #3366/#3340, not inferred from this source checkbox.

## Dependencies and Execution Strategy

T001 -> T002/T003 -> T004 is the MVP recipe. T005 uses accepted handoffs; T006 uses actual completed delivery receipts. T007 precedes the review/delivery handoff in T008; T009 prepares the live post-merge record. The task is complete only when all stories and the live gates below pass.

Independent source/receipt/link reads may be batched. Edits, builds and owned servers run sequentially with the build-slot wrapper. There is no implementation delegation; only the required correctness/standards/acceptance review lenses run independently in parallel. Do not close parents or mark Verification Passed from planning, source-only tests or an unreviewed candidate.


## Live delivery gates

Source checkboxes track deliverables that can be committed in this PR. They do not assert future CI, merge or board actions. [Issue #3366](https://github.com/elsa-workflows/elsa-foundation/issues/3366) owns live execution state, and [Program #3340](https://github.com/elsa-workflows/elsa-foundation/issues/3340) owns the final receipt.

Keep the PR draft until the independent correctness/standards/acceptance loop converges on its final head (at most six rounds). Require actual exact-head Build & test, maps, Full tier and Studio checks, candidate/sample provenance and no human hold before authorized reviewed merge. Preserve failed controls, known quarantine, skips and superseded runs as observed.

After merge, verify actual current main CI/Studio/CodeQL, fresh candidates and the exact external sample. Only then reconcile and close #3366 -> #3350 -> #3343 -> #3340, read back all 30 native issue/body/board Status/Agent State/Verification records and release the exact claim. Record that resulting-main receipt on the linked issues; no follow-up source-only checkbox commit substitutes for it. No release, deployment or publisher activation.
