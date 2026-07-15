# Quickstart: Validate Reusable Activity Definitions

This is the plan-stage end-to-end validation guide. It describes the public seams and expected evidence; implementation details and exhaustive test cases belong in the later `tasks.md` and implementation phase.

## Prerequisites

- .NET SDK capable of building `net10.0` projects.
- A combined Elsa Design + Publishing + Runtime host for authoring/publication scenarios.
- A Runtime-only host composition for artifact-only restart verification.
- Groundwork SQLite configured as durable storage for the release-gate scenario.
- A caller with activity authoring/publishing, workflow authoring/publishing, runtime execution, structure inspection, and (for one comparison) sensitive-value inspection permissions.

## Contract map

- Authoring/publication: [contracts/authoring-api.md](contracts/authoring-api.md)
- Validation errors: [contracts/validation-errors.md](contracts/validation-errors.md)
- Version diff: [contracts/version-diff.md](contracts/version-diff.md)
- Dependencies/upgrades: [contracts/dependencies-and-upgrades.md](contracts/dependencies-and-upgrades.md)
- Provider/Runtime seams: [contracts/provider-runtime-seams.md](contracts/provider-runtime-seams.md)
- Runtime inspection: [contracts/runtime-inspection.md](contracts/runtime-inspection.md)
- Entities/invariants: [data-model.md](data-model.md)

## 1. Build the current baseline

```bash
dotnet build Elsa.Server.slnx -c Release
```

Expected: the baseline builds before feature work. After implementation, the same command includes the new graph Design/Runtime projects and remains green.

## 2. Create and validate an activity draft

1. `POST /design/activities/definitions` with provider key `elsa.activity-graph`, graph schema `1`, one required input, one required output, and `Done`.
2. Record `definitionId`, `draftId`, and `revision` from `201 Created`.
3. `PUT /design/activities/drafts/{draftId}` with the expected revision, a graph containing one suspending descendant, one graph-local durable value, and one public output mapping.
4. `POST /design/activities/drafts/{draftId}/validate` with the new revision.

Expected:

- definition and initial draft are created atomically;
- update advances the revision exactly once;
- validation returns `200` and `isValid=true`;
- a repeated update with the old revision returns RFC 7807 `409 activity.draft.stale-revision`;
- structure-authorized callers see the public contract, while callers without provider-author permission do not receive the opaque manifest payload.

Negative authority check:

1. Select a CLR-reconciled source-owned definition.
2. Attempt to create/update a general authoring draft under that lineage.

Expected: `409 activity.definition.content-authority`; no competing draft is created. Forking to a new Design-owned identity is the supported customization path.

## 3. Preview a diff and enforce SemVer

1. Publish the initial valid draft as `1.0.0` with expected head `null`.
2. Clone that exact version to a new draft.
3. Change an optional input to required without a default and remove/change an existing default.
4. `POST /design/activities/drafts/{draftId}/diff` against the `1.0.0` version.
5. Attempt publication as `1.1.0`.
6. Publish the same candidate as `2.0.0` with the correct expected head.

Expected:

- diff returns deterministic per-change entries and overall `requiredBump=Major`;
- minor publication returns `422 activity.publication.invalid` with `activity.version.bump-insufficient` and no partial version/template/reference/edge/head;
- major publication returns `201`, including exact version, template hash, Source Reference, provider fingerprint, requirements, and the same diff classification;
- the old version/template remains unchanged and executable.

## 4. Verify dependency reads and a staged upgrade plan

1. Publish activity A.
2. Publish activity B whose graph pins A v1.
3. Publish A v2.
4. Read outbound direct dependencies for B and inbound transitive uses for A v1.
5. Create an upgrade plan replacing A v1 with A v2 for selected activity/workflow draft roots.
6. Mutate one selected draft after planning, then attempt apply.
7. Recreate the plan and apply a dependency-closed selection.

Expected:

- B's direct outbound edge is `AuthoritativeDirect` and pins A v1/template hash;
- incoming/transitive results identify `DerivedProjection` plus `asOf` watermark;
- no query resolves A v2 as “latest” on behalf of the caller;
- stale apply returns `409 activity.upgrade.stale-plan` and writes nothing;
- fresh apply creates/updates only drafts atomically and never mutates published B;
- if a parent needs an unpublished child result, the plan reports the explicit multi-stage handoff rather than inventing a future version id.

## 5. Publish a consuming workflow and prove deterministic placement

1. Author a workflow that places the same exact activity version twice.
2. Publish the workflow.
3. Inspect its executable material through the existing publishing inspector.

Expected:

- both outer nodes pin the exact activity-definition version/template;
- each placement has distinct executable-node and resume-target identities;
- the same source and invocation origin reproduce identical ids across repeat compilation;
- unrelated subtree identities remain stable when one placement changes;
- the workflow artifact hash is behavioral only;
- the executed Source Reference carries boundary-scoped hierarchical layout, and layout changes alone do not change the artifact hash.

## 6. Mandatory suspend, destroy, restart, and resume gate

1. Start the published consuming workflow and choose one placed graph activity.
2. Let the graph entry checkpoint commit, then let its descendant create a native bookmark and suspend.
3. Record the workflow execution id, outer activity execution id, descendant activity execution id, bookmark id, and last checkpoint id.
4. Stop and dispose the host completely. Do not preserve DI scopes, in-memory registries, queues, or caches.
5. Start a fresh Runtime-only host against the same Groundwork SQLite state and the same installed Runtime consumers. Do not configure Activity or Workflow Design stores.
6. Resume the recorded bookmark and wait for terminal completion.

Expected release-gate evidence:

- exactly one workflow execution id before and after restart;
- no child workflow instance or actor;
- the outer graph activity remains a visible boundary;
- the bookmark belongs to the actual descendant execution;
- committed descendants are not replayed and keep their ids/sequences;
- effective public inputs were evaluated/captured once;
- output mapping, `Done`, outer terminalization, and parent continuation commit once at exit;
- the final public output is propagated exactly once;
- execution succeeds without any Design source/version/layout read.

## 7. Inspect the hierarchy and pinned layout

1. Read the outer activity execution detail.
2. Page `/descendants` with a deliberately small page size until `nextCursor=null`.
3. If a returned child is another graph boundary, repeat using that child's activity execution id.
4. Read `/layout` for each expanded boundary.
5. Repeat detail under structure-only permission and under value permission.
6. Mutate current Design layout after the run and read the old execution again.

Expected:

- detail returns outer lifecycle separately from derived aggregate status;
- repeated loop/retry executions remain distinct and attempt lineage links retries;
- pages use a fixed committed watermark, deterministic ordering, and no duplicates/omissions;
- a cursor reused for another root/query/tenant/permission profile returns `409 activity.cursor.binding-mismatch`;
- nested boundaries are clicked through without one unbounded response;
- layout comes from the executed Source Reference and still shows the old historical geometry;
- structure remains visible to the structure-only caller, while captured protected values are explicitly redacted/omitted according to existing payload policy.

## 8. Exercise faults, cancellation, retry, and activation preflight

Fault:

- force an internal descendant fault after entry commit;
- verify the inner incident remains and the outer boundary has a causally linked fault.

Cancellation race:

- force resume-first and cancel-first durable orderings;
- verify exactly one winning terminal path;
- when cancellation wins, verify descendant bookmarks, timers, and pending work are gone before outer `Cancelled` is visible.

Retry:

- retry the failed boundary;
- verify a new outer activity execution/scope, fresh descendants, same pinned template/effective input snapshot, and first/previous attempt links.

Activation:

- remove a required Runtime consumer from a test host;
- run `/publishing/preflight` over the retained artifact fixture;
- attempt activation only in the negative test.

Expected: preflight reports the missing consumer/schema; activation records a deployment/system incident and does not enter ordinary activity retry.

## 9. Verify draft test runs

1. Submit an exact active draft revision to `/publishing/activity-drafts/{draftId}/test-runs`.
2. Run it to completion or suspension.
3. Inspect its workflow and outer activity executions.
4. Let the test-run Source Reference expire and run reference-derived cleanup.

Expected:

- the test uses a synthetic wrapper workflow, normal Runtime pipeline, and the one content-addressed artifact store;
- behaviorally identical draft and published material resolve to the same artifact id with different Source References;
- expiry/cleanup is reference-derived and does not remove an artifact while another live reference exists.

## 10. Verify Elsa 3 plan/apply conversion

Use a fixture collection containing:

- a reusable workflow referenced by two workflows;
- a direct-start use of that reusable workflow;
- a missing reference;
- unsupported trigger behavior;
- an exact reusable-composition cycle.

Expected:

- analysis writes nothing and reports deterministic identities, exact rewrites, wrapper workflows, missing/unsupported facts, and complete cycle paths;
- applying a valid selected closure creates an activity definition plus wrapper workflow and rewrites exact references atomically;
- repeat analysis/apply is deterministic/idempotent;
- recursive composition is never silently replaced with separate-workflow execution.

## 11. Run focused suites

```bash
dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj -c Release
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj -c Release
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj -c Release
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj -c Release
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj -c Release
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj -c Release
dotnet build Elsa.Server.slnx -c Release
```

Expected after implementation: all suites pass; the architecture suite reports zero new Runtime -> Activity Design, Workflow Design, or Publishing implementation references and no reintroduction of the removed workflow-as-activity surface.

## Recorded implementation evidence (2026-07-16)

- Mandatory SQLite gate: `ActivityDraftTestRunTests.Groundwork_sqlite_graph_run_suspends_restarts_in_runtime_only_host_resumes_inspects_and_propagates_output_once` passed in Release. Generation 1 used a combined host to publish and suspend a real `GraphActivity`; all host/store state was disposed. Generation 2 used Runtime, Activities Runtime, Graph Runtime, and Groundwork SQLite only, resumed the same bookmark/workflow, preserved execution ids/sequences, captured the boundary input once, propagated the required output once, and read the executed Source Reference layout/hierarchy.
- Focused Release suites: Activity Design 348, Design Groundwork 33, Graph 35, Activities Runtime 186, Publishing 152, Workflows Runtime 833, Groundwork 208, Elsa3 Mapping 19, Architecture 78 — 1,892 passing tests total.
- `dotnet build Elsa.Server.slnx -c Release`: passed with 0 errors (one retained obsolete legacy-EF-column warning).
- `git diff --check`, clean-break searches, Runtime-boundary searches, and migration search passed. Legacy `UsableAsActivity` remains only in Elsa 3 import models/analyzers and guard fixtures; explicit `ExecuteWorkflow` remains available for separate-workflow execution.
