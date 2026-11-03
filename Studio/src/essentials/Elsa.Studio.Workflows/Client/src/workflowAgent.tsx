import React, { lazy } from "react";
import type { StudioAgentClientExecutionResult, StudioAgentContextAttachment, StudioAgentPreparedInvocation, StudioAgentRegistry, StudioAgentResultRendererProps, StudioAgentToolAvailability } from "@elsa-workflows/studio-sdk";
import { workflowAgentCapabilities, workflowPromptStarters } from "./workflowAgentPromptStarters";

/** Whether a reviewed batch's whole-change Undo may act now; designer history decides (spec 1193 FR-009). */
export interface WorkflowBatchUndoAvailability {
  status: "available" | "unavailable";
  reason?: string;
  /** The user's explicit save covered the batch. */
  persisted: boolean;
}

export interface WorkflowAgentController {
  snapshot: StudioAgentContextAttachment | null;
  execute: ((invocation: StudioAgentPreparedInvocation, signal?: AbortSignal) => Promise<StudioAgentClientExecutionResult>) | null;
  undo: ((token: string) => Promise<string>) | null;
  /** Designer-history Undo of a reviewed Operation Batch, owned by the open editor. */
  batch?: {
    state(token: string): WorkflowBatchUndoAvailability;
    undo(token: string): Promise<string>;
    subscribe(listener: () => void): () => void;
  } | null;
  /** Whether a prepared invocation's originating editor still exists, before anything is dispatched. */
  targetAvailability?: ((invocation: StudioAgentPreparedInvocation) => StudioAgentToolAvailability) | null;
}

/** Registration closes over one native module instance; live editor objects never enter shared state. */
export function registerWorkflowAgent(agent: StudioAgentRegistry): WorkflowAgentController {
  const controller: WorkflowAgentController = { snapshot: null, execute: null, undo: null, batch: null, targetAvailability: null };
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
  // The reviewed Operation Batch (spec 1193 FR-007). The designer applies it atomically to the originating working draft
  // as one history entry; the strict schema is enforced by the server and again by the applier. Weaver never saves.
  agent.toolContracts.add({
    id: "workflow_apply_operation_batch", slotId: "workflows.working-state", moduleId: "Elsa.Studio.Workflows",
    displayName: "Change workflow draft", description: "Apply one reviewed Operation Batch to the exact unsaved working draft as one Undo step.",
    surfaces: ["/workflows"], contractVersion: "1", executionLocation: "clientWorkingState", risk: "review-required",
    inputSchema: { type: "object" }, resourceTargetSchema: { type: "object" }, resultSchema: { type: "object" },
    invocationModes: ["proposal"], resultRendererIds: ["weaver.workflows.batch-result"],
    clientTargetAvailability(invocation) {
      return controller.targetAvailability?.(invocation) ?? { status: "unavailable", reason: "The originating workflow editor is unavailable. Nothing was applied." };
    },
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
  // The designer-history Undo of an applied batch belongs to the module that owns the designer. It is a result control:
  // the host shows it after whichever renderer presents the batch result (Weaver Workflows' card).
  agent.resultControls.add({
    id: "workflows.agent.batch-undo", moduleId: "Elsa.Studio.Workflows", displayName: "Workflow batch Undo",
    resultTypes: ["workflows.batch-result"], schemaVersions: ["1"],
    component: lazy(() => import("./workflowAgentResult").then(module => ({
      default: (props: StudioAgentResultRendererProps) => <module.WorkflowBatchUndoControl {...props} controller={controller} />
    })))
  });
  return controller;
}
