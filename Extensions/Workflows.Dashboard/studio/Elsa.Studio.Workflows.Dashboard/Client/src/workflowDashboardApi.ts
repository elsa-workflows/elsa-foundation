import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

export interface UnavailableSnapshot { status: "unavailable"; generatedAt: string; errorCode?: string; }
export interface PortfolioSnapshot {
  status: "ready";
  generatedAt: string;
  activeDefinitionCount: number;
  publishedDefinitionCount: number;
  unpublishedDraftCount: number;
  invalidDraftCount: number;
}
export interface RunBucket {
  from: string;
  to: string;
  startedCount: number;
  failedCount: number;
  incidentBearingRunCount: number;
}
/** The server supplies only the identity and count. displayName is optional Design enrichment. */
export interface FailingDefinition { definitionId: string; failedCount: number; displayName?: string; }
export interface RunHealthSnapshot {
  status: "ready";
  generatedAt: string;
  from: string;
  to: string;
  timeZone: string;
  bucket: "hour" | "day";
  includeTestRuns: boolean;
  startedCount: number;
  succeededCount: number;
  failedCount: number;
  cancelledCount: number;
  incompleteCount: number;
  incidentBearingRunCount: number;
  incidentCount: number;
  runningCount: number;
  failurePercentage: number;
  incidentBearingPercentage: number;
  buckets: RunBucket[];
  highestFailureDefinitions: FailingDefinition[];
}
export type WorkflowPortfolioSnapshot = PortfolioSnapshot | UnavailableSnapshot;
export type WorkflowRunHealthSnapshot = RunHealthSnapshot | UnavailableSnapshot;
export type RunHealthRange = "24h" | "7d" | "30d";
export interface RunHealthSettings { range: RunHealthRange; includeTestRuns: boolean; }
export const defaultRunHealthSettings: RunHealthSettings = { range: "24h", includeTestRuns: false };

export function normalizeRunHealthSettings(value: unknown): RunHealthSettings {
  const settings = isRecord(value) ? value : {};
  return {
    range: settings.range === "7d" || settings.range === "30d" ? settings.range : "24h",
    includeTestRuns: settings.includeTestRuns === true
  };
}

export async function loadPortfolio(api: ElsaStudioModuleApi, signal: AbortSignal): Promise<WorkflowPortfolioSnapshot> {
  return loadDashboardResource<PortfolioSnapshot>(api, "/_elsa/workflows/dashboard/definitions", signal);
}

export async function loadRunHealth(api: ElsaStudioModuleApi, settings: unknown, signal: AbortSignal, now = new Date()): Promise<WorkflowRunHealthSnapshot> {
  const options = normalizeRunHealthSettings(settings);
  const duration = options.range === "24h" ? 86_400_000 : options.range === "7d" ? 604_800_000 : 2_592_000_000;
  const from = new Date(now.getTime() - duration).toISOString();
  const to = now.toISOString();
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
  const bucket = options.range === "24h" ? "hour" : "day";
  const query = new URLSearchParams({ from, to, timeZone, bucket, includeTestRuns: String(options.includeTestRuns) });
  const snapshot = await loadDashboardResource<RunHealthSnapshot>(api, `/_elsa/workflows/dashboard/runs?${query}`, signal);
  if (snapshot.status !== "ready" || !snapshot.highestFailureDefinitions?.length) return snapshot;

  // At most five definitions are returned by the aggregate. Name reads are optional and bounded;
  // denied/deleted Design records must never hide otherwise readable runtime statistics.
  const highestFailureDefinitions = await Promise.all(snapshot.highestFailureDefinitions.slice(0, 5).map(async item => {
    if (!safeDefinitionId(item.definitionId)) return item;
    try {
      const response = await api.backend.http.getJson<unknown>(`/design/workflows/definitions/${encodeURIComponent(item.definitionId)}`, { signal });
      const definition = isRecord(response) && isRecord(response.definition) ? response.definition : response;
      const displayName = isRecord(definition) && typeof definition.name === "string" ? definition.name.trim() : "";
      return displayName ? { ...item, displayName } : item;
    } catch (error) {
      if (signal.aborted) throw error;
      return item;
    }
  }));
  return { ...snapshot, highestFailureDefinitions };
}

async function loadDashboardResource<T extends PortfolioSnapshot | RunHealthSnapshot>(api: ElsaStudioModuleApi, path: string, signal: AbortSignal): Promise<T | UnavailableSnapshot> {
  try {
    return await api.backend.http.getJson<T | UnavailableSnapshot>(path, { signal });
  } catch (error) {
    if (!signal.aborted && isRecord(error) && error.status === 404) {
      return { status: "unavailable", generatedAt: "", errorCode: "DASHBOARD_NOT_INSTALLED" };
    }
    throw error;
  }
}

function safeDefinitionId(value: string) {
  return value.length > 0 && value !== "." && value !== ".." && !/[\\/%]/.test(value);
}
function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
