import type { StudioAgentContextProviderContribution, StudioAgentSurface } from "../../sdk";

export interface WorkflowAgentContextSnapshot {
  workflowId: string;
  workflowDefinitionId?: string;
  workflowVersionId?: string | null;
  draftId?: string | null;
  revision?: string | null;
  version?: string;
  selectedNodeId?: string | null;
  selectedActivityType?: string | null;
  selectedActivityId?: string;
  summary?: string;
  activities?: Array<{ id: string; type: string; displayName?: string }>;
  connections?: unknown[];
  diagnostics?: Array<{ severity: string; message: string }>;
}

declare global {
  interface Window {
    __ELSA_STUDIO_WORKFLOW_CONTEXT__?: WorkflowAgentContextSnapshot;
  }
}

export function createWorkflowAgentContextProvider(getSnapshot: () => WorkflowAgentContextSnapshot | null = getWindowWorkflowSnapshot): StudioAgentContextProviderContribution {
  return {
    id: "workflow.context",
    displayName: "Active workflow",
    surfaces: ["/workflows"],
    sensitivity: "internal",
    async collect({ surface }) {
      const snapshot = getSnapshot();
      if (!snapshot || !isWorkflowSurface(surface)) {
        return [];
      }

      return [{
        id: `workflow:${snapshot.workflowId}`,
        source: "workflow",
        sourceId: snapshot.workflowId,
        label: snapshot.summary ?? "Active workflow",
        contentType: "workflow.definition",
        sensitivity: "internal",
        scope: snapshot.selectedActivityId ? "selection" : "screen",
        content: snapshot
      }];
    }
  };
}

function getWindowWorkflowSnapshot() {
  return window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ ?? null;
}

function isWorkflowSurface(surface: StudioAgentSurface) {
  return surface.route === "/workflows" || surface.route.startsWith("/workflows/");
}
