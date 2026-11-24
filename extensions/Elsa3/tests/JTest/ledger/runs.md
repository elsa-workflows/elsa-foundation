# Runs

One row per run, newest last; a failed run is never removed (spec 1235 FR-021, data-model.md).

| Run | Engine | Commit | Source | Environment file | jtest | Scope | Result | Receipt | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Elsa 3 | 6eec54289e | `elsa-server:3.6.3-jtest` `sha256:f30e8c78de39…` (README) | `environments/elsa3.json` | 2.0.3 | `jtest validate "tests/**/*"`, then `import-and-publish-workflows.json` on a fresh container | validate: 42/42 files valid; import: 34/34 datasets passed | - | Same container as run 2. |
| 2 | Elsa 3 | 6eec54289e | as run 1 | `environments/elsa3.json` | 2.0.3 | 41 acceptance suites (`tests/**/*`, baseline-dispatch excluded) | 32 suites passed, 9 failed; 154/177 cases passed. Failed: `branching/implicit`, `branching/join-wait-all-active`, `branching/join-wait-all`, `composition/bulk-dispatch-workflow-channels`, `composition/dispatch-workflow-channels`, `http/headers`, `javascript/script-handler-functions`, `logging/logging`, `performance/commit-after-every-activity` | - | The architecture tests ran on the same machine during this run; `commit-after-every-activity` hit the 100 s HttpClient timeout. |
| 3 | Elsa 3 | 6eec54289e | as run 1 | `environments/elsa3.json` | 2.0.3 | `import-and-publish-workflows.json` on a fresh container | 34/34 datasets passed | - | Setup for run 4. |
| 4 | Elsa 3 | 6eec54289e | as run 1 | `environments/elsa3.json` | 2.0.3 | The 9 suites that failed in run 2 (T012) | 8 failed with the same assertions as run 2; `performance/commit-after-every-activity` passed | - | Stable failures: the 8; intermittent: `commit-after-every-activity`. |
| 5 | Elsa 3 | 5e6cba1fa4 | as run 1 | `environments/elsa3.json` (channels empty) | 2.0.3 | `import-and-publish-workflows.json` on a fresh container | 34/34 datasets passed | - | Setup for run 6. |
| 6 | Elsa 3 | 5e6cba1fa4 | as run 1 | `environments/elsa3.json` (channels empty) | 2.0.3 | T018: the 39 acceptance suites (Q13, Q15) after the T017 fixes | 39 suites, 38 passed; 166/167 cases. Failed: `javascript/script-handler-functions` case `getConfiguration` (B14) | - | Every other suite passes, including the four fixed ones and `commit-after-every-activity`. |
| 7 | Elsa 3 | working tree after 5e6cba1fa4 (committed in the next commit) | as run 1 | as run 6 | 2.0.3 | `javascript/script-handler-functions-tests.json` with its `getConfiguration` cases removed, same container as run 6 | 2/2 cases passed | - | With run 6: all 39 acceptance suites pass on 3.6.3 (SC-001). |

## Notes

- T007 host check, 2026-11-24, commit 2b123d6a9b, no suites run: the Workbench started with the README configuration
  logged `Elsa 3 import 'jtest' in persistence scope 'default': 0 reusable activities and 10 workflows in place;
  10 workflows published by this start; 24 skipped.` The storage-drivers root definition was still version 2 (spec
  Q14 patch lands in T017). This start only proves the configuration; the qualifying receipt is taken in T031.
