import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
export interface UnavailableSnapshot { status: "unavailable"; generatedAt: string; errorCode?: string; }
export interface PortfolioSnapshot { status: "ready"; generatedAt: string; activeDefinitionCount: number; publishedDefinitionCount: number; unpublishedDraftCount: number; invalidDraftCount: number; }
export interface RunBucket { from: string; to: string; startedCount: number; failedCount: number; incidentBearingRunCount: number; }
export interface FailingDefinition { definitionId: string; name?: string; failedCount: number; }
export interface RunHealthSnapshot { status: "ready"; generatedAt: string; from: string; to: string; timeZone: string; bucket: "hour"|"day"; includeTestRuns: boolean; startedCount: number; succeededCount: number; failedCount: number; cancelledCount: number; incompleteCount: number; incidentBearingRunCount: number; incidentCount: number; runningCount: number; failurePercentage: number; incidentBearingPercentage: number; buckets: RunBucket[]; highestFailureDefinitions: FailingDefinition[]; }
export type WorkflowPortfolioSnapshot = PortfolioSnapshot | UnavailableSnapshot;
export type WorkflowRunHealthSnapshot = RunHealthSnapshot | UnavailableSnapshot;
export type RunHealthRange = "24h" | "7d" | "30d";
export interface RunHealthSettings { range: RunHealthRange; includeTestRuns: boolean; }
export async function loadPortfolio(api: ElsaStudioModuleApi, signal: AbortSignal) { return api.backend.http.getJson<WorkflowPortfolioSnapshot>("/_elsa/workflows/dashboard/definitions", { signal }); }
export async function loadRunHealth(api: ElsaStudioModuleApi, settings: RunHealthSettings, signal: AbortSignal, now = new Date()) {
  const duration = settings.range === "24h" ? 86_400_000 : settings.range === "7d" ? 604_800_000 : 2_592_000_000;
  const from = new Date(now.getTime() - duration).toISOString(); const to = now.toISOString();
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC"; const bucket = settings.range === "24h" ? "hour" : "day";
  const query = new URLSearchParams({ from, to, timeZone, bucket, includeTestRuns: String(settings.includeTestRuns) });
  return api.backend.http.getJson<WorkflowRunHealthSnapshot>(`/_elsa/workflows/dashboard/runs?${query}`, { signal });
}
