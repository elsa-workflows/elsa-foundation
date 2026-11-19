# Broadcast template reachability and ordinary GC composition

Source parent `334541128ff2c1670dc08098ccb1fa01affbca11`.
The [machine receipt](broadcast-template-reachability.json) binds eight source/contract/catalog/task files.
**T049 remains open. This increment does not qualify atomic template collection or enable publication.**

The existing GC derived retained templates only from the workflow artifact's compatibility metadata.
Ordinary reusable placements instead record `activity.templateHash` on boundary nodes, including nested
slots. The collector now reads the artifact's flattened immutable `NodesById` snapshot and wrapper metadata,
then follows present templates' own metadata and exact `ClosedTemplates`/`DirectDependencies` identities.
Traversal is iterative and visits each template once; shared edges terminate. Missing or mismatched declared
exact dependencies retain the whole sweep before deleting any reference or material. An inlined imported
workflow may legitimately carry provenance for a template not installed on this host; that alone does not
invent a template dependency or stop unrelated cleanup.

The default DI implementation descriptor could also select the new, longest cohort constructor merely
because `IStimulusDeliveryStore` was registered. Its unfinished conservative template safeguard would then
retain every unrelated template and template reference indefinitely, even while publisher activation is
disabled. Both ordinary registrations (Runtime and the reference-GC pump feature) now explicitly select the
original constructor. They preserve ordinary cleanup and report `SupportsStimulusDeliveryRetention=false`.
Either registration order is covered. The explicit cohort constructor remains available to the existing
finite provider/root controls; it still retains unguarded template material. This is a deliberate dormant
composition boundary, not completed cohort-aware default GC. Publication must remain disabled until that
qualified composition and native atomic template lifecycle are delivered.

Final restored results:

| Check | Passed | Failed / skipped |
|---|---:|---:|
| Whole Workflow Runtime | 2,895 | 0 / 0 |
| Whole Runtime EF | 1,317 | 0 / 0 |
| Focused GC controls, included in Runtime | 41 | 0 / 0 |

The twenty new Runtime cases cover wrapper, root-node and nested workflow placements; exact direct and
closed dependencies; template-local compatibility and nested-node metadata without declared edges; mixed
three-level/shared closure; missing/hash-mismatched exact dependencies; an inlined import without its
original template store; and both actual ordinary DI registration orders. Retained completed executions
keep the exact templates and references. Removing the final execution root releases them; unrelated
material remains collectible. The whole EF suite adds no new template-lifecycle qualification.

Every discovered method maps to TRX results. Runtime lists 2,884 cases, with three deferred theories
expanding to fourteen executions: 2,884−3+14=2,895. EF lists and executes 1,317. Truncated display-name
collisions are not subtracted. Eight behavioral mutations went red and were restored: workflow-node edges,
template-local edges, direct/closed edges, exact dependency integrity, legitimate inlined absence and each
of the two explicit DI factories. There is no compile-error mutation counted. Core API delta is +0/0;
frozen authoring remains `41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

All three component source and final receipt reviews are clean. Full-PR rounds remain zero.
The preceding CI-repair parent passed remote Build & test, architecture and maps in run40377823312;
new source still needs its own remote checks. Full tier was not run on the draft. GitGuardian's older
public-source-digest incident38996611 remains unresolved; no protection/check is bypassed.

Next: finish actual template/reference material coordination, including publication/adoption and draft-test
writers, then qualify default cohort GC. T050 immutable input/exact bookmark/schema activation, T051 actual
host/fresh-provider recovery, T045/other stories, deletion/guards, final current host/Studio, full review,
exact-head checks/resulting-main and the third ADR0063 PR remain open. #2898/#2907/program production
acceptance stays open; Orleans/task-runner remain deferred.
