# Workflow Agent extension points

Owner project: `Elsa.Agent.Workflows`
Domain: `Elsa.Agent.Workflows`

## Contributions to other domains

### `IAgentCapabilityProvider` *(Agent core contract — `Elsa.Agent.Core`)*

Kind: Source (returns workflow capability descriptors).

Known implementation:

- `WorkflowAgentCapabilityProvider` *(cross-domain)* — contributes `workflow.explain`, `workflow.troubleshoot`, and `workflow.propose-change`.

### `IAgentContextProvider` *(Agent core contract — `Elsa.Agent.Core`)*

Kind: Source (returns minimized workflow context attachments).

Known implementation:

- `DefaultWorkflowAgentContextProvider` *(cross-domain)* — returns workflow definition context with explicit redaction notes and no secrets/provider tokens/full execution payloads.

## Overridable contracts

### `IWorkflowAgentContextProvider`

Workflow-specific context shape provider for explanations, troubleshooting, and proposal grounding.

### `IWorkflowRevisionProvider`

Base revision lookup seam used to reject stale workflow-change proposals before approval or execution.

### `IWorkflowChangePermissionEvaluator`

Permission seam for checking whether an actor can propose a workflow change. The default denies all until wired to the owning authorization model.

### `IWorkflowChangeProposalService`

Creates reviewable `workflow.change` proposals after revision and permission validation.
