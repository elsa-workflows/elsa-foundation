# reusable-activities — workflows used as activities

Backend REST tests for Foundation's "workflows used as activities". In Foundation this is a first-class
**reusable Activity Definition** authored as an `elsa.activity-graph` graph with a public boundary contract
(inputs / outputs / schema-1 `done` or schema-2 mapped outcomes), published to an immutable version, and consumed by a parent
(workflow or another reusable activity) by **exact `activityVersionId`**. The consumer **inlines** the child at
publish time (content-addressed template placement) — this is *not* the runtime `DispatchWorkflow` sub-execution.

All scripts share `_ReusableCommon.ps1` and run against a from-source `Elsa.Workbench` (see ../README.md).

## Authoring lifecycle (over REST)

1. `POST design/activities/definitions` — `{category, displayName, description, provider:{providerKey:"elsa.activity-graph", schemaVersion:"1", payload:<manifest>}, contract:{contractSchemaVersion:"1", inputs, outputs, outcomes:[{referenceKey:"done",name:"Done",isEmitted:true}]}, layout:[]}` → `{definition, draft}`.
2. `POST design/activities/drafts/{draftId}/publication-preflight` — `{expectedDraftRevision, expectedDefinitionHeadVersionId}` → `{reviewToken, minimumVersion, isPublishable, diagnostics}`.
3. `POST design/activities/drafts/{draftId}/publish` — `{expectedDraftRevision, expectedDefinitionHeadVersionId, version, reviewToken, idempotencyKey}` → published `definitionVersionId`.

**Graph manifest** (the opaque provider `payload`): `{ variables:[], rootActivity:{nodeId, activityVersionId, inputs:[], outputs:[], structure:{kind:"elsa.sequence.structure",schemaVersion:"1.0.0",payload:{activities:[…]}}}, outputMappings:[] }`. Structure handlers are resolved by this exact `(kind, schemaVersion)` contract. `variables` and `outputMappings` are **required arrays** — authored as raw JSON because PowerShell drops empty `@()` arrays.

## Scripts

| Script | What it exercises |
|--------|-------------------|
| `Test-ReusableActivity.ps1` | author+publish a reusable activity, consume it as a **parent workflow root**, execute; asserts it runs inlined |
| `Test-ReusableActivityDeep.ps1` | **3-layer hierarchy** C←B←A with C nested in B's Sequence; published bottom-up; verifies B→C dependency and that all layers run |
| `Test-ReusableActivityPinning.ps1` | a consumer binds the child by an **exact, immutable version id** (no floating/auto-upgrade) — the core "no auto-cascade" signal |
| `Test-ActivityUpgradePlan.ps1` | persisted **upgrade-plan** journey across A→B→C: create/get, apply a staged dependency update, publish the exact handoff, refresh, apply the successor, read receipts, and verify the final exact-version pins |
| `Test-DraftTestRun.ps1` | execute a **workflow DRAFT** via `publishing/workflows/drafts/test-runs` (no publish) |
| `Test-ActivityDraftTestRun.ps1` | execute a **reusable-activity DRAFT** via `publishing/activity-drafts/{draftId}/test-runs` (no publish) |
| `Test-SetOutcome.ps1` | **Set Outcome** (`Control` intrinsic) + Flowchart routes only the matching branch (both outcomes) |
| `Test-ReusableActivityOutcomeLimit.ps1` | publishes schema-2 mapped outcomes and proves only the matching parent branch runs |
| `Test-ReusableSequenceNesting.ps1` | regression coverage for reusable placement inside a workflow Sequence |

## Composition: what works, what doesn't

- ✅ **Reusable activity as the consumer's ROOT** — inlined and executed.
- ✅ **Root-wrapping** — a reusable activity whose graph `rootActivity` *is* another reusable activity version. This wires the dependency (verified via the authoritative outbound edge) and executes all layers.
- ✅ **Reusable reference nested in a reusable graph's `Sequence` structure** — the canonical `elsa.sequence.structure@1.0.0` contract records the dependency and executes the child. `Test-ReusableActivityDeep.ps1` is the runnable regression coverage.
- ✅ **Reusable reference nested as one child inside a `Sequence` structure in a workflow.** The boundary keeps its authored id and both the reusable and following sibling execute.
- (The compiler also supports reusable placement inside a **Flowchart**; not exercised by this suite.)

## Findings vs the Elsa-3 mental model

- **"Workflows as activities"** → reusable **Activity Definition** graphs (there is no publish-a-workflow-as-an-activity bridge).
- **"auto-publish consuming workflows = true → whole line updates"** → **no such flag**. Consumers pin to exact versions (`Test-ReusableActivityPinning`). Propagation is a deliberate, staged **upgrade-plan** API (`design/activities/upgrade-plans` — create → apply stage → publish handoff draft → refresh → repeat up the hierarchy), not automatic.
- **Publishing successive reusable-activity versions** → supported through exact-version-bound publication preflight. `Test-ReusableActivityPinning` publishes C v1 and C v2 over REST, then verifies that consumer B remains pinned to C v1.
- **"whole hierarchy in sync while in draft"** → **no draft-consumes-draft**; a parent draft binds only *published* child versions. Draft test-run compiles the draft under edit; nested children come from their published versions.
- **"execute a draft"** → yes (`Test-DraftTestRun`, `Test-ActivityDraftTestRun`).
- **"outcomes of workflows used as activities"** → schema-2 reusable boundaries map entry outcomes to public outcomes, and the parent routes them through generic Flowchart ports (`Test-ReusableActivityOutcomeLimit`).
