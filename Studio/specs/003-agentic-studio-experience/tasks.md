# Tasks: Elsa Studio Agentic Experience

**Input**: Design documents from `/specs/003-agentic-studio-experience/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Include focused Vitest coverage for Studio SDK/shell behavior, dotnet/backend contract tests for elsa-foundation agent services, and quickstart validation for the workflow-first proof.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. Studio tasks target `elsa-foundation-studio`; backend tasks target the planned `elsa-foundation` / Elsa.Server agent modules.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish the cross-repository skeleton and planning references for the agentic experience.

- [X] T001 Create Studio agent UI directory structure in `src/Elsa.Studio.Web/Client/src/app/agent/`
- [X] T002 [P] Create Studio agent test directory structure in `src/Elsa.Studio.Web/Client/src/app/agent/__tests__/`
- [X] T003 [P] Add placeholder agent feature exports in `src/Elsa.Studio.Web/Client/src/app/agent/index.ts`
- [ ] T004 [P] Create planned backend agent abstractions project skeleton in `elsa-foundation/src/Elsa.Foundation.Agent.Abstractions/`
- [ ] T005 [P] Create planned backend agent API project skeleton in `elsa-foundation/src/Elsa.Foundation.Agent.Api/`
- [ ] T006 [P] Create planned Copilot provider project skeleton in `elsa-foundation/src/Elsa.Foundation.Agent.GitHubCopilot/`
- [ ] T007 [P] Create planned workflow agent project skeleton in `elsa-foundation/src/Elsa.Foundation.Workflows.Agent/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define provider-agnostic contracts, SDK registries, client/server models, and safety seams that all user stories depend on.

**CRITICAL**: No user story implementation should bypass these contracts or call an external agent provider from Studio.

- [ ] T008 Define `AgentSession`, `AgentMessage`, `AgentContextAttachment`, `AgentCapability`, `AgentActionProposal`, `AgentAuditEvent`, and `AgentPolicy` contracts in `elsa-foundation/src/Elsa.Foundation.Agent.Abstractions/Models/`
- [ ] T009 Define backend service contracts for sessions, policy, context collection, proposal approval, streaming, feedback, and audit in `elsa-foundation/src/Elsa.Foundation.Agent.Abstractions/Services/`
- [ ] T010 Define provider facade interfaces for GitHub Copilot SDK sessions, streaming, tool approval, and provider diagnostics in `elsa-foundation/src/Elsa.Foundation.Agent.Abstractions/Providers/`
- [ ] T011 Define ASP.NET endpoint registration for `/_elsa/agent/*` routes in `elsa-foundation/src/Elsa.Foundation.Agent.Api/AgentEndpointRouteBuilderExtensions.cs`
- [X] T012 Define Studio agent request/response/event TypeScript types in `src/Elsa.Studio.Web/Client/src/app/agent/agentTypes.ts`
- [X] T013 Extend `ElsaStudioModuleApi` with `agent` contribution registries in `src/Elsa.Studio.Web/Client/src/sdk/index.ts`
- [X] T014 Wire agent registries into `createStudioRegistry` in `src/Elsa.Studio.Web/Client/src/app/registry.ts`
- [X] T015 [P] Add Studio SDK registry tests for context providers, prompt starters, capabilities, and actions in `src/Elsa.Studio.Web/Client/src/__tests__/agent-registry.test.ts`
- [ ] T016 [P] Add backend contract tests for policy-denied context, proposal approval requirements, and audit emission in `elsa-foundation/tests/Elsa.Foundation.Agent.Tests/AgentContractTests.cs`
- [X] T017 Implement shared agent HTTP client for bootstrap, sessions, messages, proposals, feedback, and audit in `src/Elsa.Studio.Web/Client/src/app/agent/agentClient.ts`
- [X] T018 Implement shared agent stream client for message/progress/proposal events in `src/Elsa.Studio.Web/Client/src/app/agent/agentStream.ts`
- [X] T019 Implement user-safe agent error normalization in `src/Elsa.Studio.Web/Client/src/app/agent/agentErrors.ts`

**Checkpoint**: Studio and backend agent contracts compile, registry tests pass, and no Studio code depends on provider-specific SDK calls.

---

## Phase 3: User Story 1 - Guided Studio Assistant (Priority: P1) MVP

**Goal**: A user can open a persistent assistant from any Studio screen, ask contextual questions, and receive grounded answers with visible context and safe next actions.

**Independent Test**: Open the assistant from Overview or Modules, ask about the active Studio area, and verify contextual answer rendering, context chips, streaming state, disabled state, and graceful error behavior without workflow-specific dependencies.

### Tests for User Story 1

- [X] T020 [P] [US1] Add assistant launcher and focus behavior tests in `src/Elsa.Studio.Web/Client/src/app/agent/__tests__/AgentLauncher.test.tsx`
- [X] T021 [P] [US1] Add assistant panel rendering, streaming, disabled, and error state tests in `src/Elsa.Studio.Web/Client/src/app/agent/__tests__/AgentPanel.test.tsx`
- [X] T022 [P] [US1] Add agent client bootstrap/session/message tests in `src/Elsa.Studio.Web/Client/src/app/agent/__tests__/agentClient.test.ts`

### Implementation for User Story 1

- [X] T023 [P] [US1] Implement assistant launcher control in `src/Elsa.Studio.Web/Client/src/app/agent/AgentLauncher.tsx`
- [X] T024 [P] [US1] Implement assistant composer with prompt submission and disabled states in `src/Elsa.Studio.Web/Client/src/app/agent/AgentComposer.tsx`
- [X] T025 [P] [US1] Implement assistant message list with streaming deltas, errors, and feedback controls in `src/Elsa.Studio.Web/Client/src/app/agent/AgentMessageList.tsx`
- [X] T026 [P] [US1] Implement context chip list for visible agent attachments in `src/Elsa.Studio.Web/Client/src/app/agent/AgentContextChips.tsx`
- [X] T027 [US1] Implement assistant panel composition and session lifecycle in `src/Elsa.Studio.Web/Client/src/app/agent/AgentPanel.tsx`
- [X] T028 [US1] Mount the assistant launcher and panel in the shell frame in `src/Elsa.Studio.Web/Client/src/app/App.tsx`
- [X] T029 [US1] Add agent surface styling using Studio tokens in `src/Elsa.Studio.Web/Client/src/app/agent/agent.css`
- [X] T030 [US1] Import agent styling through the shell style pipeline in `src/Elsa.Studio.Web/Client/src/app/styles.css`
- [X] T031 [US1] Add bootstrap unavailable and provider-error handling to `src/Elsa.Studio.Web/Client/src/app/agent/AgentPanel.tsx`
- [X] T032 [US1] Run `pnpm --filter @elsa-workflows/studio-web test` from `src/Elsa.Studio.Web/Client/package.json`

**Checkpoint**: The global assistant is visible, accessible, context-aware at the shell level, and independently testable without workflow-specific actions.

---

## Phase 4: User Story 2 - Workflow Context, Troubleshooting, And Proposals (Priority: P2)

**Goal**: A workflow builder can ask the assistant to explain a workflow, troubleshoot validation/runtime issues, and create a reviewable workflow-change proposal.

**Independent Test**: Open a workflow, ask "what does this workflow do?", then ask "how should I handle stalled approvals?"; verify explanation is read-only and proposed changes require review before execution.

### Tests for User Story 2

- [X] T033 [P] [US2] Add workflow context provider tests in `src/Elsa.Studio.Web/Client/src/app/agent/__tests__/workflowAgentContext.test.ts`
- [X] T034 [P] [US2] Add proposal review rendering and approval/denial tests in `src/Elsa.Studio.Web/Client/src/app/agent/__tests__/AgentProposalReview.test.tsx`
- [ ] T035 [P] [US2] Add backend workflow proposal revision and permission tests in `elsa-foundation/tests/Elsa.Foundation.Workflows.Agent.Tests/WorkflowProposalTests.cs`
- [ ] T036 [P] [US2] Add backend workflow explanation/troubleshooting context tests in `elsa-foundation/tests/Elsa.Foundation.Workflows.Agent.Tests/WorkflowContextTests.cs`

### Implementation for User Story 2

- [X] T037 [P] [US2] Implement workflow context attachment collection in `src/Elsa.Studio.Web/Client/src/app/agent/workflowAgentContext.ts`
- [X] T038 [P] [US2] Implement workflow prompt starters for explanation, troubleshooting, and stalled-approval guidance in `src/Elsa.Studio.Web/Client/src/app/agent/workflowPromptStarters.ts`
- [X] T039 [P] [US2] Implement proposal review UI with impact summary, operations, risks, rollback, approve, deny, and execute controls in `src/Elsa.Studio.Web/Client/src/app/agent/AgentProposalReview.tsx`
- [X] T040 [US2] Integrate workflow context providers and prompt starters into the active agent contribution set in `src/Elsa.Studio.Web/Client/src/app/agent/agentContext.ts`
- [X] T041 [US2] Render created proposals from stream events inside `src/Elsa.Studio.Web/Client/src/app/agent/AgentPanel.tsx`
- [X] T042 [US2] Add proposal approval, denial, execution, and revision-mismatch client methods in `src/Elsa.Studio.Web/Client/src/app/agent/agentClient.ts`
- [ ] T043 [US2] Implement backend workflow context provider in `elsa-foundation/src/Elsa.Foundation.Workflows.Agent/WorkflowAgentContextProvider.cs`
- [ ] T044 [US2] Implement backend workflow explain and troubleshoot capability descriptors in `elsa-foundation/src/Elsa.Foundation.Workflows.Agent/WorkflowAgentCapabilities.cs`
- [ ] T045 [US2] Implement backend workflow-change proposal builder and revision validation in `elsa-foundation/src/Elsa.Foundation.Workflows.Agent/WorkflowChangeProposalService.cs`
- [ ] T046 [US2] Implement proposal approval/execution audit events in `elsa-foundation/src/Elsa.Foundation.Agent.Api/AgentProposalEndpoints.cs`
- [ ] T047 [US2] Run `dotnet test` for `elsa-foundation/tests/Elsa.Foundation.Workflows.Agent.Tests/Elsa.Foundation.Workflows.Agent.Tests.csproj`
- [X] T048 [US2] Run `pnpm --filter @elsa-workflows/studio-web test` from `src/Elsa.Studio.Web/Client/package.json`

**Checkpoint**: Workflow explanations are read-only, troubleshooting is grounded in workflow diagnostics, and workflow changes are structured proposals that require approval.

---

## Phase 5: User Story 3 - Modular Agent Contributions (Priority: P3)

**Goal**: Module authors can contribute agent context, prompt starters, capabilities, and reviewable action descriptors that appear only when relevant and permitted.

**Independent Test**: Enable a sample module with agent contributions, navigate to its route, and verify the assistant shows relevant prompt starters/capabilities while excluding disabled or incompatible module contributions.

### Tests for User Story 3

- [X] T049 [P] [US3] Add module contribution filtering tests in `src/Elsa.Studio.Web/Client/src/app/agent/__tests__/agentContributionFiltering.test.ts`
- [ ] T050 [P] [US3] Add malformed/duplicate contribution diagnostics tests in `src/Elsa.Studio.Web/Client/src/__tests__/agent-contribution-diagnostics.test.ts`
- [ ] T051 [P] [US3] Add sample module agent contribution tests in `src/Elsa.Studio.Samples.Dashboard/Client/src/__tests__/agent-contributions.test.ts`

### Implementation for User Story 3

- [X] T052 [US3] Implement active agent contribution filtering by route, surface, module status, compatibility, permission, and policy in `src/Elsa.Studio.Web/Client/src/app/agent/agentContributions.ts`
- [ ] T053 [US3] Add agent contribution diagnostics to module loader failures and duplicate IDs in `src/Elsa.Studio.Web/Client/src/app/loader.ts`
- [ ] T054 [US3] Surface agent contribution diagnostics through the existing diagnostics registry in `src/Elsa.Studio.Web/Client/src/app/registry.ts`
- [ ] T055 [US3] Add sample dashboard agent context provider and prompt starter in `src/Elsa.Studio.Samples.Dashboard/Client/src/module.tsx`
- [ ] T056 [US3] Update Studio SDK declaration files for sample modules in `src/Elsa.Studio.Samples.Dashboard/Client/src/studio-sdk.d.ts`
- [X] T057 [US3] Render filtered prompt starters in the assistant panel in `src/Elsa.Studio.Web/Client/src/app/agent/AgentPromptStarters.tsx`
- [X] T058 [US3] Integrate `AgentPromptStarters` into `src/Elsa.Studio.Web/Client/src/app/agent/AgentPanel.tsx`
- [X] T059 [US3] Run `pnpm --filter @elsa-workflows/studio-web test` from `src/Elsa.Studio.Web/Client/package.json`
- [ ] T060 [US3] Run sample dashboard module tests from `src/Elsa.Studio.Samples.Dashboard/Client/`

**Checkpoint**: Agent capabilities are modular, diagnosed, and filtered consistently without module authors copying host internals.

---

## Phase 6: User Story 4 - Governance, Audit, And Safe Operations (Priority: P4)

**Goal**: Administrators can control agent availability and capabilities, audit agent activity, and prevent sensitive or destructive actions without approval.

**Independent Test**: Change policy to disable or restrict a capability, attempt the restricted action, and verify denial, user-safe feedback, and administrator-visible audit records.

### Tests for User Story 4

- [ ] T061 [P] [US4] Add backend policy enforcement tests for disabled agent, denied context, required approval, and retention in `elsa-foundation/tests/Elsa.Foundation.Agent.Tests/AgentPolicyTests.cs`
- [ ] T062 [P] [US4] Add backend audit query tests for sessions, context decisions, approvals, denials, executions, and feedback in `elsa-foundation/tests/Elsa.Foundation.Agent.Tests/AgentAuditTests.cs`
- [X] T063 [P] [US4] Add Studio governance disabled/read-only state tests in `src/Elsa.Studio.Web/Client/src/app/agent/__tests__/AgentGovernance.test.tsx`

### Implementation for User Story 4

- [ ] T064 [US4] Implement backend agent policy evaluation service in `elsa-foundation/src/Elsa.Foundation.Agent.Abstractions/Policies/AgentPolicyEvaluator.cs`
- [ ] T065 [US4] Implement backend context minimization and redaction pipeline in `elsa-foundation/src/Elsa.Foundation.Agent.Abstractions/Context/AgentContextSanitizer.cs`
- [ ] T066 [US4] Implement append-only audit writer in `elsa-foundation/src/Elsa.Foundation.Agent.Abstractions/Audit/AgentAuditWriter.cs`
- [ ] T067 [US4] Implement audit query endpoint in `elsa-foundation/src/Elsa.Foundation.Agent.Api/AgentAuditEndpoints.cs`
- [ ] T068 [US4] Implement feedback endpoint and audit linkage in `elsa-foundation/src/Elsa.Foundation.Agent.Api/AgentFeedbackEndpoints.cs`
- [X] T069 [US4] Add policy-aware disabled, denied, and unavailable presentation in `src/Elsa.Studio.Web/Client/src/app/agent/AgentPanel.tsx`
- [X] T070 [US4] Add visible context redaction indicators in `src/Elsa.Studio.Web/Client/src/app/agent/AgentContextChips.tsx`
- [X] T071 [US4] Add feedback submission UI in `src/Elsa.Studio.Web/Client/src/app/agent/AgentMessageList.tsx`
- [ ] T072 [US4] Run `dotnet test` for `elsa-foundation/tests/Elsa.Foundation.Agent.Tests/Elsa.Foundation.Agent.Tests.csproj`
- [X] T073 [US4] Run `pnpm --filter @elsa-workflows/studio-web test` from `src/Elsa.Studio.Web/Client/package.json`

**Checkpoint**: Agent availability, sensitive context, proposals, approvals, and feedback are governed and auditable.

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Validate the narrow workflow-first MVP end-to-end and prepare follow-on diagnostics/module/feature management proofs without expanding the first MVP scope.

- [X] T074 [P] Update agentic experience validation notes in `specs/003-agentic-studio-experience/quickstart.md`
- [X] T075 [P] Add module author guidance for agent contributions in `src/Elsa.Studio.Web/Client/src/sdk/README.md`
- [ ] T076 [P] Add backend provider setup guidance in `elsa-foundation/src/Elsa.Foundation.Agent.GitHubCopilot/README.md`
- [X] T077 Run quickstart static validation from `specs/003-agentic-studio-experience/quickstart.md`
- [ ] T078 Run quickstart backend contract validation from `specs/003-agentic-studio-experience/quickstart.md`
- [ ] T079 Run quickstart Studio workflow MVP validation from `specs/003-agentic-studio-experience/quickstart.md`
- [ ] T080 Browser-verify assistant keyboard/focus, streaming announcements, context chips, disabled state, and proposal review in `src/Elsa.Studio.Web/Client/src/app/agent/`
- [X] T081 Audit changed CSS for unmanaged typography, color, radius, shadow, status, and focus styles in `src/Elsa.Studio.Web/Client/src/app/agent/agent.css`
- [X] T082 Run `pnpm --filter @elsa-workflows/studio-web build` from `src/Elsa.Studio.Web/Client/package.json`
- [X] T083 Run `pnpm --filter @elsa-workflows/studio-web test` from `src/Elsa.Studio.Web/Client/package.json`
- [ ] T084 Run `dotnet build` for the elsa-foundation agent solution
- [ ] T085 Run `dotnet test` for the elsa-foundation agent test projects

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup and blocks user stories.
- **US1 Guided Studio Assistant (Phase 3)**: Depends on Foundational; delivers the shell-level MVP.
- **US2 Workflow Context, Troubleshooting, And Proposals (Phase 4)**: Depends on US1 because it renders workflow context/proposals inside the assistant shell.
- **US3 Modular Agent Contributions (Phase 5)**: Depends on Foundational and can overlap with US2 after the basic assistant shell exists.
- **US4 Governance, Audit, And Safe Operations (Phase 6)**: Depends on Foundational; should be completed before production rollout of US2 mutating proposals.
- **Polish**: Depends on selected user stories and validates the workflow-first MVP.

### User Story Dependencies

- **US1 (P1)**: No dependency on other stories after Foundational.
- **US2 (P2)**: Depends on US1 assistant shell and Foundational proposal/client contracts.
- **US3 (P3)**: Depends on Foundational agent registries; integrates with US1 assistant UI for prompt starter display.
- **US4 (P4)**: Depends on Foundational policy/audit contracts; production execution of US2 proposals depends on US4 policy/audit completion.

### Parallel Opportunities

- T002-T007 can run in parallel during setup.
- T015-T016 can run in parallel once foundational models are defined.
- T020-T022 can run in parallel for US1 tests.
- T023-T026 can run in parallel for US1 UI primitives.
- T033-T036 can run in parallel for US2 tests.
- T037-T039 can run in parallel for US2 client workflow UI/context work.
- T043-T046 can run in parallel with T037-T042 if backend and Studio developers coordinate on contracts.
- T049-T051 can run in parallel for US3 tests.
- T061-T063 can run in parallel for US4 tests.
- T074-T076 can run in parallel during polish.

---

## Parallel Example: User Story 1

```text
Task: "Add assistant launcher and focus behavior tests in src/Elsa.Studio.Web/Client/src/app/agent/__tests__/AgentLauncher.test.tsx"
Task: "Add assistant panel rendering, streaming, disabled, and error state tests in src/Elsa.Studio.Web/Client/src/app/agent/__tests__/AgentPanel.test.tsx"
Task: "Implement assistant launcher control in src/Elsa.Studio.Web/Client/src/app/agent/AgentLauncher.tsx"
Task: "Implement context chip list for visible agent attachments in src/Elsa.Studio.Web/Client/src/app/agent/AgentContextChips.tsx"
```

## Parallel Example: User Story 2

```text
Task: "Add workflow context provider tests in src/Elsa.Studio.Web/Client/src/app/agent/__tests__/workflowAgentContext.test.ts"
Task: "Add proposal review rendering and approval/denial tests in src/Elsa.Studio.Web/Client/src/app/agent/__tests__/AgentProposalReview.test.tsx"
Task: "Implement workflow context attachment collection in src/Elsa.Studio.Web/Client/src/app/agent/workflowAgentContext.ts"
Task: "Implement backend workflow context provider in elsa-foundation/src/Elsa.Foundation.Workflows.Agent/WorkflowAgentContextProvider.cs"
```

## Parallel Example: User Story 3

```text
Task: "Add module contribution filtering tests in src/Elsa.Studio.Web/Client/src/app/agent/__tests__/agentContributionFiltering.test.ts"
Task: "Add sample module agent contribution tests in src/Elsa.Studio.Samples.Dashboard/Client/src/__tests__/agent-contributions.test.ts"
Task: "Implement active agent contribution filtering by route, surface, module status, compatibility, permission, and policy in src/Elsa.Studio.Web/Client/src/app/agent/agentContributions.ts"
Task: "Add sample dashboard agent context provider and prompt starter in src/Elsa.Studio.Samples.Dashboard/Client/src/module.tsx"
```

## Parallel Example: User Story 4

```text
Task: "Add backend policy enforcement tests for disabled agent, denied context, required approval, and retention in elsa-foundation/tests/Elsa.Foundation.Agent.Tests/AgentPolicyTests.cs"
Task: "Add Studio governance disabled/read-only state tests in src/Elsa.Studio.Web/Client/src/app/agent/__tests__/AgentGovernance.test.tsx"
Task: "Implement backend context minimization and redaction pipeline in elsa-foundation/src/Elsa.Foundation.Agent.Abstractions/Context/AgentContextSanitizer.cs"
Task: "Add visible context redaction indicators in src/Elsa.Studio.Web/Client/src/app/agent/AgentContextChips.tsx"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1) to deliver the global assistant shell.
3. Complete the read-only portions of Phase 4 (workflow context, explanation, and troubleshooting).
4. Stop and validate the assistant opens from Studio, uses workflow context, and answers without mutating state.

### Workflow-First Increment

1. Add Phase 4 proposal review and backend workflow proposal execution.
2. Complete Phase 6 policy/audit tasks required for production-safe proposal execution.
3. Validate proposal deny/approve/execute flows and audit records.

### Post-MVP Platform Expansion

1. Complete Phase 5 module contribution tasks so modules can enrich the assistant.
2. Use the same contracts for follow-on diagnostics, module management, and feature management proofs.
3. Complete polish validation and browser verification before broad rollout.

## Notes

- Keep provider-specific GitHub Copilot SDK calls in elsa-foundation backend modules only.
- Keep Studio UI provider-agnostic and token-driven.
- Do not execute mutating actions from assistant text; always create and review proposals.
- Treat paths prefixed with `elsa-foundation/` as cross-repository implementation targets.
- Do not include diagnostics, settings/configuration, module management, feature management, or general operations assistance in the first MVP beyond workflow-specific troubleshooting context.
