# Package and identifier renames

The pre-GA clean breaks that renamed published packages or persisted identifiers, with the old-to-new map a consumer needs. Each entry names the decision that authorised it. Renamed package ids start again at the current version line (`line.major.minor.0`); the old ids stay frozen on the preview feed and are never republished ([versioning README](../../tools/versioning/README.md)).

## 2026-11-09: Weaver leaves the `Elsa.Agent` root (stage 1 of the agent rename)

Decision: [ADR 0094 amendment](../adr/0094-agent-definitions-own-authored-content-and-providers-publish-activities.md#amendment--2026-11-09-accepted), plan and inventory on [#4027](https://github.com/elsa-workflows/elsa-foundation/issues/4027). Stage 2 (the `Elsa.Agent.Specialists.*` packages to `Elsa.Agent.*`) and the words stage follow; this entry covers stage 1 only.

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
