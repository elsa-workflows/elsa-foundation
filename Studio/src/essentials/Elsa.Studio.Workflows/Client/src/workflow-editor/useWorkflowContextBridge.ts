import { useEffect, useMemo, useRef } from "react";
import type { ActivityCatalogItem, ActivityNode, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import type { StudioActivityDescriptor } from "@elsa-workflows/studio-sdk";
import { collectWorkflowContextActivities, collectWorkflowContextConnections, collectWorkflowContextStructure, getDraftContentRevision, type WorkflowContextStructure } from "./editorHelpers";
import { readSecretReference, type SecretReferenceView } from "../maskedInput";
import { getChildSlots } from "../workflowAdapter";
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
  /** True while the designer shows a historical (read-only) snapshot instead of the editable working draft. */
  historical?: boolean;
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
  catalogByVersion,
  historical = false
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
  // Structure (spec 1193 FR-014): each activity's exact version, parent and slot, and its own slots, so an Operation
  // Batch can name an explicit scope path and slot. Compact, by index into `activities`, so it stays small.
  const structure = useMemo(() => draft ? compactWorkflowStructure(activities, collectWorkflowContextStructure(draft.state.rootActivity, catalogByVersion)) : null,
    [activities, catalogByVersion, draft]);
  // Visible Secret References are those the draft already binds: names, types and scopes only, never a value.
  const secretReferences = useMemo(() => draft ? collectVisibleSecretReferences(draft.state.rootActivity, catalogByVersion) : [], [catalogByVersion, draft]);
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
    const target = { moduleId: "Elsa.Studio.Workflows", resourceType: "workflow-definition", resourceId: details.definition.id, revision: getDraftContentRevision(draft), displayName: details.definition.name };
    const attachment = {
      id: `workflow:${details.definition.id}`, moduleId: "Elsa.Studio.Workflows", schemaVersion: "1", target,
      source: "workflow", sourceId: details.definition.id, label: details.definition.name, contentType: "workflow.definition", sensitivity: "internal" as const,
      scope: selectedNodeId ? "selection" : "screen", origin: "clientSnapshot" as const, client: { clientId, generationId: generation }, agentScopes: ["workflow-author"],
      summary: details.definition.name, references: { workflowDefinitionId: details.definition.id, draftId: draft.id, revision: target.revision },
      content: { workflowDefinitionId: details.definition.id, draftId: draft.id, revision: target.revision, selectedNodeId,
        selectedActivityType: resolveActivityType(selectedNode, selectedDescriptor, catalogByVersion),
        inspectedNodeId: inspectedNode?.nodeId ?? null, inspectedActivityType: resolveActivityType(inspectedNode, inspectedDescriptor, catalogByVersion), inspectedIsScopeOwner,
        workingState: historical ? "historical" : "draft",
        activities, connections, diagnostics, positions: draft.layout.map(item => ({ nodeId: item.nodeId, x: item.x, y: item.y })), secretReferences,
        structure } as Record<string, unknown>
    };
    // The server admits at most 64 KiB of snapshot content. Structure is left out rather than letting a draft that fits
    // without it be refused: explanation keeps working, and an Operation Batch then says the structure is required.
    if (JSON.stringify(attachment.content).length > workflowSnapshotContentLimit - workflowSnapshotStructureMargin) delete attachment.content.structure;
    controller.snapshot = attachment;
    return () => { if (controller.snapshot === attachment) controller.snapshot = null; };
  }, [activities, catalogByVersion, clientId, connections, controller, details, diagnostics, draft, historical, inspectedDescriptor, inspectedIsScopeOwner, inspectedNode, secretReferences, selectedDescriptor, selectedNode, selectedNodeId, selectionKey, structure]);
}

/** The server's bound on `workflow.definition` content (`WorkflowAgentSnapshot`), in UTF-16 code units of its JSON text. */
export const workflowSnapshotContentLimit = 65536;
/** Headroom for differences between this serialization and the one the server measures. */
const workflowSnapshotStructureMargin = 2048;

/**
 * The compact containment structure: activity indexes grouped by exact version, and the root plus every activity with
 * slots, each with its parent, its slot in the parent and its slots' member indexes.
 */
export function compactWorkflowStructure(activities: Array<{ id: string }>, structure: Map<string, WorkflowContextStructure>) {
  const indexOf = new Map(activities.map((activity, index) => [activity.id, index]));
  const versions = new Map<string, number[]>();
  activities.forEach((activity, index) => {
    const versionId = structure.get(activity.id)?.activityVersionId;
    if (versionId) versions.set(versionId, [...(versions.get(versionId) ?? []), index]);
  });
  const containers = activities.flatMap(activity => {
    const node = structure.get(activity.id);
    if (!node || node.parentId !== null && node.slots.length === 0) return [];
    return [{ id: activity.id, parentId: node.parentId, slot: node.slot, slots: node.slots.map(slot => ({ ...slot,
      nodes: activities.flatMap(child => { const member = structure.get(child.id); return member?.parentId === activity.id && member.slot === slot.id ? [indexOf.get(child.id)!] : []; }) })) }];
  });
  return { versions: [...versions].map(([id, nodes]) => ({ id, nodes })), containers };
}

/** The distinct Secret References the draft binds, bounded to 64. A Secret Reference names a secret; it holds no value. */
function collectVisibleSecretReferences(root: ActivityNode | null | undefined, catalogByVersion: Map<string, ActivityCatalogItem>) {
  const found = new Map<string, SecretReferenceView>();
  const visit = (activity: ActivityNode) => {
    for (const value of Object.values(activity)) {
      if (found.size >= 64) return;
      if (!value || typeof value !== "object" || !("expression" in value)) continue;
      const expression = (value as { expression?: { type?: unknown; value?: unknown } }).expression;
      const reference = readSecretReference(typeof expression?.type === "string" ? expression.type : null, expression?.value);
      if (reference) found.set(JSON.stringify([reference.name, reference.typeName, reference.scope]), reference);
    }
    for (const slot of getChildSlots(activity, catalogByVersion)) for (const child of slot.activities) visit(child);
  };
  if (root) visit(root);
  return [...found.values()];
}
