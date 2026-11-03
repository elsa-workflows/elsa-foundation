import React, { lazy } from "react";
import type { StudioAgentClientExecutionResult, StudioAgentContextAttachment, StudioAgentPreparedInvocation, StudioAgentRegistry, StudioAgentResultRendererProps } from "@elsa-workflows/studio-sdk";
import { workflowAgentCapabilities, workflowPromptStarters } from "./workflowAgentPromptStarters";

export interface WorkflowAgentController {
  snapshot: StudioAgentContextAttachment | null;
  execute: ((invocation: StudioAgentPreparedInvocation, signal?: AbortSignal) => Promise<StudioAgentClientExecutionResult>) | null;
  undo: ((token: string) => Promise<string>) | null;
}

/** Registration closes over one native module instance; live editor objects never enter shared state. */
export function registerWorkflowAgent(agent: StudioAgentRegistry): WorkflowAgentController {
  const controller: WorkflowAgentController = { snapshot: null, execute: null, undo: null };
  agent.contextProviders.add({
    id: "workflows.active-draft", moduleId: "Elsa.Studio.Workflows", displayName: "Workflow working draft",
    surfaces: ["/workflows"], sensitivity: "internal",
    async collect() { return { attachments: controller.snapshot ? [controller.snapshot] : [], diagnostics: [] }; }
  });
  for (const capability of workflowAgentCapabilities) agent.capabilities.add({ ...capability, moduleId: "Elsa.Studio.Workflows" });
  for (const starter of workflowPromptStarters) agent.promptStarters.add({ ...starter, moduleId: "Elsa.Studio.Workflows" });
  agent.toolSlots.add({ id: "workflows.working-state", moduleId: "Elsa.Studio.Workflows", displayName: "Workflow working state", surfaces: ["/workflows"], invocationModes: ["proposal"] });
  agent.toolContracts.add({
    id: "workflow_set_designer_position", slotId: "workflows.working-state", moduleId: "Elsa.Studio.Workflows",
    displayName: "Move workflow activity", description: "Prepare a reviewed layout change to the exact working draft.",
    surfaces: ["/workflows"], contractVersion: "1", executionLocation: "clientWorkingState", risk: "review-required",
    inputSchema: { type: "object" }, resourceTargetSchema: { type: "object" }, resultSchema: { type: "object" },
    invocationModes: ["proposal"], resultRendererIds: ["workflows.agent.layout-result"],
    async executeClient(invocation, signal) {
      if (!controller.execute) return { outcome: "refused", summary: "The originating workflow editor is unavailable." };
      return controller.execute(invocation, signal);
    }
  });
  agent.resultRenderers.add({
    id: "workflows.agent.layout-result", moduleId: "Elsa.Studio.Workflows", displayName: "Workflow layout change",
    resultTypes: ["workflows.layout-change"], schemaVersions: ["1"],
    component: lazy(() => import("./workflowAgentResult").then(module => ({
      default: (props: StudioAgentResultRendererProps) => <module.WorkflowLayoutResult {...props} controller={controller} />
    })))
  });
  return controller;
}
