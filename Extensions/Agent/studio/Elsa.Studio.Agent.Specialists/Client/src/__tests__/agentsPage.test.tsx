import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { StudioHttpError, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";

// The signed-in session the page reads to decide whether to offer creation; null is a host without sign-in.
const auth = vi.hoisted(() => ({ session: null as null | { status: string; permissions: string[] } }));
vi.mock("@elsa-workflows/studio-sdk", async original => ({
  ...await original<typeof import("@elsa-workflows/studio-sdk")>(),
  useOptionalAuthContext: () => auth.session ? { session: auth.session } : null
}));

import { AgentsPage, activityDefinitionPath } from "../AgentsPage";
import type { AgentRow } from "../agentsApi";

const cleanup = new Set<() => void>();
afterEach(() => { for (const dispose of cleanup) dispose(); cleanup.clear(); auth.session = null; });

const authoring = {
  contractSchemaVersions: ["1"],
  providers: [
    { providerKey: "elsa.activity-graph", manifestSchemas: [{ schemaVersion: "2", isAuthorable: true }], requiredOutcomes: [] },
    { providerKey: "elsa.specialist", manifestSchemas: [{ schemaVersion: "1", isAuthorable: true }, { schemaVersion: "2", isAuthorable: true }],
      requiredOutcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] }
  ]
};

function row(id: string, overrides: Partial<AgentRow> = {}): AgentRow {
  return {
    definition: { definitionId: id, activityTypeKey: `Agents.${id}`, category: "Agents", displayName: `Agent ${id}`, description: `Purpose ${id}`, contentAuthority: { kind: "design" } },
    lifecycle: { draftCount: 0, versionCount: 1, head: { versionId: `${id}-v1`, version: "1.0.0", lifecycle: "active", providerKey: "elsa.specialist", providerSchemaVersion: "2" } },
    actions: [{ action: "edit-definition", allowed: true }],
    updatedAt: "2026-11-02T10:00:00Z",
    ...overrides
  };
}

type Page = { items: AgentRow[]; totalCount: number; hasMore?: boolean; continuation?: string };

function fakeContext(pages: Array<Page | Error | Promise<Page>>, options: { authoring?: unknown; drafts?: Record<string, string[] | Promise<string[]>> } = {}) {
  const getJson = vi.fn(async (path: string) => {
    if (path.startsWith("/design/activities/authoring-capabilities")) {
      if (options.authoring instanceof Error) throw options.authoring;
      return options.authoring ?? authoring;
    }
    const drafts = /^\/design\/activities\/definitions\/([^/?]+)\/drafts\?/.exec(path);
    if (drafts) return { items: (await options.drafts?.[decodeURIComponent(drafts[1])] ?? []).map(draftId => ({ draft: { draftId } })) };
    const page = await pages.shift();
    if (!page) throw new Error(`unexpected ${path}`);
    if (page instanceof Error) throw page;
    return { count: page.items.length, hasMore: false, continuation: null, ...page };
  });
  const postJson = vi.fn(async (_path: string, _body: unknown) => ({ definition: { definitionId: "new-agent" }, draft: { draftId: "draft-1" } }));
  return { context: { http: { getJson, postJson } } as unknown as StudioEndpointContext, getJson, postJson };
}

async function settle() {
  for (let index = 0; index < 8; index++) await new Promise(resolve => setTimeout(resolve, 0));
}

async function render(context: StudioEndpointContext, navigate = vi.fn()) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<AgentsPage context={context} navigate={navigate} />));
  await settle();
  await new Promise(resolve => setTimeout(resolve, 300));
  await settle();
  cleanup.add(() => { root.unmount(); container.remove(); });
  return container;
}

function input(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(element), "value")!.set!;
  setter.call(element, value);
  element.dispatchEvent(new Event("input", { bubbles: true }));
}

const listPaths = (getJson: ReturnType<typeof vi.fn>) => getJson.mock.calls.map(call => String(call[0])).filter(path => path.startsWith("/design/activities/definitions"));

describe("agents page", () => {
  it("asks the server for specialist definitions only and shows its exact count", async () => {
    const { context, getJson } = fakeContext([{ items: [row("a"), row("b")], totalCount: 2 }]);
    const container = await render(context);

    const [path] = listPaths(getJson);
    const parameters = new URL(path, "http://studio").searchParams;
    expect(parameters.get("providerKey")).toBe("elsa.specialist");
    expect(parameters.get("limit")).toBe("25");
    expect(container.querySelector(".agent-collection-count")?.textContent).toBe("2 agents");
    expect(container.textContent).toContain("Agent a");
    expect(container.textContent).toContain("Purpose b");
  });

  it("shows draft-only, configuration-owned, read-only and other-provider rows truthfully", async () => {
    const { context } = fakeContext([{ items: [
      row("draft", { lifecycle: { draftCount: 1, versionCount: 0, head: null } }),
      row("configured", { definition: { ...row("configured").definition, contentAuthority: { kind: "providerSource", authorityKey: "config" } },
        actions: [{ action: "edit-definition", allowed: false, unavailableCode: "activity.definition.source-owned" }, { action: "fork-definition", allowed: true }] }),
      row("pinned", { definition: { ...row("pinned").definition, contentAuthority: { kind: "providerSource" } }, actions: [{ action: "edit-definition", allowed: false }] }),
      row("viewer", { actions: [{ action: "edit-definition", allowed: false }] }),
      row("mixed", { lifecycle: { draftCount: 1, versionCount: 1, head: { versionId: "mixed-v1", version: "1.0.0", lifecycle: "active", providerKey: "elsa.activity-graph", providerSchemaVersion: "2" } } }),
      row("retired", { lifecycle: { draftCount: 0, versionCount: 1, head: { versionId: "retired-v1", version: "1.0.0", lifecycle: "retired", providerKey: "elsa.specialist", providerSchemaVersion: "2" } } })
    ], totalCount: 6 }]);
    const container = await render(context);
    const text = (id: string) => [...container.querySelectorAll("[role='row']")].find(x => x.textContent?.includes(`Agent ${id}`))?.textContent ?? "";

    expect(text("draft")).toContain("Not published");
    expect(text("draft")).toContain("1 draft of any kind in this definition");
    expect(text("configured")).toContain("Configuration");
    expect(text("configured")).toContain("Read-only; fork it to change it");
    expect(text("pinned")).toContain("Configuration");
    expect(text("pinned")).not.toContain("fork");
    expect(text("viewer")).toContain("Studio");
    expect(text("viewer")).toContain("Read-only for you");
    // Another provider's published version is not the agent's publication, and is disclosed without its provider key.
    expect(text("mixed")).toContain("Not published");
    expect(text("mixed")).not.toContain("Published 1.0.0");
    expect(text("mixed")).toContain("Its latest version is another kind of activity");
    expect(text("mixed")).toContain("1 draft · 1 version of any kind in this definition");
    expect(text("mixed")).not.toContain("elsa.activity-graph");
    expect(text("retired")).toContain("1.0.0 retired");
    expect(text("retired")).not.toContain("Published");
  });

  it("continues with the server's cursor and appends the next page", async () => {
    const { context, getJson } = fakeContext([
      { items: [row("a")], totalCount: 2, hasMore: true, continuation: "cursor-1" },
      { items: [row("b")], totalCount: 2 }
    ]);
    const container = await render(context);

    [...container.querySelectorAll("button")].find(x => x.textContent === "Load more")!.click();
    await settle();

    expect(new URL(listPaths(getJson)[1], "http://studio").searchParams.get("cursor")).toBe("cursor-1");
    expect(container.textContent).toContain("Agent a");
    expect(container.textContent).toContain("Agent b");
    expect([...container.querySelectorAll("button")].some(x => x.textContent === "Load more")).toBe(false);
  });

  it("names which version is another kind of activity when only one of them is", async () => {
    const graph = { versionId: "g1", version: "2.0.0", lifecycle: "active", providerKey: "elsa.activity-graph", providerSchemaVersion: "2" };
    const agent = { versionId: "s1", version: "1.0.0", lifecycle: "active", providerKey: "elsa.specialist", providerSchemaVersion: "2" };
    const { context } = fakeContext([{ items: [row("x", { lifecycle: { draftCount: 0, versionCount: 2, head: graph, recommendation: agent } })], totalCount: 1 }]);
    const container = await render(context);

    expect(container.textContent).toContain("Published 1.0.0");
    expect(container.textContent).toContain("Its latest version is another kind of activity");
  });

  it("says when the list changed under its cursor and restarts only when asked", async () => {
    const changed = new StudioHttpError(400, "Bad Request", null, { errorCode: "activity.management.cursor-invalid" });
    const { context, getJson } = fakeContext([{ items: [row("a")], totalCount: 2, hasMore: true, continuation: "cursor-1" }, changed]);
    const container = await render(context);

    [...container.querySelectorAll("button")].find(x => x.textContent === "Load more")!.click();
    await settle();

    expect(container.textContent).toContain("Your access or this list's filter changed since it was loaded");
    expect(listPaths(getJson)).toHaveLength(2);
  });

  it("says when its snapshot expired instead of silently restarting the list", async () => {
    const expired = new StudioHttpError(410, "Gone", null, { errorCode: "activity.cursor.expired", recovery: { instruction: "restart-without-cursor" } });
    const { context, getJson } = fakeContext([{ items: [row("a")], totalCount: 2, hasMore: true, continuation: "cursor-1" }, expired, { items: [row("a"), row("b")], totalCount: 3 }]);
    const container = await render(context);

    [...container.querySelectorAll("button")].find(x => x.textContent === "Load more")!.click();
    await settle();

    expect(container.textContent).toContain("This list has expired since it was loaded");
    expect(container.querySelector(".agent-collection-count")?.textContent).toBe("2 agents");
    expect(listPaths(getJson)).toHaveLength(2);
    [...container.querySelectorAll("button")].find(x => x.textContent?.includes("Reload from the start"))!.click();
    await settle();
    expect(new URL(listPaths(getJson)[2], "http://studio").searchParams.get("cursor")).toBeNull();
    expect(container.querySelector(".agent-collection-count")?.textContent).toBe("3 agents");
    expect(document.activeElement).toBe(container.querySelector(".agent-collection-count"));
  });

  it("moves focus to Retry when a reload fails, and not again when a later search succeeds", async () => {
    const expired = new StudioHttpError(410, "Gone", null, { errorCode: "activity.cursor.expired" });
    const { context } = fakeContext([{ items: [row("a")], totalCount: 2, hasMore: true, continuation: "cursor-1" }, expired, new Error("offline"), { items: [row("a")], totalCount: 1 }]);
    const container = await render(context);

    [...container.querySelectorAll("button")].find(x => x.textContent === "Load more")!.click();
    await settle();
    [...container.querySelectorAll("button")].find(x => x.textContent?.includes("Reload from the start"))!.click();
    await settle();
    expect(container.textContent).toContain("Agents are unavailable");
    expect(document.activeElement?.textContent).toContain("Retry");
    const search = container.querySelector("input[type='search']") as HTMLInputElement;
    search.focus();
    input(search, "a");
    await new Promise(resolve => setTimeout(resolve, 300));
    await settle();

    expect(container.textContent).toContain("Agent a");
    expect(document.activeElement).toBe(search);
  });

  it("keeps focus deliberate when Retry is refused for lack of permission", async () => {
    const { context } = fakeContext([new Error("offline"), new StudioHttpError(403, "Forbidden")]);
    const container = await render(context);

    [...container.querySelectorAll("button")].find(x => x.textContent?.includes("Retry"))!.click();
    await settle();

    expect(container.textContent).toContain("You can't view agents");
    expect(document.activeElement?.getAttribute("aria-label")).toBe("You can't view agents");
  });

  it("leaves focus where the author moved it while a reload was running", async () => {
    let finish: (page: Page) => void = () => undefined;
    const pending = new Promise<Page>(resolve => { finish = resolve; });
    const expired = new StudioHttpError(410, "Gone", null, { errorCode: "activity.cursor.expired" });
    const { context } = fakeContext([{ items: [row("a")], totalCount: 2, hasMore: true, continuation: "cursor-1" }, expired, pending]);
    const container = await render(context);

    [...container.querySelectorAll("button")].find(x => x.textContent === "Load more")!.click();
    await settle();
    [...container.querySelectorAll("button")].find(x => x.textContent?.includes("Reload from the start"))!.click();
    await settle();
    [...container.querySelectorAll("button")].find(x => x.textContent?.includes("New agent"))!.click();
    await settle();
    expect(container.querySelector('[aria-label="Search agents"]')).toBeNull();
    expect(container.querySelector('[role="table"]')).toBeNull();
    await settle();
    const name = container.querySelector(".agent-create-form input");
    expect(document.activeElement).toBe(name);
    finish({ items: [row("a")], totalCount: 1 });
    await settle();

    expect(container.querySelector(".agent-collection-count")).toBeNull();
    expect(document.activeElement).toBe(name);
    [...container.querySelectorAll("button")].find(x => x.textContent === "Cancel")!.click();
    await settle();
    expect(container.querySelector(".agent-collection-count")?.textContent).toBe("1 agent");
  });

  it("keeps the list when another refusal stops loading more", async () => {
    const { context } = fakeContext([{ items: [row("a")], totalCount: 2, hasMore: true, continuation: "cursor-1" }, new StudioHttpError(400, "Bad Request")]);
    const container = await render(context);

    [...container.querySelectorAll("button")].find(x => x.textContent === "Load more")!.click();
    await settle();

    expect(container.textContent).toContain("More agents could not be loaded.");
    expect(container.textContent).toContain("Agent a");
  });

  it("searches on the server", async () => {
    const { context, getJson } = fakeContext([{ items: [row("a")], totalCount: 1 }, { items: [], totalCount: 0 }]);
    const container = await render(context);

    input(container.querySelector("input[type='search']")!, "moss");
    await new Promise(resolve => setTimeout(resolve, 300));
    await settle();

    expect(new URL(listPaths(getJson).at(-1)!, "http://studio").searchParams.get("search")).toBe("moss");
    expect(container.textContent).toContain("No matching agents");
  });

  it("explains a denied list without showing rows", async () => {
    const { context } = fakeContext([new StudioHttpError(403, "Forbidden")]);
    const container = await render(context);

    expect(container.textContent).toContain("You can't view agents");
    expect(container.querySelector("[role='row']")).toBeNull();
  });

  it("creates a specialist draft with the newest schema and opens it in the generic editor", async () => {
    const navigate = vi.fn();
    const { context, postJson } = fakeContext([{ items: [], totalCount: 0 }]);
    const container = await render(context, navigate);

    [...container.querySelectorAll("button")].find(x => x.textContent?.includes("New agent"))!.click();
    await settle();
    expect(container.querySelector('[aria-label="Search agents"]')).toBeNull();
    expect(container.querySelector('[role="table"]')).toBeNull();
    await settle();
    input(container.querySelector(".agent-create-form input")!, "Copywriter");
    input(container.querySelector(".agent-create-form textarea")!, "Writes short articles.");
    await settle();
    (container.querySelector(".agent-create-form") as HTMLFormElement).requestSubmit();
    await settle();

    const [path, body] = postJson.mock.calls[0] as [string, Record<string, unknown>];
    expect(path).toBe("/design/activities/definitions");
    expect(body.displayName).toBe("Copywriter");
    expect(body.description).toBe("Writes short articles.");
    expect(body.provider).toMatchObject({ providerKey: "elsa.specialist", schemaVersion: "2",
      payload: { instructions: "", allowContinuity: false, limits: { maximumModelCalls: 8, timeoutMilliseconds: 300000 } } });
    expect(body.contract).toEqual({ contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] });
    expect(navigate).toHaveBeenCalledWith(activityDefinitionPath("new-agent", "draft-1"));
    expect(activityDefinitionPath("new-agent", "draft-1")).toBe("/workflows/activity-definitions?definition=new-agent&section=editor&draft=draft-1");
  });

  it("offers no creation to a signed-in reader without the manage permission", async () => {
    auth.session = { status: "authenticated", permissions: ["activities/design:read"] };
    const { context } = fakeContext([{ items: [row("a")], totalCount: 1 }]);
    const container = await render(context);

    expect([...container.querySelectorAll("button")].some(x => x.textContent?.includes("New agent"))).toBe(false);
    expect(container.textContent).toContain("Agent a");
  });

  it("offers creation to a signed-in author and returns focus to it on cancel", async () => {
    auth.session = { status: "authenticated", permissions: ["activities/design:read", "activities/design:manage"] };
    const { context } = fakeContext([{ items: [], totalCount: 0 }]);
    const container = await render(context);
    const newAgent = () => [...container.querySelectorAll("button")].find(x => x.textContent?.includes("New agent"));

    newAgent()!.click();
    await settle();
    expect(document.activeElement).toBe(container.querySelector(".agent-create-form input"));
    [...container.querySelectorAll("button")].find(x => x.textContent === "Cancel")!.click();
    await settle();

    expect(document.activeElement).toBe(newAgent());
  });

  it("moves focus to the error when creating fails", async () => {
    const { context, postJson } = fakeContext([{ items: [], totalCount: 0 }]);
    postJson.mockRejectedValueOnce(new StudioHttpError(500, "Server Error"));
    const container = await render(context);

    [...container.querySelectorAll("button")].find(x => x.textContent?.includes("New agent"))!.click();
    await settle();
    expect(container.querySelector('[aria-label="Search agents"]')).toBeNull();
    expect(container.querySelector('[role="table"]')).toBeNull();
    await settle();
    input(container.querySelector(".agent-create-form input")!, "Copywriter");
    await settle();
    (container.querySelector(".agent-create-form") as HTMLFormElement).requestSubmit();
    await settle();

    expect(document.activeElement?.textContent).toBe("The agent could not be created. Nothing was saved.");
  });

  it("offers no creation when the server has no authorable specialist schema", async () => {
    const { context } = fakeContext([{ items: [row("a")], totalCount: 1 }], { authoring: { contractSchemaVersions: ["1"], providers: [] } });
    const container = await render(context);

    expect([...container.querySelectorAll("button")].some(x => x.textContent?.includes("New agent"))).toBe(false);
    expect(container.textContent).toContain("Agent a");
  });

  it("drops a slower draft lookup once another row was opened", async () => {
    const navigate = vi.fn();
    let finish: (drafts: string[]) => void = () => undefined;
    const slow = new Promise<string[]>(resolve => { finish = resolve; });
    const { context } = fakeContext([{ items: [row("a"), row("draft", { lifecycle: { draftCount: 1, versionCount: 0, head: null } })], totalCount: 2 }], { drafts: { draft: slow } });
    const container = await render(context, navigate);
    const rowFor = (id: string) => [...container.querySelectorAll("[role='row']")].find(x => x.textContent?.includes(`Agent ${id}`)) as HTMLElement;

    rowFor("draft").click();
    await settle();
    rowFor("a").click();
    finish(["draft-1"]);
    await settle();

    expect(navigate.mock.calls.map(call => call[0])).toEqual(["/workflows/activity-definitions?definition=a&version=a-v1"]);
  });

  it("opens a row's own resource: its published version, its only draft, or the definition to choose from", async () => {
    const navigate = vi.fn();
    const draftOnly = (id: string) => row(id, { lifecycle: { draftCount: 2, versionCount: 0, head: null } });
    const { context, getJson } = fakeContext([{ items: [row("a"), draftOnly("one"), draftOnly("two")], totalCount: 3 }], { drafts: { one: ["draft-1"], two: ["draft-2", "draft-3"] } });
    const container = await render(context, navigate);
    const open = async (id: string) => {
      ([...container.querySelectorAll("[role='row']")].find(x => x.textContent?.includes(`Agent ${id}`)) as HTMLElement).click();
      await settle();
    };

    await open("a");
    await open("one");
    await open("two");

    expect(navigate.mock.calls.map(call => call[0])).toEqual([
      "/workflows/activity-definitions?definition=a&version=a-v1",
      "/workflows/activity-definitions?definition=one&section=editor&draft=draft-1",
      "/workflows/activity-definitions?definition=two&section=drafts"
    ]);
    const draftQuery = new URL(getJson.mock.calls.map(call => String(call[0])).find(path => path.includes("/one/drafts"))!, "http://studio").searchParams;
    expect(Object.fromEntries(draftQuery)).toEqual({ limit: "2", providerKey: "elsa.specialist", status: "Active" });
  });
});
