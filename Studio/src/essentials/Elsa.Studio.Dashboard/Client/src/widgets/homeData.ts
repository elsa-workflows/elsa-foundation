import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";

export interface HomeWorkflowSummary {
  id: string;
  name: string;
}

export interface HomeRunSummary {
  id: string;
  name: string;
  status?: string;
  definitionId?: string;
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

function runName(item: Record<string, unknown>) {
  return String(item.name ?? item.definitionName ?? item.workflowExecutionId ?? item.id ?? "Untitled run");
}

function runId(item: Record<string, unknown>) {
  return String(item.workflowExecutionId ?? item.id ?? "");
}

export async function loadRecentWorkflows(api: ElsaStudioModuleApi, signal: AbortSignal): Promise<HomeWorkflowSummary[]> {
  try {
    const payload = await api.backend.http.getJson<unknown>("/design/workflows/definitions?take=8", { signal });
    return asArray(payload)
      .filter(item => item && typeof item === "object")
      .map(item => {
        const record = item as Record<string, unknown>;
        return { id: String(record.id ?? record.definitionId ?? ""), name: workflowName(record) };
      })
      .filter(item => item.id);
  } catch {
    return [];
  }
}

export async function loadRecentRuns(api: ElsaStudioModuleApi, signal: AbortSignal): Promise<HomeRunSummary[]> {
  try {
    const payload = await api.backend.http.getJson<unknown>("/runtime/workflows/instances/page?take=8", { signal });
    return asArray(payload)
      .filter(item => item && typeof item === "object")
      .map(item => {
        const record = item as Record<string, unknown>;
        return {
          id: runId(record),
          name: runName(record),
          status: typeof record.status === "string" ? record.status : undefined,
          definitionId: typeof record.definitionId === "string" ? record.definitionId : undefined
        };
      })
      .filter(item => item.id);
  } catch {
    return [];
  }
}

export async function loadNeedsAttention(api: ElsaStudioModuleApi, signal: AbortSignal): Promise<HomeRunSummary[]> {
  try {
    const payload = await api.backend.http.getJson<unknown>("/runtime/workflows/instances/health?incidentHealth=active&take=8", { signal });
    return asArray(payload)
      .filter(item => item && typeof item === "object")
      .map(item => {
        const record = item as Record<string, unknown>;
        return {
          id: runId(record),
          name: runName(record),
          status: typeof record.status === "string" ? record.status : "Needs attention",
          definitionId: typeof record.definitionId === "string" ? record.definitionId : undefined
        };
      })
      .filter(item => item.id);
  } catch {
    return [];
  }
}

export async function homeHasWorkflow(api: ElsaStudioModuleApi, signal: AbortSignal) {
  return (await loadRecentWorkflows(api, signal)).length > 0;
}

export async function homeHasRun(api: ElsaStudioModuleApi, signal: AbortSignal) {
  return (await loadRecentRuns(api, signal)).length > 0;
}
