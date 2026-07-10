import type { Node } from "@xyflow/react";
import type { StudioActivityDescriptor, StudioAiContributionApi, StudioAiPromptActionContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityExecutionStateSummary, ActivityNode, WorkflowDefinitionSummary, WorkflowDefinitionVersionDetails, WorkflowDraft, WorkflowExecutableReference, WorkflowExecutableRunResponse, WorkflowExecutableSummary, WorkflowTestRunView } from "../workflowTypes";
import type { ChildSlot } from "../workflowAdapter";
import { flowchartEdges, getActivityDesignerSupport, getActivityDisplay, getChildSlots, resolveScope } from "../workflowAdapter";
import { shortTypeName } from "../workflowFormatting";
import { groupByCategory } from "../categoryGrouping";
import { workflowSidePanelMaximizedStorageKey } from "./constants";
import type { ActivityPaletteGroup, CreateWorkflowDraft, CreateWorkflowKind, WorkflowConnectSource, WorkflowGraphConnection, WorkflowMetadataSuggestion, WorkflowSidePanel } from "./editorTypes";

export function pageItems<T>(items: T[], page: number, pageSize: number) {
  return items.slice((page - 1) * pageSize, page * pageSize);
}

export function getTotalPages(totalCount: number, pageSize: number) {
  return Math.max(1, Math.ceil(totalCount / pageSize));
}

export function findAiAction(ai: StudioAiContributionApi, id: string) {
  return ai.promptActions.list().find(action => action.id === id) ?? null;
}

export function dispatchAiAction<TContext>(ai: StudioAiContributionApi, action: StudioAiPromptActionContribution<TContext>, context: TContext) {
  const prompt = action.createPrompt(context);
  if (!prompt) return false;

  ai.dispatchPrompt(prompt);
  return true;
}

// Weaver is asked to return a fenced ```json {"name","description"} block; parse defensively and fall
// back to a bare object or "Name:"/"Description:" lines so a slightly off-format reply still applies.
export function parseWorkflowMetadataSuggestion(text: string): WorkflowMetadataSuggestion | null {
  if (!text) return null;
  const candidates: string[] = [];
  const fenced = text.match(/```(?:json)?\s*([\s\S]*?)```/i);
  if (fenced) candidates.push(fenced[1]);
  const brace = text.match(/\{[\s\S]*?\}/);
  if (brace) candidates.push(brace[0]);
  for (const candidate of candidates) {
    try {
      const parsed = JSON.parse(candidate.trim()) as Record<string, unknown>;
      const suggestion = pickSuggestion(parsed.name, parsed.description);
      if (suggestion) return suggestion;
    } catch {
      // Not valid JSON — try the next candidate.
    }
  }
  const nameLine = text.match(/name\s*[:-]\s*(.+)/i)?.[1];
  const descLine = text.match(/description\s*[:-]\s*(.+)/i)?.[1];
  return pickSuggestion(nameLine, descLine);
}

function pickSuggestion(name: unknown, description: unknown): WorkflowMetadataSuggestion | null {
  const cleanName = typeof name === "string" ? unquote(name) : undefined;
  const cleanDescription = typeof description === "string" ? unquote(description) : undefined;
  return cleanName || cleanDescription ? { name: cleanName || undefined, description: cleanDescription || undefined } : null;
}

function unquote(value: string) {
  // Strip a trailing comma first (e.g. a lifted JSON line `"Order Approval",`), then a surrounding
  // quote pair. Stripping each end independently avoids leaving a dangling quote when both are present.
  return value.trim().replace(/,$/, "").trim().replace(/^["']/, "").replace(/["']$/, "").trim();
}

export function getCreateRootActivityVersionId(draft: CreateWorkflowDraft, activities: ActivityCatalogItem[]) {
  return draft.rootActivityVersionId ?? findRootKindActivity(activities, draft.rootKind)?.activityVersionId ?? null;
}

export function findRootKindActivity(activities: ActivityCatalogItem[], rootKind: CreateWorkflowKind) {
  return activities.find(activity => getRootKind(activity) === rootKind);
}

function getRootKind(activity: ActivityCatalogItem | undefined) {
  if (!activity) {
    return null;
  }

  if (isFlowchartActivity(activity)) {
    return "flowchart";
  }

  if (isSequenceActivity(activity)) {
    return "sequence";
  }

  return null;
}

export function groupActivityPalette(activities: ActivityCatalogItem[]): ActivityPaletteGroup[] {
  return groupByCategory(activities, activity => activity.category).map(group => ({
    category: group.category,
    activities: group.items.sort((left, right) => getActivityDisplay(left).localeCompare(getActivityDisplay(right)))
  }));
}

function isFlowchartActivity(activity: ActivityCatalogItem) {
  const displayName = getActivityDisplay(activity);
  return displayName === "Flowchart" || activity.activityTypeKey.endsWith(".Flowchart");
}

function isSequenceActivity(activity: ActivityCatalogItem) {
  const displayName = getActivityDisplay(activity);
  return displayName === "Sequence" || activity.activityTypeKey.endsWith(".Sequence");
}

export function isActivityBrowsable(activity: ActivityCatalogItem) {
  return (activity as { isBrowsable?: unknown }).isBrowsable !== false && (activity as { browsable?: unknown }).browsable !== false;
}

export function formatExecutableRoot(executable: WorkflowExecutableSummary) {
  const rootName = formatActivityTypeName(executable.rootActivityType);
  return rootName || executable.rootActivityType;
}

export function executableMatchesDefinitionFilter(executable: WorkflowExecutableSummary, normalizedFilter: string) {
  return [
    executable.definitionId,
    executable.definitionVersionId,
    executable.sourceId,
    executable.sourceVersion
  ].some(value => value?.toLowerCase().includes(normalizedFilter));
}

export function executableBelongsToDefinition(executable: WorkflowExecutableSummary, definitionId: string) {
  return executable.definitionId === definitionId || executable.sourceId === definitionId;
}

export function compareExecutablesByPublishedDate(left: WorkflowExecutableSummary, right: WorkflowExecutableSummary) {
  return getExecutablePublishedTime(right) - getExecutablePublishedTime(left);
}

function getExecutablePublishedTime(executable: WorkflowExecutableSummary) {
  const value = executable.publishedAt ?? executable.createdAt;
  const time = value ? new Date(value).getTime() : 0;
  return Number.isNaN(time) ? 0 : time;
}

export function formatExecutableSourceKind(sourceKind?: string | null) {
  const normalized = sourceKind?.trim().toLowerCase() ?? "";

  if (!normalized || normalized === "definition" || normalized === "workflowdefinition") {
    return "Definition";
  }

  if (normalized === "definitionversion" || normalized === "workflowdefinitionversion") {
    return "Definition version";
  }

  return sourceKind!
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[-_]+/g, " ")
    .replace(/\s+/g, " ")
    .trim()
    .replace(/\b\w/g, letter => letter.toUpperCase());
}

export function readExecutableRunWorkflowExecutionId(response: WorkflowExecutableRunResponse | null | undefined) {
  const workflowExecutionId = response?.workflowExecutionId ?? response?.runId ?? response?.executionId;
  return typeof workflowExecutionId === "string" && workflowExecutionId.trim() ? workflowExecutionId : null;
}

// Surfaces the reference-gate refusal behind a failed executable run (ADR 0040): the backend answers
// 409 with { error } distinguishing an expired test-run reference from a retired one. The backend
// sentence is shown verbatim, followed by the recovery hint matching the structured reason.
export function formatExecutableRunError(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error);
  const payload = (typeof error === "object" && error && "payload" in error ? (error as { payload?: unknown }).payload : null)
    ?? tryParseJson(message);
  const backendError = payload && typeof payload === "object" && typeof (payload as { error?: unknown }).error === "string"
    ? (payload as { error: string }).error
    : null;
  if (!backendError) return message;

  if (/no live .* reference/i.test(backendError)) {
    return /expired/i.test(backendError)
      ? `${backendError} Start a new test run from the definition editor to mint a fresh reference.`
      : `${backendError} Restore the executable or republish its definition to run it.`;
  }

  return backendError;
}

function tryParseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

// Display label for a Source Reference scope ("Published" | "TestRun" on the wire).
export function formatReferenceScope(scope: string | null | undefined) {
  const normalized = scope?.trim().toLowerCase() ?? "";
  if (normalized === "published") return "Published";
  if (normalized === "testrun" || normalized === "test-run") return "Test run";
  return scope?.trim() || "Unknown";
}

export type ExecutableReferenceStatus = "live" | "retired" | "expired";

export function getExecutableReferenceStatus(reference: WorkflowExecutableReference, now: Date = new Date()): ExecutableReferenceStatus {
  if (reference.deletedAt) return "retired";
  if (reference.expiresAt && new Date(reference.expiresAt).getTime() <= now.getTime()) return "expired";
  return "live";
}

// How the inspected reference's source relates to its definition in THIS environment. "absent" is the
// promotion case (the artifact traveled without its definition); "behind" drives the drift caption.
//
// This is a pure VERSION comparison for now. The behavioral-equivalence upgrade (plan §3 / studio#262:
// a draft test run resolving to this same artifact id proves the draft is behaviorally identical)
// slots in here as a third input once the test-run equivalence signal ships.
export type ExecutableSourceDrift =
  | { kind: "absent" }
  | { kind: "current" }
  | { kind: "behind"; referenceVersion: string | null; latestVersion: string | null };

export function computeExecutableSourceDrift(
  reference: WorkflowExecutableReference,
  definition: WorkflowDefinitionSummary | null
): ExecutableSourceDrift {
  if (!definition) return { kind: "absent" };
  if (!definition.latestVersionId || definition.latestVersionId === reference.definitionVersionId) return { kind: "current" };
  return {
    kind: "behind",
    referenceVersion: reference.sourceVersion ?? reference.artifactVersion ?? null,
    latestVersion: definition.latestVersion ?? null
  };
}

export async function copyTextToClipboard(value: string) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(value);
    return;
  }

  const textArea = document.createElement("textarea");
  textArea.value = value;
  textArea.setAttribute("readonly", "");
  textArea.style.position = "fixed";
  textArea.style.opacity = "0";
  document.body.appendChild(textArea);
  textArea.select();
  const copied = document.execCommand("copy");
  textArea.remove();

  if (!copied) {
    throw new Error("Clipboard copy failed.");
  }
}

function formatActivityTypeName(typeName: string) {
  return typeName.split(".").filter(Boolean).at(-1) ?? typeName;
}

export function indexActivityDescriptors(descriptors: StudioActivityDescriptor[]) {
  const index = new Map<string, StudioActivityDescriptor>();

  for (const descriptor of descriptors) {
    addDescriptorIndex(index, descriptor.typeName, descriptor);
    addDescriptorIndex(index, descriptor.name, descriptor);
    addDescriptorIndex(index, descriptor.displayName, descriptor);
    const shortName = descriptor.typeName.split(".").filter(Boolean).at(-1);
    addDescriptorIndex(index, shortName, descriptor);
  }

  return index;
}

export function resolveActivityDescriptor(
  activity: ActivityNode,
  catalogByVersion: Map<string, ActivityCatalogItem>,
  descriptorsByType: Map<string, StudioActivityDescriptor>
) {
  const catalogItem = catalogByVersion.get(activity.activityVersionId);
  return descriptorsByType.get(normalizeDescriptorKey(catalogItem?.activityTypeKey)) ??
    descriptorsByType.get(normalizeDescriptorKey(shortTypeName(catalogItem?.activityTypeKey))) ??
    descriptorsByType.get(normalizeDescriptorKey(catalogItem?.displayName)) ??
    descriptorsByType.get(normalizeDescriptorKey(activity.activityVersionId)) ??
    null;
}

function addDescriptorIndex(index: Map<string, StudioActivityDescriptor>, key: string | null | undefined, descriptor: StudioActivityDescriptor) {
  const normalized = normalizeDescriptorKey(key);
  if (normalized && !index.has(normalized)) index.set(normalized, descriptor);
}

function normalizeDescriptorKey(key: string | null | undefined) {
  return key?.trim().toLowerCase() ?? "";
}

export function readStoredNumber(key: string, fallback: number, min: number, max: number) {
  const storage = getLocalStorage();
  if (!storage) return fallback;

  const storedValue = storage.getItem(key);
  if (storedValue == null) return fallback;

  const value = Number(storedValue);
  return Number.isFinite(value) ? clamp(value, min, max) : fallback;
}

export function readStoredBoolean(key: string, fallback: boolean) {
  const storage = getLocalStorage();
  if (!storage) return fallback;

  const value = storage.getItem(key);
  if (value === "true") return true;
  if (value === "false") return false;
  return fallback;
}

export function readStoredMaximizedSide(): WorkflowSidePanel | null {
  const storage = getLocalStorage();
  if (!storage) return null;

  const value = storage.getItem(workflowSidePanelMaximizedStorageKey);
  return value === "palette" || value === "inspector" ? value : null;
}

function getLocalStorage(): Pick<Storage, "getItem" | "setItem" | "removeItem"> | null {
  if (typeof window === "undefined") return null;

  const storage = window.localStorage as Partial<Storage> | undefined;
  return storage &&
    typeof storage.getItem === "function" &&
    typeof storage.setItem === "function" &&
    typeof storage.removeItem === "function"
    ? storage as Pick<Storage, "getItem" | "setItem" | "removeItem">
    : null;
}

export function writeStoredValue(key: string, value: string | null) {
  const storage = getLocalStorage();
  if (!storage) return;

  if (value == null) {
    storage.removeItem(key);
  } else {
    storage.setItem(key, value);
  }
}

export function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, Math.round(value)));
}

export function formatRunKind(runKind?: string | null) {
  switch (normalizeRunKind(runKind)) {
    case "testrun":
      return "Test Run";
    case "publishedrun":
      return "Published Run";
    case "backgroundweaverrun":
      return "Background Weaver Run";
    default:
      return "Unknown / legacy";
  }
}

function normalizeRunKind(runKind?: string | null) {
  return (runKind ?? "").replace(/[\s_-]+/g, "").toLowerCase();
}

export function getVisibleWorkflowGraphNodeIds(definitionVersion: WorkflowDefinitionVersionDetails, activityCatalog: ActivityCatalogItem[]) {
  const root = definitionVersion.state.rootActivity;
  if (!root) return new Set<string>();

  const rootCatalogItem = activityCatalog.find(activity => activity.activityVersionId === root.activityVersionId);
  if (getActivityDesignerSupport(root, rootCatalogItem) === "unsupported") return new Set([root.nodeId]);

  const scope = resolveScope(root, [], activityCatalog);
  return new Set(scope?.slot.activities.map(activity => activity.nodeId) ?? [root.nodeId]);
}

export function activityNodeKey(activity: ActivityExecutionStateSummary) {
  return activity.authoredActivityId || activity.executableNodeId;
}

export function createNodeId(activity: ActivityCatalogItem) {
  const base = getActivityDisplay(activity).replace(/[^a-z0-9]+/gi, "").toLowerCase() || "activity";
  return `${base}-${crypto.randomUUID().slice(0, 8)}`;
}

export function rightOf(node: Node) {
  return { x: node.position.x + 280, y: node.position.y };
}

export function midpointBetween(source: Node, target: Node) {
  return {
    x: Math.round((source.position.x + target.position.x) / 2),
    y: Math.round((source.position.y + target.position.y) / 2)
  };
}

function clientPointFromEvent(event: MouseEvent | TouchEvent) {
  if ("changedTouches" in event && event.changedTouches.length > 0) {
    return { x: event.changedTouches[0].clientX, y: event.changedTouches[0].clientY };
  }

  return { x: (event as MouseEvent).clientX, y: (event as MouseEvent).clientY };
}

export function isConnectEndOverExistingWorkflowNode(event: MouseEvent | TouchEvent) {
  const point = clientPointFromEvent(event);
  const releaseTarget = document.elementFromPoint?.(point.x, point.y) as HTMLElement | null | undefined;
  const target = releaseTarget ?? (event.target as HTMLElement | null);
  return !!target?.closest(".react-flow__handle, .react-flow__node");
}

export function resolveConnectEndSource(
  currentSource: WorkflowConnectSource | null,
  connectionState: { fromNode?: { id?: string | null } | null; fromHandle?: { id?: string | null } | null }
): WorkflowConnectSource | null {
  if (currentSource) return currentSource;
  const nodeId = connectionState.fromNode?.id;
  return nodeId ? { nodeId, handleId: connectionState.fromHandle?.id ?? null } : null;
}

export { clientPointFromEvent };

export function getDraftSignature(draft: WorkflowDraft) {
  return JSON.stringify({ state: draft.state, layout: draft.layout });
}

export function getDraftRevision(draft: WorkflowDraft) {
  return hashString(getDraftSignature(draft));
}

// Resolves a child activity's friendly name from the catalog, falling back to a short type name and
// finally the version id so the inspector never renders an empty label.
function resolveChildActivityName(child: ActivityNode, catalogByVersion?: Map<string, ActivityCatalogItem>): string {
  const catalogItem = catalogByVersion?.get(child.activityVersionId);
  if (catalogItem) return getActivityDisplay(catalogItem);
  return shortTypeName(child.activityVersionId) ?? child.activityVersionId;
}

// Human-readable summary of what an embedded slot currently holds, honouring the slot's cardinality.
// Single slots describe the one child (or invite the author to pick one when empty); many slots keep a
// pluralized count. Used by the inspector's embedded-slot chips.
export function describeSlotContents(slot: ChildSlot, catalogByVersion?: Map<string, ActivityCatalogItem>): string {
  if (slot.cardinality === "single") {
    const child = slot.activities[0];
    return child ? resolveChildActivityName(child, catalogByVersion) : "Empty — click to choose";
  }

  const count = slot.activities.length;
  return `${count} activit${count === 1 ? "y" : "ies"}`;
}

export function collectWorkflowContextActivities(activity: ActivityNode | null | undefined, catalogByVersion: Map<string, ActivityCatalogItem>, result: Array<{ id: string; type: string; displayName?: string }> = []) {
  if (!activity) return result;

  const catalogItem = catalogByVersion.get(activity.activityVersionId);
  result.push({
    id: activity.nodeId,
    type: catalogItem?.activityTypeKey ?? activity.activityVersionId,
    displayName: catalogItem ? getActivityDisplay(catalogItem) : undefined
  });

  for (const slot of getChildSlots(activity, catalogByVersion)) {
    for (const child of slot.activities) collectWorkflowContextActivities(child, catalogByVersion, result);
  }

  return result;
}

export function collectWorkflowContextConnections(
  activity: ActivityNode | null | undefined,
  catalogByVersion?: Map<string, ActivityCatalogItem>,
  result: WorkflowGraphConnection[] = []
) {
  if (!activity) return result;

  // flowchartEdges decodes the flowchart structure payload into source/target edges; reuse it so the
  // agent sees the same wiring the designer renders.
  for (const edge of flowchartEdges(activity)) {
    result.push({ source: edge.source, target: edge.target, sourcePort: edge.sourceHandle ?? undefined, targetPort: edge.targetHandle ?? undefined });
  }

  for (const slot of getChildSlots(activity, catalogByVersion)) {
    for (const child of slot.activities) collectWorkflowContextConnections(child, catalogByVersion, result);
  }

  return result;
}

export function cloneWorkflowDraftForUndo(draft: WorkflowDraft) {
  return typeof structuredClone === "function"
    ? structuredClone(draft)
    : JSON.parse(JSON.stringify(draft)) as WorkflowDraft;
}

export function createDraftSnapshotId(draft: WorkflowDraft) {
  return `${draft.id}-${hashString(JSON.stringify(draft.state))}`;
}

function hashString(value: string) {
  let hash = 2166136261;
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }

  return (hash >>> 0).toString(16).padStart(8, "0");
}

export function isRejectedTestRun(testRun: WorkflowTestRunView) {
  return testRun.status.toLowerCase() === "rejected";
}

export function formatWorkflowVersionLoadError(error: string) {
  try {
    const payload = JSON.parse(error) as { error?: unknown };
    if (typeof payload.error === "string") return payload.error;
  } catch {
    // Fall through to the original message when the server did not return the standard error shape.
  }

  return error;
}

export function formatWorkflowRunLoadError(error: unknown, workflowExecutionId: string) {
  const message = error instanceof Error ? error.message : String(error);
  if (isNotFoundError(error, message)) return `Run ${workflowExecutionId} was not found.`;
  return message;
}

function isNotFoundError(error: unknown, message: string) {
  const responseStatus = typeof error === "object" && error
    ? (error as { response?: { status?: unknown }; status?: unknown }).response?.status
      ?? (error as { response?: { status?: unknown }; status?: unknown }).status
    : undefined;
  if (responseStatus === 404 || /\b404\b/.test(message)) return true;

  try {
    const payload = JSON.parse(message) as { error?: unknown; title?: unknown; detail?: unknown };
    return [payload.error, payload.title, payload.detail]
      .some(value => typeof value === "string" && /not found/i.test(value));
  } catch {
    return /not found/i.test(message);
  }
}
