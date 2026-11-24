# Contract: what the skill reads from `report.json`

Spec 1237 owns the schema (`specs/1237-elsa3-migration-cli/contracts/report.schema.json`) and the command line
(`contracts/cli.md` there). This list is what the skill depends on; field names are 1237's. The skill refuses an
unknown schema version and ignores unknown properties. A missing item is raised on spec 1237's pull request, not
worked around here.

| Need | Used for |
|---|---|
| Schema version | Stop on an unknown version (R-4). |
| Tool version and command (`inventory` or `assess`) and exit status (clean, findings, refused with a reason) | Record in the plan; know whether packages were loaded; stop on a refusal (FR-022). |
| Redaction flag | Confirm what reached the AI provider (FR-005). |
| Summary counts | Report progress per run. |
| Custom-component inventory: kind (activity type, JavaScript function, variable type, storage driver), Elsa 3 name, usage count, the definitions using it | Fill the plan's components (FR-008). |
| Per definition: identity, name, version, bucket (`convert-as-is`, `convert-with-adjustments`, `incompatible`), blocked by dependency and its root | Order the work; fix roots first (FR-012). |
| Per finding: severity, code, message, JSON path, guidance, report category (Must fix, Reconsider, Take into account) and remedy (patch the definition, build a component, none) | Work the report without hard-coded knowledge (FR-013); a remedy of building a component sends the skill to the plan's component, patching sends the user to the definition. |
| Settings and closure the run used (features loaded) | Check every planned feature is loaded (FR-011). |
| Dispatched-workflow targets missing from the export | Tell the user (Take into account). |
| Output paths relative to the mounted output folder (`report.html`, `migrated/`) | Point the user at them on the host; the handoff names `migrated/`. |

JavaScript globals used other than as a direct call reach the report as ordinary converter findings; the skill
needs nothing special for them.
