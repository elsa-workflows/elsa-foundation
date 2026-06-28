# Agent Foundation extension points

Owner project: `Elsa.Agent.Core`
Domain: `Elsa.Agent`

## Overridable contracts

### `IAgentSessionService`

Session and message storage boundary for provider-agnostic agent conversations.

### `IAgentPolicyEvaluator`

Policy decision boundary for capability and context-attachment checks before data reaches any provider.

### `IAgentContextCollector`

Aggregates context from registered context providers and applies policy/minimization decisions.

### `IAgentContextSanitizer`

Redacts or removes sensitive context before policy evaluation and provider use.

### `IAgentProposalService`

Stores approval state and coordinates reviewable proposal approval, denial, and execution.

### `IAgentActionProposalExecutor`

Executes an approved proposal. Default implementation is a no-op seam until a concrete workflow executor is selected.

### `IAgentStreamingService`

Streams provider-neutral agent events to API callers. Default implementation `DefaultAgentTurnOrchestrator` drives the multi-step agent loop (model step → tool execution → feed results back → repeat) and emits step/tool-call/plan/cancel events.

### `IAgentToolInvoker`

Applies policy to a tool invocation: read-only tools run inline; mutating tools become reviewable proposals when the policy requires approval, otherwise run inline (full-auto).

### `IAgentTurnRegistry`

Tracks in-flight turns by id so they can be cancelled out-of-band (the Stop button / cancel endpoint).

### `IAgentTurnStateStore`

Persists paused-turn state when a mutating tool awaits approval, enabling resume-after-approval.

### `IAgentFeedbackService`

Stores feedback for agent messages and sessions.

### `IAgentAuditSink`

Receives audit records for sessions, policy denials, proposals, execution, feedback, and provider diagnostics.

### `IAgentAuditReader`

Reads persisted audit records for Studio audit views.

### `IAgentProviderRegistry`

Exposes the single active agent provider (`Active`). Exactly one harness may be enabled per composition; registering more than one `IAgentProvider` fails fast with `AgentHarnessConflictException`.

## Implementable contributor interfaces

### `IAgentContextProvider`

Kind: Source (returns minimized context attachments for a scope).

Known implementations:

- `DefaultWorkflowAgentContextProvider` (`Elsa.Agent.Workflows`) *(cross-domain)* — supplies workflow definition context for workflow explain/troubleshoot/change proposal capabilities.

### `IAgentCapabilityProvider`

Kind: Source (returns provider-agnostic capability descriptors).

Known implementations:

- `WorkflowAgentCapabilityProvider` (`Elsa.Agent.Workflows`) *(cross-domain)* — contributes `workflow.explain`, `workflow.troubleshoot`, and `workflow.propose-change`.

### `IAgentTool`

Kind: Source (a server-side tool the agent loop can invoke). Read-only tools execute inline; mutating tools are routed through the proposal/approval flow by `IAgentToolInvoker`. Discovered via `IAgentToolRegistry`. Tools project to the shared currency `Microsoft.Extensions.AI.AIFunction` via `AgentToolAIFunctions`, so the same tool works whether the host orchestrator drives function-calling or a vendor harness (e.g. GitHub Copilot) auto-invokes it.

## Provider bridge contracts

### `IAgentProvider`

Kind: Bridge/adapter, **single-implementation (replacement) contract** — exactly one harness/provider is enabled per composition (enforced by `IAgentProviderRegistry`, which throws `AgentHarnessConflictException` on more than one). A concrete harness feature (e.g. GitHub Copilot) registers the one provider; the workflow feature contributes only capabilities/context. Deterministic providers are test-only seams and are not registered in shipped compositions.

Providers implement `ContinueTurnAsync(AgentTurnContext)`: given the turn history and any pending tool results, they yield the next step's message deltas and tool-call requests. The orchestrator owns the loop and tool execution.

Known implementations:

- `GitHubCopilotAgentProvider` (`Elsa.Agent.GitHubCopilot`) *(intra-domain — provider adapter)* — binds Elsa's provider-neutral agent facade to the GitHub Copilot SDK when explicitly enabled and authenticated, while keeping SDK tool mutation behind Elsa-owned proposal policy. Also implements `IAgentHarness` (below).
- `DeterministicAgentProvider` (`Elsa.Agent.Core`) *(intra-domain — test/default seam)* — deterministic provider implementation for backend contract validation without an external SDK.

### `IAgentHarness`

Kind: Bridge/adapter (a provider that owns its own multi-step agent loop). When a registered `IAgentProvider` also implements `IAgentHarness`, the orchestrator delegates the whole turn to `RunTurnAsync(AgentHarnessTurnRequest)` instead of driving the host step loop — the vendor harness runs tools, reasoning, and planning. `Capabilities` (`AgentHarnessCapabilities` flags: Tools/Reasoning/Usage/Plan/SubAgents/Mcp/Permissions) let the host render and degrade per provider rather than assume a uniform feature set. Custom tools cross the boundary as `AIFunction`s (see `IAgentTool`).

Known implementations:

- `GitHubCopilotAgentProvider` (`Elsa.Agent.GitHubCopilot`) — delegates to the GitHub Copilot SDK harness; our `IAgentTool`s are passed as auto-invoked `AIFunction`s via session config, and SDK tool-execution events map to the Studio timeline. Advertises Tools/Reasoning/Usage/Plan/Mcp/Permissions.
- `ClaudeAgentProvider` (`Elsa.Agent.Core`) *(stub)* — interface-complete placeholder advertising Tools only, proving the pluggable swap; wire an Anthropic `IChatClient` + `FunctionInvokingChatClient` over the same tool currency to enable.
