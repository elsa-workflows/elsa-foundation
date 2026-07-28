import type {
  ActivityDefinitionDraftView,
  ActivityDefinitionLayoutRecord
} from "./activityDefinitionTypes";

interface ActivityDefinitionDraftJsonProjection {
  presentationLabel: string | null;
  contract: ActivityDefinitionDraftView["contract"];
  implementation: {
    providerKey: string;
    schemaVersion: string;
    payload: unknown;
  };
  layout: ActivityDefinitionLayoutRecord[];
}

export type ApplyActivityDefinitionDraftJsonResult =
  | { draft: ActivityDefinitionDraftView }
  | { error: string };

export function serializeActivityDefinitionDraftJson(draft: ActivityDefinitionDraftView) {
  return JSON.stringify(projectActivityDefinitionDraft(draft), null, 2);
}

export function applyActivityDefinitionDraftJson(
  current: ActivityDefinitionDraftView,
  text: string
): ApplyActivityDefinitionDraftJsonResult {
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    return { error: "Activity Definition JSON must be valid JSON." };
  }
  if (!isRecord(value)) return { error: "Activity Definition JSON must contain an object." };

  const implementation = value.implementation;
  if (!isRecord(implementation) ||
      typeof implementation.providerKey !== "string" ||
      typeof implementation.schemaVersion !== "string" ||
      !("payload" in implementation)) {
    return { error: "Implementation must include providerKey, schemaVersion, and payload." };
  }
  if (implementation.providerKey !== current.provider.providerKey ||
      implementation.schemaVersion !== current.provider.schemaVersion) {
    return { error: "Provider changes require the explicit provider migration workflow." };
  }

  if (!isContract(value.contract)) {
    return { error: "Contract must include a schema version and input, output, and outcome arrays." };
  }
  const contractError = validateContract(value.contract);
  if (contractError) return { error: contractError };
  if (!Array.isArray(value.layout) || !value.layout.every(isLayoutRecord)) {
    return { error: "Layout must be an array of nodeId/data records." };
  }
  const layoutError = validateLayout(value.layout);
  if (layoutError) return { error: layoutError };
  if (value.presentationLabel !== null &&
      value.presentationLabel !== undefined &&
      typeof value.presentationLabel !== "string") {
    return { error: "Presentation label must be text or null." };
  }
  if (typeof value.presentationLabel === "string" && value.presentationLabel.length > 200) {
    return { error: "Presentation label cannot exceed 200 characters." };
  }

  const payload = structuredClone(implementation.payload);
  let layout = structuredClone(value.layout);
  if (implementation.providerKey === "elsa.activity-graph") {
    const nodeInspection = inspectActivityNodeIds(payload);
    if (nodeInspection.error) return { error: nodeInspection.error };
    layout = reconcileActivityDefinitionLayout(current.layout, layout, nodeInspection.nodeIds);
  }
  return {
    draft: {
      ...current,
      contract: structuredClone(value.contract),
      provider: {
        ...current.provider,
        payload
      },
      layout,
      presentationLabel: value.presentationLabel ?? null,
      validation: null
    }
  };
}

export function projectActivityDefinitionDraft(
  draft: ActivityDefinitionDraftView
): ActivityDefinitionDraftJsonProjection {
  return {
    presentationLabel: draft.presentationLabel ?? null,
    contract: structuredClone(draft.contract),
    implementation: {
      providerKey: draft.provider.providerKey,
      schemaVersion: draft.provider.schemaVersion,
      payload: structuredClone(draft.provider.payload)
    },
    layout: structuredClone(draft.layout)
  };
}

export function reconcileActivityDefinitionLayout(
  current: ActivityDefinitionLayoutRecord[],
  authored: ActivityDefinitionLayoutRecord[],
  nodeIds: string[]
) {
  const currentByNodeId = new Map(current.map(record => [record.nodeId, record]));
  const authoredByNodeId = new Map(authored.map(record => [record.nodeId, record]));
  const existingPositions = [...current, ...authored].map(readPosition).filter(isPosition);
  let placementIndex = current.length;
  return nodeIds.map(nodeId => {
    const explicit = authoredByNodeId.get(nodeId);
    if (explicit) return explicit;
    const previous = currentByNodeId.get(nodeId);
    if (previous) return previous;

    let position: { x: number; y: number };
    do {
      position = {
        x: 80 + placementIndex % 4 * 240,
        y: 80 + Math.floor(placementIndex / 4) * 160
      };
      placementIndex += 1;
    } while (existingPositions.some(existing =>
      Math.abs(existing.x - position.x) < 80 && Math.abs(existing.y - position.y) < 60));
    existingPositions.push(position);
    return { nodeId, data: position };
  });
}

function inspectActivityNodeIds(payload: unknown): { nodeIds: string[]; error?: string } {
  const result: string[] = [];
  const seenObjects = new Set<object>();
  let error: string | undefined;
  const visit = (value: unknown) => {
    if (!value || typeof value !== "object" || seenObjects.has(value) || error) return;
    seenObjects.add(value);
    if (Array.isArray(value)) {
      value.forEach(visit);
      return;
    }
    const record = value as Record<string, unknown>;
    if (typeof record.activityVersionId === "string") {
      const nodeId = typeof record.nodeId === "string" ? record.nodeId.trim() : "";
      if (!nodeId) {
        error = "Every graph activity node must have a non-blank nodeId.";
        return;
      }
      if (result.includes(nodeId)) {
        error = `Graph activity nodeId '${nodeId}' must be unique.`;
        return;
      }
      result.push(nodeId);
    }
    Object.values(record).forEach(visit);
  };
  visit(payload);
  const rootActivity = isRecord(payload) && isRecord(payload.rootActivity)
    ? payload.rootActivity
    : null;
  const rootNodeId = rootActivity && typeof rootActivity.nodeId === "string"
    ? rootActivity.nodeId.trim()
    : null;
  return {
    nodeIds: rootNodeId ? result.filter(nodeId => nodeId !== rootNodeId) : result,
    ...(error ? { error } : {})
  };
}

function isContract(value: unknown): value is ActivityDefinitionDraftView["contract"] {
  return isRecord(value) &&
    typeof value.contractSchemaVersion === "string" &&
    Array.isArray(value.inputs) &&
    Array.isArray(value.outputs) &&
    Array.isArray(value.outcomes);
}

function validateContract(contract: ActivityDefinitionDraftView["contract"]) {
  if (!contract.contractSchemaVersion.trim()) return "Contract schema version cannot be blank.";
  const groups = [
    ["input", contract.inputs],
    ["output", contract.outputs],
    ["outcome", contract.outcomes]
  ] as const;
  for (const [kind, members] of groups) {
    const references = new Set<string>();
    for (const member of members) {
      if (!isRecord(member) || typeof member.referenceKey !== "string" || !member.referenceKey.trim()) {
        return `Every contract ${kind} must have a non-blank referenceKey.`;
      }
      if (references.has(member.referenceKey)) {
        return `Contract ${kind} referenceKey '${member.referenceKey}' must be unique.`;
      }
      references.add(member.referenceKey);
      if ((kind === "input" || kind === "output") &&
          (typeof member.storageDriverKey !== "string" || !member.storageDriverKey.trim())) {
        return `Every contract ${kind} must have a non-blank storageDriverKey.`;
      }
    }
  }
  return null;
}

function isLayoutRecord(value: unknown): value is ActivityDefinitionLayoutRecord {
  return isRecord(value) && typeof value.nodeId === "string" && "data" in value;
}

function validateLayout(layout: ActivityDefinitionLayoutRecord[]) {
  const nodeIds = new Set<string>();
  for (const record of layout) {
    const nodeId = record.nodeId.trim();
    if (!nodeId) return "Every layout record must have a non-blank nodeId.";
    if (nodeIds.has(nodeId)) return `Layout nodeId '${nodeId}' must be unique.`;
    nodeIds.add(nodeId);
  }
  return null;
}

function readPosition(record: ActivityDefinitionLayoutRecord) {
  if (!isRecord(record.data)) return null;
  const { x, y } = record.data;
  return typeof x === "number" && Number.isFinite(x) &&
    typeof y === "number" && Number.isFinite(y)
    ? { x, y }
    : null;
}

function isPosition(value: { x: number; y: number } | null): value is { x: number; y: number } {
  return value !== null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
