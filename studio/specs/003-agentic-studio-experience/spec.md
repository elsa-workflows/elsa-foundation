# Feature Specification: Elsa Studio Weaver Experience

**Feature Branch**: `sfmskywalker/agentic-studio-experience`

**Created**: 2026-06-18

**Status**: Draft

**Input**: User description: "Create an agentic Elsa Studio experience with chat plus modular assistance across Studio, powered through the Elsa.Server backend where the agent provider calls are made."

**Naming alignment**: The visible assistant is **Weaver**, matching the Elsa Core and Elsa Studio AI assistant direction. The term "agent" remains internal vocabulary for backend profiles, provider-neutral contracts, SDK contribution registries, and capability scopes.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask Weaver for Guided Help (Priority: P1)

As a Studio user, I can open Weaver from the shell, ask natural-language questions about the current workspace, and receive grounded answers with relevant actions.

**Why this priority**: A trusted command center creates the core agentic experience and is the minimum useful slice for every user persona.

**Independent Test**: Can be fully tested by opening the assistant, asking about the current Studio area, and verifying that the response references the relevant context and offers safe next actions.

**Acceptance Scenarios**:

1. **Given** a user is viewing any Studio screen, **When** they open Weaver, **Then** Weaver starts with awareness of the active screen, selected resource, user permissions, and available module capabilities.
2. **Given** a user asks for help, **When** Studio can answer from available context, **Then** Weaver returns a concise answer with source/context labels and recommended next actions.
3. **Given** a user asks for an unsupported or ambiguous action, **When** Weaver cannot confidently proceed, **Then** it asks for clarification or explains the limitation without fabricating state.

---

### User Story 2 - Receive Contextual Suggestions In Workflows (Priority: P2)

As a workflow builder, I can ask Weaver to explain, validate, refactor, or generate workflow elements from the current workflow context.

**Why this priority**: Workflow authoring is Elsa Studio's core value; agentic support should reduce workflow design friction first.

**Independent Test**: Can be tested by selecting a workflow area, requesting an explanation or improvement, and verifying that the assistant uses the selected workflow context and produces actionable guidance.

**Acceptance Scenarios**:

1. **Given** a workflow is open, **When** the user asks "what does this do?", **Then** the assistant explains the workflow in business terms and highlights risky or unclear branches.
2. **Given** the user asks to add a capability, **When** the assistant proposes changes, **Then** Studio presents a reviewable plan before any change is applied.
3. **Given** validation or runtime diagnostics exist, **When** the user asks for troubleshooting, **Then** the assistant prioritizes the relevant errors, affected activities, and suggested fixes.

---

### User Story 3 - Extend Assistance Through Modules (Priority: P3)

As a module author, I can contribute agent context, prompt starters, domain actions, and reviewable tool capabilities so my module feels first-class in Studio's assistant.

**Why this priority**: Elsa Studio is modular; the assistant must be a platform surface, not a single hard-coded chat widget.

**Independent Test**: Can be tested by enabling a module that contributes agent capabilities and verifying that those capabilities appear only in the relevant screens and respect user permissions.

**Acceptance Scenarios**:

1. **Given** a module contributes agent context, **When** the user enters that module's screen, **Then** the assistant can cite and use that module-specific context.
2. **Given** a module contributes an action, **When** the assistant recommends it, **Then** the action is clearly labeled, permission-gated, and reviewable before execution.
3. **Given** a module is disabled or incompatible, **When** the assistant builds available capabilities, **Then** disabled module capabilities are excluded and a diagnostic is available to administrators.

---

### User Story 4 - Govern Agentic Actions Safely (Priority: P4)

As an administrator, I can control which users and modules can use agent features, review agent activity, and prevent sensitive or destructive actions from happening without approval.

**Why this priority**: Agentic capabilities need explicit trust boundaries before they can be used in production environments.

**Independent Test**: Can be tested by changing permissions and policy settings, attempting restricted actions, and verifying enforcement, auditability, and clear user feedback.

**Acceptance Scenarios**:

1. **Given** a user lacks permission for a capability, **When** they ask the assistant to perform it, **Then** the assistant explains that the capability is unavailable without exposing restricted details.
2. **Given** the assistant proposes a destructive or environment-changing action, **When** the user reviews it, **Then** Studio requires explicit approval and records the decision.
3. **Given** sensitive data appears in context, **When** the assistant prepares a request, **Then** Studio applies configured redaction and context minimization rules.

### Edge Cases

- Agent provider is unavailable, rate-limited, or returns an error.
- The backend cannot reach a required Studio, workflow, module, or diagnostic context source.
- The active user changes permissions while an agent session is open.
- A module contributes malformed, duplicate, disabled, or incompatible agent capabilities.
- A request spans Studio and Server contexts with conflicting capabilities.
- A proposed action would mutate production state, secrets, package feeds, or module configuration.
- Long-running actions require progress updates, cancellation, retry, or resumption.
- The assistant's answer conflicts with known validation, runtime diagnostics, or policy rules.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Studio MUST provide a persistent Weaver entry point that can be opened from any screen without losing the user's current task context.
- **FR-002**: The assistant MUST support multi-turn conversations with screen, selection, user, tenant, permission, and module context attached to each turn as policy allows.
- **FR-003**: The system MUST distinguish answers, suggestions, plans, and executable actions so users understand whether the assistant is informing, recommending, or preparing to change state.
- **FR-004**: The system MUST require explicit user approval before executing actions that create, update, delete, install, disable, publish, deploy, or otherwise mutate Studio or Server state.
- **FR-005**: The MVP assistant MUST provide contextual support for workflow explanation, workflow authoring guidance, active workflow troubleshooting, and reviewable workflow-change proposals.
- **FR-006**: Modules MUST be able to contribute agent context providers, prompt starters, capability descriptors, and reviewable action definitions without depending on private host internals.
- **FR-007**: Module-contributed capabilities MUST be filtered by feature availability, module status, compatibility, user permissions, tenant boundaries, and administrator policy.
- **FR-008**: The system MUST centralize external agent-provider communication in the backend so Studio clients and modules do not call external agent providers directly.
- **FR-009**: The system MUST support streaming responses and action progress updates for long-running agent tasks.
- **FR-010**: The system MUST record agent sessions, requests, proposed actions, approvals, denials, execution results, and policy decisions for audit and diagnostics.
- **FR-011**: The system MUST protect sensitive data through context minimization, redaction, permission checks, and clear indicators for what context is included.
- **FR-012**: The assistant MUST degrade gracefully when agent features are disabled or unavailable, leaving the rest of Studio fully usable.
- **FR-013**: Users MUST be able to give feedback on answers and action outcomes so administrators can evaluate quality and improve prompts, context, and policies.
- **FR-014**: Administrators MUST be able to configure agent availability, allowed capabilities, approval requirements, retention policy, and module participation.
- **FR-015**: Agent-generated workflow or configuration changes MUST be reviewable as structured proposals before they are accepted.

### Key Entities

- **Agent Session**: A multi-turn interaction between a user and the assistant, including active context and policy state.
- **Agent Message**: A user request, assistant response, system notice, or action progress update.
- **Agent Context Attachment**: A bounded piece of current Studio, Server, workflow, module, diagnostic, or documentation context made available to an agent turn.
- **Agent Capability**: A declared assistant ability, such as answer generation, workflow explanation, diagnostic triage, or a module-provided action.
- **Agent Action Proposal**: A structured recommendation to perform an operation, including parameters, impact summary, required permissions, and approval state.
- **Agent Audit Event**: A durable record of requests, context classes used, policy decisions, approvals, denials, executions, and outcomes.
- **Module Agent Contribution**: A module-owned declaration of context, prompt starters, and actions that the assistant can surface in appropriate contexts.
- **Agent Policy**: Administrative rules that determine availability, context access, approval requirements, retention, and sensitive data handling.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 90% of users can find and open the assistant from any Studio screen in under 5 seconds.
- **SC-002**: 85% of contextual help requests return a relevant answer or safe clarification without requiring users to manually copy screen details.
- **SC-003**: Workflow explanation and troubleshooting tasks complete at least 40% faster for representative workflows compared with using Studio without assistant support.
- **SC-004**: 100% of mutating assistant actions require an explicit review step before execution.
- **SC-005**: 100% of agent sessions and action decisions produce administrator-visible audit records.
- **SC-006**: The workflow-first contracts leave a documented extension path for module authors to add context providers and prompt starters without host-code changes after the MVP proof.
- **SC-007**: Agent unavailability does not prevent users from loading Studio, navigating modules, or using non-agent workflows.
- **SC-008**: User satisfaction for the initial assistant experience reaches at least 4 out of 5 in internal validation.

## Assumptions

- The initial experience targets authenticated Studio users, workflow builders, operators, administrators, and module authors.
- The first delivery uses a narrow workflow-first MVP: a shell-level assistant plus workflow explanation, active workflow troubleshooting, and reviewable workflow-change proposals. Diagnostics, module management, feature management, settings, and operational assistance are explicit post-MVP follow-on proofs.
- External agent-provider calls are made only by the backend, while Studio receives provider-agnostic conversation, context, and action contracts.
- The assistant starts with review-first behavior: propose and explain before applying changes.
- Existing Studio authentication, permission, module registry, bottom panel, navigation, and backend context patterns are reused.
- Cross-repository backend work may be required in the foundation backend, while this repository owns Studio UI, SDK, and module contribution contracts.
