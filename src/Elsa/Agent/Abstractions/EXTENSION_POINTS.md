# Agent Foundation extension points

Owner project: `Elsa.Agent.Core`
Domain: `Elsa.Agent`

## Overridable contracts

### `IAgentSessionService`

Session and message storage boundary for provider-agnostic agent conversations, including atomic pending-message reservation for streaming.

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

Streams provider-neutral agent events to API callers.

### `IAgentFeedbackService`

Stores feedback for agent messages and sessions.

### `IAgentAuditSink`

Receives audit records for sessions, policy denials, proposals, execution, feedback, and provider diagnostics.

### `IAgentAuditReader`

Reads persisted audit records for Studio audit views.

### `IAgentProviderRegistry`

Resolves configured provider facades by provider ID.

## Implementable contributor interfaces

### `IAgentContextProvider`

Kind: Source (returns minimized context attachments for a scope).

Known implementations:

- `DefaultWorkflowAgentContextProvider` (`Elsa.Agent.Workflows`) *(cross-domain)* — supplies workflow definition context for workflow explain/troubleshoot/change proposal capabilities.

### `IAgentCapabilityProvider`

Kind: Source (returns provider-agnostic capability descriptors).

Known implementations:

- `WorkflowAgentCapabilityProvider` (`Elsa.Agent.Workflows`) *(cross-domain)* — contributes `workflow.explain`, `workflow.troubleshoot`, and `workflow.propose-change`.

## Provider bridge contracts

### `IAgentProvider`

Kind: Bridge/adapter (provider facade for external agent-provider SDK sessions, streaming, tool approval, and diagnostics).

Known implementations:

- `GitHubCopilotAgentProvider` (`Elsa.Agent.GitHubCopilot`) *(intra-domain — provider adapter)* — registers the GitHub Copilot provider seam and reports unavailable diagnostics until the SDK binding is configured.
- `DeterministicAgentProvider` (`Elsa.Agent.Core`) *(intra-domain — test/default seam)* — deterministic provider implementation for backend contract validation without an external SDK.
