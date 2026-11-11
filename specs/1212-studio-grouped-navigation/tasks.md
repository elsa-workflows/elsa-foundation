# Tasks

Plan reviewed against spec1212 and Studio acceptance. Execute in dependency order.

- [x] T001 Record issue/claim, approved goal/reference/states and spec/plan review; add a failing navigation regression.
- [x] T002 Implement explicit placements, named disclosures, active/search/rail/top behavior and local route views.
- [x] T003 Move all existing first-party destinations, add Library/configuration links and retain deep links/access boundaries.
- [x] T004 Apply shared clean theme spacing; update module guidance, obsolete tests and browser/acceptance journeys.
- [ ] T005 Run whole affected suites, builds/type/lint and red/green proof; commit candidate and run owned composed-host proof, inspect screenshots and architecture/maps checks.
- [ ] T006 Open draft PR with downloadable exact-head evidence; independent correctness/standards/UX review and fix/retest loop, then normal final review handoff. Set Implemented only when the implementation is ready to land with its required evidence.


T005/T006 stay open until the final committed candidate passes the affected suites, builds, type and lint checks, and the hosted composed UX proof, and until an independent reviewer has opened its screenshots and traces. The spec's Implemented status records the implementation; it does not claim that evidence.

The hosted composed suite (`tools/ux-acceptance/run.py`) requires 93 cases: the ordinary journeys in six projects (1440, 360 and 390 widths, light and dark) produce 84 cases / 996 PNG+ARIA pairs / 84 traces, a count that grows as main adds journeys. `Studio/tests/acceptance/supplements.spec.ts` adds 9 cases / 70 pairs / 9 traces for the remaining acceptance gaps: coarse pointer (6 cases at 360, 390 and 1440 in both themes, 54 pairs), a restricted reader, activity availability loading/abort/client-fulfilled HTTP 500 with keyboard Retry, and a runtime-only composition (1 case each). These replace the external Linux adapter that was planned earlier. The design-only composition is deferred to #4160 by owner decision (2026-11-11): the Workbench does not boot without the runtime API today (#4160, #4158).

Review rounds, run and artifact links, failed or not-run outcomes, and the [technical merge hold](https://github.com/elsa-workflows/elsa-foundation/pull/3866#issuecomment-6665388078) are recorded on PR [#3866](https://github.com/elsa-workflows/elsa-foundation/pull/3866), not here.
