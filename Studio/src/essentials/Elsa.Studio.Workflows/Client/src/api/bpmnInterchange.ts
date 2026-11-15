import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityNode, ActivityPresentationRecord, DesignMetadataRecord } from "../workflowTypes";
import { bpmnStructureKind } from "../bpmn/bpmnTypes";
import { readBpmnElements } from "../bpmn/bpmnAdapter";

// Client for the ingestion-style BPMN interchange endpoints (elsa-foundation PR #889): pure
// conversions between BPMN 2.0 XML (+BPMNDI) and the native elsa.bpmn.structure payload. Nothing is
// persisted server-side — the caller applies the imported node through normal draft authoring.

const root = "interchange/bpmn";

export interface BpmnImportIssue {
  severity: string | number;
  message: string;
  elementId?: string | null;
}

export interface BpmnImportAnalysis {
  processIds: string[];
  elementCounts: Record<string, number>;
  issues: BpmnImportIssue[];
  capabilities?: {
    elementId: string;
    capability: string;
    availability: string | number;
    reason: string;
    bindingRef?: string | null;
    retainedBinding?: { activityVersionId: string; inputKeys: string[] } | null;
  }[];
  hostInventoryVerified?: boolean;
  canExecute?: boolean;
}

export interface BpmnImportResult {
  processNode: ActivityNode;
  analysis: BpmnImportAnalysis;
  processNodes?: { processId: string; node: ActivityNode; participantId?: string | null; participantName?: string | null }[];
}

export function analyzeBpmnDocument(context: StudioEndpointContext, xml: string, processId?: string, signal?: AbortSignal, confirmedBindings: string[] = []) {
  return context.http.postJson<BpmnImportAnalysis>(`${root}/analyze`, { xml, processId, ...(confirmedBindings.length ? { confirmedBindings } : {}) }, signal ? { signal } : undefined);
}

export function importBpmnDocument(context: StudioEndpointContext, xml: string, processId?: string, signal?: AbortSignal, confirmedBindings: string[] = []) {
  return context.http.postJson<BpmnImportResult>(`${root}/import`, { xml, processId, ...(confirmedBindings.length ? { confirmedBindings } : {}) }, signal ? { signal } : undefined);
}

export async function exportBpmnDocument(context: StudioEndpointContext, processNode: ActivityNode, processId?: string, signal?: AbortSignal) {
  const result = await context.http.postJson<{ xml: string; omissions?: { elementId: string; code: string; message: string; referenceKey?: string | null }[] }>(`${root}/export`, { processNode, processId }, signal ? { signal } : undefined);
  if (result.omissions?.length)
    throw new Error(`BPMN export was refused because authored bindings could not be preserved: ${result.omissions.map(item => `${item.elementId}: ${item.message}${item.referenceKey ? ` (${item.referenceKey})` : ""}`).join("; ")}. The current process is unchanged.`);
  return result.xml;
}

// The importer preserves BPMNDI on the authored `diagram` payload; the canvas positions nodes from
// design-metadata layout records keyed by elementId. Bridge the two so an imported diagram renders
// exactly as its modeler drew it.
export function layoutFromBpmnDiagram(processNode: ActivityNode): DesignMetadataRecord[] {
  if (processNode.structure?.kind !== bpmnStructureKind) return [];
  const diagram = processNode.structure.payload.diagram;
  const shapes = typeof diagram === "object" && diagram !== null ? (diagram as { shapes?: Record<string, { x?: number; y?: number }> }).shapes : undefined;
  const local = typeof shapes === "object" && shapes !== null ? Object.entries(shapes)
    .filter(([, bounds]) => typeof bounds?.x === "number" && Number.isFinite(bounds.x) && typeof bounds?.y === "number" && Number.isFinite(bounds.y))
    .map(([elementId, bounds]) => ({ nodeId: elementId, x: bounds.x!, y: bounds.y! })) : [];
  const children = processNode.structure.payload.activities;
  return [...local, ...(Array.isArray(children) ? children.filter(isBpmnNode).flatMap(layoutFromBpmnDiagram) : [])];
}

// The reverse bridge for export: stamp the canvas layout onto the `diagram` payload so the emitted
// BPMNDI reflects the author's current arrangement (elements without a layout record keep whatever
// the payload already carries; the exporter synthesizes the rest).
export function withDiagramFromLayout(processNode: ActivityNode, layout: DesignMetadataRecord[], presentation: ActivityPresentationRecord[] = []): ActivityNode {
  if (processNode.structure?.kind !== bpmnStructureKind) return processNode;

  const layoutByNodeId = new Map(layout.map(record => [record.nodeId, record]));
  const previousDiagram = processNode.structure.payload.diagram;
  const previousShapes = typeof previousDiagram === "object" && previousDiagram !== null
    ? ((previousDiagram as { shapes?: Record<string, unknown> }).shapes ?? {})
    : {};

  const shapes: Record<string, unknown> = { ...previousShapes };
  const names = new Map(presentation.map(item => [item.nodeId, item]));
  const elements = readBpmnElements(processNode).map(element => {
    const label = element.childNodeId ? names.get(element.childNodeId) : undefined;
    if (label?.description?.trim()) throw new Error(`BPMN export was refused: ${element.elementId} has an activity summary that this XML profile cannot retain. The current process is unchanged.`);
    if (label?.displayName && element.name && label.displayName !== element.name)
      throw new Error(`BPMN export was refused: ${element.elementId} has different activity and BPMN names, which this XML profile cannot retain together. The current process is unchanged.`);
    return !element.name && label?.displayName ? { ...element, name: label.displayName } : element;
  });
  for (const element of readBpmnElements(processNode)) {
    const record = layoutByNodeId.get(element.elementId);
    if (!record) continue;
    const previous = (previousShapes as Record<string, Record<string, unknown>>)[element.elementId] ?? {};
    shapes[element.elementId] = { ...previous, x: record.x, y: record.y };
  }

  return {
    ...processNode,
    structure: {
      ...processNode.structure,
      payload: {
        ...processNode.structure.payload,
        elements,
        ...(Array.isArray(processNode.structure.payload.activities) ? { activities: processNode.structure.payload.activities.map(child => isBpmnNode(child) ? withDiagramFromLayout(child, layout, presentation) : child) } : {}),
        diagram: { ...(typeof previousDiagram === "object" && previousDiagram !== null ? previousDiagram : {}), shapes }
      }
    }
  };
}

/** Standard BPMN element names become native names for their exact confirmed child identities. */
export function presentationFromBpmn(processNode: ActivityNode): ActivityPresentationRecord[] {
  if (processNode.structure?.kind !== bpmnStructureKind) return [];
  return [...readBpmnElements(processNode).filter(element => element.childNodeId && element.name).map(element => ({ nodeId: element.childNodeId!, displayName: element.name })),
    ...(Array.isArray(processNode.structure.payload.activities) ? processNode.structure.payload.activities.filter(isBpmnNode).flatMap(presentationFromBpmn) : [])];
}

export function summarizeBpmnImportIssues(analysis: BpmnImportAnalysis): string {
  if (analysis.issues.length === 0) return "";
  const dropped = analysis.issues.filter(issue => String(issue.severity).toLowerCase() === "dropped" || issue.severity === 2).length;
  const degraded = analysis.issues.filter(issue => String(issue.severity).toLowerCase() === "degraded" || issue.severity === 1).length;
  const parts: string[] = [];
  if (dropped > 0) parts.push(`${dropped} element(s) dropped`);
  if (degraded > 0) parts.push(`${degraded} degraded`);
  if (parts.length === 0) parts.push(`${analysis.issues.length} note(s)`);
  return parts.join(", ");
}

/** Direct publication must not turn a degraded conversion into apparently executable authoring. */
export function assertLosslessBpmnImport(analysis: BpmnImportAnalysis): void {
  const lost = analysis.issues.filter(issue => ![0, "info"].includes(
    typeof issue.severity === "string" ? issue.severity.toLowerCase() : issue.severity));
  if (lost.length) throw new Error(`BPMN import was refused because meaning could be lost: ${lost.map(issue => issue.message).join("; ")}. The current process is unchanged; the original file is retained for correction.`);
}

function isBpmnNode(value: unknown): value is ActivityNode {
  if (value === null || typeof value !== "object" || Array.isArray(value)) return false;
  const node = value as ActivityNode;
  return node.structure?.kind === bpmnStructureKind;
}
