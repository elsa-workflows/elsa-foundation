# Studio UX acceptance

A visible feature is complete when its user journey and rendered experience have been inspected on the actual composed Studio host. Unit tests, isolated browser fixtures and a green build are supporting evidence. They do not establish product acceptance.

## Before implementation

Put a short acceptance record in the issue or spec:

- **User goal:** what a person is trying to accomplish, including the starting point and successful outcome.
- **Reference:** an existing good screen or shared component. Use Workflow Definitions or Connections for collection structure; link [module UI guidance](../../Studio/docs/module-ui.md) for the shared controls. Prefer those components over new local styling.
- **Observable states:** the normal path and the states the change introduces or can break. Consider empty/populated, no matches, validation/recovery, denied/read-only, loading and narrow layouts. Name exclusions with a reason; do not silently omit them.
- **Coverage:** the smallest journeys that can disprove those expectations. New create/edit flows earn persistence, cancellation/focus and keyboard checks. A small icon correction earns rendered composition and an accessible name check; it does not require every permutation of the entire app.

Acceptance criteria describe outcomes such as “Create returns to the updated list with focus restored,” rather than “the component renders.”

## Composed proof

From the repository root, after installing Studio dependencies and Playwright Chromium:

```bash
pnpm --dir Studio install --frozen-lockfile
pnpm --dir Studio exec playwright install chromium
python3 tools/ux-acceptance/run.py --output /tmp/studio-ux-<unique-run>
```

The output directory must be new. The runner builds Workbench and Studio from this checkout, copies their outputs into owned temporary content roots, allocates local ports, signs in using the committed development seed and calls real APIs through the UI. It changes local origin configuration, writes the reduced feature list of the runtime-only subset composition, and seeds and narrows one reader account in its owned disposable identity store; it uses only disposable stores and terminates only its child process groups. It never attaches to an existing listener. Do not point this proof at a personal or shared data store. External models are not called.

The ordinary journeys run in six projects (1440, 360 and 390 widths, each in light and dark). The suite covers grouped navigation and its moved destinations (groups, Library, Workflows views, configuration settings, Diagnostics, search, top navigation and high contrast), workflow template metadata defaults and persistence, Business decisions authoring, feature filtering, settings and impact dialogs, collection/connection creation, Agent authoring, saving an unsaved workflow as a template with toast confirmation and persisted list navigation, User Tasks, executable designer parity against the definition editor (Sequence, nested and empty scopes, compensation, Flowchart and BPMN), and Choose data for whole activity outputs (Sequence and Flowchart choice, reopen, publish and a run that checks the consumer's committed input; a loop producer's reason; keyboard choice, Replace and Clear). User Tasks seeds a real assigned task through publication/runtime APIs, preserves a response across filtering, proves one accepted completion, and captures its forms list/editor and lower actions/history. That assigned-task journey does not qualify loading, failure or denied-access states, group claim/release, or reassignment; those require separately identified evidence. Supplementary journeys in `supplements.spec.ts` each run once in their own project: grouped navigation under real touch emulation (coarse pointer, taps, 44px targets) at 360, 390 and 1440 in both themes; a real reader account holding only `api-capabilities:read`, `workflows/execution-evidence:read` and `workflows/runtime:read` (seeded on a first boot of the owned store by the real seeder, then narrowed before the acceptance boot) with denied Features bookmarks, read-only runtime settings and no writes; activity availability through held, aborted and client-fulfilled HTTP 500 loads with keyboard Retry to real 200s; and a runtime-only Workbench composition with its own owned hosts, plus the return to the combined host. A design-only composition waits on [#4160](https://github.com/elsa-workflows/elsa-foundation/issues/4160): without the runtime API the Workbench does not boot today. Extend it when a new interaction needs composed coverage; it is not evidence for an unrelated screen merely because it passed. Add a feature-specific journey for uncovered acceptance criteria. Relevant states may also use existing `tests/live` journeys against an owned host; fixture responses must be explicitly identified as isolated component evidence.

The separate User Tasks authoring/result journey creates and publishes a form and workflow through Studio, checks output autosave and draft name recovery, pins the selected form version, completes a task after a newer form version is published, and inspects the captured result and deliberate configuration faults. It adds one journey in each of the four viewport/theme projects. The total execution and artifact counts follow the complete current journey inventory. Graph wiring uses the desktop authoring viewport; narrow evidence covers forms, selectors, task response, results, incidents and read-only runtime canvas controls. It does not qualify narrow graph wiring.

`report.json` identifies the source commit, PR head, dirty state and failing stage. CI tests the merge commit and records the PR head separately. The runner refuses a dirty checkout before starting hosts; final review requires the committed revision. `playwright.json`, the HTML report, screenshots, accessibility snapshots and traces describe the actual executions. Every intended project/journey must execute successfully once and retain required artifacts; omissions, skips, retries and expected failures cannot produce acceptance success. Failures retain artifacts and stop the gate.

On relevant PRs, CI uploads `studio-ux-<tested SHA>-<attempt>` even after a failed journey. Download the artifact from the exact Actions run, open `html/index.html` or the screenshots and use the trace for interaction failures. Link the Actions run and artifact in the PR evidence so another reviewer can retrieve them. Local absolute paths alone are not shared evidence. Artifacts expire after 14 days; if they are unavailable at review time, rerun the proof.

The existing required **Build & test** aggregates selected Studio frontend, isolated browser, .NET and composed UX checks. A failed or skipped selected check fails that aggregate before the documentation-only fast path. Unrelated PRs retain the existing fast path. This uses the existing required context and read-only CI token, with no administrator ruleset or credential change.

## Independent UX acceptance review

The existing acceptance/spec reviewer also owns this lens for a UI change. Give that reviewer the user goal, reference screen, acceptance record, exact head, running owned app where available, and downloadable source-run evidence. Have them inspect the complete affected experience, not only changed lines. They must open the screenshots; DOM assertions and an author's screenshots summary are insufficient.

Inspect hierarchy, spacing, alignment, borders, labels, redundant information, primary/secondary actions, focus, keyboard operation and recovery. Compare empty and populated states with the reference. Check that create/edit/list transitions are coherent and that saved values survive reopening. Inspect narrow and theme captures for changed surfaces. Test additional risky interactions in the running app where the recorded proof does not answer the acceptance criteria. Log a specific state and visible/user-facing consequence for each finding.

The review receipt, in the existing PR evidence/review comment, contains:

```text
UX acceptance: passed | failed | incomplete
Reviewed PR head: <full SHA>
Tested source/merge SHA: <full SHA>
Goal and reference: <links>
Observed journeys/states: <what was actually inspected>
Actions run/artifact: <downloadable links, plus named screenshots/traces>
Independent reviewer: <reviewer/lens; distinguish an author's subagent from final app review>
Findings and retest: <open findings, fixes, exact run after fixes>
Not verified: <states and reasons, or none>
```

A machine report's `passed` means its assertions passed. It never means independent visual approval. An author-written receipt is not an independent review. Missing, stale, inaccessible or uninspected evidence makes UX acceptance **incomplete**. Unresolved usability defects make it **failed**. Keep that status visible in the PR and completion response; do not report the feature accepted. An independent agent review can satisfy the author's review loop; the org's separate final review/merge rules still apply. Do not self-merge an incomplete UX acceptance under the standing exception.

After a fix, rerun the affected whole suites and composed journey, refresh the screenshots and review the new exact head. Do not bless a new screenshot baseline to turn a failure green. After the existing review cap, escalate the unresolved finding as the normal protocol requires, preserving failed/incomplete acceptance rather than converting it to a pass.

## Learn from escaped defects

The periodic [UX QA routine](../skills/catalog.md#ux-qa-routine) remains an exploratory complement. For a reproduced escape, add its failing journey/regression to the issue and the normal fix/retest path. When multiple escapes share a cause, fix the shared UI primitive and add a representative regression there, then inspect affected screens. Record the cause and prevention in the existing issue/PR; do not create a separate receipt issue or a new governance layer.
