# Decisions Studio acceptance

User goal: starting in Decisions, create a reusable approval policy, enter table rules, save and validate it, save boundary cases, catch a deliberate threshold regression, publish, reload its immutable version and reuse it from a workflow. Import/export preserves the original decision document.

Reference: Connections and Workflow Definitions collection structure, using [public module UI](../../../../Studio/docs/module-ui.md). The editor uses native labeled controls and a table with persistent row/column headers. Editing a cell patches its XML node; the module never regenerates an imported document from a reduced model.

Observable states: empty/populated/no search results; loading/failed/unavailable/denied; create cancellation; editable draft and read-only version; missing source access; invalid and unsupported imports; located validation errors; clean/dirty/saving/conflicted draft; saved and failed cases; exact candidate test result after later edits; protected history with unavailable details; publish/reload/clone; keyboard focus and dirty navigation; 320px layout and light/dark.

Coverage: mounted tests for XML preservation, exact decimal values, stale identity requests, conflicts, actions and output disclosure. Actual composed Studio must create/save/reload/test/regress/repair/publish/reload/clone/import/export with real identity and storage. Inspect screenshots at desktop and 320px in light/dark and retain failed states. Independent review of the actual captures remains required; component tests and builds do not establish acceptance.

Status: implementation in progress; no composed journey or visual acceptance has passed yet. Whole-workflow/native/BPMN execution qualification is owned by the integration lane; this module contributes selection authoring.
