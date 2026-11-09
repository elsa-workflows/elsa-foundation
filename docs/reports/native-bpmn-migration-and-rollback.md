# Native and direct-BPMN: migration and rollback

Status: **operator-facing qualification note**, 7–8 November 2026. Rechecked on main `c1f35d2dc73201df0e3890e1f0c851d9ddd52a37` plus the interrupted-publication GC tests (`9812e60f54f1178498e82c3d092268b859d4a36b`). The unpushed pair was rebased from `60d284932` onto that main with no conflicts. This is not an architecture decision, not a retention redesign, and not closure of #2905, #2892, FR-014 or SC-005.

Native publish and direct-BPMN publish are two different pipelines. Keep them separate: their journal and fenced-write semantics differ. Do not consolidate them to share a save path.

## What each publish actually writes

**Native** (`PublishWorkflowRequestHandler`): after preflight, `IWorkflowExecutableStore.SaveAsync` persists the executable. Only later does `PublicationActivator.ActivateAsync` write a publication-journal candidate and ask the activation coordinator to enroll the source reference under a root-write lease. The first durable write is therefore the artifact, not the retention root.

**Direct BPMN** (`BpmnModelRuntime.PublishCoreAsync`): `SaveModelAsync` persists model material, then `leases.ExecuteFencedAsync` plus `SaveWithRootWriteLeasesAsync` writes the live source reference. The first durable write is again the material, not the fenced reference.

Neither path takes a root-write lease before that first save. Reference GC (`WorkflowExecutableReferenceGarbageCollector`) deletes artifacts older than `ArtifactCreationGracePeriod` (host default 5 minutes) that have no live retention root. The periodic pump default is `SweepInterval` 5 minutes, `MaxBackoffInterval` 30 minutes. Grace exists to cover this staging interval; it is not enrollment in retention.

## Interrupted publication (qualified, not fixed)

On this source, interrupting either publish after the save and before the retention root, then advancing the clock past grace and running one sweep, collects the unreferenced material. A later retry on a clean host recreates material and commits a live source/publication. That is classification **(1) loss safely refused or retryable**: no committed definition pointed at missing material. Classification **(2) unsafe commit** was not observed on SQLite.

This does not enroll publish in retention, does not change grace, and does not pull in R3 / managed-store work (#3951). PostgreSQL, SQL Server and MySQL were not run here (no Docker / Testcontainers on this VM). The existing retention acceptance criteria are unchanged.

## Migrating a definition between models

- A native workflow definition stays a native workflow. A direct-BPMN model stays a BPMN model. Publication identity, artifact ids and source-reference kinds are not interchangeable.
- Authoring a BPMN diagram inside Studio and publishing it natively is the native-wrapper path already qualified under spec 1184 compatibility; it is not a conversion of an existing native Sequence/Flowchart into a direct-BPMN source.
- There is no supported live migration that rewrites an in-flight native execution into a direct-BPMN execution, or the reverse. Accepted work recovers on the model that admitted it.
- Frozen saved-Studio families and old-BPMN baseline captures remain the compatibility oracles. Do not edit those fixtures, including assets owned by open PRs #3946, #3941, #3905 and #3848.

## Rollback

| What you are rolling back | How | What you must not do |
|---|---|---|
| This qualification follow-up (tests and reports) | Revert the follow-up commits. No production persistence format changed. | Do not delete the interrupted-GC tests to hide a later red run. |
| Native hosting qualification (spec 249) | Revert its test fixture, tests and documentation. Disable opt-in observation to restore the existing fixture profile. | Do not remove runtime fencing, membership authorization or receipt guards to make a scenario pass. |
| Direct-BPMN Studio qualification (spec 1184) | Revert hosting tests and evidence docs. Restore gitignored fixtures with `python3 tools/bpmn/restore_evidence.py` (not `--all` unless you intend the 849 MB complete archive). | Do not treat quarantined Workbench rows as green. Do not present historical captures as a fresh run. |
| A published native version | Publish a previous version through the existing publication slot/journal. | Do not delete artifacts that a live source reference still names. |
| A published direct-BPMN model | Publish the prior model material under the same fenced source identity rules. | Do not bypass `ExecuteFencedAsync` / `SaveWithRootWriteLeasesAsync`. |
| Host admission | Turn new admission off; already accepted work remains recoverable (FR-015). | Do not equate authoring-unavailable with execution failure. |
| The separately delivered task-lifetime / inspection repairs | Those have their own PRs; reverting them requires rechecking the preserved recovery baseline. | Do not mix those reverts with this docs/test follow-up. |

Safe admission rollback is: stop admitting new work, keep implementation and stores, let accepted executions finish or recover. SQLite local-process results do not qualify other databases, multi-machine partitions, clock skew, Orleans or other execution models. Physical external effects remain at least once.

## Recheck commands (do not reassign historical SHAs)

Commands, source SHA and counts for the 7–8 November 2026 recheck live in [native hosting conformance](native-hosting-conformance.md#pr2-recheck-78-november-2026) and [spec 1184 compatibility](../../specs/1184-direct-bpmn-studio/evidence/compatibility/README.md#pr2-recheck-7-november-2026). Historical October evidence remains bound to its original commits. The 8 November rerun was on main `c1f35d2dc`.

Cleanup in this follow-up: no obsolete tests or production code were deleted. Native and BPMN publish were not consolidated.
