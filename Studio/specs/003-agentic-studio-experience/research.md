# Research: Elsa Studio Agentic Experience

## Decision: Backend-owned Copilot provider facade

**Decision**: External agent-provider communication is centralized in the
Elsa.Server / elsa-foundation backend behind provider-agnostic agent contracts.
Studio and modules exchange sessions, messages, context attachments, proposals,
and approvals with Elsa endpoints; they do not call the GitHub Copilot SDK or
other model providers directly.

**Rationale**: Backend ownership keeps provider credentials, policy enforcement,
tenant boundaries, context minimization, redaction, tool execution, rate limits,
and audit in one trusted place. It also lets Studio remain modular and
provider-agnostic.

**Alternatives considered**:

- Browser-side provider integration: rejected because it exposes provider
  credentials/behavior and makes module-level governance inconsistent.
- Studio-host-only integration: rejected because workflow runtime context,
  server diagnostics, and privileged tool execution live in Elsa.Server.
- Provider-specific SDK in module contracts: rejected because module authors
  would bind to one provider and duplicate safety controls.

## Decision: Workflow-first MVP

**Decision**: The first implementation slice delivers a global assistant
foundation plus workflow explanation, workflow troubleshooting, and reviewable
workflow-change proposals.

**Rationale**: Workflow authoring is Elsa Studio's core job. A workflow-first
proof validates contextual grounding, review-first actions, streaming, and
policy gates while producing visible user value.

**Alternatives considered**:

- Command-center-only MVP: rejected because chat without a real workflow proof
  risks becoming a generic assistant.
- Platform-contract-only MVP: rejected because module contracts need at least
  one real screen to prove shape and ergonomics.
- Diagnostics-first MVP: useful, but workflow authoring has broader strategic
  value for Studio.

## Decision: Review-first action model

**Decision**: Agent-generated changes are represented as structured proposals
that users approve, deny, edit, or rerun before execution. Mutating actions never
execute silently.

**Rationale**: Review-first behavior protects workflows, module configuration,
features, package feeds, secrets, and runtime state while preserving user trust.
It also creates a clear audit boundary between "assistant suggested" and "user
approved".

**Alternatives considered**:

- Fully autonomous actions: rejected for production safety.
- Prompt-only recommendations: rejected because the experience would not become
  genuinely agentic.
- Per-module ad hoc confirmations: rejected because approval UX and audit would
  become inconsistent.

## Decision: Streaming responses and progress updates

**Decision**: Assistant responses and long-running action progress should stream
from the backend to Studio. The existing console stream establishes a useful
pattern for live updates; the agent contract should support both conversation
tokens/deltas and structured progress events.

**Rationale**: Streaming makes the assistant feel responsive under normal model
latency and gives users cancellation/progress affordances for long-running
actions. It also supports incremental diagnostics and proposal building.

**Alternatives considered**:

- Polling: simpler but poorer latency and more wasteful for long conversations.
- Request/response only: insufficient for long-running analysis and tool calls.

## Decision: Agent contributions extend the Studio SDK registry model

**Decision**: Add agent-facing registries alongside existing navigation, routes,
widgets, panels, setting editors, and diagnostics registries. Modules can
contribute context providers, prompt starters, capability descriptors, and
action handlers through stable contracts.

**Rationale**: The current SDK already treats Studio as a modular product
surface. Agent capabilities should follow the same model so module authors can
enrich the assistant without private shell coupling.

**Alternatives considered**:

- Hard-code agent behavior in the shell: rejected because module-specific
  workflows would require host changes.
- Let each module create its own assistant: rejected because users need one
  governed, context-aware assistant with consistent policy and audit.

## Decision: Align visible assistant naming with Weaver

**Decision**: Use **Weaver** as the user-visible assistant name. Keep `agent`
terminology for backend profiles, provider-neutral contracts, and Studio SDK
contribution registries until the codebase can safely introduce compatibility
aliases.

**Rationale**: Cross-repo comparison found that `elsa-core` defines Weaver as
Elsa's AI copilot platform, while `elsa-studio` contains a Weaver UI feature at
`/ai/weaver` and `elsa-studio-weaver` contains an older Weaver chat-panel
prototype. In those designs, "agent" means a selectable behavior profile such as
workflow authoring or diagnostics, not the assistant's product name.

**Compatibility notes**:

- `elsa-core` currently exposes Core Weaver APIs under `/ai/*` with
  `conversation.started`, `assistant.delta`, `tool.result`,
  `proposal.created`, and `conversation.error` stream events.
- `elsa-studio` implements a dedicated Weaver workspace with capability
  discovery, tool metadata, context reference chips, activity timeline,
  disabled proposal actions when endpoints are absent, and no provider SDK
  dependencies.
- This MVP keeps the foundation `/_elsa/agent/*` facade because the backend
  harness is already aligned to it, but the UI labels and stream parser now
  tolerate Weaver/Core naming so the facade can evolve toward `/_elsa/weaver/*`
  or `/ai/*` without another product-language change.

**Alternatives considered**:

- Keep "Assistant" as the visible name: rejected because it diverges from Core
  and the existing Studio Weaver feature.
- Rename every internal `agent` symbol immediately: deferred because it would be
  high churn; compatibility aliases can be introduced deliberately later.

## Decision: Context minimization with visible context attachments

**Decision**: Each agent turn carries a bounded set of context attachments with
scope, source, sensitivity, and permission metadata. Studio shows context chips
or equivalent labels so users understand what the assistant used.

**Rationale**: Agent quality requires context, but user trust and safety require
explicit minimization, redaction, and visibility. This creates a clear contract
for backend policy and client UX.

**Alternatives considered**:

- Send all current screen data: rejected because it increases privacy and token
  risk.
- Hide context details from users: rejected because users need confidence in
  grounded answers and redaction behavior.
