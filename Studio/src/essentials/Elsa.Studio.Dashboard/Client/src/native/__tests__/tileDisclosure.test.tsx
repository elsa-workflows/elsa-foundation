import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DashboardApi } from "../api";
import { QuestionTile } from "../NamedDashboards";
import type { DashboardResource, QueryResult } from "../types";
import { stubApi, waitUntil } from "../../__tests__/testSupport";

const question = (id = "q1"): DashboardResource => ({ id, kind: "question", revision: `${id}-r1`, name: `Question ${id}`, description: "", ownerId: "owner", capabilities: { canRead: true, canEdit: true, canShare: true, canCopy: true, canDelete: true }, definitionState: "available", createdAt: "", updatedAt: "" });
const dashboard = (): DashboardResource => ({ ...question("d1"), kind: "dashboard", revision: "d-r1", tiles: [{ id: "same-tile", questionId: "q1", size: "medium", referenceState: "available" }] });
const result = (marker: string): QueryResult => ({ outcome: "succeeded", columns: [{ id: "category", label: "Customer category", type: "string" }, { id: "value", label: "Elapsed duration", type: "decimal" }], rows: [{ key: marker, values: { category: { type: "string", value: marker }, value: { type: "decimal", value: "41" } }, drill: { token: "current-membership" } }], coverage: { status: "unknown", description: "Retained records only." } });
type Props = React.ComponentProps<typeof QuestionTile>;
const disposers: (() => void)[] = [];
function mount(props: Props) {
  const view = document.createElement("div"); document.body.append(view); const root = createRoot(view);
  const update = (next: Props) => flushSync(() => root.render(<QuestionTile {...next} />)); update(props);
  disposers.push(() => { flushSync(() => root.unmount()); view.remove(); }); return { view, update };
}
function fixture() {
  const module = stubApi(); const api = new DashboardApi(module.backend); const saved = dashboard();
  module.backend.http.getJson = vi.fn(async (url: string) => question(decodeURIComponent(url.split("/").pop()!))) as typeof module.backend.http.getJson;
  return { module, props: { api, dashboard: saved, tile: saved.tiles![0], refresh: 0, draftLayout: false, navigate: vi.fn() } satisfies Props };
}
afterEach(() => { disposers.splice(0).forEach(dispose => dispose()); vi.restoreAllMocks(); });

describe("native tile disclosure lifetime", () => {
  it.each([422, 503])("withholds a previous row at refresh admission and after a source failure (%s)", async status => {
    const { module, props } = fixture();
    let reject!: (error: unknown) => void;
    const post = vi.fn((_url: string, _body: unknown, _init?: unknown) => post.mock.calls.length === 1
      ? Promise.resolve(result("previous-authorized-row")) : new Promise<QueryResult>((_resolve, fail) => { reject = fail; }));
    module.backend.http.postJson = post as typeof module.backend.http.postJson;
    const { view, update } = mount(props);
    await waitUntil(() => view.textContent?.includes("previous-authorized-row") === true);
    update({ ...props, refresh: 1 });
    expect(view.querySelector(".named-result")).toBeNull(); expect(view.textContent).not.toContain("previous-authorized-row");
    await waitUntil(() => typeof reject === "function");
    reject(Object.assign(new Error(status === 422 ? "This field or aggregate is no longer supported." : "The source is temporarily unavailable."), { status }));
    await waitUntil(() => view.textContent?.includes("Result values are withheld") === true);
    expect(view.querySelector(".named-result")).toBeNull(); expect(view.textContent).not.toContain("previous-authorized-row");
    expect(view.textContent).toContain(status === 422 ? "field or aggregate is no longer supported" : "source is temporarily unavailable");
    expect(view.querySelector("a")?.textContent).toBe("Question q1"); expect(view.textContent).not.toContain("No authorized records matched");
    expect(view.textContent).not.toContain("Stale result.");
    expect(post.mock.calls[1][1]).toEqual({ questionRevision: "q1-r1" });
    expect(post.mock.calls[1][2]).toMatchObject({ headers: { "If-Match": '"d-r1"' } });
  });

  it.each([403, 422, 503])("withholds the aggregate and earlier drill rows when current drill authority fails (%s)", async status => {
    const { module, props } = fixture();
    const post = vi.fn(async (url: string) => {
      if (url.endsWith("/drill")) throw Object.assign(new Error("Current matching records could not be authorized."), { status });
      return result("previous-aggregate-row");
    });
    module.backend.http.postJson = post as typeof module.backend.http.postJson;
    const { view } = mount(props);
    await waitUntil(() => view.textContent?.includes("previous-aggregate-row") === true);
    const drill = [...view.querySelectorAll("button")].find(button => button.textContent === "View matching records")!;
    flushSync(() => drill.click());
    await waitUntil(() => view.textContent?.includes("Current matching records could not be authorized") === true);
    expect(view.querySelectorAll(".named-result")).toHaveLength(0); expect(view.textContent).not.toContain("previous-aggregate-row");
    expect(view.textContent).toContain("Result values are withheld"); expect(view.textContent).not.toContain("No authorized records matched");
    expect(view.querySelector(".named-tile-body > a")?.textContent).toBe(status === 403 ? undefined : "Question q1");
  });

  it.each(["question", "presentation", "parameters", "revision"] as const)("rejects a superseded reply when the retained tile's %s binding changes", async change => {
    const { module, props } = fixture();
    let late!: (value: QueryResult) => void; let oldSignal!: AbortSignal;
    const post = vi.fn((_url: string, _body: unknown, init?: unknown) => {
      if (post.mock.calls.length === 1) return Promise.resolve(result("initial-authorized-row"));
      if (post.mock.calls.length === 2) {
        oldSignal = (init as { signal: AbortSignal }).signal;
        return new Promise<QueryResult>(resolve => { late = resolve; });
      }
      return Promise.resolve(result("current-binding-row"));
    });
    module.backend.http.postJson = post as typeof module.backend.http.postJson;
    const { view, update } = mount(props);
    await waitUntil(() => view.textContent?.includes("initial-authorized-row") === true);
    update({ ...props, refresh: 1 }); await waitUntil(() => typeof late === "function");
    const changed = { ...props, refresh: 1,
      dashboard: change === "revision" ? { ...props.dashboard, revision: "d-r2" } : props.dashboard,
      tile: { ...props.tile, ...(change === "question" ? { questionId: "q2" } : change === "presentation" ? { presentation: { kind: "list" as const, y: [], label: "Updated tile presentation" } } : change === "parameters" ? { parameters: { category: { type: "string", literal: "current" } } } : {}) }
    };
    update(changed); expect(oldSignal.aborted).toBe(true);
    expect(view.textContent).not.toContain("initial-authorized-row");
    await waitUntil(() => view.textContent?.includes("current-binding-row") === true);
    late(result("SUPERSEDED-PRIVATE-ROW")); await new Promise(resolve => setTimeout(resolve, 0));
    expect(view.textContent).toContain("current-binding-row"); expect(view.textContent).not.toContain("SUPERSEDED-PRIVATE-ROW");
    expect(view.querySelector("a")?.textContent).toBe(change === "question" ? "Question q2" : "Question q1");
    if (change === "question") expect(post.mock.calls[2][1]).toEqual({ questionRevision: "q2-r1" });
    if (change === "revision") expect(post.mock.calls[2][2]).toMatchObject({ headers: { "If-Match": '"d-r2"' } });
    expect(post.mock.calls.every(call => !String(call[0]).includes("dashboard-queries"))).toBe(true);
  });

  it("withholds the aggregate after cancelling a tile investigation and ignores late matching records", async () => {
    const { module, props } = fixture();
    let complete!: (value: QueryResult) => void; let signal!: AbortSignal;
    module.backend.http.postJson = vi.fn((url: string, _body: unknown, init?: unknown) => {
      if (!url.endsWith("/drill")) return Promise.resolve(result("previous-aggregate-row"));
      signal = (init as { signal: AbortSignal }).signal; return new Promise<QueryResult>(resolve => { complete = resolve; });
    }) as typeof module.backend.http.postJson;
    const { view } = mount(props);
    await waitUntil(() => view.textContent?.includes("previous-aggregate-row") === true);
    flushSync(() => [...view.querySelectorAll("button")].find(button => button.textContent === "View matching records")!.click());
    await waitUntil(() => typeof complete === "function");
    flushSync(() => [...view.querySelectorAll("button")].find(button => button.textContent === "Close")!.click());
    expect(signal.aborted).toBe(true); expect(view.querySelectorAll(".named-result")).toHaveLength(0);
    expect(view.textContent).toContain("Investigation cancelled"); expect(view.textContent).toContain("Result values are withheld");
    complete(result("LATE-PRIVATE-ROW")); await new Promise(resolve => setTimeout(resolve, 0));
    expect(view.textContent).not.toContain("LATE-PRIVATE-ROW"); expect(view.textContent).not.toContain("previous-aggregate-row");
    expect(view.textContent).not.toContain("No authorized records matched"); expect(view.querySelector("a")?.textContent).toBe("Question q1");
  });
});
