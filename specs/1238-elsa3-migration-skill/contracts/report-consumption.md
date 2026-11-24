# Contract: what the skill reads from `report.json`

Spec 1237 owns the schema. This list is what the skill depends on; it does not define field names. A missing item is
raised on spec 1237's pull request, not worked around here.

| Need | Used for |
|---|---|
| Schema version | Stop on an unknown version (R-4). |
| Tool version and command (`inventory` or `assess`) and exit status | Record in the plan; know whether packages were loaded. |
| Redaction flag | Confirm what reached the AI provider (FR-005). |
| Summary counts | Report progress per run. |
| Custom-component inventory: kind (activity type, JavaScript function, variable type, storage driver), Elsa 3 name, usage count, the definitions using it | Fill the plan's components (FR-008). |
| Per definition: identity, name, version, bucket (`convert-as-is`, `convert-with-adjustments`, `incompatible`), blocked by dependency and its root | Order the work; fix roots first (FR-012). |
| Per finding: severity, code, message, JSON path, guidance, report category (Must fix, Reconsider, Take into account) | Work the report without hard-coded knowledge (FR-013). |
| Settings and closure the run used (features loaded) | Check every planned feature is loaded (FR-011). |
| Dispatched-workflow targets missing from the export | Tell the user (Take into account). |
| Output paths relative to the mounted output folder (`report.html`, `converted/`) | Point the user at them on the host; the handoff names `converted/`. |
| JavaScript globals used other than as a direct call, per definition and JSON path | Tell the user to make them direct calls before the next run (case 14); without this, the skill would have to read script bodies. |
