# Elsa Agents: publication and lifecycle research

Status: M0 research under [#3533](https://github.com/elsa-workflows/elsa-foundation/issues/3533),
within [Program #3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519).
Recommendations are design inputs for M1, not accepted production architecture or product acceptance.
The [SDK/source study](elsa-agents-research.md) owns the pinned framework compatibility evidence.

## Runnable witness and evidence boundary

The [qualifier](../../tools/agents/publication/README.md) builds a private snapshot of committed
product source and consumes its actual NuGet packages from separate author/runtime projects.
The fixture source hashes are recorded separately from that product commit. Nothing changes
Foundation's central package versions, publishes remotely or invokes an external model.

| Boundary | Probe | Claim supported |
|---|---|---|
| Extension ownership | Separate `Acme.Specialists.Design` and `.Runtime` packages; no consumer project references | A specialist provider/compiler can use the existing stable contracts; the runtime package needs no Design dependency |
| Publication | Actual reviewed publisher, generic compiler and EF command across three private SQLite databases | Exact immutable specialist version/template and idempotent reviewed publication |
| Capability admission | Missing runtime consumer and missing JSON storage controls | Publication refuses with the corresponding diagnostic before creating a publication or Runtime template |
| Candidate change | Change agent version/instructions and recompile; reload earlier template | Behavior hash changes while the earlier immutable version remains unchanged |
| Execution | Separate native and BPMN executable consumers, actual Elsa actor/scheduler and actual MAF adapter | Published input/model/instructions/schema reach the client; validated JSON is committed; duplicate start does not repeat model work |
| Result refusal | Provider fixture returns `wrong` instead of `article` | Workflow faults and accepts no result; raw MAF text is not automatically a valid Elsa result |
| Conversation | Private SQLite CAS sketch plus real MAF session restoration | Exact scope/revision admission, accepted session/result atomicity, stale completion fences and explicit uncertainty policy can surround an SDK session |

The model client is deterministic. The small output contract is exactly one required string
`article`; this is not a general JSON-schema validator. Consumer fixtures construct executable
nodes and BPMN structure directly; they do not prove Studio activity selection, workflow publishing,
BPMN XML interchange, ordinary hosted CShells/Nuplane composition or the supported database matrix.
Runtime stores in the consumer are in-memory despite using Elsa's Durable persistence policy;
no restart durability follows from that policy setting. The publication stores are real SQLite files.
The lifecycle CAS store is an experiment outside Elsa's production checkpoint implementation.

The strict consumer dependency control exposed an existing package gap: `Elsa.Activities.Bpmn`
depends on `Elsa.Workflows.Design.Core`, which brings in `Elsa.Activities.Design.Core`.
The specialist runtime package and native consumer are Design-free; the separate BPMN consumer
has those two contract assemblies. It performs no Design-store lookup, but a fully Design-free BPMN
package closure is unproved. Preserve this finding as `ObservedUnresolved`, owned by M1's architecture
packet and M4's runtime/authoring package separation. The research does not repair it by filtering
package contents, bypassing normal restore or redefining the requirement. Program acceptance remains open.

The research disposition is to accept the bounded extension evidence and revise BPMN packaging
through M1/M4. [#3533](https://github.com/elsa-workflows/elsa-foundation/issues/3533) now distinguishes
the specialist/native package exit from the observed BPMN closure; M1 explicitly owns the split
before production contract freeze, and M4 owns its implementation and package-consumer qualification.
This is a research handoff, not a production boundary exception or a completed product criterion.

## Architecture recommendations

Adopt the generic reusable activity lifecycle for specialist authoring, publication, exact version
selection and retirement. A specialist manifest contains prompt/model/schema/tool policy facts;
the provider/compiler owns validation and lowering. Publishing owns catalog versions, behavioral
hashes, dependencies, review tokens and commits. Avoid a competing specialist publication database.

Keep MAF inside a runtime adapter built on `IChatClient`; keep SDK objects out of persisted Elsa
artifacts. The published descriptor pins behavior and runtime capabilities. Resolve an authorized
model connection at point of use using the existing Connections boundary. Credentials never become
prompt/descriptor fields. Provider/model capability discovery needs its own authoritative descriptor;
`IChatClient` availability alone is insufficient evidence that a particular setting is supported.

Fresh execution remains the default. Continuing a conversation requires an explicit scoped
reference, an exact agent version and an expected revision. Derive tenant/owner authority from trusted
runtime context, rather than accepting it from model text or a caller-authored session payload.
Admit one turn per conversation revision; serialize or refuse overlap predictably. Persist accepted
result and accepted session together. A cancelled or superseded attempt cannot replace that pair.
Define reopening/forking and version migration in M1; the prototype deliberately does not choose them.

An accepted result is Elsa-owned durable execution state. A response lost before acceptance is
`OutcomeUnknown`, not completed work or proof of no effect. Production recovery must distinguish
resuming a provider operation with an authoritative continuation identity from issuing new work.
Generic transport retries cannot establish exactly-once model/tool effects. Keep production decision,
approval, effect, budget and recovery work coordinated with [#2912](https://github.com/elsa-workflows/elsa-foundation/issues/2912).

## Team execution ownership

Recommend native structural composition over specialist invocations for the first bounded team
patterns. Elsa owns child scheduling, parent continuation, checkpointing, cancellation and retries;
MAF owns one admitted agent turn and its in-call model/tool protocol. This witness proves the same
specialist can sit under Elsa BPMN, while the [reviewed #2913 research](unified-execution/witness-review-2026-10-17.md#agent-collaboration-2913)
provides historical bounded evidence for barriers, feedback and accepted-decision/effect recovery.
That historical witness does not establish production hosting or live token budgets.

MAF Workflows is an optional future adapter if a concrete team semantics requirement warrants it.
Do not wrap an independently retrying MAF workflow inside an independently retrying Elsa activity.
Before adopting that adapter, specify one authoritative checkpoint/dispatch boundary and prove
crash/cancel/replay at that boundary. Microsoft's [checkpoint documentation](https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints)
describes superstep snapshots; that does not prove atomicity with Elsa checkpoints or external effects.
No MAF workflow persistence claim is made by this experiment, and no second engine is required for
the specialist product. Preserve #2912's production ownership and prerequisites.

## Model connection and settings inventory

| Existing surface | Current scope | M1 decision input |
|---|---|---|
| `Extensions/Agent` Anthropic adapter | Weaver assistant provider; official SDK `AsIChatClient(model)` factory, model and maximum output tokens | Reuse the SDK integration seam where justified; keep specialist configuration distinct from the single active Studio assistant provider |
| `Extensions/Agent` Copilot adapter | Assistant model/effort, streaming, session/context/tools and provider-specific authentication | Do not infer arbitrary JSON-schema/model-setting support from assistant capability flags |
| Connections `IConnectionResolver` | Tenant/owner scoped, uncached point-of-use resolution; v1 tenant owner | Reuse its credential and host policy boundary; add specialist model connection types through that module's extension points |
| MEAI/MAF options | Schema, model selection, tool protocol, usage, streaming and cancellation have bounded SDK probe evidence | Validate the selected provider/model's actual capabilities before publication/test; a forwarded option is not a provider guarantee |
| Temperature/top-p/effort/output limits | Provider/model-dependent settings; existing adapters expose different subsets | Show supported settings, ranges and defaults only; reject stale or unsupported saved values with actionable diagnostics |

The minimum capability record should distinguish structured JSON/schema mode, tools/approval,
streaming, cancellation/continuation, reasoning effort, sampling controls, output/context limits,
modalities and usage reporting. Values require provider-owned discovery or an explicit verified
configuration; unknown means unknown, not supported. Bind the capability snapshot/revision used for
test/publish and resolve operational connection authority at execution. M1 must choose how changes
in endpoint capability invalidate a configuration without rewriting an existing artifact.

## Foundry-inspired authoring concepts

Microsoft's [agent development lifecycle](https://learn.microsoft.com/en-in/azure/foundry/agents/concepts/development-lifecycle?view=foundry)
separates playground iteration, versioning, tools/data, tracing, evaluation and publishing. Borrow
that discoverable progression: create specialist → configure model → define JSON I/O → test with a
fresh or explicit continuing conversation → save evaluation cases → compare version results →
review/publish → use in workflows → inspect linked executions. The specialist designer should make
continuity visible independently of knowledge or memory. Unsupported options should be explained
at the relevant field, and tests should display the exact candidate/version and conversation scope.

Keep Elsa's published specialist as a native activity with normal workflow/BPMN bindings and
inspection. Foundry inspiration does not imply a mandatory Azure project, an opaque remote agent
definition as Elsa's source of truth, or a new free-form team editor. M1 owns the actual journeys,
wireframes, error states, accessibility and reviewed architecture packet; M2–M7 own product delivery.

## Handoff and remaining product gates

M1 must resolve the detailed descriptor/result-schema contract, connection capability authority,
conversation fork/reopen/version rules, accepted-result/recovery integration and bounded team
patterns. The research establishes feasible extension boundaries, not frozen public APIs.
M2–M4 must implement those choices through actual authoring/publishing and workflow binding paths.
M5 owns the real Studio/evaluation journeys. M6 respects #2912 for tools/knowledge/team recovery.
M7 must prove normal packaged/custom hosts, supported stores/providers, authorized live-model
execution, process recovery, exact-head review/CI and resulting-main qualification. These remain
open on the Program's finite acceptance matrix.
