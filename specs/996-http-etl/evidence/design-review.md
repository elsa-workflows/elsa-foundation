# HTTP ETL design checkpoint

Date: 2026-10-21. Baseline `b555bd8460270476e49496e519c69b65180e9505`.
Issue [#3151](https://github.com/elsa-workflows/elsa-foundation/issues/3151); owner-delegated design authority.

## Review and disposition

Root reviewed the complete spec, plan, state model, protocol and tasks. Two bounded read-only Luna Extra High reviewers independently checked source compatibility and acceptance; root checked every finding against current source or primary documentation. Three design passes ended with no concrete unresolved blocker. This is approval to implement, not runtime qualification.

| Finding | Resolution |
|---|---|
| Fully filtered batches would not advance under the original prose | Account matching input/counts and settle the reservation with zero effects; no empty POST. |
| Discovery used a previous Connections use-permission shape | Current `IConnectionPermissionGate` has no use right; use existing ETL publish permission and host-approved scoped aliases. |
| Overall retry deadline did not bound each request | Finite request timeout, clamped to remaining deadline; retain physical allowance through actual unwind. |
| Safe Studio views did not establish safe transport output | ETL-owned logs/errors/spans omit raw values; test logger/exporter boundaries. Clarify the broad Connections Core masking rule for non-workflow consumers. |
| A connector cannot scrub every host HTTP listener | Distinguish ETL-owned spans from built-in `System.Net.Http`; document host filtering and default query-redaction limits, without global telemetry changes. |
| Anonymous bindings cannot consult a missing connection policy | ETL endpoint policy always applies; connection policy additionally applies when configured. |
| ETL permits null tenant while Connections requires a tenant | Refuse authenticated binding publication/admission with null retained tenant; no fallback. |
| Same origin alone does not constrain destination path | Finite exact canonical path allowlist, default seed paths; test normalization and disallowed alternatives. |
| Artifact replay alone would not prove in-flight upgrade | Capture a genuine pre-change database with owed/reserved file work, migrate and resume with successor, preserving identity and accounting. |

The final two reviewers found no residual blocker in these corrections. Implementation tests must still establish feasibility. The current-main first CI attempt timed out; its unchanged-head rerun is pending, with [separate evidence](https://github.com/elsa-workflows/elsa-foundation/pull/3111#issuecomment-6306818554). No green-main or HTTP implementation claim follows from this checkpoint.

## Local verification

Specification checklist: 8/8 complete. Task IDs T001–T022 are unique; only discovery and design review are complete. Local Markdown links and whitespace are checked before the checkpoint commit. Runtime builds/tests are not needed for these documentation-only changes and have not been run for HTTP behavior.

## Next step

Capture pre-change v1 published/native and in-flight database fixtures before changing the producer or persistence model. Then add the explicit v2 reader/model and connector-specific transitions. All three journeys and final gates remain required.
