# Package and identifier renames

The pre-GA clean breaks that renamed published packages or persisted identifiers, with the old-to-new map a consumer needs. Each entry names the decision that authorised it. Renamed package ids start again at the current version line (`line.major.minor.0`); the old ids stay frozen on the preview feed and are never republished ([versioning README](../../tools/versioning/README.md)).

## 2026-11-09: Weaver leaves the `Elsa.Agent` root (stage 1 of the agent rename)

Decision: [ADR 0094 amendment](../adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md#amendment--2026-11-09-accepted), plan and inventory on [#4027](https://github.com/elsa-workflows/elsa-foundation/issues/4027). Stage 2 is the entry below; the words stage follows.

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

Unchanged in stage 1: C# type names inside the Weaver layer (`AgentSession`, `IAgentProvider`, ...), the Studio module SDK surface `api.agent`, the `AgentSource*` types, `agent/definitions` permissions, `AgentSourcesEntityFrameworkCore` and `elsa_agent_sources_*` tables (they belong to user-defined agents), and every `Elsa.Agent.Specialists.*` package.

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

Frozen records keep the old words: dated reports, spec folders such as `specs/1179-specialist-agents`, ADR text and the consolidation ledgers describe what happened then.
