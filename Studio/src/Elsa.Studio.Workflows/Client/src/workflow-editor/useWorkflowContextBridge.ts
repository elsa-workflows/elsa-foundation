import { useEffect } from "react";
import type { ActivityCatalogItem, ActivityNode, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import type { StudioActivityDescriptor } from "@elsa-workflows/studio-sdk";
import { collectWorkflowContextActivities, collectWorkflowContextConnections, getDraftRevision } from "./editorHelpers";

interface WorkflowContextBridgeParams {
  details: WorkflowDefinitionDetails | null;
  draft: WorkflowDraft | null;
  selectedNode: ActivityNode | null;
  selectedNodeId: string | null;
  selectedDescriptor: StudioActivityDescriptor | null;
  catalogByVersion: Map<string, ActivityCatalogItem>;
}

// Publishes a snapshot of the open workflow (identity, selection, activities, connections, diagnostics)
// onto `window.__ELSA_STUDIO_WORKFLOW_CONTEXT__` so Weaver's out-of-band tooling can read the live design.
// Kept in sync with the current draft/selection and cleared when the editor unmounts or switches workflow.
export function useWorkflowContextBridge({
  details,
  draft,
  selectedNode,
  selectedNodeId,
  selectedDescriptor,
  catalogByVersion
}: WorkflowContextBridgeParams) {
  useEffect(() => {
    if (!details || !draft) return;

    window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
      workflowId: details.definition.id,
      workflowDefinitionId: details.definition.id,
      workflowVersionId: draft.sourceVersionId ?? null,
      draftId: draft.id,
      revision: getDraftRevision(draft),
      selectedNodeId,
      selectedActivityType: selectedDescriptor?.typeName ?? (selectedNode ? catalogByVersion.get(selectedNode.activityVersionId)?.activityTypeKey ?? selectedNode.activityVersionId : null),
      summary: details.definition.name,
      activities: collectWorkflowContextActivities(draft.state.rootActivity, catalogByVersion),
      connections: collectWorkflowContextConnections(draft.state.rootActivity),
      diagnostics: draft.validationErrors.map(error => ({ severity: error.code ?? "warning", message: error.message ?? "Workflow validation issue." }))
    };

    return () => {
      if (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === details.definition.id) {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = undefined;
      }
    };
  }, [catalogByVersion, details, draft, selectedDescriptor, selectedNode, selectedNodeId]);
}
