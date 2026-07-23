# reusable-activities — workflows used as activities

Backend REST tests for Foundation's "workflows used as activities". In Foundation this is a first-class
**reusable Activity Definition** authored as an `elsa.activity-graph` graph with a public boundary contract
(inputs / outputs / a single `done` outcome), published to an immutable version, and consumed by a parent
(workflow or another reusable activity) by **exact `activityVersionId`**. The consumer **inlines** the child at
publish time (content-addressed template placement) — this is *not* the runtime `DispatchWorkflow` sub-execution.

All scripts share `_ReusableCommon.ps1` and run against a from-source `Elsa.Server` (see ../README.md).

## Authoring lifecycle (over REST)

1. `POST design/activities/definitions` — `{category, displayName, description, provider:{providerKey:"elsa.activity-graph", schemaVersion:"1", payload:<manifest>}, contract:{contractSchemaVersion:"1", inputs, outputs, outcomes:[{referenceKey:"done",name:"Done",isEmitted:true}]}, layout:[]}` → `{definition, draft}`.
2. `POST design/activities/drafts/{draftId}/publication-preflight` — `{expectedDraftRevision, expectedDefinitionHeadVersionId}` → `{reviewToken, minimumVersion, isPublishable, diagnostics}`.
3. `POST design/activities/drafts/{draftId}/publish` — `{expectedDraftRevision, expectedDefinitionHeadVersionId, version, reviewToken, idempotencyKey}` → published `definitionVersionId`.

**Graph manifest** (the opaque provider `payload`): `{ variables:[], rootActivity:{nodeId, activityVersionId, inputs:[], outputs:[], structure:{kind,schemaVersion,payload:{activities:[…]}}}, outputMappings:[] }`. `variables` and `outputMappings` are **required arrays** — authored as raw JSON because PowerShell drops empty `@()` arrays.

## Scripts

| Script | What it exercises |
|--------|-------------------|
| `Test-ReusableActivity.ps1` | author+publish a reusable activity, consume it as a **parent workflow root**, execute; asserts it runs inlined |
| `Test-ReusableActivityDeep.ps1` | **3-layer hierarchy** C←B←A via **root-wrapping** (a reusable activity whose graph root *is* the child); published bottom-up; verifies B→C dependency and that all layers run |
| `Test-ReusableActivityPinning.ps1` | a consumer binds the child by an **exact, immutable version id** (no floating/auto-upgrade) — the core "no auto-cascade" signal |
| `Test-DraftTestRun.ps1` | execute a **workflow DRAFT** via `publishing/workflows/drafts/test-runs` (no publish) |
| `Test-ActivityDraftTestRun.ps1` | execute a **reusable-activity DRAFT** via `publishing/activity-drafts/{draftId}/test-runs` (no publish) |
| `Test-SetOutcome.ps1` | **Set Outcome** (`Control` intrinsic) + Flowchart routes only the matching branch (both outcomes) |
| `Test-ReusableActivityOutcomeLimit.ps1` | documents the boundary limitation — a reusable-activity graph may emit only the single `done` outcome |
| `Test-ReusableSequenceNesting.ps1` | living tracker for **issue #1007** (see below) |

## Composition: what works, what doesn't

- ✅ **Reusable activity as the consumer's ROOT** — inlined and executed.
- ✅ **Root-wrapping** — a reusable activity whose graph `rootActivity` *is* another reusable activity version. This wires the dependency (verified via the authoritative outbound edge) and executes all layers. It is the working way to build a multi-layer hierarchy.
- ❌ **Reusable reference nested as one child inside a `Sequence` structure.** In a **workflow** this publishes but **faults at runtime** (`Sequence executable node '…' references missing child '…'`) — **issue #1007**. Inside a **reusable graph** the reference is silently **not recorded as a dependency** (and does not run). So a layer cannot both call a child and do other work via a Sequence today.
- (The compiler also supports reusable placement inside a **Flowchart**; not exercised by this suite.)

## Findings vs the Elsa-3 mental model

- **"Workflows as activities"** → reusable **Activity Definition** graphs (there is no publish-a-workflow-as-an-activity bridge).
- **"auto-publish consuming workflows = true → whole line updates"** → **no such flag**. Consumers pin to exact versions (`Test-ReusableActivityPinning`). Propagation is a deliberate, staged **upgrade-plan** API (`design/activities/upgrade-plans` — create → apply stage → publish handoff draft → refresh → repeat up the hierarchy), not automatic.
- **"whole hierarchy in sync while in draft"** → **no draft-consumes-draft**; a parent draft binds only *published* child versions. Draft test-run compiles the draft under edit; nested children come from their published versions.
- **"execute a draft"** → yes (`Test-DraftTestRun`, `Test-ActivityDraftTestRun`).
- **"outcomes of workflows used as activities"** → Set Outcome + branch-routing works **inside a workflow** (`Control` + Flowchart, `Test-SetOutcome`); a reusable-activity **boundary** is limited to the single `done` outcome (`Test-ReusableActivityOutcomeLimit`), so a parent cannot branch on a *custom* outcome coming out of a reusable activity.

## Open blockers surfaced here

- **Issue #1007** — reusable activity nested in a workflow `Sequence` faults at runtime; only root/Flowchart placement works. Tracked by `Test-ReusableSequenceNesting.ps1`.
- **Second-version publish over REST** trips `activity.publication.review-stale`: after publishing v1, a v2 draft passes preflight (`isPublishable=true`) but the publish rejects the review token as stale (the token is a hash over draft+head+diff+validVersions+readiness; it diverges for a version bump). This **blocked exercising the full upgrade-plan cascade** end to end (which requires publishing successive versions). Needs deeper investigation before filing — noted here so the gap is explicit, not silently skipped.
