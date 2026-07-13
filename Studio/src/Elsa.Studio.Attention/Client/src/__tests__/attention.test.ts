import { describe, expect, it } from "vitest";
import { groupAttentionItems, loadAttention, type AttentionItem } from "../attentionApi";
import { isSnoozed, snooze } from "../attentionPreferences";
describe("Studio Attention", () => {
  it("sorts severity, bounds presentation members, and preserves partial failures", async () => { const response = { generatedAt: "2026-01-01T00:00:00Z", contributors: [{ contributorId: "logs", displayName: "Logs", status: "failed", evaluatedAt: "", detail: "offline" }, { contributorId: "runs", displayName: "Runs", status: "ready", evaluatedAt: "", items: Array.from({ length: 25 }, (_, index) => raw(index, index === 4 ? "critical" : "warning")) }] }; const api = { host: { http: { getJson: async () => response }, baseUrl: "studio" }, backend: { http: { getJson: async () => response }, baseUrl: "backend" }, runtime: {} } as any; const snapshot = await loadAttention(api, new AbortController().signal); expect(snapshot.items).toHaveLength(10); expect(snapshot.items[0].severity).toBe("critical"); expect(snapshot.failures).toHaveLength(2); });
  it("groups only typed matching correlations", () => { const a = item("a", [{ kind: "trace", value: "1" }]); const b = item("b", [{ kind: "trace", value: "1" }]); const c = item("c", [{ kind: "incident", value: "1" }]); expect(groupAttentionItems([a,b,c]).map(group => group.length)).toEqual([2,1]); });
  it("unions transitive multi-correlation overlaps", () => { const a = item("a", [{ kind: "trace", value: "1" }]); const bridge = item("b", [{ kind: "trace", value: "1" }, { kind: "execution", value: "2" }]); const c = item("c", [{ kind: "execution", value: "2" }]); expect(groupAttentionItems([a,c,bridge])).toHaveLength(1); });
  it("preserves rejected hosts as incomplete-source failures", async () => { const api = { host: { baseUrl: "studio", http: { getJson: async () => { throw new Error("offline"); } } }, backend: { baseUrl: "backend", http: { getJson: async () => ({ generatedAt: "2026-01-01T00:00:00Z", contributors: [] }) } }, runtime: {} } as any; const snapshot = await loadAttention(api, new AbortController().signal); expect(snapshot.items).toHaveLength(0); expect(snapshot.failures[0]).toMatchObject({ contributorId: "host.studio", status: "unavailable" }); });
  it("treats an absent optional Studio API differently from an absent backend API", async () => {
    const missing = () => { const error = new Error("missing") as Error & { status: number }; error.status = 404; throw error; };
    const backendResponse = { generatedAt: "2026-01-01T00:00:00Z", contributors: [] };
    const available = { host: { baseUrl: "studio", http: { getJson: async () => missing() } }, backend: { baseUrl: "backend", http: { getJson: async () => backendResponse } }, runtime: {} } as any;
    expect((await loadAttention(available, new AbortController().signal)).failures).toHaveLength(0);

    const unavailable = { ...available, backend: { baseUrl: "backend", http: { getJson: async () => missing() } } } as any;
    expect((await loadAttention(unavailable, new AbortController().signal)).failures).toEqual([expect.objectContaining({ contributorId: "host.backend" })]);
  });
  it("breaks snooze on escalation or a new generation", () => { const warning = item("a", []); const record = snooze(warning, 60_000, 0); expect(isSnoozed(warning, [record], 1)).toBe(true); expect(isSnoozed({ ...warning, severity: "critical" }, [record], 1)).toBe(false); expect(isSnoozed({ ...warning, generation: "2" }, [record], 1)).toBe(false); });
});
function raw(index: number, severity: string) { return { id: String(index), generation: "1", severity, title: `Item ${index}`, occurredAt: `2026-01-01T00:00:${String(index).padStart(2,"0")}Z`, lastObservedAt: "2026-01-01T00:00:00Z", count: 1, destination: { path: "/", label: "Open" }, correlations: [], sensitivity: "metadata" }; }
function item(id: string, correlations: AttentionItem["correlations"]): AttentionItem { return { ...raw(0, "warning"), id, severity: "warning", correlations, contributorId: "test", contributorName: "Test" } as AttentionItem; }
