import type { ElsaStudioModuleApi, StudioDraftValidationIssue } from "@elsa-workflows/studio-sdk";
import {
  asDecisionValueMap,
  extraDecisionInputs,
  invalidDecisionInputs,
  mismatchedDecisionInputs,
  parseDecisionPin,
  readDecisionInputs,
  readDecisionPin
} from "./decisionInputs";
import { failureMessage, getDecision, version } from "./decisionsApi";
import { parseTable, type Table } from "./decisionXml";

export type LoadedDecisionTable = {
  table: Table | null;
  unpublished: boolean;
  error: string;
};

const tableLoads = new WeakMap<ElsaStudioModuleApi, Map<string, Promise<LoadedDecisionTable>>>();

export function loadDecisionTable(api: ElsaStudioModuleApi, assetId: string, selection: string): Promise<LoadedDecisionTable> {
  let byPin = tableLoads.get(api);
  if (!byPin) {
    byPin = new Map();
    tableLoads.set(api, byPin);
  }
  const key = `${assetId}@${selection}`;
  const pending = byPin.get(key);
  if (pending) return pending;
  const load = fetchDecisionTable(api, assetId, selection).catch(failure => ({
    table: null,
    unpublished: false,
    error: failureMessage(failure)
  }));
  byPin.set(key, load);
  return load;
}

export async function collectEvaluateDecisionValidation(
  api: ElsaStudioModuleApi,
  activity: unknown
): Promise<StudioDraftValidationIssue[]> {
  const pin = readDecisionPin(activity);
  const parsed = parseDecisionPin(pin);
  if (!parsed) return [];
  const nodeId = activityNodeId(activity);
  const loaded = await loadDecisionTable(api, parsed.assetId, parsed.selection);
  const issues: StudioDraftValidationIssue[] = [];
  if (loaded.error) {
    issues.push({
      type: "Decision/Unavailable",
      message: loaded.error,
      path: nodeId ? `${nodeId}/Decision` : "Decision"
    });
  }
  if (loaded.unpublished) {
    issues.push({
      type: "Decision/UnpublishedLatest",
      message: "This decision has no published version. Publish it first; Review & publish cannot pin a draft with @latest.",
      path: nodeId ? `${nodeId}/Decision` : "Decision"
    });
  }
  if (!loaded.table) return issues;
  const values = asDecisionValueMap(readDecisionInputs(activity));
  const extras = extraDecisionInputs(values, loaded.table.inputs);
  if (extras.length) {
    const names = extras.map(item => item.name).join(", ");
    issues.push({
      type: "Decision/InputMembership",
      message: `${names} ${extras.length === 1 ? "is" : "are"} not in this table. Runtime will refuse extra inputs (InputMembership). Remove them before Review & publish.`,
      path: nodeId ? `${nodeId}/Inputs` : "Inputs"
    });
  }
  for (const item of mismatchedDecisionInputs(values, loaded.table.inputs)) {
    issues.push({
      type: "Decision/InputTypeMismatch",
      message: `${item.name} is stored as ${item.storedKind}; this table expects ${item.tableType}. Runtime will refuse InputTypeMismatch.`,
      path: nodeId ? `${nodeId}/Inputs` : "Inputs"
    });
  }
  for (const item of invalidDecisionInputs(values, loaded.table.inputs)) {
    issues.push({
      type: "Decision/InvalidValue",
      message: `${item.name} is empty or not a valid ${item.tableType}. Runtime will refuse InvalidValue.`,
      path: nodeId ? `${nodeId}/Inputs` : "Inputs"
    });
  }
  return issues;
}

async function fetchDecisionTable(api: ElsaStudioModuleApi, assetId: string, selection: string): Promise<LoadedDecisionTable> {
  if (selection === "latest") {
    const asset = await getDecision(api, assetId);
    if (asset.publishedVersionId) {
      const published = await version(api, assetId, asset.publishedVersionId);
      return readLoadedTable(published.xml, false, "The published table source is unavailable.");
    }
    return readLoadedTable(asset.xml, true, "The draft table source is unavailable.");
  }
  const published = await version(api, assetId, selection);
  return readLoadedTable(published.xml, false, "The published table source is unavailable.");
}

function readLoadedTable(xml: string | null | undefined, unpublished: boolean, missing: string): LoadedDecisionTable {
  if (!xml) return { table: null, unpublished, error: missing };
  try {
    return { table: parseTable(xml), unpublished, error: "" };
  } catch {
    return { table: null, unpublished, error: "The selected decision's table could not be read. JSON editing remains available." };
  }
}

function activityNodeId(activity: unknown): string {
  if (!activity || typeof activity !== "object") return "";
  const nodeId = (activity as { nodeId?: unknown }).nodeId;
  return typeof nodeId === "string" ? nodeId : "";
}
