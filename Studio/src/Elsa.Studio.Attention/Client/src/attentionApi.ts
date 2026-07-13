import type { ElsaStudioModuleApi, StudioHttpClient } from "@elsa-workflows/studio-sdk";

export type AttentionSeverity = "critical" | "warning" | "info";
export interface AttentionItem { id: string; generation: string; severity: AttentionSeverity; title: string; summary?: string; occurredAt: string; lastObservedAt: string; count: number; destination: { path: string; label: string }; correlations: Array<{ kind: string; value: string }>; sensitivity: "metadata" | "restricted"; contributorId: string; contributorName: string; }
export interface AttentionContributorResult { contributorId: string; displayName: string; status: "ready" | "forbidden" | "unavailable" | "timedOut" | "failed"; evaluatedAt: string; totalCount?: number; isTruncated?: boolean; items?: Omit<AttentionItem, "contributorId" | "contributorName">[]; errorCode?: string; detail?: string; }
export interface AttentionResponse { generatedAt: string; contributors: AttentionContributorResult[]; }
export interface AttentionSnapshot { generatedAt: string; items: AttentionItem[]; failures: AttentionContributorResult[]; }

export async function loadAttention(api: ElsaStudioModuleApi, signal: AbortSignal): Promise<AttentionSnapshot> {
  const sources = sameEndpoint(api.host.baseUrl, api.backend.baseUrl)
    ? [{ label: "Studio and backend", http: api.host.http, optional: false }]
    : [{ label: "Studio", http: api.host.http, optional: true }, { label: "Backend", http: api.backend.http, optional: false }];
  const calls = sources.map(source => read(source.http, signal));
  const settled = await Promise.allSettled(calls);
  const responses = settled.flatMap(result => result.status === "fulfilled" ? [result.value] : []);
  const contributors = responses.flatMap(response => response.contributors);
  const hostFailures: AttentionContributorResult[] = settled.flatMap((result, index) => {
    const source = sources[index];
    if (result.status !== "rejected" || (source.optional && status(result.reason) === 404)) return [];
    return [{ contributorId: `host.${source.label.toLowerCase().replaceAll(" ", "-")}`, displayName: `${source.label} attention`, status: "unavailable", evaluatedAt: new Date().toISOString(), errorCode: "HOST_UNAVAILABLE", detail: `${source.label} attention could not be loaded.` }];
  });
  const failures = [...contributors.filter(result => result.status !== "ready" && result.status !== "forbidden"), ...hostFailures];
  const items = contributors.flatMap(result => (result.items ?? []).slice(0, 5).map(item => ({ ...item, contributorId: result.contributorId, contributorName: result.displayName })))
    .sort(compareAttentionItems).slice(0, 20);
  return { generatedAt: responses.map(value => value.generatedAt).sort().at(-1) ?? new Date().toISOString(), items, failures };
}

export function compareAttentionItems(left: AttentionItem, right: AttentionItem) { return severityRank(left.severity) - severityRank(right.severity) || Date.parse(right.occurredAt) - Date.parse(left.occurredAt); }
export function groupAttentionItems(items: AttentionItem[]) {
  const groups: AttentionItem[][] = [];
  for (const item of items) {
    const matching = groups.filter(group => group.some(member => overlaps(member, item)));
    if (!matching.length) { groups.push([item]); continue; }
    const merged = [item, ...matching.flat()];
    for (const group of matching) groups.splice(groups.indexOf(group), 1);
    groups.push(merged.sort(compareAttentionItems));
  }
  return groups.sort((left, right) => compareAttentionItems(left[0], right[0]));
}
export function attentionScopeKey(api: ElsaStudioModuleApi, subject = "anonymous", tenant = "default") { return [api.runtime.hostId ?? "default", api.host.baseUrl, api.backend.baseUrl, subject, tenant].join("|"); }
function severityRank(value: AttentionSeverity) { return value === "critical" ? 0 : value === "warning" ? 1 : 2; }
async function read(http: StudioHttpClient, signal: AbortSignal) { return http.getJson<AttentionResponse>("/_elsa/attention/items", { signal }); }
function overlaps(left: AttentionItem, right: AttentionItem) { const keys = new Set(left.correlations.map(value => `${value.kind}\u0000${value.value}`)); return right.correlations.some(value => keys.has(`${value.kind}\u0000${value.value}`)); }
function sameEndpoint(left: string, right: string) { return left.replace(/\/+$/, "") === right.replace(/\/+$/, ""); }
function status(error: unknown) { return error instanceof Error ? (error as Error & { status?: number }).status : undefined; }
