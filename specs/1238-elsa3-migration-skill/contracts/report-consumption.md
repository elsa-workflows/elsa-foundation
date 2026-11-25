# Contract: what the skill reads from `report.json`

Spec 1237 owns the schema (`specs/1237-elsa3-migration-cli/contracts/report.schema.json`) and the command line
(`contracts/cli.md` there). This list is what the skill depends on; field names are 1237's. The skill refuses an
unknown schema version and ignores unknown properties. A missing item is raised on spec 1237's pull request, not
worked around here.

| Need | Used for |
|---|---|
| Schema version | Stop on an unknown version (R-4). |
| Tool name and version, command (`inventory` or `assess`), exit code (0: no Must fix or Reconsider finding, Take into account findings may remain; 1: findings) and the redacted marker | Record the command and exit code in the plan; know whether the local features were composed; confirm what reached the AI provider (FR-005). |
| Summary counts | Report progress per run. |
| Custom-component inventory: kind (activity type, JavaScript function, variable type, storage driver), Elsa 3 name, usage count, the definitions using it | Fill the plan's components (FR-008). |
| Per definition: identity, name, version, bucket (`convert-as-is`, `convert-with-adjustments`, `incompatible`), blocked by dependency and its root | Order the work; fix roots first (FR-012). |
| Per finding: severity, code, message, JSON path, guidance, report category (Must fix, Reconsider, Take into account), remedy (Must fix only: patch the definition, build a component, none), whether it is routine, and, for a component, its kind and name | Work the report without hard-coded knowledge (FR-013); a remedy of building a component sends the skill to the plan's component row named by the finding, patching sends the user to the definition, none means the user decides (redesign, drop or accept the refusal) and no code is written; a Reconsider or Take into account finding carries no remedy and goes to the component row its kind and name point to when present, otherwise to the definition and JSON path; a component row is `verified` when no finding of any category names it, except a storage-driver row, which ends
`interviewed` because its finding repeats on every run; a definition whose only findings are routine goes smoothly. |
| The features the run composed | Check every planned feature is loaded (FR-011). |
| Dispatched-workflow targets missing from the export | Tell the user (Take into account). |
| Per definition, the exported file relative to the output folder, and for a reusable also its wrapper workflow's file | Point the user at them under the output folder the skill passed; the handoff names `migrated/`. |
| Findings that belong to no definition: input findings (such as an unreadable export file) and run findings (a refusal of the shared import that names no single definition) | Show them first; the run is not finished while one is neither fixed nor accepted (FR-012). |

A JavaScript global used other than as a direct call is listed at inventory as a JavaScript-function component,
like a called one. Once a host function provides it, `assess` reports the use as a Must fix finding with the remedy
patch the definition and no component kind, and the function leaves the inventory; the skill sends the user to the
definition and JSON path, where case 14 says to make it a direct call. The skill needs nothing special for it.

A refusal to run (exit code 2) writes no report: the skill reads the `dotnet run` exit code and the tool's one-line
reason on standard error (spec 1237's command-line contract) and stops (FR-022). A run wrote a report only when it
exits 0 or 1 and its last standard-output line is the output path; otherwise the build or the tool failed, and a
`report.json` left in the output folder is from an earlier run and is not read.

A storage-driver finding names the Elsa 3 driver, memory or custom, as a storage-driver component; the skill answers
it with the case 17 interview, not with a port, once per driver type (the component name), not once per finding.
It is a Reconsider finding (no remedy) for both drivers, which spec 1237 lists as components.
