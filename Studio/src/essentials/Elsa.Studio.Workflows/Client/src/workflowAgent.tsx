import React, { lazy } from "react";
import type { StudioAgentClientExecutionResult, StudioAgentContextAttachment, StudioAgentPreparedInvocation, StudioAgentRegistry, StudioAgentResultRendererProps, StudioAgentToolAvailability } from "@elsa-workflows/studio-sdk";
import { workflowAgentCapabilities, workflowPromptStarters } from "./workflowAgentPromptStarters";

/** Whether a reviewed batch's whole-change Undo may act now; designer history decides (spec 1193 FR-009). */
export interface WorkflowBatchUndoAvailability {
  status: "available" | "unavailable";
  reason?: string;
  /** The user's explicit save covered the batch; null when this editor cannot know (its history is not the one that applied it). */
  persisted: boolean | null;
}

/**
 * The workflow library pages' agent state (spec 1193 slice E): the visible list, the open create dialog and the visible
 * template list by context kind, and each page's handler for its own working-state tool. A page sets and clears its own
 * entries; nothing here holds a live page object.
 */
export interface WorkflowLibraryAgentController {
  snapshots: Map<string, StudioAgentContextAttachment>;
  handlers: Map<string, (invocation: StudioAgentPreparedInvocation, signal?: AbortSignal) => Promise<StudioAgentClientExecutionResult>>;
  /** Each applied page change's revert, by its receipt's token: a message on success, or the reason it cannot revert now. */
  reverts: Map<string, () => string>;
  revert?: ((token: string) => string) | null;
}

/**
 * The read-only page contexts of spec 1193 story 8: the open Executable Inspector's exact Executable and the selected
 * persisted workflow version, each keyed by its page instance's generation so two mounted pages never replace each other.
 */
export interface WorkflowInspectionAgentController {
  executables: Map<string, StudioAgentContextAttachment>;
  versions: Map<string, StudioAgentContextAttachment>;
  /** The run page's exact Run and selected activity execution (spec 1193 slice G). */
  runs?: Map<string, StudioAgentContextAttachment>;
}

export interface WorkflowAgentController {
  snapshot: StudioAgentContextAttachment | null;
  /** The Executable Inspector's and version history's read-only contexts (spec 1193 slice F). */
  inspection?: WorkflowInspectionAgentController;
  /** The library pages' contexts (by kind and page generation) and working-state handlers (by tool and page generation). */
  library?: WorkflowLibraryAgentController;
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
  const controller: WorkflowAgentController = { snapshot: null, execute: null, undo: null, batch: null, targetAvailability: null,
    library: { snapshots: new Map(), handlers: new Map(), reverts: new Map(), revert: null },
    inspection: { executables: new Map(), versions: new Map(), runs: new Map() } };
  controller.library!.revert = token => controller.library!.reverts.get(token)?.() ?? "This change can no longer be reverted on this page.";
  agent.contextProviders.add({
    id: "workflows.active-draft", moduleId: "Elsa.Studio.Workflows", displayName: "Workflow working draft",
    surfaces: ["/workflows"], sensitivity: "internal",
    async collect() { return { attachments: controller.snapshot ? [controller.snapshot] : [], diagnostics: [] }; }
  });
  // The library list, the open create dialog and the template list (spec 1193 FR-020, FR-023, FR-024), each bounded and
  // attributed to its page's revision and client binding. The server admits them strictly (workflows.library-context).
  agent.contextProviders.add({
    id: "workflows.library-view", moduleId: "Elsa.Studio.Workflows", displayName: "Workflow library page",
    surfaces: ["/workflows"], sensitivity: "internal",
    async collect() { return { attachments: [...(controller.library?.snapshots.values() ?? [])], diagnostics: [] }; }
  });
  // The open Executable Inspector's exact Executable, bound to its Artifact Hash (spec 1193 FR-028). The Runtime-side server
  // package (Elsa.Agent.Workflows.Runtime) admits it; a host without it reports the context kind as unsupported.
  agent.contextProviders.add({
    id: "workflows.executable-inspector", moduleId: "Elsa.Studio.Workflows", displayName: "Executable Inspector",
    surfaces: ["/workflows"], sensitivity: "internal",
    async collect() { return { attachments: [...(controller.inspection?.executables.values() ?? [])], diagnostics: [] }; }
  });
  // The run page's exact Run and the activity execution selected on it (spec 1193 FR-029). The Runtime-side server package
  // admits it; a host without it reports the context kind as unsupported.
  agent.contextProviders.add({
    id: "workflows.run-inspector", moduleId: "Elsa.Studio.Workflows", displayName: "Workflow run",
    surfaces: ["/workflows"], sensitivity: "internal",
    async collect() { return { attachments: [...(controller.inspection?.runs?.values() ?? [])], diagnostics: [] }; }
  });
  // The selected persisted workflow version, bound to its version identity (spec 1193 FR-026).
  agent.contextProviders.add({
    id: "workflows.version-history", moduleId: "Elsa.Studio.Workflows", displayName: "Workflow version history",
    surfaces: ["/workflows"], sensitivity: "internal",
    async collect() { return { attachments: [...(controller.inspection?.versions.values() ?? [])], diagnostics: [] }; }
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
  // The library pages' own working state: a reviewed, revertible change applied only by the originating page. Library writes
  // (tags, favorites, folders, views) are server tools; they need no client contract here.
  agent.toolSlots.add({ id: "workflows.library-state", moduleId: "Elsa.Studio.Workflows", displayName: "Workflow library page state", surfaces: ["/workflows"], invocationModes: ["proposal"] });
  for (const [id, displayName, description, rendererId] of [
    ["workflow_library_set_list_state", "Adjust workflow list", "Apply one reviewed change to the visible workflow list only; revertible, nothing is saved.", "weaver.workflows.list-state-result"],
    ["workflow_create_dialog_prefill", "Prefill new workflow", "Fill the open new-workflow dialog; creating the workflow stays the user's Create action.", "weaver.workflows.create-dialog-result"]
  ] as const)
    agent.toolContracts.add({
      id, slotId: "workflows.library-state", moduleId: "Elsa.Studio.Workflows", displayName, description,
      surfaces: ["/workflows"], contractVersion: "1", executionLocation: "clientWorkingState", risk: "review-required",
      inputSchema: { type: "object" }, resourceTargetSchema: { type: "object" }, resultSchema: { type: "object" },
      invocationModes: ["proposal"], resultRendererIds: [rendererId],
      async executeClient(invocation, signal) {
        // The originating page instance, by the reviewed invocation's own client generation.
        const handler = controller.library?.handlers.get(`${id}:${invocation.client?.generationId ?? ""}`);
        if (!handler) return { outcome: "refused", summary: "The originating library page is unavailable. Nothing was applied." };
        return handler(invocation, signal);
      }
    });
  // The revert of an applied list or create-dialog change belongs to the module that owns those pages.
  agent.resultControls?.add({
    id: "workflows.agent.page-revert", moduleId: "Elsa.Studio.Workflows", displayName: "Revert page change",
    resultTypes: ["workflows.list-state", "workflows.create-dialog-prefill"], schemaVersions: ["1"],
    component: lazy(() => import("./workflowAgentResult").then(module => ({
      default: (props: StudioAgentResultRendererProps) => <module.WorkflowPageRevertControl {...props} controller={controller} />
    })))
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
  agent.resultControls?.add({
    id: "workflows.agent.batch-undo", moduleId: "Elsa.Studio.Workflows", displayName: "Workflow batch Undo",
    resultTypes: ["workflows.batch-result"], schemaVersions: ["1"],
    component: lazy(() => import("./workflowAgentResult").then(module => ({
      default: (props: StudioAgentResultRendererProps) => <module.WorkflowBatchUndoControl {...props} controller={controller} />
    })))
  });
  return controller;
}
