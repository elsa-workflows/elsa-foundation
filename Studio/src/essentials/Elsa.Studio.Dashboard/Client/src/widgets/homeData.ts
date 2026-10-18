import { requestStudioNavigation, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

export interface HomeWorkflowSummary {
  id: string;
  name: string;
  description?: string;
  createdAt?: string;
  lastModifiedAt?: string;
  latestVersion?: string;
  publicationState?: string;
}

export interface HomeRunSummary {
  id: string;
  name: string;
  status?: string;
  definitionId?: string;
  definitionVersion?: string;
  createdAt?: string;
  startedAt?: string;
  updatedAt?: string;
  completedAt?: string;
  activeIncidentCount?: number;
  blockingIncidentCount?: number;
}

function asArray(value: unknown): unknown[] {
  if (Array.isArray(value)) return value;
  if (value && typeof value === "object" && Array.isArray((value as { items?: unknown[] }).items)) {
    return (value as { items: unknown[] }).items;
  }
  return [];
}

function workflowName(item: Record<string, unknown>) {
  return String(item.name ?? item.displayName ?? item.id ?? "Untitled workflow");
}

function runName(item: Record<string, unknown>, workflowName?: string) {
  return String(item.name ?? item.definitionName ?? workflowName ?? item.workflowExecutionId ?? item.id ?? "Untitled run");
}

function runId(item: Record<string, unknown>) {
  return String(item.workflowExecutionId ?? item.id ?? "");
}

function optionalString(value: unknown) {
  return typeof value === "string" && value.length > 0 ? value : undefined;
}

function optionalNumber(value: unknown) {
  return typeof value === "number" && Number.isFinite(value) ? value : undefined;
}

function toWorkflowSummary(record: Record<string, unknown>): HomeWorkflowSummary {
  return {
    id: String(record.id ?? record.definitionId ?? ""),
    name: workflowName(record),
    description: optionalString(record.description),
    createdAt: optionalString(record.createdAt),
    lastModifiedAt: optionalString(record.lastModifiedAt),
    latestVersion: optionalString(record.latestVersion),
    publicationState: optionalString(record.publicationState)
  };
}

function toRunSummary(
  record: Record<string, unknown>,
  fallbackStatus?: string,
  definitionNames?: ReadonlyMap<string, string>
): HomeRunSummary {
  const definitionId = optionalString(record.definitionId);
  return {
    id: runId(record),
    name: runName(record, definitionId ? definitionNames?.get(definitionId) : undefined),
    status: typeof record.status === "string" ? record.status : fallbackStatus,
    definitionId,
    definitionVersion: optionalString(record.artifactVersion),
    createdAt: optionalString(record.createdAt),
    startedAt: optionalString(record.startedAt),
    updatedAt: optionalString(record.updatedAt),
    completedAt: optionalString(record.completedAt),
    activeIncidentCount: optionalNumber(record.activeIncidentCount),
    blockingIncidentCount: optionalNumber(record.blockingIncidentCount)
  };
}

function workflowTimestamp(item: HomeWorkflowSummary) {
  const lastModifiedAt = item.lastModifiedAt ? Date.parse(item.lastModifiedAt) : Number.NaN;
  if (Number.isFinite(lastModifiedAt)) return lastModifiedAt;
  const createdAt = item.createdAt ? Date.parse(item.createdAt) : Number.NaN;
  return Number.isFinite(createdAt) ? createdAt : undefined;
}

export async function loadRecentWorkflows(api: ElsaStudioModuleApi, signal: AbortSignal): Promise<HomeWorkflowSummary[]> {
  const payload = await api.backend.http.getJson<unknown>("/design/workflows/definitions?take=8", { signal });
  return asArray(payload)
    .filter(item => item && typeof item === "object")
    .map(item => toWorkflowSummary(item as Record<string, unknown>))
    .filter(item => item.id)
    .sort((left, right) => {
      const leftTimestamp = workflowTimestamp(left);
      const rightTimestamp = workflowTimestamp(right);
      if (leftTimestamp === undefined) return rightTimestamp === undefined ? 0 : 1;
      if (rightTimestamp === undefined) return -1;
      return rightTimestamp - leftTimestamp;
    });
}

export async function loadRecentRuns(api: ElsaStudioModuleApi, signal: AbortSignal): Promise<HomeRunSummary[]> {
  return loadRuns(api, signal, "/runtime/workflows/instances/page?take=8");
}

export async function loadNeedsAttention(api: ElsaStudioModuleApi, signal: AbortSignal): Promise<HomeRunSummary[]> {
  return loadRuns(api, signal, "/runtime/workflows/instances/page?incidentHealth=active&take=8", "Needs attention");
}

async function loadRuns(
  api: ElsaStudioModuleApi,
  signal: AbortSignal,
  path: string,
  fallbackStatus?: string
): Promise<HomeRunSummary[]> {
  const payload = await api.backend.http.getJson<unknown>(path, { signal });
  const records = asArray(payload).filter(item => item && typeof item === "object") as Record<string, unknown>[];
  if (!records.length) return [];

  let definitionNames: ReadonlyMap<string, string> = new Map();
  try {
    definitionNames = new Map((await loadRecentWorkflows(api, signal)).map(item => [item.id, item.name]));
  } catch (error) {
    if (isAbortError(error, signal)) throw error;
  }

  return records
    .map(record => toRunSummary(record, fallbackStatus, definitionNames))
    .filter(item => item.id);
}

function isAbortError(error: unknown, signal: AbortSignal) {
  return signal.aborted || (error !== null && typeof error === "object" && "name" in error && error.name === "AbortError");
}

export async function homeHasWorkflow(api: ElsaStudioModuleApi, signal: AbortSignal) {
  try {
    return (await loadRecentWorkflows(api, signal)).length > 0;
  } catch {
    return false;
  }
}

export async function homeHasRun(api: ElsaStudioModuleApi, signal: AbortSignal) {
  try {
    return (await loadRecentRuns(api, signal)).length > 0;
  } catch {
    return false;
  }
}

export function navigateStudioPath(href: string) {
  if (!requestStudioNavigation(window.location.pathname, href)) return;
  window.history.pushState({}, "", href);
  window.dispatchEvent(new PopStateEvent("popstate"));
}

export function runStatusTone(status: string | undefined): "neutral" | "success" | "warning" | "danger" | "accent" {
  switch ((status ?? "").trim().toLowerCase()) {
    case "completed":
    case "finished":
      return "success";
    case "faulted":
    case "failed":
      return "danger";
    case "running":
    case "executing":
      return "accent";
    case "suspended":
    case "waiting":
      return "warning";
    default:
      return "neutral";
  }
}
