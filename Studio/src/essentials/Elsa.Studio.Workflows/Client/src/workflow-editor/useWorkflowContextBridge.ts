import { useEffect, useMemo, useRef } from "react";
import type { ActivityCatalogItem, ActivityNode, ValidationError, WorkflowDefinitionDetails, WorkflowDraft } from "../workflowTypes";
import type { StudioActivityDescriptor } from "@elsa-workflows/studio-sdk";
import { collectWorkflowContextActivities, collectWorkflowContextConnections, collectWorkflowContextStructure, getDraftContentRevision, type WorkflowContextStructure } from "./editorHelpers";
import { readSecretReference, type SecretReferenceView } from "../maskedInput";
import { getChildSlots } from "../workflowAdapter";
import { authSessionEndedEvent, authSessionStartedEvent } from "@elsa-workflows/studio-sdk";
import type { WorkflowAgentController } from "../workflowAgent";
import { indexActivityPresentation } from "../activityPresentation";
import { admittedSnapshotLength, collectGraphActivityReferences, collectSelectedActivityContext, collectWorkflowContextDiagnostics } from "./workflowExplainContext";

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
  /**
   * The validation the designer shows for this working state (its panel's combined, humanized list). Explanations use only
   * these (spec 1193 FR-018); without it the draft's own reconciled errors are disclosed.
   */
  validationErrors?: ValidationError[] | null;
  /** A template editor's own resource (FR-024); absent for a workflow definition draft. */
  agentResource?: { resourceType: "workflow-template"; contentType: "workflows.template-editor"; revision(): string | null };
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
  historical = false,
  validationErrors,
  agentResource
}: WorkflowContextBridgeParams) {
  // The full-tree walks depend only on the draft + catalog; memoized so selection/inspection changes
  // (which re-fire the effect) only rewrite the scalar snapshot fields.
  const parts = useMemo(() => draft ? collectWorkflowSnapshotParts(draft, catalogByVersion) : null, [catalogByVersion, draft]);

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
    // A template is its own resource: its identity and persisted revision replace the definition and draft (FR-005, FR-024).
    const templateRevision = agentResource?.revision() ?? null;
    if (agentResource && !templateRevision) return;
    const target = { moduleId: "Elsa.Studio.Workflows", resourceType: agentResource?.resourceType ?? "workflow-definition", resourceId: details.definition.id, revision: getDraftContentRevision(draft), displayName: details.definition.name };
    const identity: Record<string, string> = agentResource ? { templateId: details.definition.id, templateRevision: templateRevision! } : { workflowDefinitionId: details.definition.id, draftId: draft.id };
    const attachment = {
      id: `${agentResource ? "workflow-template" : "workflow"}:${details.definition.id}`, moduleId: "Elsa.Studio.Workflows", schemaVersion: "1", target,
      source: "workflow", sourceId: details.definition.id, label: details.definition.name, contentType: agentResource?.contentType ?? "workflow.definition", sensitivity: "internal" as const,
      scope: selectedNodeId ? "selection" : "screen", origin: "clientSnapshot" as const, client: { clientId, generationId: generation }, agentScopes: ["workflow-author"],
      summary: details.definition.name, references: { ...identity, revision: target.revision },
      content: buildWorkflowSnapshotContent(draft, catalogByVersion, {
        header: { ...identity, revision: target.revision, selectedNodeId,
          selectedActivityType: resolveActivityType(selectedNode, selectedDescriptor, catalogByVersion),
          inspectedNodeId: inspectedNode?.nodeId ?? null, inspectedActivityType: resolveActivityType(inspectedNode, inspectedDescriptor, catalogByVersion), inspectedIsScopeOwner,
          workingState: historical ? "historical" : "draft" },
        validationErrors, selectedNode, selectedNodeId, selectedDescriptor, parts: parts ?? undefined
      })
    };
    controller.snapshot = attachment;
    return () => { if (controller.snapshot === attachment) controller.snapshot = null; };
  }, [agentResource, catalogByVersion, clientId, controller, details, draft, historical, inspectedDescriptor, inspectedIsScopeOwner, inspectedNode, parts, selectedDescriptor, selectedNode, selectedNodeId, selectionKey, validationErrors]);
}

/** The draft-derived walks of the snapshot, which depend only on the draft and the catalog (so the bridge memoizes them). */
export interface WorkflowSnapshotParts {
  activities: Array<{ id: string; type: string; displayName?: string; description?: string }>;
  connections: ReturnType<typeof collectWorkflowContextConnections>;
  positions: Array<{ nodeId: string; x: number; y: number }>;
  secretReferences: SecretReferenceView[];
  structure: ReturnType<typeof compactWorkflowStructure>;
  versionOf: Map<string, string>;
}

/**
 * The draft-derived part of the snapshot: activities, connections, positions, visible Secret References and (spec 1193
 * FR-014) the compact structure, so an Operation Batch can name an explicit scope path and slot.
 */
export function collectWorkflowSnapshotParts(draft: WorkflowDraft, catalogByVersion: Map<string, ActivityCatalogItem>): WorkflowSnapshotParts {
  const activities = collectWorkflowContextActivities(draft.state.rootActivity, catalogByVersion, [], indexActivityPresentation(draft.activityPresentation));
  const byNode = collectWorkflowContextStructure(draft.state.rootActivity, catalogByVersion);
  const structure = compactWorkflowStructure(activities, byNode, [...(draft.layout ?? []), ...(draft.activityPresentation ?? [])].map(record => record.nodeId));
  const placed = new Set(activities.map(activity => activity.id));
  return {
    activities,
    connections: collectWorkflowContextConnections(draft.state.rootActivity, catalogByVersion),
    // Only placed activities: a layout record the draft still holds for a removed node (kept by the side table) names no
    // activity, and the server admits positions only for the snapshot's own activities. Its id stays in reservedIds.
    positions: (draft.layout ?? []).filter(item => placed.has(item.nodeId))
      .map(item => ({ nodeId: item.nodeId, x: item.x, y: item.y })),
    // Visible Secret References are those the draft already binds: names, types and scopes only, never a value.
    secretReferences: collectVisibleSecretReferences(draft.state.rootActivity, catalogByVersion),
    structure,
    versionOf: new Map([...byNode].map(([nodeId, node]) => [nodeId, node.activityVersionId]))
  };
}

export interface WorkflowSnapshotOptions {
  /** The editor's identity, revision, selection and working-state fields, which lead the content. */
  header?: Record<string, unknown>;
  /** The designer's validation (spec 1193 FR-018); without it the draft's own reconciled errors. */
  validationErrors?: ValidationError[] | null;
  selectedNode?: ActivityNode | null;
  selectedNodeId?: string | null;
  selectedDescriptor?: StudioActivityDescriptor | null;
  /** The draft-derived walks, when the caller already holds them for this draft and catalog. */
  parts?: WorkflowSnapshotParts;
}

/**
 * The whole `workflow.definition` snapshot content, bounded. This is the single composition point: the bridge sends
 * exactly this, and the shared server contract tests (`workflowBatchConformance`, `workflowExplainSnapshot`) build it.
 * Beyond the draft-derived parts it carries (spec 1193 slice D) the designer's diagnostics, graph activities by
 * definition and version (never their bodies), and the selected activity's declared properties with withheld credentials.
 *
 * The size guard keeps the content inside the effective admission bound: the host's 32 KiB per-attachment budget on the
 * server's admitted projection (`WeaverContextServices`), which is tighter than the module's own 64 KiB bound on what the
 * editor sends (`WorkflowWeaverSnapshot`). Parts are left out in this order, each only while the content is still over:
 * the selected activity's property context, then the diagnostics (their count stays in `diagnosticsOmitted`), then the
 * structure (explanation keeps working; an Operation Batch then says the structure is required).
 */
export function buildWorkflowSnapshotContent(draft: WorkflowDraft, catalogByVersion: Map<string, ActivityCatalogItem>, options: WorkflowSnapshotOptions = {}) {
  const parts = options.parts ?? collectWorkflowSnapshotParts(draft, catalogByVersion);
  const diagnostics = collectWorkflowContextDiagnostics(options.validationErrors ?? draft.validationErrors ?? []);
  const graphActivities = collectGraphActivityReferences(parts.activities, nodeId => parts.versionOf.get(nodeId), catalogByVersion);
  // Property context only from the descriptor of the node's exact catalog version. When that version is not in the
  // catalog the designer falls back to another version's descriptor (resolveActivityDescriptor), whose inputs the server
  // would rightly refuse as undeclared, so the property context is left out instead.
  const exact = options.selectedNode ? catalogByVersion.get(options.selectedNode.activityVersionId) : undefined;
  const selected = options.selectedNode && options.selectedDescriptor && options.selectedNode.nodeId === options.selectedNodeId
    && exact && options.selectedDescriptor.typeName === exact.activityTypeKey
    ? collectSelectedActivityContext(options.selectedNode, options.selectedDescriptor) : null;
  const { versionOf: _versions, ...drafted } = parts;
  const content: Record<string, unknown> = {
    ...options.header,
    diagnostics: diagnostics.diagnostics, ...(diagnostics.omitted ? { diagnosticsOmitted: diagnostics.omitted } : {}),
    ...drafted,
    ...(graphActivities.length ? { graphActivities } : {}), ...(selected ? { selectedActivity: selected } : {})
  };
  const over = () => admittedSnapshotLength(content) > workflowContextCandidateBudget - workflowCandidateMargin
    || JSON.stringify(content).length > workflowSnapshotContentLimit - workflowSnapshotMargin;
  if (over()) delete content.selectedActivity;
  if (over() && diagnostics.diagnostics.length) {
    content.diagnostics = [];
    content.diagnosticsOmitted = diagnostics.omitted + diagnostics.diagnostics.length;
  }
  if (over()) delete content.structure;
  return content;
}

/** The server's bound on `workflow.definition` content (`WorkflowWeaverSnapshot`), in UTF-16 code units of its JSON text. */
export const workflowSnapshotContentLimit = 65536;
/**
 * The host's bound on one context attachment's content (`WeaverContextServices`: `JsonSerializer.Serialize(content).Length`
 * over the admitted projection, at most 32,768), which every admitted workflow snapshot must also fit.
 */
export const workflowContextCandidateBudget = 32768;
/**
 * Headroom below the host budget. The estimate (`admittedSnapshotLength`) equals the host's measure on the shared fixture
 * (`workflowExplainSnapshot.json`, held equal by Elsa.Weaver.Tests); this covers number formatting the two may differ on.
 */
const workflowCandidateMargin = 256;
/** Headroom for differences between this serialization and the raw text the server bounds at 64 KiB. */
const workflowSnapshotMargin = 2048;

/**
 * The compact containment structure: activity indexes grouped by exact version, and the root plus every activity with
 * slots, each with its parent, its slot in the parent and its slots' member indexes.
 */
export function compactWorkflowStructure(activities: Array<{ id: string }>, structure: Map<string, WorkflowContextStructure>, sideTableIds: string[] = []) {
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
  // Ids only the side tables still hold: a new node never reuses one, so the server must know them too (bounded).
  const reservedIds = [...new Set(sideTableIds.filter(id => !indexOf.has(id)))].slice(0, 256);
  return { versions: [...versions].map(([id, nodes]) => ({ id, nodes })), containers, ...(reservedIds.length ? { reservedIds } : {}) };
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
