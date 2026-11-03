# Independent Weaver conformance witness

The complete package witness at `76ec58d0061135cfa028e1bc47ec7639fd139e7f` qualifies the bounded M1 contribution contract: all 38 cases completed (that historical run had 135 stages; no stage count is a requirement), with 16 positives and 22 recognized Failed controls. Original and restored resource/workflow journeys run through both actual Copilot and Anthropic adapters. See the [owning #3438 receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6538498830), [curated evidence](../../../specs/1174-weaver-package-witness/evidence/qualification-76ec58d00.json), and [artifact identities](../../../specs/1174-weaver-package-witness/quickstart.md#qualified-code-baseline).

The qualified code baseline has 80 product packages and a retained SDK tgz, external module authors, and two package-only consumers. All 155 backend and 10,108 Studio common baseline files remain identical through native add/remove/incompatible/restore transitions. Consumer product-source references and frontend invocations are zero. Both owned hosts exit 0 with empty runtime caches. This is deterministic provider-protocol evidence; live-provider success is unclaimed.

Follow [owning issue #3438](https://github.com/elsa-workflows/elsa-foundation/issues/3438) and [PR #3497](https://github.com/elsa-workflows/elsa-foundation/pull/3497) for current final-source qualification, required exact-head CI, independent review, merge and resulting-main verification. [T026–T028](../../../specs/1174-weaver-package-witness/tasks.md) define those mandatory integration gates. This M1 proof does not establish durable recovery, full current-module/workflow coverage, new binary compatibility or M2–M5 completion.

## Reproduce

Run from committed source with a new absolute output directory outside the checkout and a unique local version:

```sh
python3 tools/studio/Weaver.Conformance.Probe/qualify.py \
  --output /absolute/new-owned-weaver-proof \
  --version 4.0.0-weaver3438.local1
```

Producer/module-author steps need .NET 10, Python 3, Node/pnpm 11.9 and Chromium automation. Ordinary consumer restore/build/publish/run uses the prebuilt distributions with frontend tools denied and audited. The driver retains locks, closure/cache/archive hashes, common published bytes, actual native membership/serving generations and owner/browser receipts. It only starts owned processes with isolated native state and normal authentication. Nothing publishes remotely or consumes existing developer credentials/hosts. [Consumer recipes](../../../specs/1174-weaver-package-witness/contracts/hosts.md) and the [validation guide](../../../specs/1174-weaver-package-witness/quickstart.md) describe the sequence.

The [integration workflow](../../../.github/workflows/ci.yml) runs the same complete qualifier with read-only repository permissions. Partial execution, missing observations, wrong-cause failures, altered artifacts and source/frontend consumer shortcuts cannot qualify. Common changes require fresh complete distributions and all cases again.

## Cross-domain contributions

The separate `Acme.Home.Weaver` package contributes `WidgetContextSource`, `WidgetCapabilities`, `ReadWidgetTool` and `SetWidgetVisibilityTool` through `AcmeHomeWidgetFeature` and the existing named collection lifecycle. It owns the personal Home widget's identity, bounded context/guidance, validation, permissions, CAS and observed-result semantics. The actual Preferences service/native store owns persistence; independent native document reads and Home observations establish the effect while preserving unrelated JSON.

`Acme.Home.Studio` uses the public SDK for the Home widget, settings route, context, prompt/tool declarations and review/result rendering. `Acme.Weaver.ProtocolFixtures` separately implements the public Copilot and Anthropic client factories. Both adapter artifacts are installed in the common baseline, and exactly one provider feature/profile is enabled at a time. The protocol fixture neither references the resource package nor interprets its payload; the real provider adapters and generic engine execute the journeys.

These external packages are qualification fixtures, not shipped product modules. The M1 baseline's packed external tests comprised 78 resource and 21 protocol cases. The #3440 size extension preserves those 78 and adds 45 resource cases: compiled discovery and packed execution match all 123 in component qualification, with the separate Studio author typechecked/built/packed against the retained SDK. Its probe has 166 passing component checks, including distinct automatic SSE and reviewed HTTP receipt paths and exact preflight request provenance. This component evidence reuses source-bound product archives; final #3440 qualification is the complete package witness described below, and `qualify_autonomy.py` remains diagnostic. The [Agent catalog](../../../Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md) indexes their public seams; the [canonical M1 contract and preview migration](../../../specs/1018-weaver-module-contributions/contracts/contributions.md) own generic API meanings, removed surfaces and the supported workflow subset.

## Evidence and control boundaries

`browser.mjs` drives real resource review/readback and native workflow export, validation, whole Undo, explicit Save and reload. Controlled ACK faults alter or lose an actual exchange after the native browser edit. `owner_resources.py` seeds through native APIs; `agent_transport.py` retains actual HTTP/SSE/provider observations. Reads require matching native ContributedResult and provider payloads; execution requires the owning receipt. Transport success alone cannot establish an effect.

Expected negative children stay Failed with separately recognized precise causes. Before-effect controls retain unchanged owner state; concurrent CAS preserves its actual winner, and post-dispatch uncertainty/confirmation retry cannot replay an accepted edit. The general private IAM setup uses normal persisted principals and predeclared grants, followed by normal sign-in. Module-owned CAS rendezvous and provider pre-call gates only coordinate real native operations; they do not replace stores or the engine or add product contracts.

The exporter selects evidence references rather than uploading the run directory. The 76ec safe export contains 11,823 references with original statuses and no private authentication, seed configuration, runtime databases or keys. Report/index hashes are in the curated receipt. Earlier failed runs and diagnostic component versions remain preserved on the owning issue/private outputs; this complete run does not relabel them. [Tasks](../../../specs/1174-weaver-package-witness/tasks.md#causal-corrections-retained-for-review) retain useful causal correction references.


## Current-authority context qualification (#3439)

The same immutable package run now additionally requires four native cases in
`contextDisclosureQualification`, separate from the original38case records. Both actual adapters
must receive no request/call/continuation after accepted-message read authority is withdrawn and
after an actual accepted effect's receipt becomes undisclosable. Native Identity revision/grants,
fresh same-actor authentication, the exact owner403, strict SSE cause, unchanged trace prefixes,
retained receipt equality and final independent owner readback establish these outcomes. Wrong
transport/auth/session errors cannot substitute for disclosure denial. Each stage is bounded and
never retried automatically. The original M1 cases and their recognized Failed controls remain
mandatory. This evidence is deterministic provider-protocol qualification, not a live-provider claim.

Modules link tools to their exact disclosure source and own `AdmitDisclosureAsync`; see the
[Agent catalog](../../../Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md#context-and-result-disclosure-3439-under-qualification).
Session stores must preserve complete journal provenance before a result can be sent to a provider.

## Established-session autonomy (#3440)

`qualify_autonomy.py` runs the four mode cases on each actual adapter through the same fresh
package-composed hosts. Each established conversation must change mode through exactly one actual
conditional `PUT /_elsa/agent/sessions/{id}/autonomy` carrying the revision acknowledged at creation,
receive HTTP 200 at the next revision with unchanged limits, read it back from session detail and
admit the size prompt under that revision; unchanged controls send no autonomy request. The CDP
metadata admits only that PUT and its CORS preflight. A native outcome that contradicts the
acknowledged mode is a failed case with cause `autonomy.mode_mismatch`, never an expected one; every
failed case keeps its precise browser or verification cause and the remaining cases still run once.
Success needs all eight passed. It never sets `qualified` or `independentPackageWitness`: the
complete thirteen-case matrix and package witness remain T015/T025.

```sh
python3 tools/studio/Weaver.Conformance.Probe/qualify_autonomy.py \
  --output /absolute/new-owned-autonomy-proof --version 4.0.0-weaver3440.local1
```

### Per-adapter lifecycle matrix (#3440 T015)

`--scope matrix` runs all thirteen cases from the
[mandatory native qualification inventory](../../../specs/1186-weaver-effects/contracts/session-policy.md#mandatory-native-qualification-inventory)
once on each actual adapter, through the same fresh package-composed hosts. The four mode cases run
unchanged. `lifecycle_journeys.py` drives the other nine by reusing the original driver operations:
deny, stale revision, write-denied authority, pre-call-gate cancellation and duplicate execute/delivery
with the module's size operation, the policy route while review, approval and receipt continuation are
retained, and the resource, workflow and lost-confirmation browser journeys with FullAuto selected
before the conversation exists. Each record names its adapter and observed outcome, owner readback and
native session/turn/proposal/invocation/call correlation; `require_lifecycle_matrix` refuses
omissions, duplicates, wrong-cause outcomes and missing or changed evidence. Every case runs once, a
failed case keeps its precise cause, and success needs all twenty-six passed. It never sets
`qualified` or `independentPackageWitness`: T025 owns the complete package witness.

```sh
python3 tools/studio/Weaver.Conformance.Probe/qualify_autonomy.py --scope matrix \
  --output /absolute/new-owned-autonomy-matrix --version 4.0.0-weaver3440.local2
```

### Complete package witness with the autonomy inventory (#3440 T025)

`qualify.py` is the only complete witness. In one fresh run and one owned host pair it runs the
unchanged original sequence (38 cases, then the four current-authority cases), then a preflight that
releases the original cancel's held pre-call gate, requires the one-shot CAS control to be consumed and
re-observes that the restored original package composes all three resource tools with an available
provider, then the same twenty-six case runners as `--scope matrix`. The run declares
`evidence.witness_manifest()` (38 + 4 + 26 = 68 cases plus the fourteen manifest-named matrix browser
stages); `validate_complete_witness` refuses a missing, duplicated or extra case, a wrong adapter, a
wrong-cause outcome, absent or changed owner/correlation evidence, a failed current-authority record,
an absent or repeated matrix stage, or any failed stage, on top of every original consumer
no-source/no-frontend/byte-equality and owned-host cleanup check. No total stage count (the original
run's 135) is required. A failed matrix case keeps its precise cause, the rest still run once, and
the run fails. Only that passing run sets `independentPackageWitness`. `export_evidence.py` records
`witnessCompleteness` and refuses a report that claims the witness without the complete inventory.

```sh
python3 tools/studio/Weaver.Conformance.Probe/qualify.py \
  --output /absolute/new-owned-complete-witness --version 4.0.0-weaver3440.local3
```

### Workflow authorization unavailable (spec 1193 T006)

Right after the four current-authority cases, the complete witness runs
`authorization_journeys.py` once on each actual adapter (`{copilot,anthropic}-workflow-authorization-unavailable`).
It selects the backend's test-only `QualificationWorkflowAuthority` feature through the native feature apply, which
wraps the actual workflow definition authorizer behind a private mode file. On a freshly seeded workflow, a denying
replacement must keep the existing design 403 body structurally identical (compared as parsed JSON), with no `errorCode`; `DefinitionReadAuthorizationTests` pins its exact bytes. A replacement that cannot decide
must answer 503 with `errorCode` `workflow.authorization.unavailable` and the fixed message. A pre-call gate then holds a
`workflow_read_graph` turn after its context was admitted by the actual owner, and the mode changes. The released call's
pre-dispatch admission refresh asks the same owner before the tool can, so the turn must stop there, before any
dispatch or adapter return. For the unavailable owner the stop is `workflow.authorization.unavailable` with 503 and the
engine's fixed owner-unavailable text. For the denying owner it stays `agent.context.incomplete` (400) with the
existing read-permission text. The
tool's own 503 outcome is component evidence, because no native turn reaches it. A random private cause canary must appear on no HTTP body, SSE frame or adapter
trace. Deselecting the seam must restore the unchanged native definition read. `evidence.witness_manifest()` lists
the two cases, and `require_workflow_authorization` refuses a missing, failed, duplicated or changed record.
Each admitted context there must also carry the host's available `workflow.catalog`: the consumer composes the
Activities Design API, so the catalog is the real one.

### Workflow truth seams (spec 1193 T012)

Next, `truth_seam_journeys.py` runs once on each actual adapter (`{copilot,anthropic}-workflow-truth-seams`). Four
dedicated principals are appended after the original five. A manager, a read-only reader and a reader without
`activities/design:read` in the admin's tenant run one `workflow_read_graph` turn each against the admin's seeded
workflow. A reader in a second tenant compares catalogs only: the packaged consumer's workflow persistence has one scope,
so every definition is stored under `default` whatever tenant creates it, and the owner rightly refuses another
tenant's principal. Its Weaver catalog is the collection admitted with a message over a target-free `workflow.request`,
and no stream, turn or provider call follows. The backend's test-only `QualificationTenantActivity` feature, selected through the native
feature apply for this inventory only, adds one activity to the designer's catalog read for the first tenant. For each
actor, every admitted context must carry one server-read `workflow.catalog` and the exact target's
`workflow.permissions`, identical across the turn's admissions. The catalog must equal the designer's own
`GET /design/activities/catalog?availability=all` for the same actor: the same entries, order, availability and
versions, or its marked prefix. The tenant-restricted activity must appear in exactly the first tenant's two answers.
The permissions must equal the owning design API's answers: the definition GET for read, and the manage-gated promotion
preflight (200 or 403) for manage. The tools the actual adapter is offered must follow them: the layout tool only for a
manager. Each dedicated actor holds exactly the grants its journey reaches (`truth_seam_journeys.py` lists them):
`api-capabilities:read` for owner-link discovery through `GET /capabilities`, the design read or manage it exercises (the evaluator lets manage satisfy read),
`activities/design:read` except for the catalog-denied actor, and `expressions:read` for the reader's Studio editor. The
browser journey fails with the exact paths when Studio is refused a read it depends on (owner-link discovery, the
definition and its draft, the activity catalog and expression descriptors). Other refused reads, such as runtime
executables or publishing profiles the reader is not granted, are retained as evidence only; layout absence is proven
from the adapter trace, not from what the browser shows. The actor without `activities/design:read` must get a 403 from the designer catalog and a denied
catalog with no entries from Weaver. The host's generation observation must report no placeholder type in the loaded
Agent Workflows package and exactly `HttpActivityAuthoringCatalog`. Last, the reader runs the actual packaged Studio
journey `workflow-read-only` in `browser.mjs`, with a fresh normal-login state of its own: the accepted message's
context must carry `manage: denied` and only `workflow_read_graph`, explanation must complete, and a layout request
must end without a proposal, node move, review or effect request, or owning write, with the stored draft unchanged.
The driver checks the adapter trace for each command the browser retained: the layout tool was never offered and never
called. `require_workflow_truth_seams` refuses a missing, failed, duplicated or changed record.

The tenant-restricted activity is synthesized by the seam's reader wrapper. It proves that the designer and Weaver share
one tenant-aware catalog read path for the request principal, not that the activity stores scope rows by tenant; store
tenant scoping is the Activities Design persistence tests' evidence.

### Workflow Operation Batch (spec 1193 T022)

Next, `batch_journeys.py` runs once on each actual adapter (`{copilot,anthropic}-workflow-batch`) against a nested
workflow it seeds (a root flowchart holding two Write Lines and a nested flowchart that holds a third; the packaged
consumer's catalog has no other container, and each container's payload is its catalog `initialPayload`). The backend's test-only
`QualificationBatchCatalog` feature adds a credential-input activity and a listed-but-excluded activity to the designer's
catalog read for this inventory only. Native turns, each in its own full-auto session, send the open editor's compact
structured snapshot:

- one `workflow_apply_operation_batch` call of seven operations of six kinds (an add into the nested flowchart wired
  after its own activity, so validation stays clean) must pause at exactly one review, in each adapter's own pinned
  frame order: `requiresApproval`, client working state, destructive (it disconnects), per-operation risk labels,
  the exact target by identity; it is then denied;
- six negative controls must each end at preparation, with no review, with the module's own text: an activity the
  catalog does not offer, the excluded activity, an undeclared input, a display name that matches three activities, a
  literal on the credential input (a random canary appears in no admitted context, result or refusal), and a historical
  working state;
- the truth-seam reader, without manage permission, is never offered the batch tool and never calls it.

The owner's draft is unchanged across all of them. Then the admin runs the actual packaged Studio journey
`workflow-batch` in `browser.mjs` with a fresh normal-login state. An edit after the proposal, a selection change and a
remount (SPA navigation away and back, same document) are each refused with zero change. Then a hand edit, whose
ordinary autosave settles first, is part of the base revision of the seven-operation batch, which is approved and applied:
the designer's own export, validation and owner reads show the nested add, the declared input, the display name, the
move and the rewiring, a dirty working draft, no owning write past the autosave interval, an exact restore of the base
through the review card's Undo (designer history), the whole batch again through the designer's Redo, and still no
save or publish. Last, a proposal approved and applied from a second tab of the same Studio is refused by that tab's
host before anything is applied. The driver checks the adapter trace for every command the browser retained.
`require_workflow_batch` refuses a missing, failed, duplicated or changed record; `witness_manifest()` counts the two
cases.

### Workflow explanation (spec 1193 T028)

Next, `explain_journeys.py` runs once on each actual adapter (`{copilot,anthropic}-workflow-explain`) against the same
nested shape, seeded with b's text bound to a workflow variable that does not exist, so the owner's draft validation
reports it by activity and input. With the same `QualificationBatchCatalog` seam selected, the unsaved working state adds
a JavaScript expression on the nested activity and two credential activities: one holding a credential literal (a random
canary), one bound to a visible Secret Reference. Four native read turns (`workflow_read_graph`, each in its own
`auto-read-only` session, in each adapter's own pinned frame order) select the credential literal, the invalid input,
the JavaScript expression and the Secret Reference in turn. The accepted message's admitted context
(`data.message.context`), every admission in the turn and the read result must each equal the working state as Studio's
bridge discloses it: activities and nested structure, the designer's diagnostics (the owner's validations by activity
and input), the visible Secret Reference, and the selected activity's declared properties, with the credential literal
only withheld and the server's own `interpretation: "unavailable"` on every non-literal expression (no Expression Module
registers Weaver tools). The read result addresses each activity by its scope path. A control snapshot that reports the
credential input as an ordinary literal with a value of its own is refused at admission (`agent.context.incomplete`,
400, with the module's reason) before any provider call. The owner's draft is unchanged. The packaged consumer ships no
JavaScript Expression Module and no graph activity definition: JavaScript is disclosed by type and content only, and
graph activity references are covered by the server and Studio unit tests.

Then the admin runs the actual packaged Studio journey `workflow-explain` with a fresh normal-login state: with autosave
turned off for that editor, the invalid activity is selected and read (its input's Variable expression, the unavailable
interpretation and the designer's own diagnostic for it are admitted); a hand edit moves an activity, and the next read
is attributed to a new revision with the moved position, equal to the editor's own export; nothing is saved. Last, the
canary is searched for in every retained evidence file of the case (frames, adapter traces, HTTP receipts, owner reads,
browser captures) and must appear in none. `require_workflow_explain` refuses a missing, failed, duplicated or changed
record, a missing turn or control, or a canary search that found anything; `witness_manifest()` counts the two cases
(97 in all).

### Restart inventory over the packaged durable store (#3441 T031)

After the autonomy matrix, the same complete witness appends the
[mandatory native restart inventory](../../../specs/1189-weaver-recovery/contracts/recovery.md#4-mandatory-native-restart-inventory).
The backend consumer references `Elsa.Agent.Persistence.EntityFrameworkCore` and `Elsa.Secrets` from the candidate feed, so
its common published baseline already contains them, but nothing selects them until this phase. The driver owns the backend
through `runtime.RestartableServer`, one owned origin with a separate receipt, log and empty runtime caches for every
generation. It first restarts the host on the in-memory default and requires the pending review to be lost
(`restart-without-durable-store`). Then, while no generation runs, `consumers.enable_durable_agent_store` selects
`FoundationAgentEntityFrameworkCore` on SQLite over a new database under the run's output root. `restart_journeys.py` then runs
three restarts per actual adapter: graceful, abrupt (the owned process group is killed while a write is held at the module's
native rendezvous and a provider turn is held at the pre-call gate), and after-deadline (stopped before and started after the
prepared deadline on the real clock, about five minutes). Ten cases per adapter assert the recovered outcome, the execution
count at the independent native owner and the audit `settledBy` values. A failed case keeps its precise cause and the rest
still run. The Studio host keeps running throughout; consumer no-source/no-frontend and cleanup rules are unchanged.
`witness_manifest()` is now 38 + 4 + 26 + 20 restart cases + 1 restart control = 89. `validate_complete_witness` refuses a
report without the passed restart section, and only a run that passes everything sets `independentPackageWitness`.

```sh
python3 tools/studio/Weaver.Conformance.Probe/qualify.py \
  --output /absolute/new-owned-weaver3441-proof --version 4.0.0-weaver3441.local1
```
