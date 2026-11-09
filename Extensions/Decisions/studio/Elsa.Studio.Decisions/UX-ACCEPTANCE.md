# Decisions Studio acceptance

User goal: starting in Decisions, create a reusable approval policy, enter table rules, save and validate it, save boundary cases, catch a deliberate threshold regression, publish, reload its immutable version and reuse it from a workflow. Import/export preserves the original decision document.

Reference: Connections and Workflow Definitions collection structure, using [public module UI](../../../../Studio/docs/module-ui.md). The editor uses native labeled controls and a table with persistent row/column headers. Editing a cell patches its XML node; the module never regenerates an imported document from a reduced model.

Observable states: empty/populated/no search results; loading/failed/unavailable/denied; create cancellation; editable draft and read-only version; missing source access; invalid and unsupported imports; located validation errors; clean/dirty/saving/conflicted draft; saved and failed cases; exact candidate test result after later edits; protected history with unavailable details; publish/reload/clone; keyboard focus and dirty navigation; 320px layout and light/dark.

Coverage: mounted tests for XML preservation, exact decimal values, stale identity requests, conflicts, actions and output disclosure. Actual composed Studio must create/save/reload/test/regress/repair/publish/reload/clone/import/export with real identity and storage. Inspect screenshots at desktop and 320px in light/dark and retain failed states. Independent review of the actual captures remains required; component tests and builds do not establish acceptance.

Status: the clean composed journey at `2c8925a2ac808db26c6cb1b051e534b3736afbfd` passed all four configurations once, with 68 retained logical PNGs, accessibility snapshots and traces. The focus race, unsaved-case replacement, new-form dirty metadata and narrow rule-label findings were corrected and their regression tests passed. Independent retest, downloadable exact Actions evidence and final review are tracked on [PR #3689](https://github.com/elsa-workflows/elsa-foundation/pull/3689); machine success alone does not establish UX acceptance. Whole-workflow/native/BPMN execution qualification is owned by the integration lane; this module contributes selection authoring.

Rendered loading/denied/protected-source/invalid-import and located-validation recovery states are not covered by the composed capture set; mounted tests and API/authority suites supply bounded functional evidence for these states. No claim of independent visual verification is made for them.
