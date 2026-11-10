# Package and identifier renames

The pre-GA clean breaks that renamed published packages or persisted identifiers, with the old-to-new map a consumer needs. Each entry names the decision that authorised it. Renamed package ids start again at the current version line (`line.major.minor.0`); the old ids stay frozen on the preview feed and are never republished ([versioning README](../../tools/versioning/README.md)).

## 2026-11-09: Weaver leaves the `Elsa.Agent` root (stage 1 of the agent rename)

Decision: [ADR 0094 amendment](../adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md#amendment--2026-11-09-accepted), plan and inventory on [#4027](https://github.com/elsa-workflows/elsa-foundation/issues/4027). Stages 1b, 2 and 3 are the entries below.

| Kind | Was | Is |
|---|---|---|
| Packages | `Elsa.Agent`, `Elsa.Agent.Core`, `Elsa.Agent.Api`, `Elsa.Agent.Anthropic`, `Elsa.Agent.GitHubCopilot`, `Elsa.Agent.Persistence.EntityFrameworkCore`, `Elsa.Agent.Workflows`, `Elsa.Agent.Workflows.Runtime` | `Elsa.Weaver`, `Elsa.Weaver.Core`, `Elsa.Weaver.Api`, `Elsa.Weaver.Anthropic`, `Elsa.Weaver.GitHubCopilot`, `Elsa.Weaver.Persistence.EntityFrameworkCore`, `Elsa.Weaver.Workflows`, `Elsa.Weaver.Workflows.Runtime` (first published as 4.0.0-preview) |
| Source folders | `Extensions/Agent/src/{Elsa.Agent,Core,Api,Anthropic,GitHubCopilot,Persistence,Workflows}`, `Extensions/Agent/tests/{Tests,Persistence,Workflows}` | `Extensions/Weaver/src/...`, `Extensions/Weaver/tests/...` |
| Namespaces | `Elsa.Agent.*` (not `Elsa.Agent.Specialists.*`) | `Elsa.Weaver.*` |
| Shell feature keys (`shells.json`) | `FoundationAgentAbstractions`, `FoundationAgentApi`, `AnthropicAgent`, `GitHubCopilotAgent`, `FoundationAgentEntityFrameworkCore`, `FoundationWorkflowsAgent`, `FoundationWorkflowsRuntimeAgent` | `Weaver`, `WeaverApi`, `WeaverAnthropic`, `WeaverGitHubCopilot`, `WeaverEntityFrameworkCore`, `WeaverWorkflows`, `WeaverWorkflowsRuntime` |
| Configuration sections | `Elsa:Agent:Anthropic`, `Elsa:Agent:GitHubCopilot` | `Elsa:Weaver:Anthropic`, `Elsa:Weaver:GitHubCopilot` |
| REST routes | `/_elsa/agent/...` | `/_elsa/weaver/...` |
| API capability | `elsa.api.agent` (`agent-bootstrap`, `agent-sessions`) | `elsa.api.weaver` (`weaver-bootstrap`, `weaver-sessions`) |
| Permissions | `agent:use`, `agent/proposals:manage`, `agent/audit:read` | `weaver:use`, `weaver/proposals:manage`, `weaver/audit:read` |
| Error codes | `agent.*` (182 codes, including `agent.policyDenied`) | `weaver.*` |
| EF module | module `Agent`, history `ElsaAgent`, tables `elsa_agent_sessions`, `elsa_agent_messages`, `elsa_agent_disclosure_snapshots`, `elsa_agent_context`, `elsa_agent_proposals`, `elsa_agent_call_keys`, `elsa_agent_audit`, `elsa_agent_audit_owners`, `elsa_agent_audit_sequence` | module `Weaver`, history `ElsaWeaver`, `elsa_weaver_*`; existing Weaver databases are recreated, not migrated |
| Persisted codecs | `elsa.agent.{session,paused-turn,message,snapshot,context,proposal,audit,pending-audits}/1`, `agent.operation-outcome` | `elsa.weaver.*/1`, `weaver.operation-outcome` |
| Endpoint names | `ElsaAgentApiEndpoints*` | `ElsaWeaverApiEndpoints*` |
| Glossary | Agent run, Agent harness provider | Weaver run, Weaver harness provider, Weaver session |

Unchanged in stage 1: C# type names inside the Weaver layer (`AgentSession`, `IAgentProvider`, ...) and the Studio module SDK surface `api.agent` (both renamed in stage 1b, below), the `AgentSource*` types, `agent/definitions` permissions, `AgentSourcesEntityFrameworkCore` and `elsa_agent_sources_*` tables (they belong to user-defined agents), and every `Elsa.Agent.Specialists.*` package.

## 2026-11-10: Weaver types and the Studio SDK surface take the Weaver name (stage 1b of the agent rename)

Decision: Sipke Schoorstra on 2026-11-10, recorded on [#4027](https://github.com/elsa-workflows/elsa-foundation/issues/4027): the Weaver layer's own types stop saying "agent", so `Agent*` in code always means a user-defined agent. Source and SDK names only; nothing persisted or on the wire changes. It landed after stage 2, so the `Agent*` names below are the Weaver layer's, not the user-defined agent layer's.

| Kind | Was | Is |
|---|---|---|
| C# type names in `Elsa.Weaver.*` | `Agent*`, `IAgent*`, `*Agent*` (`AgentSession`, `IAgentProvider`, `AgentToolInvocation`, `AgentPolicy`, `AgentPermissionContributor`, `WorkflowAgentSnapshot`, `EfAgentSessionRecordStore`, `AgentRecordCodec`, ...; 361 types, plus the Workbench test class `WorkbenchAgentDurableStoreTests`) | `Weaver*` in the same position (`WeaverSession`, `IWeaverProvider`, `WeaverToolInvocation`, `WeaverPolicy`, `WeaverPermissionContributor`, `WorkflowWeaverSnapshot`, `EfWeaverSessionRecordStore`, `WeaverRecordCodec`, ...; `WorkbenchWeaverDurableStoreTests`) |
| Registration and selection methods | `AddAgentStores`, `AddAgentModel`, `AddAgentEntityFrameworkCore`, `MapAgentApi`, `SelectAgentSessionRecordStore`, `SelectAgentAuditStore` | `AddWeaverStores`, `AddWeaverModel`, `AddWeaverEntityFrameworkCore`, `MapWeaverApi`, `SelectWeaverSessionRecordStore`, `SelectWeaverAuditStore` |
| Studio module SDK surface | `api.agent` (`promptActions`, `resultRenderers`, ...) on `ElsaStudioModuleApi` | `api.weaver` |
| Studio module SDK types (`@elsa-workflows/studio-sdk`) | `StudioAgent*` (`StudioAgentRegistry`, `StudioAgentPromptActionContribution`, `StudioAgentRisk`, ...) | `StudioWeaver*` |
| Studio module SDK factory and slot keys | `createAgentRegistry`; `studioSlotIds.agent*` and `studioSlots.agent*` (`agentPromptStarters`, `agentToolSlots`, ...) | `createWeaverRegistry`; `studioSlotIds.weaver*` and `studioSlots.weaver*`. The slot id values (`studio.weaver.*`) are unchanged |
| Source files | `Extensions/Weaver/**/*Agent*.cs` (143 files) | the same paths with `Weaver` in the file name |

`WeaverFeature` and `WeaverDbContext` already had their names from stage 1. `AgentDbContext` and the other `Agent*` types under `Extensions/Agent` belong to the user-defined agent layer; stage 1b does not touch them.

What follows the type names: OpenAPI schema ids derived from the CLR type names, logger categories (`Elsa.Weaver...Weaver*`), source-generated JSON context member names, the contributor type name the permission catalog reports for the Weaver permissions (`WeaverPermissionContributor`, computed at startup), the CLR type names recorded in the EF migration designer and snapshot files, and exception text built from a type name (the record codecs' "A recorded `<type>` cannot be read" and the registration validator's contract names). None of these is read back from stored data.

Unchanged in stage 1b: every persisted or wire identifier. Table and column names, the `Weaver` EF module and `ElsaWeaver` history, JSON property names (`agentScopes`, ...), enum member names (`AgentHarnessProvider`, `SubAgents`) and their wire values, routes, capability ids, error codes, permission keys, codecs, shell feature keys, Studio module ids, local-storage keys, CSS classes and test ids stay as stage 1 left them. Studio-internal file and function names that still say "agent" (`app/agent/`, `workflowAgent*.ts`, `registerWorkflowAgent`, the `workflows.agent.*` contribution ids) are not part of the SDK surface and are tracked on #4027. On the SDK surface two property names keep the word: `agentScopes` on a context attachment, which is a wire name, and `agent` on `StudioWeaverPromptRequest`, which asks for a named assistant other than Weaver.

Breaking for Studio module authors: a module that reads `api.agent`, imports a `StudioAgent*` type, calls `createAgentRegistry` or reads a `studioSlots.agent*` key must switch to `api.weaver`, `StudioWeaver*`, `createWeaverRegistry` and `studioSlots.weaver*` when it moves to this SDK version; there is no alias.

## 2026-11-10: user-defined agents take the `Elsa.Agent` root (stage 2 of the agent rename)

Decision: [ADR 0094 amendment](../adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md#amendment--2026-11-09-accepted), plan and inventory on [#4027](https://github.com/elsa-workflows/elsa-foundation/issues/4027). The `Specialists` vocabulary is retired; the product concept is an agent. Pre-GA clean break: persisted identifiers and tables are renamed too, existing agent journals are recreated, not migrated, and compiled agent executables carrying the old fingerprint are refused.

| Kind | Was | Is |
|---|---|---|
| Packages | `Elsa.Agent.Specialists`, `Elsa.Agent.Specialists.Core`, `.Api`, `.Anthropic`, `.AzureOpenAI`, `.OpenAI`, `.Connections`, `.Design`, `.JsonSchema`, `.Inspection.Api`, `.Persistence.EntityFrameworkCore`, `.Review`, `.Review.Api`; `Elsa.Studio.Agent.Specialists` | `Elsa.Agent`, `Elsa.Agent.Core`, `.Api`, `.Anthropic`, `.AzureOpenAI`, `.OpenAI`, `.Connections`, `.Design`, `.JsonSchema`, `.Inspection.Api`, `.Persistence.EntityFrameworkCore`, `.Review`, `.Review.Api`; `Elsa.Studio.Agent` (first published as 4.0.0-preview) |
| Source folders | `Extensions/Agent/src/Specialists/...`, `Extensions/Agent/tests/Specialists/...`, `Extensions/Agent/studio/Elsa.Studio.Agent.Specialists` | `Extensions/Agent/src/...`, `Extensions/Agent/tests/...`, `Extensions/Agent/studio/Elsa.Studio.Agent` |
| Namespaces | `Elsa.Agent.Specialists.*`, `Elsa.Studio.Agent.Specialists` | `Elsa.Agent.*`, `Elsa.Studio.Agent` |
| C# type names | `Specialist*`, `*Specialists*` (`SpecialistActivity`, `SpecialistTurnRecord`, `ISpecialistTurnStore`, `SpecialistsDbContext`, ...) | `Agent*` (`AgentActivity`, `AgentTurnRecord`, `IAgentTurnStore`, `AgentDbContext`, ...) |
| Shell feature keys (`shells.json`) | `Specialists`, `SpecialistsRuntime`, `SpecialistsDesign`, `SpecialistsApi`, `SpecialistsInspectionApi`, `SpecialistsReview`, `SpecialistsReviewApi`, `SpecialistsEntityFrameworkCore`, `SpecialistsInMemoryJournal`, `AnthropicSpecialists`, `OpenAISpecialists`, `AzureOpenAISpecialists`, `AgentSpecialistsStudio` | `Agent`, `AgentRuntime`, `AgentDesign`, `AgentApi`, `AgentInspectionApi`, `AgentReview`, `AgentReviewApi`, `AgentEntityFrameworkCore`, `AgentInMemoryJournal`, `AnthropicAgents`, `OpenAIAgents`, `AzureOpenAIAgents`, `AgentStudio` |
| Studio module | id `agent-specialists`, npm `@elsa-workflows/studio-agent-specialists`, assets `studio/modules/agent-specialists/` | id `agent`, npm `@elsa-workflows/studio-agent`, assets `studio/modules/agent/` |
| REST routes | `agent/specialists/model-providers`, `agent/specialists/model-capabilities`, `agent/specialists/invocations/{...}/evidence`, `agent/specialists/test-runs/{id}/evidence` | `agent/model-providers`, `agent/model-capabilities`, `agent/invocations/{...}/evidence`, `agent/test-runs/{id}/evidence` (`agent/definitions` and `agent/reviews` unchanged) |
| API capabilities | `elsa.api.agent-specialists`, `elsa.api.agent-specialist-inspection` | `elsa.api.agent`, `elsa.api.agent-inspection` (`elsa.api.agent-definitions`, `elsa.api.agent-reviews` unchanged) |
| Persisted activity type ids | `Elsa.Agent.Specialists.Activities.SpecialistActivity`, `Elsa.Agent.Specialists.Activities.SpecialistModelTurn` | `Elsa.Agent.Activities.AgentActivity`, `Elsa.Agent.Activities.AgentModelTurn` |
| Provider key, codecs, fingerprint | `elsa.specialist`, `elsa.specialist.invocation`, `elsa.agent.specialists.core`, `elsa.agent.specialists.connections`, `elsa.agent.specialists.jsonschema`, `elsa.agent.specialists.review`, compiler fingerprint `elsa.specialist/compiler/2.1.0` | `elsa.agent`, `elsa.agent.invocation`, `elsa.agent.core`, `elsa.agent.connections`, `elsa.agent.jsonschema`, `elsa.agent.review`, `elsa.agent/compiler/2.1.0` |
| Connection kinds | `specialist-anthropic`, `specialist-openai`, `specialist-azure-openai` | `agent-anthropic`, `agent-openai`, `agent-azure-openai` |
| Diagnostic codes | `specialist.*` (`specialist.turn.fence`, `specialist.journal.conflict`, ...) | `agent.*` (the prefix Weaver vacated in stage 1) |
| Capability link rels | `agent-specialist-model-providers`, `agent-specialist-model-capabilities`, `agent-specialist-invocation-evidence`, `agent-specialist-test-run-evidence` | `agent-model-providers`, `agent-model-capabilities`, `agent-invocation-evidence`, `agent-test-run-evidence` |
| Checkpoint and turn codecs | every `elsa.specialist.*` purpose (`elsa.specialist.{applied,cancel,continuation,accepted-session,json,structured-object,text-history}/1`, `elsa.specialist.turn/{request,result}/{1,2}`, `elsa.specialist.invocation/2`) | the same purposes under `elsa.agent.*` |
| Nightly EF suite | `specialist-journal` | `agent-journal` |
| EF module | module `Specialists`, history `ElsaAgentSpecialists`, tables `elsa_specialist_conversations`, `elsa_specialist_operations`, `elsa_specialist_turns` | module `Agent`, history `ElsaAgent`, `elsa_agent_conversations`, `elsa_agent_operations`, `elsa_agent_turns`; one regenerated `Initial` migration per provider (`elsa_agent_sources_*` unchanged) |
| Endpoint names | `ElsaAgentSpecialistsApiEndpoints*`, `ElsaAgentSpecialistsInspectionApiEndpoints*`, `ElsaAgentSpecialistsReviewApiEndpoints*` | `ElsaAgentApiEndpoints*`, `ElsaAgentInspectionApiEndpoints*`, `ElsaAgentReviewApiEndpoints*` |
| Tool templates (`tools/agents`) | features `SpecialistPackageHost`, `SpecialistDurablePackageHost`, `SpecialistDurablePackageProbe`, `SpecialistResult` | `AgentPackageHost`, `AgentDurablePackageHost`, `AgentDurablePackageProbe`, `AgentResult` |
| Glossary | Specialist (historical synonym row), "legacy: specialist ..." notes | removed; Agent conversation, Agent turn, Agent team stand alone |

Reused identifiers: `Elsa.Agent`, `Elsa.Agent.Core`, `Elsa.Agent.Api`, `Elsa.Agent.Anthropic` and `Elsa.Agent.Persistence.EntityFrameworkCore` were Weaver package ids until stage 1, as were the capability `elsa.api.agent` (v1) and the EF history `ElsaAgent`. Stage 2 reuses them on purpose for user-defined agents. The publish-state record is keyed by package id and is never removed, so the reused ids continue their version sequence above the Weaver-era `4.0.x-preview` versions instead of restarting; a consumer that pinned a Weaver-era `Elsa.Agent*` package moves to `Elsa.Weaver.*`, and a Studio module that required `elsa.api.agent` for the Weaver client now requires `elsa.api.weaver`.

Frozen records keep the old words: dated reports, evidence folders, ADR text and the consolidation ledgers describe what happened then.

## 2026-11-10: the agent spec and the docs take the new words (stage 3 of the agent rename)

Decision: plan on [#4027](https://github.com/elsa-workflows/elsa-foundation/issues/4027). Documentation and enforcement only; no package, type, route or persisted identifier changes in this stage.

| Kind | Was | Is |
|---|---|---|
| Spec folder | `specs/1179-specialist-agents` | `specs/1179-agents`; every link to it follows, including the links in ADR 0094 and in specs 002, 003 and 221 (their text is otherwise unchanged) |
| Spec text | Specialist, `Elsa.Agent.Specialists.*`, `elsa.specialist.*`, `specialist-openai`, `Extensions/Agent/src/Specialists/...` | Agent and the stage 2 names, so the spec describes the code as it is. The spec's Terminology note records the old name |
| Retired terms (`.editorconfig`, enforced by `Elsa.Architecture.Analyzers`) | none for these folders | `Specialist`, `Specialists` in type and namespace names under `Extensions/Agent/src`; `Agent`, `Agents` in type and namespace names under `Extensions/Weaver/src` |

Not renamed: the branch name `agent/codex/3538-specialist-design` in the spec header (it is the branch the design was written on), other specs that mention specialists in their own history, and evidence files.

Version line: not bumped. The plan named one Minor bump for the rename. The 4.0 line has no stable release yet, so the renamed ids start again on the current line (see the top of this page) and this ledger records the break; raising `ElsaVersion` in `VersionLines.props` would move every Line B package to a new minor. Bumping anyway is an owner decision and a one-line change.

Weaver enum members and JSON property names that still say agent (`agentScopes`, `AgentHarnessProvider`, `SubAgents`) are wire names and stay until an owner decision; the analyzer checks type and namespace declarations only, so it does not flag them.
