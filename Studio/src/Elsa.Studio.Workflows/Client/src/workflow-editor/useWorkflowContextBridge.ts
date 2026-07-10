import { useEffect, useMemo } from "react";
import type { ActivityCatalogItem, ActivityNode, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import type { StudioActivityDescriptor } from "@elsa-workflows/studio-sdk";
import { collectWorkflowContextActivities, collectWorkflowContextConnections, getDraftRevision } from "./editorHelpers";

interface WorkflowContextBridgeParams {
  details: WorkflowDefinitionDetails | null;
  draft: WorkflowDraft | null;
  selectedNode: ActivityNode | null;
  selectedNodeId: string | null;
  selectedDescriptor: StudioActivityDescriptor | null;
  inspectedNode: ActivityNode | null;
  inspectedDescriptor: StudioActivityDescriptor | null;
  inspectedIsScopeOwner: boolean;
  catalogByVersion: Map<string, ActivityCatalogItem>;
}

function resolveActivityType(node: ActivityNode | null, descriptor: StudioActivityDescriptor | null, catalogByVersion: Map<string, ActivityCatalogItem>) {
  return descriptor?.typeName ?? (node ? catalogByVersion.get(node.activityVersionId)?.activityTypeKey ?? node.activityVersionId : null);
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
  inspectedNode,
  inspectedDescriptor,
  inspectedIsScopeOwner,
  catalogByVersion
}: WorkflowContextBridgeParams) {
  // The full-tree walks depend only on the draft + catalog; memoized so selection/inspection changes
  // (which re-fire the effect) only rewrite the scalar snapshot fields.
  const activities = useMemo(() => draft ? collectWorkflowContextActivities(draft.state.rootActivity, catalogByVersion) : [], [catalogByVersion, draft]);
  const connections = useMemo(() => draft ? collectWorkflowContextConnections(draft.state.rootActivity, catalogByVersion) : [], [catalogByVersion, draft]);
  const diagnostics = useMemo(
    () => (draft?.validationErrors ?? []).map(error => ({ severity: error.code ?? "warning", message: error.message ?? "Workflow validation issue." })),
    [draft]
  );

  useEffect(() => {
    if (!details || !draft) return;

    window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = {
      workflowId: details.definition.id,
      workflowDefinitionId: details.definition.id,
      workflowVersionId: draft.sourceVersionId ?? null,
      draftId: draft.id,
      revision: getDraftRevision(draft),
      selectedNodeId,
      selectedActivityType: resolveActivityType(selectedNode, selectedDescriptor, catalogByVersion),
      inspectedNodeId: inspectedNode?.nodeId ?? null,
      inspectedActivityType: resolveActivityType(inspectedNode, inspectedDescriptor, catalogByVersion),
      inspectedIsScopeOwner,
      summary: details.definition.name,
      activities,
      connections,
      diagnostics
    };

    return () => {
      if (window.__ELSA_STUDIO_WORKFLOW_CONTEXT__?.workflowId === details.definition.id) {
        window.__ELSA_STUDIO_WORKFLOW_CONTEXT__ = undefined;
      }
    };
  }, [activities, catalogByVersion, connections, details, diagnostics, draft, inspectedDescriptor, inspectedIsScopeOwner, inspectedNode, selectedDescriptor, selectedNode, selectedNodeId]);
}
