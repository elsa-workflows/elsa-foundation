import { useEffect, useMemo, useRef } from "react";
import type { ActivityCatalogItem, ActivityNode, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import type { StudioActivityDescriptor } from "@elsa-workflows/studio-sdk";
import { collectWorkflowContextActivities, collectWorkflowContextConnections, getDraftRevision } from "./editorHelpers";
import { authSessionEndedEvent, authSessionStartedEvent } from "@elsa-workflows/studio-sdk";
import type { WorkflowAgentController } from "../workflowAgent";
import { indexActivityPresentation } from "../activityPresentation";

interface WorkflowContextBridgeParams {
  controller?: WorkflowAgentController;
  clientId: string;
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

// Owns a bounded snapshot in the native module instance; no global window context.
export function useWorkflowContextBridge({
  controller, clientId,
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
  const activities = useMemo(
    () => draft
      ? collectWorkflowContextActivities(
          draft.state.rootActivity,
          catalogByVersion,
          [],
          indexActivityPresentation(draft.activityPresentation))
      : [],
    [catalogByVersion, draft]);
  const connections = useMemo(() => draft ? collectWorkflowContextConnections(draft.state.rootActivity, catalogByVersion) : [], [catalogByVersion, draft]);
  const diagnostics = useMemo(
    () => (draft?.validationErrors ?? []).map(error => ({ severity: error.code ?? "warning", message: error.message ?? "Workflow validation issue." })),
    [draft]
  );

  const generation = useMemo(() => globalThis.crypto?.randomUUID?.() ?? `generation-${Date.now()}-${Math.random()}`, [details?.definition?.id, selectedNodeId]);
  const authorized = useRef(true);
  useEffect(() => {
    const end = () => { authorized.current = false; if (controller) controller.snapshot = null; };
    // A new authentication session requires a fresh editor lifetime; old prepared actions stay invalid.
    window.addEventListener(authSessionEndedEvent, end); window.addEventListener(authSessionStartedEvent, end);
    return () => { window.removeEventListener(authSessionEndedEvent, end); window.removeEventListener(authSessionStartedEvent, end); };
  }, [controller]);
  useEffect(() => {
    if (!controller || !details?.definition || !draft || !authorized.current) return;
    const target = { moduleId: "Elsa.Studio.Workflows", resourceType: "workflow-definition", resourceId: details.definition.id, revision: getDraftRevision(draft), displayName: details.definition.name };
    const attachment = {
      id: `workflow:${details.definition.id}`, moduleId: "Elsa.Studio.Workflows", schemaVersion: "1", target,
      source: "workflow", sourceId: details.definition.id, label: details.definition.name, contentType: "workflow.definition", sensitivity: "internal" as const,
      scope: selectedNodeId ? "selection" : "screen", origin: "clientSnapshot" as const, client: { clientId, generationId: generation }, agentScopes: ["workflow-author"],
      summary: details.definition.name, references: { workflowDefinitionId: details.definition.id, draftId: draft.id, revision: target.revision },
      content: { workflowDefinitionId: details.definition.id, draftId: draft.id, revision: target.revision, selectedNodeId,
        selectedActivityType: resolveActivityType(selectedNode, selectedDescriptor, catalogByVersion),
        inspectedNodeId: inspectedNode?.nodeId ?? null, inspectedActivityType: resolveActivityType(inspectedNode, inspectedDescriptor, catalogByVersion), inspectedIsScopeOwner,
        activities, connections, diagnostics, positions: draft.layout.map(item => ({ nodeId: item.nodeId, x: item.x, y: item.y })) }
    };
    controller.snapshot = attachment;
    return () => { if (controller.snapshot === attachment) controller.snapshot = null; };
  }, [activities, catalogByVersion, clientId, connections, controller, details, diagnostics, draft, generation, inspectedDescriptor, inspectedIsScopeOwner, inspectedNode, selectedDescriptor, selectedNode, selectedNodeId]);
}
