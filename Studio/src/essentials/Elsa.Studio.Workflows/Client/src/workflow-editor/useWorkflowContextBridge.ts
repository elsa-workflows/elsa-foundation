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

  const selectionKey = JSON.stringify([details?.definition?.id, selectedNodeId]);
  const activeGeneration = useRef<string | null>(null);
  const authorized = useRef(true);
  const committedBinding = useRef<{ controller: WorkflowAgentController | undefined; clientId: string } | null>(null);
  useEffect(() => {
    const previous = committedBinding.current;
    if (!previous || previous.controller !== controller || previous.clientId !== clientId) {
      committedBinding.current = { controller, clientId };
      authorized.current = true;
    }
    // A retained editor may lose its effects and auth listeners while hidden. Reactivating those
    // effects creates a new origin even when its selection, controller and browser binding survive.
    const generation = globalThis.crypto?.randomUUID?.() ?? `generation-${Date.now()}-${Math.random()}`;
    activeGeneration.current = generation;
    const end = () => { authorized.current = false; if (controller) controller.snapshot = null; };
    // Authentication invalidates this binding. Only an explicitly new committed binding (or a new
    // editor mount) restores it; effect replay and ordinary draft updates cannot restore authority.
    window.addEventListener(authSessionEndedEvent, end); window.addEventListener(authSessionStartedEvent, end);
    return () => {
      if (activeGeneration.current === generation) activeGeneration.current = null;
      window.removeEventListener(authSessionEndedEvent, end); window.removeEventListener(authSessionStartedEvent, end);
    };
  }, [controller, clientId, selectionKey]);
  useEffect(() => {
    // Selection changes reactivate the lifetime effect; ordinary draft updates reuse its origin.
    void selectionKey;
    const generation = activeGeneration.current;
    if (!controller || !details?.definition || !draft || !authorized.current || !generation) return;
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
  }, [activities, catalogByVersion, clientId, connections, controller, details, diagnostics, draft, inspectedDescriptor, inspectedIsScopeOwner, inspectedNode, selectedDescriptor, selectedNode, selectedNodeId, selectionKey]);
}
