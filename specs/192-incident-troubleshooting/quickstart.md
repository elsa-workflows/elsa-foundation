# Acceptance and verification

1. Build affected projects only through normal build-slot wrapper; run scoped backend/API/provider tests and Studio Vitest/typecheck/lint/build. Record actual commands/results; queued/untested is not passed.
2. Start rebuilt Workbench with a fresh isolated database and unused loopback port. Start rebuilt Studio with backend URL and explicit unused loopback port. Use normal shell composition, not substitute endpoints/stores. Never stop the user's existing hosts.
3. In real Studio, create an isolated draft with a WriteLine Text JavaScript input `qaMissingVariable`, no matching variable. Validate, run, open resulting instance.
4. Confirm accepted-with-incident feedback; lifecycle remains truthful; header/list show current intervention health; active/blocking list filters include it regardless of page; node shows accessible restrained cue; input says failed with useful root cause; node/incident actions navigate both directions after reopen.
5. Repeat healthy, resolved, nonblocking, pending retry, repeated occurrence, nested Flowchart/Sequence/BPMN, unassociated engine, permission/loading states. Label fixture-only assertions separately from actual browser proof.
6. Check split/maximized/collapsed pane layouts, keyboard and current light/dark themes. Capture before/after screenshots under `docs/reports/incident-troubleshooting/` and an acceptance report with boundaries.
7. Run architecture/maps and independent diff reviews; revert one causal association and one Studio navigation/health behavior and show targeted tests fail, then restore and pass.
8. Publish gate evidence on each PR; exact-head CI must pass before merge. Verify post-merge main CI/Maps and close hierarchy only when evidence meets spec. Keep unrelated main-red #2293 cause visible.
