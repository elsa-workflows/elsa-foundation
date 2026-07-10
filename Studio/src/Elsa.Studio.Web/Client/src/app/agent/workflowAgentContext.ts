import type { StudioAgentContextProviderContribution, StudioAgentSurface, StudioWorkflowContextConnection, StudioWorkflowContextSnapshot } from "../../sdk";

// The snapshot contract (and the window global declaration) lives in the SDK so the writer — the
// Workflows module's context bridge — and this reader cannot drift; these aliases keep the names this
// module has always exported.
export type WorkflowGraphConnection = StudioWorkflowContextConnection;
export type WorkflowAgentContextSnapshot = StudioWorkflowContextSnapshot;

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
        scope: snapshot.selectedNodeId ? "selection" : "screen",
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
