# Qualification

The feature uses no new project/package references. Qualification owns its temporary
hosts/databases and leaves existing development hosts untouched.

| Gate | Result |
| --- | --- |
| Design Core whole suite | 655 passed, no skips |
| Design API whole suite | 183 passed, no skips |
| Design EF whole suite | 128 passed, no skips |
| Shared persistence schema/migration whole suite | 501 passed, no skips |
| Architecture whole suite after graph restore | 592 passed, no skips |
| Generated solution filters | Fresh |
| Workflows frontend whole suite | 199 files, 2703 tests passed |
| Workflows/browser TypeScript | Passed |
| Changed-file ESLint / template CSS stylelint | Passed; existing autofocus and generation-ref advisory warnings |
| Workflows production bundle | Passed without raising budgets |
| Owned rebuilt Workbench | CRUD/replay, 409, restart, independence, layout/labels passed |
| Browser | Light/dark create/edit/copy/delete/use with variables, I/O; live Save as template; failed rename/navigation recovery passed |

Discovery comparison: EF lists/runs 128 and API lists/runs 183. Core lists 585 and
runs 655; architecture lists 583 and runs 592 (execution expands data-driven cases).
All four whole projects ran without filters or skips.

Mutation proof: removing the stale-revision guard makes the restart/stale-edit test
fail; restoring it returns the EF suite to green. Removing the explicit template-port
ownership guard makes the composition test fail; restoring it returns all 127 to green.
Removing template Back persistence makes the rendered failed-save retention regression
fail; restoring it returns all four browser journeys to green. The delayed-Back
regression also failed when Ctrl+Z bypassed the inert editor through its window
listener; guarding that listener preserves declarations during the pending save.

Production frontend output: entry 110.65 kB; stylesheet 167.32 kB; authenticated
Definitions total 385.01 kB (387.50 budget); activity upgrades 376.30 kB (379.00 budget).
Heavy template management/editor surfaces stay deferred. Four provider Initial
migrations were regenerated; local owned persistence execution is SQLite. Required
CI supplies the provider matrix and whole-server build gates; pending checks are not
counted as passed. Exact-head CI/review evidence belongs on PR #3472.

Code Review follow-up: the 999→1000 boundary test reproduced successful overflow
before the fix. Creation now holds the host's tenant-specific distributed catalog
lock and checks capacity inside the atomic stage; refusal leaves read/edit/delete,
replay and re-creation after deletion working. All 128 EF tests pass. Hosts without
the optional port now return501 on all five routes (five API cases). Restoring a
mandatory List store injection reproduces500 in that test; restoring optional
injection returns the whole183-test API suite to green. Built-in recipes live in
a tiny module separate from template API/designer helpers; bundle budgets stay
unchanged (the measured closure is384.38kB, not a claimed headroom gain).


Hosted PR shard3 exposed a missing central schema-content declaration for
WorkflowTemplateEntity.ContentJson, plus the cross-module payload inventory missing
its new column (eight failures across four providers). The fix declares that content
in the existing WorkflowsDesign family and adds its one encoded ContentJson column
to the expected inventory. No guard was removed, no EF model/schema change was
needed, and all501 shared persistence tests plus128 Design EF tests pass locally.
Fresh hosted checks are required on the repair commit.


Main integration after the schema repair: merged main e81401851 (the landed
Weaver contributions and Studio UX fixes). Resolve the editor conflict by retaining
both the template document adapter and main's hold-for-review persistence controls;
use the new StudioAgentRegistry / api.agent contract in the template route and fixture.
Templates do not register a workflow-definition Weaver controller. The complete
Workflows suite now passes2703 cases in199 files; all four template browser journeys
and TypeScript checks pass. The combined production bundle passes main's existing
budgets (385.01/387.50kB); this feature does not raise them.
