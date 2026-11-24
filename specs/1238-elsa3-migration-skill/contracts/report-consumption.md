# Contract: what the skill reads from `report.json`

Spec 1237 owns the schema (`specs/1237-elsa3-migration-cli/contracts/report.schema.json`) and the command line
(`contracts/cli.md` there). This list is what the skill depends on; field names are 1237's. The skill refuses an
unknown schema version and ignores unknown properties. A missing item is raised on spec 1237's pull request, not
worked around here.

| Need | Used for |
|---|---|
| Schema version | Stop on an unknown version (R-4). |
| Tool version, image version, command (`inventory` or `assess`), exit code (0: no Must fix or Reconsider finding, Take into account findings may remain; 1: findings) and the redacted marker | Record in the plan; know whether packages were loaded; confirm what reached the AI provider (FR-005). |
| Summary counts | Report progress per run. |
| Custom-component inventory: kind (activity type, JavaScript function, variable type, storage driver), Elsa 3 name, usage count, the definitions using it | Fill the plan's components (FR-008). |
| Per definition: identity, name, version, bucket (`convert-as-is`, `convert-with-adjustments`, `incompatible`), blocked by dependency and its root | Order the work; fix roots first (FR-012). |
| Per finding: severity, code, message, JSON path, guidance, report category (Must fix, Reconsider, Take into account), remedy (Must fix only: patch the definition, build a component, none), whether it is routine, and, for a component, its kind and name | Work the report without hard-coded knowledge (FR-013); a remedy of building a component sends the skill to the plan's component row named by the finding, patching sends the user to the definition, none means the user decides (redesign, drop or accept the refusal) and no code is written; a Reconsider or Take into account finding carries no remedy and goes to the component row its kind and name point to when present, otherwise to the definition and JSON path; a component row is `verified` when no finding of any category names it, except a storage-driver row, which ends
`interviewed` because its finding repeats on every run; a definition whose only findings are routine goes smoothly. |
| Settings and closure the run used (features loaded) | Check every planned feature is loaded (FR-011). |
| Dispatched-workflow targets missing from the export | Tell the user (Take into account). |
| Per definition, the exported file relative to the output folder, and for a reusable also its wrapper workflow's file | Point the user at them under the host folder the skill mounted; the handoff names `migrated/`. |
| Findings that belong to no definition: input findings (such as an unreadable export file) and run findings (a refusal of the shared import that names no single definition) | Show them first; the run is not finished while one is neither fixed nor accepted (FR-012). |

JavaScript globals used other than as a direct call are to reach the report (a converter change spec 1237 depends on) as ordinary converter findings that name the
global as a JavaScript-function component; the skill needs nothing special for them.

A refusal to run (exit code 2) writes no report: the skill reads the `docker run` exit code and the tool's one-line
reason on standard error (spec 1237's command-line contract) and stops (FR-022).

A storage-driver finding names the Elsa 3 driver, memory or custom, as a storage-driver component; the skill answers
it with the case 17 interview, not with a port, once per driver type (the component name), not once per finding.
Once the pending converter change lands it is a Reconsider finding (no remedy) for both drivers, which spec 1237
lists as components; until then it is the converter's refusal.
