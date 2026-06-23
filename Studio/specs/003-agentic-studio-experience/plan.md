# Implementation Plan: Elsa Studio Weaver Experience

**Branch**: `sfmskywalker/agentic-studio-experience` | **Date**: 2026-06-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-agentic-studio-experience/spec.md`

## Summary

Create a workflow-first Weaver Studio experience: a global assistant entry point
with contextual workflow explanation, troubleshooting, and reviewable workflow
change proposals as the MVP proof. Studio owns provider-agnostic UI/SDK contracts
and module contribution seams; Elsa.Server / elsa-foundation owns the GitHub
Copilot SDK harness, context policy, tool execution, streaming, approvals, and
audit.

## Technical Context

**Language/Version**: TypeScript 5.6 / React 19 for Studio client and SDK
contracts; C#/.NET 10 for Studio host APIs and the planned elsa-foundation
backend agent harness.

**Primary Dependencies**: Existing React/Vite Studio shell, Studio SDK module
registry, auth SDK, shared UI primitives, SignalR pattern already used by console
streaming, ASP.NET Core minimal APIs, and the GitHub Copilot SDK from the
backend side. New client dependencies are avoided for the planning slice.

**Storage**: Studio client state for open assistant UI and transient streaming;
backend-owned persistence for agent sessions, messages, proposals, approvals,
feedback, policy decisions, and audit events through elsa-foundation stores.

**Testing**: Vitest for Studio SDK, assistant shell behavior, and module
contribution filtering; dotnet test for Studio API contracts where applicable;
foundation backend tests for agent policy, provider harness, streaming, proposal
approval, and audit; browser verification for the workflow-first assistant proof.

**Target Platform**: ASP.NET-hosted modular React Studio shell backed by
Elsa.Server / elsa-foundation services.

**Project Type**: Modular full-stack web application spanning Studio shell,
module SDK, backend APIs, and cross-repository foundation services.

**Performance Goals**: Assistant entry opens in under 5 seconds from any screen;
context collection for the active workflow remains fast enough not to block
normal editing; first streamed response content appears promptly under normal
provider latency; long-running actions stream progress and support cancellation.

**Constraints**: Studio and modules never call external agent providers directly;
mutating operations are review-first; context is minimized, redacted, permission
filtered, and visible to users; agent unavailability cannot block non-agent
Studio workflows; module authors must not depend on private host CSS or runtime
internals.

**Scale/Scope**: MVP covers only the global assistant foundation plus active
workflow explanation, workflow troubleshooting, and reviewable workflow-change
proposals. Diagnostics, settings/configuration, module management, feature
management, and operational assistance are planned follow-on proof surfaces
using the same contracts after the MVP is validated.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Modular UI contract**: PASS. Agent surfaces are host-provided shared
  primitives, while modules contribute context, prompt starters, and action
  descriptors through SDK contracts instead of copying host internals.
- **Workbench pattern fit**: PASS. The assistant uses the Command Center /
  bottom-panel pattern for global interaction and Split Inspector-style
  proposal review for workflow changes.
- **Typography and token discipline**: PASS. Assistant shell, message states,
  context chips, proposal cards, risk/status chips, and approval controls use
  existing Studio token surfaces and shared UI primitives.
- **Accessible interaction**: PASS. The design includes keyboard-open/close,
  focus restoration, message streaming announcements, loading/disabled/error
  states, screen-reader labels, and review controls for proposals.
- **Real-screen proof**: PASS. The workflow-first assistant proof is a real
  Studio surface, with follow-on diagnostics/module/feature proofs documented.

## Project Structure

### Documentation (this feature)

```text
specs/003-agentic-studio-experience/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── agent-backend-api.md
│   ├── studio-agent-sdk.md
│   └── workflow-agent-contract.md
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
# elsa-foundation-studio (this repo)
src/Elsa.Studio.Web/Client/src/app/
├── App.tsx                         # assistant entry point and shell mounting
├── registry.ts                     # module API creation, agent registry wiring
├── ui/                             # shared Workbench primitives/tokens
└── agent/                          # planned assistant UI/client services
    ├── AgentPanel.tsx
    ├── AgentComposer.tsx
    ├── AgentMessageList.tsx
    ├── AgentProposalReview.tsx
    ├── agentClient.ts
    └── agentContext.ts

src/Elsa.Studio.Web/Client/src/sdk/
└── index.ts                        # module-facing agent contribution contracts

src/Elsa.Studio.Web/Client/src/__tests__/
└── *agent*.test.ts(x)              # SDK, shell, filtering, disabled states

src/Elsa.Studio.Api/
└── planned agent proxy/manifest extensions only where Studio host must expose
    module contribution metadata; provider calls remain in elsa-foundation.

# elsa-foundation / Elsa.Server (planned backend work)
src/Elsa.Foundation.Agent.Abstractions/
├── Sessions/
├── Context/
├── Capabilities/
├── Actions/
├── Policies/
└── Audit/

src/Elsa.Foundation.Agent.GitHubCopilot/
└── GitHub Copilot SDK provider harness

src/Elsa.Foundation.Agent.Api/
└── /_elsa/agent/* endpoints and streaming hub

src/Elsa.Foundation.Workflows.Agent/
└── workflow context providers and reviewable workflow proposal handlers
```

**Structure Decision**: Keep Studio-side implementation focused on the
provider-agnostic SDK, shell UI, and workflow proof. Keep Copilot SDK calls,
tool execution, context policy, and audit in elsa-foundation so the browser and
Studio modules never own provider secrets or privileged execution.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Cross-repository backend contracts | Agent provider calls, audit, and policy belong in elsa-foundation / Elsa.Server while this repo owns Studio UI and SDK surfaces | Browser-only or Studio-host-only integration would expose provider concerns to modules and weaken central governance |
| New Command Center assistant primitive | Existing bottom panel proves the chrome pattern, but chat, context chips, streaming, and proposal review need dedicated primitives | Reusing a plain console/log panel would not support accessible conversation, context visibility, or approval workflows |

## Phase 0: Research

Research is captured in [research.md](./research.md). Key decisions:

- Use a backend-owned Copilot provider facade so Studio and modules remain
  provider-agnostic.
- Use Weaver as the visible product name while retaining `agent` for internal
  profile/contribution contracts during the MVP.
- Use review-first action proposals for all mutating operations.
- Use streaming for assistant responses and action progress.
- Start with workflow-first context/actions as the MVP proof.
- Extend the existing Studio module registry pattern with agent contributions.

## Phase 1: Design & Contracts

Design artifacts:

- [data-model.md](./data-model.md)
- [contracts/agent-backend-api.md](./contracts/agent-backend-api.md)
- [contracts/studio-agent-sdk.md](./contracts/studio-agent-sdk.md)
- [contracts/workflow-agent-contract.md](./contracts/workflow-agent-contract.md)
- [quickstart.md](./quickstart.md)

## Post-Design Constitution Check

- **Modular UI contract**: PASS. The Studio SDK contract exposes explicit agent
  contribution registries and forbids module dependence on private assistant UI
  internals.
- **Workbench pattern fit**: PASS. Global assistant uses Command Center /
  bottom-panel behavior, while proposal review follows Split Inspector and
  diagnostics/logs patterns.
- **Typography and token discipline**: PASS. Contracts call out context chips,
  risk chips, message states, proposal cards, focus rings, and action controls
  as tokenized shared surfaces.
- **Accessible interaction**: PASS. Quickstart validation covers keyboard,
  focus, streaming announcements, disabled/unavailable states, and proposal
  approval semantics.
- **Real-screen proof**: PASS. Workflow explanation/troubleshooting/proposal
  review is the required MVP proof, with diagnostics, module management, and
  feature management as follow-on real screens.
