# GitHub Copilot agent provider

`Elsa.Agent.GitHubCopilot` owns the provider-specific bridge between the
provider-neutral agent contracts in `Elsa.Agent.Core` and the
GitHub Copilot SDK.

## Current MVP status

The exact GitHub Copilot SDK package/API is intentionally not hard-coded in this
slice. `GitHubCopilotAgentProvider` is a registered facade with provider ID
`github-copilot`; it reports unavailable diagnostics and returns a user-safe stream
error until a concrete SDK binding is supplied.

Studio and browser modules must continue to call only `/_elsa/agent/*` endpoints.
Provider tokens, SDK sessions, tool approvals, and provider diagnostics stay behind
this backend facade.

## Binding checklist

When the SDK surface is selected, replace the stub implementation behind
`IAgentProvider` without changing Studio-facing DTOs:

1. Create provider sessions from `AgentSession` using backend-owned credentials or
   provider profile references. Do not return secrets to Studio.
2. Stream SDK deltas as provider-neutral `AgentStreamEvent` values.
3. Normalize provider errors to `AgentError` / user-safe API problem details.
4. Route SDK tool approval callbacks through `AgentProviderToolApprovalRequest`.
5. Emit diagnostics through `AgentProviderDiagnostics` without secret material.
6. Keep workflow mutations review-first: SDK output may create
   `AgentActionProposal`, but execution must go through proposal approval,
   revision validation, permission checks, and audit.

For deterministic backend contract validation without the real SDK, register
`DeterministicAgentProvider` from `Elsa.Agent.Core.Services` in
test hosts.
