# Weaver integration research

**Feature:** [#3604](https://github.com/elsa-workflows/elsa-foundation/issues/3604).
**Observed:** 6 November 2026, source baseline `e0ba87ecfc3b1e774c509ed274b19cd3f34a1124`.
**Status:** Historical source inventory at the baseline above. Superseding 6 November owner decisions select one model-agnostic Dashboard author over Weaver, the existing Copilot SDK adapter and backend options/configuration. The current [implementation contract](contracts/weaver.md) governs; the historical alternatives/gaps below do not require a Connections integration. No live-provider qualification is established by research.

## Existing ownership and delivery boundary

[Weaver Across Studio](../../docs/program-goals/weaver-across-studio.md), [Studio terminology](../../Studio/CONTEXT.md) and the [resource-agnostic decision](../../Studio/docs/adr/0003-make-weaver-tools-and-proposals-resource-agnostic.md) govern the integration. Weaver owns generic orchestration, discovery, transport, policy, review, cancellation and audit. Participating modules own resource identity, context, installed guidance, tools, validation, rendering and application semantics. Provider adapters transport module payloads opaquely.

| Owning record | Observed scope and state | Consequence for #3604 |
| --- | --- | --- |
| [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428) | M0–M3 generic contribution, context, effects/recovery and workflow delivery are recorded in the program document; broader product acceptance remains open. | Consume delivered contracts; this feature does not claim broader Weaver completion. |
| [Module coverage #3443](https://github.com/elsa-workflows/elsa-foundation/issues/3443) and [spec1203 FR001](../1203-weaver-module-support/spec.md) | Current Home/widget explanation and supported personal layout changes belong to M4. New widgets and saved queries are explicitly excluded to #3604. | Preserve existing hierarchy and claims. Do not turn Home inventory work into saved-question delivery or reparent it. |
| [Generic leaf #3831](https://github.com/elsa-workflows/elsa-foundation/issues/3831) and [draft PR #3845](https://github.com/elsa-workflows/elsa-foundation/pull/3845) | [Latest inspected checkpoint](https://github.com/elsa-workflows/elsa-foundation/issues/3443#issuecomment-6670655283) retains one active M4 leaf, published `209a`, additional unpublished persistence work and open acceptance gates. | Pending generic effects/live-support changes are not current-main behavior or frozen contracts. Coordinate a concrete dependency if one becomes necessary. Do not implement the same generic mechanism independently here. |

The recommended read-only preview plus ordinary native Save path uses delivered main contracts and does not require M4's proposed effect outcome for an approved operation that persists nothing. Issue and PR state must be refreshed before implementation or integration claims depend on it.

## Current extension paths

The [Agent extension catalog](../../Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md) is the navigation authority; the linked source files own exact signatures.

| Need | Existing source | Application to dashboards |
| --- | --- | --- |
| Add module context and current disclosure checks | [`IAgentContextProvider`](../../Extensions/Agent/src/Core/Contracts/AgentContracts.cs), [`AgentContextDisclosureRequest`](../../Extensions/Agent/src/Core/Models/AgentContextDisclosureRequest.cs) | Collect bounded exact dashboard/question references and drafts. Admit retained context/results under current owning-domain authority before another provider disclosure. |
| Add tools and module authorization | [`IAgentTool`, `IAgentToolAuthorization`, descriptors and invocation contracts](../../Extensions/Agent/src/Core/Contracts/AgentToolContracts.cs) | Discover supported datasets and operations; validate and preview composed questions through owning native capabilities. |
| Preserve resource and payload identity | [`AgentResourceTarget`, `AgentPayload`, preparation and receipt models](../../Extensions/Agent/src/Core/Models/AgentContributionModels.cs) | Use stable module/resource/identity/revision bindings. Version the module-owned query/result payloads. Never infer persistence from text. |
| Register contributions | [Workflow registration example](../../Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/Extensions/WorkflowAgentServiceCollectionExtensions.cs), [Runtime example](../../Extensions/Agent/src/Workflows/Runtime/Extensions/WorkflowRuntimeAgentServiceCollectionExtensions.cs) | Add scoped context, capability and tool implementations through `TryAddEnumerable`. Reuse the existing named collection handlers rather than adding another registry or dispatch loop. |
| Enrich the Studio assistant | [SDK Weaver contracts](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/index.ts), [SDK guide](../../Studio/src/essentials/Elsa.Studio.Hosting/Client/src/sdk/README.md) | Register `api.agent` context providers, prompt actions/starters, capabilities, tool slots/contracts, result renderers and optional result controls. Keep routes and permissions scoped to participating surfaces. |
| Apply a reviewed browser draft | [Workflow contribution example](../../Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflowAgent.tsx) | If used, bind `executeClient` to the originating editor generation and exact prepared revision, report an observed outcome and acknowledge through the backend. Navigation cannot retarget an operation. |
| Render module results | [Weaver Workflows adapter example](../../Extensions/Weaver/studio/Elsa.Studio.Weaver.Workflows/Client/src/module.tsx) | Render validated question/result payloads and explicit module controls in the host review/result shell. Use `resourceModuleIds` when an optional integration package renders another module's resource. |

The implementation should follow framework §§2.6.1–2.6.4 and §2.14: additive contribution through the existing named collection lifecycle, explicit replacement contracts where required, separate discovery and execution concerns, and optional integration packages when their consumption shape warrants them. Do not place dataset-specific branches in the assistant, host or provider adapter. The owning modules must remain usable without Weaver.

## Proposed ask, preview and save flow

1. The Dashboard/question editor contributes its exact surface, draft identity/revision, selected dataset references and explicit user selections. Avoid unrelated dashboard results or hidden rows. Installed backend guidance supplies supported query semantics; user-authored titles, questions and source values remain untrusted content.
2. Dataset-owning modules expose bounded authorized discovery and validation/execution through the native query contract. The Dashboard integration composes these capabilities; it does not invent metric formulas, queryable variables, history coverage or relationships.
3. Discovery and preview are read-only Weaver tools. The model composes a supported query and presentation definition; native validation checks every identifier, operation, parameter, bound and effective permission before execution. Unsupported questions and ambiguous metric/time meanings remain actionable clarification or unavailability outcomes.
4. The preview tool returns a typed validated query/presentation, human-readable interpretation, filters, metric/time/coverage provenance and bounded actual result. Module rendering presents those facts. Optional model explanation must not invent a result, history or causal conclusion.
5. A module-owned explicit user control opens/uses the proposal in the ordinary editor. That user action performs native editing and does not imply Save. If AI directly changes existing browser working state, use the delivered reviewed `ClientWorkingState` path instead, with target/generation/revision checks and observed acknowledgement.
6. Ordinary controls edit supported filters, groups, sort, time, parameters and presentation. Explicit native Save validates again and persists through the owning dashboard domain API with its concurrency and ownership checks. The initial design need not provide an AI persistence tool.
7. Saved definitions refresh through the native query API. Reopen, refresh, ordinary editing, drill-down and supported export have no dependency on model availability or conversation state. Provider failure preserves draft work and gives an actionable retry without converting missing results into zero.

The contracts above are existing mechanisms, not proposed names for final #3604 APIs. Tool names, payload versions and package identities require the feature's design/plan review. If direct AI persistence is later required, use a mutating server proposal with explicit review, expected revision, current owner authorization and independent persisted readback; do not infer success from generated text, status or successful dispatch.

## Retained evidence and current authority

[`IAgentContextProvider.AdmitDisclosureAsync`](../../Extensions/Agent/src/Core/Contracts/AgentContracts.cs) defaults to refusal. A new context source must implement it deliberately. Returning success authorizes the supplied historical context/results unchanged; returning fresh narrower attachments does not erase broader evidence already in the retained provider transcript.

For dashboard queries, this requires more than confirming that the definition is shared or that a query can run now:

- Recheck the current authenticated session owner/tenant, definition visibility, dataset availability, field visibility, record scope and aggregate disclosure rules. A shared definition never grants the author's data access.
- Retain bounded module-owned authorization provenance sufficient to verify the original disclosed result. Dataset owners must be able to decide whether that exact evidence remains admissible after access changes. If they cannot establish this, refuse redisclosure and require a fresh query/session as appropriate.
- A new narrower query result cannot authorize an older broader aggregate. A changed permission version or cache key alone is not proof that historical evidence remains safe.
- Re-admission performs no effects and never retries a Save. Preserve the distinction between historical evidence and a new result with a new read time.
- Module removal, unsupported schema versions, revoked access and inability to decide must fail meaningfully. Authority refusal cannot be downgraded to an optional warning; inability to decide cannot become an empty successful result.
- Result rendering and ordinary refresh must also enforce current access through their native APIs. Weaver admission is an additional provider-disclosure boundary, not the data authorization boundary.

The existing [workflow Run context source](../../Extensions/Agent/src/Workflows/Runtime/Services/WorkflowRunContextProvider.cs) and [read tool](../../Extensions/Agent/src/Workflows/Runtime/Tools/WorkflowReadRunTool.cs) illustrate current owner reads, bounded results, unavailable reasons and refusal when retained evidence exceeds current access. They do not establish a dashboard-wide aggregate authorization solution.

## Provider and Connections gap

No provider is selected by this research.

| Existing capability | Current behavior | Limit |
| --- | --- | --- |
| [GitHub Copilot Weaver provider](../../Extensions/Agent/src/GitHubCopilot/README.md) | Provider-neutral Agent API with delegated SDK loop; backend token/environment, explicitly configured local login or external runtime. | Does not resolve Elsa Connections/Secrets. Local login availability is not assumed. |
| [Anthropic Weaver provider](../../Extensions/Agent/src/Anthropic/README.md) | Provider-neutral Agent API with host-driven step loop; backend API key/configuration/environment. | Does not resolve Elsa Connections/Secrets. Credentials and actual usable model access are not established here. |
| [Specialist model Connections integration](../../Extensions/Agent/src/Specialists/Connections/README.md) | Separate optional Anthropic, OpenAI and Azure OpenAI specialist packages use existing Connections/Secrets with endpoint, model, capability, host and transport checks. | Its documented features do **not** activate Weaver or select its harness. It is not an existing connection-backed Weaver provider. |

Use exactly one active Weaver provider and preserve provider-neutral Studio APIs. A connection-backed Weaver integration is real scoped work or a concrete dependency to coordinate, not an already delivered capability. Extending the selected adapter's operational configuration/credential acquisition through [`IConnectionResolver`](../../Essentials/src/Connections/Core/Contracts/IConnectionResolver.cs) and short-lived [`IResolvedConnection`](../../Essentials/src/Connections/Core/Contracts/IResolvedConnection.cs) is a candidate, subject to design review. Preserve exact tenant scope, connection type, creation identity/revision, endpoint/allowed-host policy and credential-change/revocation checks at the transport boundary. Resolve secrets only in the backend; do not place them in browser configuration, question definitions, context, results, logs or evidence.

Do not reuse specialist orchestration as a second dashboard conversation engine. Do not assume every connection type or existing provider auth mode supports the chosen credential profile. Provider choice, usable access and any necessary operational integration must be settled before live qualification. No credential inspection, setup, provider invocation or purchase is performed by this research.

## Qualification plan and evidence classes

The [existing conformance probe](../../tools/studio/Weaver.Conformance.Probe/README.md) demonstrates separately packaged resources, context, tools, renderers, native persistence/readback and add/remove transitions through both actual provider adapters. Its [Acme Home backend](../../tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Backend/README.md) and [Studio package](../../tools/studio/Weaver.Conformance.Probe/fixtures/Resource.Studio/README.md) are useful extension patterns. They are qualification fixtures and do not deliver saved queries or prove live model behavior.

Keep these evidence classes separate:

| Evidence | Required #3604 observation | What it does not prove |
| --- | --- | --- |
| Deterministic component tests | Validation, metric/result oracle, bounds, normal editing, provider-unavailable behavior, current authorization and meaningful failures. | Natural-language model interpretation or actual provider access. |
| Deterministic provider-protocol tests | Real generic engine and actual adapter handle contributed schemas, preview payloads, clarification, cancellation and expected results with controlled provider responses. | A live model chose the right query. |
| Composed native Studio journey | Ask, clarify/refine, preview, ordinary edit, explicit Save, arrange, reopen, refresh, share and drill-down in the actual host, with inspected current-commit visuals/traces and native persisted/result readback. | Live-provider behavior if the provider response is deterministic. |
| Independent module witness | Separate package contributes a new real dataset/business dimension through the same reviewed contracts; no dataset-specific changes to Weaver core, host or adapter. Numerical results match an independently checked oracle. | Arbitrary external database support or automatically queryable workflow variables. |
| Actual supported provider | Real configured provider completes a novel supported question and refinement, invokes the installed tools, previews measured results and leads to explicit native Save. Retain redacted provider/model/configuration identity, request/tool correlation and observed outcomes. | Broader provider/model support beyond the tested configuration. |
| Provider-free refresh | After Save, disable/unavailable the provider, reopen/edit/refresh the saved question, verify native results and prove no additional model request occurred. Count the chosen adapter/transport requests, not only UI prompts. | Authorization isolation unless different users/tenants and access changes are also exercised. |

The acceptance matrix must also cover unauthorized/sharing-revoked viewers, field/record and aggregate restrictions, tenant isolation, no privileged result-cache reuse, redisclosure after permission changes, unavailable modules/schemas, empty versus zero, incomplete/stale/failed results, cancellation and bounded query cost. Module/provider changes must not silently substitute a source, metric or query meaning.

Current-main composed/module/provider evidence remains source-bound to its owning records. A fixture pass, an accepted schema, a draft CI pass or an approved plan must never be reported as #3604 delivery. Required review/checks, merge and resulting-main qualification remain open until performed for the actual implementation.
