# Research: Offline composition plan

The [merged command decision](../../docs/reports/runtime-composition/developer-plan-command-contract.md) is the source of truth for the user task, evidence limits, and secret boundary. This file records implementation decisions for #2001.

| Decision | Evidence and reason | Alternative deferred |
|---|---|---|
| Register `composition plan` in the existing CLI front end | `ElsaCli.Build()` owns the root command; `persistence plan` already names EF migration inspection. The planning assembly has no package dependencies. | A second tool duplicates distribution and error conventions; invoking the persistence worker would require a host for a file-only task. |
| Keep selection and dependency evidence in the shared planner | `SelectionPlanner.Plan` already owns exact selection and `HostAssessment` already chooses runtime descriptor versus manifest edges. The current result omits successful edges and reviewed explanations. Add rows there so CLI and builder share facts. | Reconstructing edges in the CLI would create a second decision engine. |
| Use explicit strict v1 inventory and resource-hint readers | Catalog/composition/workspace parsers exist; inventory and persistence are typed-only. A supplied resource file is not a trusted live check. | General host scraping or caller-asserted `checked` status would overstate readiness. |
| Project only safe fields to text/JSON | Opaque `settings` and `resources` can contain secrets. Planner reasons can contain arbitrary rationale from user-supplied definitions. Output action/source/fixed finding text and safe IDs, never raw rationale or raw exceptions. | Serializing the typed plan or authored document directly can echo secrets. |
| Treat valid unresolved plan as a successful inspection | The command answered the query and reported the unresolved facts. Stable exit 2 means invalid input, exit 3 means resolution/I/O failure. | Returning failure merely for unavailable host evidence would make offline planning unusable. |

No network research or new package is needed. The source baseline is the merged #1999 decision at main `f3fd511c96c03fdbf0bb29d8b115eab3d586a5a0`.
