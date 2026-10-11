import type {
  StudioActivityDefinitionImplementationState,
  StudioActivityDefinitionLayoutRecord
} from "@elsa-workflows/studio-sdk";
import type { ActivityNode, DesignMetadataRecord } from "./workflowTypes";
import type { GraphDocumentAdapter } from "./graph-authoring/graphDocumentAdapter";

type ActivityGraphPayloadRecord = Record<string, unknown> & {
  rootActivity?: unknown;
};

export const activityGraphDocumentAdapter: GraphDocumentAdapter<StudioActivityDefinitionImplementationState> = {
  resourceKind: "activity-definition-graph",
  readRoot: document => normalizeRoot(readPayload(document).rootActivity),
  readLayout: document => activityGraphLayoutToDesign(document.layout),
  replaceRoot: (document, rootActivity) => ({
    ...document,
    payload: { ...readPayload(document), rootActivity }
  }),
  replaceLayout: (document, layout) => ({
    ...document,
    layout: activityGraphLayoutFromDesign(layout, document.layout)
  }),
  replaceGraph: (document, rootActivity, layout) => ({
    ...document,
    payload: { ...readPayload(document), rootActivity },
    layout: activityGraphLayoutFromDesign(layout, document.layout)
  })
};

export function activityGraphLayoutToDesign(
  layout: StudioActivityDefinitionLayoutRecord[]
): DesignMetadataRecord[] {
  return layout.map(record => {
    const data = isRecord(record.data) ? record.data : {};
    const { x, y, width, height, ...additionalProperties } = data;
    return {
      nodeId: record.nodeId,
      x: finiteNumber(x) ?? 0,
      y: finiteNumber(y) ?? 0,
      ...(finiteNumber(width) !== null ? { width: finiteNumber(width) } : {}),
      ...(finiteNumber(height) !== null ? { height: finiteNumber(height) } : {}),
      ...(Object.keys(additionalProperties).length > 0
        ? { additionalProperties: structuredClone(additionalProperties) }
        : {})
    };
  });
}

export function activityGraphLayoutFromDesign(
  layout: DesignMetadataRecord[],
  previous: StudioActivityDefinitionLayoutRecord[] = []
): StudioActivityDefinitionLayoutRecord[] {
  const previousByNodeId = new Map(previous.map(record => [record.nodeId, record]));
  return layout.map(record => {
    const previousData = previousByNodeId.get(record.nodeId)?.data;
    const previousRecord = isRecord(previousData) ? previousData : {};
    const additionalProperties = isRecord(record.additionalProperties)
      ? record.additionalProperties
      : {};
    return {
      nodeId: record.nodeId,
      data: {
        ...structuredClone(previousRecord),
        ...structuredClone(additionalProperties),
        x: record.x,
        y: record.y,
        ...(record.width !== undefined && record.width !== null ? { width: record.width } : {}),
        ...(record.height !== undefined && record.height !== null ? { height: record.height } : {})
      }
    };
  });
}

function readPayload(document: StudioActivityDefinitionImplementationState): ActivityGraphPayloadRecord {
  return isRecord(document.payload) ? document.payload as ActivityGraphPayloadRecord : {};
}

function normalizeRoot(value: unknown): ActivityNode {
  const root = isRecord(value) ? value as Partial<ActivityNode> : {};
  return {
    ...root,
    nodeId: typeof root.nodeId === "string" && root.nodeId ? root.nodeId : "root",
    activityVersionId: typeof root.activityVersionId === "string" ? root.activityVersionId : "",
    inputs: Array.isArray(root.inputs) ? root.inputs : [],
    outputs: Array.isArray(root.outputs) ? root.outputs : [],
    structure: root.structure ?? null
  };
}

function finiteNumber(value: unknown) {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
