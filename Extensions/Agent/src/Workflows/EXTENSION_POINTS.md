# Workflow Agent extension points

Owner project: `Elsa.Agent.Workflows`
Domain: `Elsa.Agent.Workflows`

## Cross-domain contributions

The module contributes to [Agent foundation](../Elsa.Agent/EXTENSION_POINTS.md) through contracts in `Elsa.Agent.Core`.

- `IAgentContextProvider`: `DefaultWorkflowAgentContextProvider` admits `workflow.definition` schema `1` for exact `workflow-definition` targets. It whitelists bounded activity identities, connections and positions, stamps caller material as `ClientSnapshot`, and emits installed guidance separately. Property values, caller permission claims and arbitrary diagnostics are excluded. A separate `workflow.request` schema `1` admits only bounded intent/name/description/reference metadata with no target, client binding or tool scopes; it never supplies graph authority. Other workflow kinds and versions produce explicit diagnostics.
- `IAgentCapabilityProvider`: `WorkflowAgentCapabilityProvider` declares explanation of the admitted graph and a reviewed position-change journey. It does not advertise runtime investigation, activity creation, save or publish.
- `IAgentTool`: `WorkflowReadGraphTool` returns a typed `workflows.graph` result. `WorkflowSetDesignerPositionTool` prepares exact `activityId`, finite `x`/`y`, target revision and originating client generation; it requires current `workflows/design:manage` permission and explicit review even under FullAuto.
- `IAgentClientTool`: the same layout tool admits the owning editor's observed result as `workflows.layout-change` schema `1`, renderer `workflows.agent.layout-result`. The receipt proves unsaved local state only. Native editor application and whole-batch undo remain module-owned; save and publish remain explicit.

The named context/capability/tool fan-in and generic review lifecycle are owned by Agent foundation. This module registers contributions, not additional aggregating handlers or provider loops.

## Legacy domain seams

`IWorkflowAgentContextProvider` retains the minimized workflow-specific context view. `IWorkflowActivityCatalogProvider` currently returns no invented catalog entries. `IWorkflowRevisionProvider` has no authoritative unsaved-editor revision source; exact layout preparation uses the admitted client snapshot and the owning editor rechecks its live revision before application.

`IWorkflowChangePermissionEvaluator` defaults to denial. The legacy broad `IWorkflowChangeProposalService` returns `agent.workflow.operation_unsupported`; broader graph authoring is not a second proposal engine. The retained domain graph vocabulary and classifier do not advertise unsupported activity-creation journeys. Future workflow work must connect the real catalog and owning resource before enabling them.
