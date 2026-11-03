# Tasks: Isolated Studio Package Consumer Proof

Inputs: [spec](spec.md), [plan](plan.md), [research](research.md), [report model](data-model.md), [validation guide](quickstart.md). Actual artifact controls are the test discipline; no application TDD cadence or performance work is introduced.

## Phase1 — Setup and baseline

- [x] T001 Verify #3363 prerequisites/exclusive claim/main health, allocate spec1013 across current/archive/open-PR specs, run the required branch hook, and review the specification/design artifacts under `specs/1013-studio-package-consumer-proof/` before implementation.
- [x] T002 Run the existing Api-only positive against genuine current-main candidates in `tools/studio/ApiPackageClosure.Probe/qualify.py`; bind the already completed current-main Hosting positive by archive/tool hashes and record the baseline in `validation.md`.

## Phase2 — US1 declared package consumption

Independent test: normal Api-only consumer restores a locked graph, compiles/publishes and serves its real public API metadata from published libraries; Hosting proof supplies the standard UI/module journey.

- [x] T003 [US1] Add lock-file generation/locked restore, frontend-tool denial/audit and precise artifact/tool provenance to `tools/studio/ApiPackageClosure.Probe/qualify.py`, preserving its single Api reference, isolated cache, payload hashes and owned host cleanup.

## Phase3 — US2 causal omission controls

Independent test: normal/fault/restored outcomes are0/1/0 for both omission kinds, with the intended diagnostic and original/payload integrity verified.

- [x] T004 [US2] Add `--omit-api-core-dependency` to `tools/studio/ApiPackageClosure.Probe/qualify.py`: retain originals, refuse signed/already-missing edges, change only private nuspec metadata, and report whether the expected stage/graph/diagnostic was observed without turning a failed product run green.
- [x] T005 [US2] Run actual Api normal/omitted/restored profiles and invalid-control prerequisites; verify original/archive/library hashes, missing-Core graph/compile failure, locked restores and unrelated-failure rejection, retaining reports in `validation.md`.
- [x] T006 [US2] Run the production `tools/studio/HostingAssets.Probe/qualify.py` missing-vendor control and restored positive against the exact baseline archives; require the specific React404 failure and restored browser/resource/customization/frontend-denial success.

## Phase4 — Documentation and delivery

- [x] T007 Document command/exit/report behavior in `tools/studio/ApiPackageClosure.Probe/README.md` and link the existing browser proof; keep custom-module integration, CI wiring and the final recipe in #3364/#3365/#3366.
- [x] T008 Run Python syntax, complete generated consumer profiles, required architecture graph/whole architecture project and generated maps; compare the actual diff and retain exact evidence in `specs/1013-studio-package-consumer-proof/validation.md`.

## Phase5 — US1 runtime configuration correction

- [x] T009 [US1] Close the round1 runtime-config coverage gap in `tools/studio/HostingAssets.Probe/qualify.py` and `browser.mjs`: assert HTTP/MIME/config plus browser nondefault HostId, prove missing/empty and browser-only faults fail, and restore the normal production profiles.

## Phase6 — Review and resulting-main delivery

- [x] T010 Open/attach one draft PR for #3363, independently review correctness/standards/acceptance to convergence, set terminal spec status/remove process checklist in that implementation PR, and complete exact-head CI and the current organization-authorized review/merge path.
- [x] T011 Verify reviewed merge/resulting main and real candidate/control outcomes, then reconcile #3363/Program3340/Project63 and release the exact claim.

## Dependencies and execution

T001→T002→T003→T004→T005→T006→T007→T008→T009→T010→T011. US1 retains the smallest working positive before US2 changes one input; every restored profile is fresh. One implementation worker/leaf, no parallel implementation. Independent read-only reviewers operate only after the complete diff is frozen; documentation/artifact inspection can be independent of running owned consumers. No new test project or unrelated local solution/frontend rerun is required for a Python/JavaScript qualification-only change.

## Final record reconciliation

T010, T011 were delivered through [the reviewed delivery receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3391#issuecomment-6442490345) and merged commit `26f0893468320500d56fa26dc3fcd42db63a36ba`, verified as an ancestor of current main. #3366 reconciles these previously stale checkboxes from actual delivery/main/candidate evidence; it does not invent a new execution or formal approval. The current organization protocol governs review/merge authorization.
