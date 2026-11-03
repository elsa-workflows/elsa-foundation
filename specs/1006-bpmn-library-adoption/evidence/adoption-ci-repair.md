# Adoption PR: observed CI repairs

Source parent `15e89eae6de56d2c7fe62d4293ffd588cd7a5414`.
The [machine receipt](adoption-ci-repair.json) binds eleven source/contract/fixture/lock files.
**These local checks do not establish green remote CI or completion of the switch.**

CI run [40369434902](https://github.com/elsa-workflows/elsa-foundation/actions/runs/40369434902)
failed its configured retries for four related groups:

- The Core implementation-shape guard flagged the child-control callback envelope because it carries a live
  execution-context handle. `RuntimeChildControlContext` is now a sealed non-positional record with the same
  explicit constructor/get-only snapshots. It performs no dispatch, persistence or DI work. Its remarks make
  the live handle/shallow copy boundary explicit; equality grants no authority or deduplication. A bounded
  `ToString()` keeps live execution/control data out of incidental diagnostics. Seven generated record API
  members are tracked; no existing declaration is removed and the guard's ratchet is unchanged.
- The schema-readability fixture still expected only the current outbox format. It now independently requires
  predecessor literal `1.0.0` and current `2.0.0`, preserving detection of a lost legacy reader or declaration.
- The DispatchWorkflow behavioral drive and runtime-only export/import probe configure shell features manually.
  Both omitted Events, although DispatchWorkflow's feature already declares that dependency. Temporary runtime
  tracing showed parent resumes returned `Dispatched` with `AcceptedButFaulted`: the inline event publisher
  needed by resume validation was missing. Both fixtures now compose Events and reference its implementation.
  No production fallback, synthetic outcome, weakened coverage or relaxed forbidden-assembly check was added.

| Final local check | Passed | Failed / skipped |
|---|---:|---:|
| Whole Activity Behavioral | 215 | 0 / 0 |
| Whole Workflow Publishing | 61 | 0 / 0 |
| Whole Cluster Readability | 117 | 0 / 0 |
| Whole BPMN activities | 297 | 0 / 0 |
| Whole DispatchWorkflow | 339 | 0 / 0 |
| Core implementation-shape architecture guard | 1 | 0 / 0 |

The Publishing suite includes its actual fresh runtime process: imported parent and child execute to their
ordinary expected completion, and the runtime process still refuses loaded Design/Publishing assemblies.
This establishes that existing scenario only, not upgraded-host broadcast recovery, T045 or Studio proof.
Behavioral coverage again observes every declared waited outcome/output through real child fates and resumes.

Locked restore first correctly refused the two newly added direct project references. After refreshing the
lock graph, unrelated SDK package-pruning churn was removed: the two consumer locks add only Events and
Events.Strategies project entries, retaining every existing package version/hash/dependency. Final locked
restores pass for Behavioral and the full Publishing/ExecutionProbe graph. Existing warnings remain.
Temporary diagnostic source was restored. Frozen authoring SHA256 remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

All three component source and final receipt reviews are clean. Full-PR review remains zero
rounds. Exact new-head CI still has to pass. T049 final GC composition, T050 immutable input/exact bookmark/
schema activation, T051 actual host recovery, T045/other stories, duplicate-semantics deletion/guards, final
current host/Studio, full review/resulting-main and third ADR0063 PR remain open. GitGuardian incident38996611
still flags an older receipt's public source SHA256; no protection/check is bypassed. #2898/#2907 and program
production acceptance remain open; Orleans/task-runner stay deferred.
