import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityNode, DesignMetadataRecord } from "../workflowTypes";
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
}

export interface BpmnImportResult {
  processNode: ActivityNode;
  analysis: BpmnImportAnalysis;
}

export function analyzeBpmnDocument(context: StudioEndpointContext, xml: string, processId?: string, signal?: AbortSignal) {
  return context.http.postJson<BpmnImportAnalysis>(`${root}/analyze`, { xml, processId }, signal ? { signal } : undefined);
}

export function importBpmnDocument(context: StudioEndpointContext, xml: string, processId?: string, signal?: AbortSignal) {
  return context.http.postJson<BpmnImportResult>(`${root}/import`, { xml, processId }, signal ? { signal } : undefined);
}

export async function exportBpmnDocument(context: StudioEndpointContext, processNode: ActivityNode, processId?: string, signal?: AbortSignal) {
  const result = await context.http.postJson<{ xml: string }>(`${root}/export`, { processNode, processId }, signal ? { signal } : undefined);
  return result.xml;
}

// The importer preserves BPMNDI on the authored `diagram` payload; the canvas positions nodes from
// design-metadata layout records keyed by elementId. Bridge the two so an imported diagram renders
// exactly as its modeler drew it.
export function layoutFromBpmnDiagram(processNode: ActivityNode): DesignMetadataRecord[] {
  if (processNode.structure?.kind !== bpmnStructureKind) return [];
  const diagram = processNode.structure.payload.diagram;
  if (typeof diagram !== "object" || diagram === null) return [];
  const shapes = (diagram as { shapes?: Record<string, { x?: number; y?: number }> }).shapes;
  if (typeof shapes !== "object" || shapes === null) return [];

  return Object.entries(shapes)
    .filter(([, bounds]) => typeof bounds?.x === "number" && typeof bounds?.y === "number")
    .map(([elementId, bounds]) => ({ nodeId: elementId, x: Math.round(bounds.x!), y: Math.round(bounds.y!) }));
}

// The reverse bridge for export: stamp the canvas layout onto the `diagram` payload so the emitted
// BPMNDI reflects the author's current arrangement (elements without a layout record keep whatever
// the payload already carries; the exporter synthesizes the rest).
export function withDiagramFromLayout(processNode: ActivityNode, layout: DesignMetadataRecord[]): ActivityNode {
  if (processNode.structure?.kind !== bpmnStructureKind) return processNode;

  const layoutByNodeId = new Map(layout.map(record => [record.nodeId, record]));
  const previousDiagram = processNode.structure.payload.diagram;
  const previousShapes = typeof previousDiagram === "object" && previousDiagram !== null
    ? ((previousDiagram as { shapes?: Record<string, unknown> }).shapes ?? {})
    : {};

  const shapes: Record<string, unknown> = { ...previousShapes };
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
        diagram: { ...(typeof previousDiagram === "object" && previousDiagram !== null ? previousDiagram : {}), shapes }
      }
    }
  };
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
