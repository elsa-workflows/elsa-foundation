# Elsa Agents integration research

Status: M0 research in progress under [#3529](https://github.com/elsa-workflows/elsa-foundation/issues/3529),
[Program #3519](https://github.com/elsa-workflows/elsa-foundation/issues/3519). Findings inform design;
they do not establish product delivery or freeze a public contract.

## Scope and source baseline

Source inspected at `3e93e06d9bd22763c81767ca296c8924142d030b`, 31 October 2026. Earlier source inspection
started at `ab46bc22a`; no intervening agent implementation change is assumed from that older inspection.
The owner's Elsa 3 persona/activity description is the product requirement. This research has not
independently inspected the off-limits legacy extension repository.

| Concern | Current source | Finding |
|---|---|---|
| Agent providers | [Agent contracts](../../Extensions/Agent/src/Core/Contracts/AgentContracts.cs) and [default services](../../Extensions/Agent/src/Elsa.Agent/Services/DefaultAgentServices.cs) | Substantial Weaver infrastructure; registry permits one active harness. Default sessions are in memory. This is not a user-defined specialist catalog |
| Turn lifecycle | [Turn services](../../Extensions/Agent/src/Elsa.Agent/Services/AgentTurnServices.cs) | Existing tools/proposals/cancellation are reuse candidates; default paused-turn state and foreground leases are process local |
| Real model client | [Anthropic factory](../../Extensions/Agent/src/Anthropic/Services/AnthropicChatClientFactory.cs) | Existing official SDK adapter exposes `IChatClient`; does not establish a specialist connection/resource model |
| Authoring source | [Activity-provider contracts](../../Essentials/src/Activities/Design/Core/Contracts/ReusableActivityContracts.cs) and [manifest/contract models](../../Essentials/src/Activities/Design/Core/Models/ReusableActivityModels.cs) | Generic drafts, contracts, manifests and versions are a candidate for specialist authoring; investigate reuse before adding a parallel catalog/store |
| Publication | [Template compiler](../../Essentials/src/Workflows/Publishing/Core/Contracts/IActivityTemplateProviderCompiler.cs) and [Graph provider](../../Essentials/src/Activities/Graph/Design/Services/GraphActivityProvider.cs) | Authoring validation and publishing compilation have distinct contracts; compilation has a stable fingerprint and resolved dependencies. A specialist needs a runtime descriptor/consumer, not runtime reads of a mutable agent draft |
| Connections | [Connection resolver](../../Essentials/src/Connections/Core/Contracts/IConnectionResolver.cs) and [type provider](../../Essentials/src/Connections/Core/Contracts/IConnectionTypeProvider.cs) | Resolve at point of use with tenant/owner context. Add model connection capabilities through owning contracts; do not embed credentials in agent manifests or compiled artifacts |
| BPMN authoring | [Adapter](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/bpmnAdapter.ts) and [element inspector](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/BpmnElementInspector.tsx) | BPMN elements already bind exact child activity versions. A named specialist can use the same binding path; a new BPMN agent token model is unnecessary |
| BPMN interchange | [Catalog binding](../../Essentials/src/Activities/Bpmn/Interchange/Services/BpmnCatalogBindingSession.cs) | Imports resolve against the actual visible catalog. Successful in-editor binding does not establish XML interchange support |
| Existing collaboration | [Reviewed #2913 witness](unified-execution/witness-review-2026-10-17.md) and [#2912](https://github.com/elsa-workflows/elsa-foundation/issues/2912) | Historical bounded native and MAF evidence exists. Production decisions, scopes, approval/effect identities, budgets and recovery remain on the owning #2892 hierarchy |

These are source findings. The new specialist publication/runtime/BPMN journey and independently
packaged specialist contribution have not been executed by this spike.

## Pinned Microsoft integration

The executable experiment uses `Microsoft.Agents.AI` and `Microsoft.Agents.AI.Workflows` **1.27.0**,
with a complete transitive package lock. Package source tag `dotnet-1.27.0` resolves to
[`4d1674b78951d6fd127ada532660514637f906e9`](https://github.com/microsoft/agent-framework/tree/4d1674b78951d6fd127ada532660514637f906e9).
Package metadata declares MIT. This is actual package execution, distinct from reading the latest docs.

The package graph requires `Microsoft.Extensions.AI.Abstractions` 10.10.1 and `Microsoft.Extensions.AI`
10.10.0. Foundation currently pins AI abstractions 10.5.1. The experiment isolates its dependencies in
an owned temporary project; it does not upgrade Foundation. Shared-host package resolution, adapter
compatibility and the production version selection remain open. An isolated successful run cannot
prove all Foundation composition paths will resolve that dependency graph safely.

The additional source-project compatibility mode activates the current Anthropic and GitHub Copilot
features in separate real CShells compositions, resolves their Agent contracts, and constructs the
existing Anthropic `IChatClient` alongside MAF 1.27.0. The resolved abstractions assembly is 10.10.0.0
(package 10.10.1). That bounded composition passes without changing central package pins or making
provider requests. It is not an independently packaged consumer, a full Workbench host, or a completed
existing-provider regression suite. An initial `/tmp` versus `/private/tmp` project-reference path
failure was corrected by using a physical temporary path; no production source fix was required.

Primary product references, consulted 31 October:

- [Agent Framework overview](https://learn.microsoft.com/en-us/agent-framework/overview/): agents,
  model/tool/context integrations and workflow orchestration are separate building blocks.
- [Structured outputs](https://learn.microsoft.com/en-us/agent-framework/agents/structured-outputs):
  support depends on agent/client capabilities; raw JSON schema is relevant to user-defined contracts.
- [Agent sessions](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/conversations/session):
  session serialization/continuation is distinct from application-owned access and persistence.
- [Foundry overview](https://learn.microsoft.com/en-us/azure/foundry/agents/overview): configuration,
  testing, lifecycle and operations inspire the Elsa experience. No Foundry service interoperability is
  claimed by this local experiment.

## Executable observations

Run [the isolated probe](../../tools/agents/research/README.md):

```bash
bash tools/agents/research/run.sh
bash tools/agents/research/run.sh --foundation-compatibility
```

The first process exercises the actual agent/workflow SDK against deterministic `IChatClient`
fixtures. The second process restores a session written by the first. Neither process calls a real
model service. Result JSON explicitly records that boundary.

| Observation | Meaning for Elsa |
|---|---|
| Dynamic JSON response schema and model selection reach the client; structured output and usage return | Reuse the SDK request/result path, then perform Elsa contract validation and capability admission |
| An intentionally wrong-shaped JSON result is returned through the raw response API | Requesting a schema does not validate a provider's raw output; invalid results must not drive a business branch |
| Configured function is invoked once and its call identity/result reach the next model request | Function execution is available; Elsa must wrap admission/authorization/effect accounting before granting a tool |
| Approval-required function pauses before the effect; approved continuation works after session recreation; denial produces no effect | Use SDK approval transport where useful, with Elsa-owned exact-request authority and durable approval/effect records |
| Fresh sessions isolate prior messages; serialized sessions restore previous messages in a recreated agent and a separate process | Explicit continuity is feasible; retention, concurrency and authoritative ownership still belong to Elsa |
| A differently named/instructed agent accepts the serialized session format | SDK serialization is not Elsa tenant/actor/agent-version authorization; validate an application-owned conversation handle first |
| Cancellation reaches an already waiting fixture client; streaming text reaches the caller | Supported cancellation/streaming primitives exist; this is not proof of remote provider termination or recovery |
| Two fixture-backed agents execute through the real workflow SDK, with both results available at fan-in delivery completion | MAF is a real collaboration candidate. This proves the bounded result cohort, not parallel overlap, distributed durability or Elsa integration |

The fan-in experiment initially used a `List<string>` receiver for individual string messages and
produced no aggregate output. Inspection of the pinned API/source showed that the barrier releases
messages individually after source membership is satisfied. The corrected receiver accumulates them
and emits at `OnMessageDeliveryFinishedAsync`. Preserve that distinction when designing typed rounds;
an edge does not automatically convert messages into a list-valued payload.

The continuity negative control deliberately creates a fresh session in place of restoring state:

```bash
bash tools/agents/research/run.sh --drop-continuity
```

It must fail at `restored session sends previous user message`. This verifies the probe observes
history continuity rather than only a successful SDK response. A separate-process restore still does
not establish an atomic Elsa checkpoint or crash-after-external-effect recovery.

## Recommendations for M1

1. Prefer a narrow MAF-backed specialist runner. Keep Foundation/Studio contracts free of SDK runtime
   types and select the production dependency graph only after a real composed-host compatibility probe.
2. Investigate using existing reusable-activity manifests/drafts/publication as the specialist source
   of truth before introducing a second definition/version database. A dedicated Agents screen can
   specialize that resource without duplicating its lifecycle. Confirm domain/permission semantics
   and the separately packaged provider/compiler/runtime-consumer witness before adopting this design.
3. Put immutable instructions, schemas, supported execution settings and exact resource references in
   compiled runtime material. Resolve authorized connection/knowledge resources at point of use under
   explicit policies. Runtime must not query an agent design draft to reconstruct executable behavior.
4. Keep fresh execution as the default. Optional continuity uses an Elsa-owned opaque handle carrying
   tenant, actor/authority, agent version, serialization format and concurrency revision. Persist SDK
   session JSON in protected runtime storage, separately from reusable published artifacts.
5. Validate structured results before committing accepted output and continuing the parent. Define
   accepted-result, approval, tool-dispatch and uncertain-outcome boundaries before effectful execution.
6. Start teams as explicit native structural composition over specialists, consistent with #2913.
   Evaluate a bounded MAF workflow adapter only where it earns its extra checkpoint/retry boundary.
   Do not run two owners independently replaying the same model decision or tool effect. The current
   MAF fan-in probe does not settle production hosting or change #2912's authority.
7. Borrow Foundry's create/test/evaluate/publish/inspect lifecycle, implemented through Elsa's shared
   Studio patterns. Model capability differences must be visible; unsupported settings cannot silently
   become effective configuration. Wireframes and usability evidence remain M1/M5 work.

These are candidate recommendations for review, not accepted architecture decisions.

## Remaining M0 gates

- Normal packaged/custom-host dependency and compatibility qualification, plus the existing-provider
  behavioral regression suites. The bounded source-project CShells composition above is only partial evidence.
- Smallest specialist publication/artifact/runtime and BPMN-binding executable witness, with exact
  version resolution and no runtime Design dependency; source navigation alone does not close it.
- Explicit same-conversation concurrency and ambiguous provider-result/recovery outcomes, including
  resource/usage accounting. SDK serialization and local function approval are only parts of that proof.
- MAF checkpoint-storage/restore boundary if selecting its workflow runtime for production teams;
  otherwise record the native-composite choice and why a second runtime is unnecessary.
- Foundry-inspired Elsa authoring concepts and capability/settings inventory. Detailed wireframes and
  their design review belong to M1.

Live provider smoke needs an authorized connection and remains separate evidence. No model purchase,
live tool side effect, production recovery guarantee or full Program acceptance is claimed here.
