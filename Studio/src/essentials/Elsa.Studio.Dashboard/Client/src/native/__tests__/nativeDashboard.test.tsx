import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnonymousAuthProvider } from "@elsa-workflows/studio-sdk";
import { DashboardApi } from "../api";
import { NamedDashboardsRoute } from "../NamedDashboards";
import { QueryControls } from "../QueryControls";
import { ResultView } from "../ResultView";
import { initialDefinition, serializeTiles, type DashboardResource, type DatasetDeclaration, type QueryResult } from "../types";
import { stubApi, waitUntil } from "../../__tests__/testSupport";

const dataset: DatasetDeclaration = { id: "acme.processing", ownerModuleId: "Acme.Processing", schemaRevision: "1", title: "Processing records", description: "Explicitly exposed processing records.", availability: "ready", fields: [{ id: "category", label: "Customer category", type: "string", filterOperators: ["eq", "ne", "in"], nullable: false, groupable: true, sortable: true, projectable: true, businessDimension: true }, { id: "duration", label: "Processing time", type: "decimal", filterOperators: ["gt"], nullable: true, groupable: false, sortable: true, projectable: true, aggregates: ["average"] }], metrics: [{ id: "processing.duration", revision: "1", label: "Average processing time", unit: "seconds", description: "Elapsed processing time for eligible records." }] };
const question = (): DashboardResource => ({ id: "q1", kind: "question", name: "Processing by category", description: "", ownerId: "user-a", revision: "r1", capabilities: { canRead: true, canEdit: true, canCopy: true, canShare: true, canDelete: true }, definition: initialDefinition(dataset), definitionState: "available", tiles: [], createdAt: "2026-11-06T10:00:00Z", updatedAt: "2026-11-06T10:00:00Z" });
const result = (): QueryResult => ({ outcome: "succeeded", columns: [{ id: "category", label: "Customer category", type: "string" }, { id: "value", label: "Failure rate", type: "decimal", unit: "%" }], rows: [{ key: "a", values: { category: { type: "string", value: "retail" }, value: { type: "decimal", value: "0" } } }, { key: "b", values: { category: { type: "string", value: "enterprise" }, value: { type: "decimal", value: null, nullReason: "noEligibleDenominator" } } }], coverage: { status: "unknown", description: "History collection cannot attest completeness." }, generatedAt: "2026-11-06T12:00:00Z" });
const mounted: (() => void)[] = [];
function render(content: React.ReactNode) { const container = document.createElement("div"); document.body.append(container); const root = createRoot(container); flushSync(() => root.render(content)); mounted.push(() => { flushSync(() => root.unmount()); container.remove(); }); return container; }
function click(container: HTMLElement, text: string) { const button = [...container.querySelectorAll("button")].find(item => item.textContent?.trim() === text); expect(button).toBeDefined(); flushSync(() => button!.click()); }
afterEach(() => { mounted.splice(0).forEach(dispose => dispose()); vi.restoreAllMocks(); });

describe("native dashboard authorization and persistence boundary", () => {
  it("refreshes saved questions and tiles through revision-bound native endpoints without raw preview or provider traffic", async () => {
    const api = stubApi(); const post = vi.fn(async (_url: string, _body: unknown, _init?: unknown) => result()); api.backend.http.postJson = post as typeof api.backend.http.postJson;
    const client = new DashboardApi(api.backend);
    await client.execute("q/1", "revision-7");
    await client.tile({ ...question(), kind: "dashboard", id: "d1", revision: "dashboard-3" }, "tile/2", "question-8");
    expect(post.mock.calls[0]).toEqual(["https://backend.example/_elsa/studio/questions/q%2F1/execute", {}, { headers: { "If-Match": "revision-7" }, signal: undefined }]);
    expect(post.mock.calls[1]).toEqual(["https://backend.example/_elsa/studio/dashboards/d1/tiles/tile%2F2/execute", { questionRevision: "question-8" }, { headers: { "If-Match": "dashboard-3" }, signal: undefined }]);
    expect(post.mock.calls.every(args => !String(args[0]).includes("preview") && !String(args[0]).includes("agent"))).toBe(true);
  });

  it("preserves unavailable references by tile identity and revision without sending protected IDs or overrides", async () => {
    const api = stubApi(); const put = vi.fn(async (_url: string, _body: unknown, _init?: unknown) => question()); api.backend.http.putJson = put as typeof api.backend.http.putJson;
    const tiles = [{ id: "first", questionId: "q1", size: "medium" as const }, { id: "second", questionId: "protected", size: "wide" as const, referenceState: "unavailable" as const, parameters: { privateValue: { type: "string", literal: "private" } } }];
    await new DashboardApi(api.backend).save({ ...question(), kind: "dashboard", tiles }, "key");
    const body = put.mock.calls[0][1] as { tiles: unknown[] };
    expect(body.tiles[1]).toEqual({ id: "second", size: "wide", preserveReference: true });
    expect(JSON.stringify(body)).not.toContain("protected"); expect(JSON.stringify(body)).not.toContain("private");
    expect(put.mock.calls[0][2]).toEqual({ headers: { "If-Match": "r1" }, signal: undefined });
  });

  it("retains distinct tile instances for the same question", () => {
    const tiles = serializeTiles([{ id: "one", questionId: "q1", size: "small" }, { id: "two", questionId: "q1", size: "full" }]);
    expect(tiles.map(tile => tile.id)).toEqual(["one", "two"]); expect(tiles.map(tile => tile.questionId)).toEqual(["q1", "q1"]);
  });

  it("clears previously displayed values when a native refresh reports lost access", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi(); api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : question()) as typeof api.backend.http.getJson;
    let denied = false;
    api.backend.http.postJson = vi.fn(async () => { if (denied) throw Object.assign(new Error("Question is unavailable."), { status: 404 }); return result(); }) as typeof api.backend.http.postJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => [...view.querySelectorAll("button")].some(button => button.textContent === "Refresh results"));
    click(view, "Refresh results"); await waitUntil(() => view.textContent?.includes("retail") === true);
    denied = true; click(view, "Refresh results"); await waitUntil(() => view.textContent?.includes("Question is unavailable.") === true);
    expect(view.textContent).not.toContain("retail"); expect(view.textContent).not.toContain("enterprise");
  });

  it("does not let an older cancelled preview replace a newer definition", async () => {
    window.history.replaceState({}, "", "/questions/q1");
    const api = stubApi(); api.backend.http.getJson = vi.fn(async (url: string) => url.includes("dashboard-datasets") ? { items: [dataset] } : question()) as typeof api.backend.http.getJson;
    let complete!: (value: QueryResult) => void;
    api.backend.http.postJson = vi.fn(() => new Promise<QueryResult>(resolve => { complete = resolve; })) as typeof api.backend.http.postJson;
    const view = render(<AnonymousAuthProvider><NamedDashboardsRoute api={api} kind="question" view="detail" navigate={vi.fn()} /></AnonymousAuthProvider>);
    await waitUntil(() => [...view.querySelectorAll("button")].some(button => button.textContent === "Refresh results"));
    click(view, "Refresh results"); await waitUntil(() => Boolean(complete)); click(view, "Cancel query"); complete(result());
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(view.textContent).not.toContain("retail"); expect(view.textContent).toContain("Query cancelled");
  });
});

describe("ordinary query editing and truthful results", () => {
  it("makes all query facets ordinary controls and exposes declared business dimensions without JSON authoring", () => {
    const view = render(<QueryControls definition={initialDefinition(dataset)} datasets={[dataset]} disabled={false} onChange={vi.fn()} />);
    expect(view.querySelector("textarea")).toBeNull();
    for (const label of ["Dataset", "Query form", "Group by", "Metrics", "Source filters", "Time and comparison", "Result conditions", "Sort and limit", "Parameters and accepted defaults", "Presentation", "Customer category"]) expect(view.textContent).toContain(label);
    expect(view.textContent).not.toContain("countDistinct");
  });

  it("distinguishes exact zero, no denominator, unknown coverage and an empty result", () => {
    const populated = render(<ResultView result={result()} visualization={{ kind: "table", y: [] }} />);
    expect(populated.querySelectorAll("tbody tr")).toHaveLength(2); expect(populated.textContent).toContain("No eligible denominator"); expect(populated.textContent).toContain("Coverage: unknown"); expect(populated.querySelector("tbody td:nth-child(2)")?.textContent).toBe("0");
    const empty = render(<ResultView result={{ ...result(), rows: [] }} visualization={{ kind: "table", y: [] }} />);
    expect(empty.textContent).toContain("empty result, not an observed zero");
  });
});
