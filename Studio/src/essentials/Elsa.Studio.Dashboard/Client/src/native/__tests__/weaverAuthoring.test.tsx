import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioAgentContextProviderContribution, StudioAgentResultRendererContribution, StudioAgentResultRendererProps, StudioAgentPromptRequest } from "@elsa-workflows/studio-sdk";
import { createDashboardAuthoringController, type DashboardAuthoringSnapshot, type DashboardAuthoringProposal } from "../authoring";
import { DashboardAskWeaver, registerDashboardWeaver } from "../weaverAuthoring";
import { initialDefinition, type QueryResult } from "../types";
import { stubApi, waitUntil } from "../../__tests__/testSupport";

function registry<T>() { const items: T[] = []; return { add(item: T) { items.push(item); }, list: () => items }; }
function harness(supported = true) {
  const base = stubApi();
  let available = true;
  const observers = new Set<(value: boolean) => void>();
  const promptResults = new Set<(value: { requestId: string; status: "completed" }) => void>();
  const agent = {
    clientId: "client-a", contextProviders: registry<StudioAgentContextProviderContribution>(), resultRenderers: registry<StudioAgentResultRendererContribution>(),
    promptStarters: registry(), capabilities: registry(), promptActions: registry(), actions: registry(), toolSlots: registry(), toolContracts: registry(),
    providerAvailability: { get: () => available, set(value: boolean) { available = value; observers.forEach(observer => observer(value)); }, subscribe(observer: (value: boolean) => void) { observers.add(observer); observer(available); return () => observers.delete(observer); } },
    dispatchPrompt: vi.fn<(request: StudioAgentPromptRequest) => void>(),
    onPromptResult(listener: (value: { requestId: string; status: "completed" }) => void) { promptResults.add(listener); return () => promptResults.delete(listener); }
  };
  const api = { ...base, agent, dialogs: { prompt: vi.fn(async () => "Group by category"), confirm: vi.fn(async () => true), alert: vi.fn(async () => undefined) }, backendSession: { key: "backend-a", disposed: false, context: base.backend, capabilities: { capabilities: supported ? [{ id: "elsa.api.studio-dashboard-authoring", contractVersion: "1", links: [] }] : [] } } } as unknown as ElsaStudioModuleApi;
  const controller = createDashboardAuthoringController(agent.clientId);
  registerDashboardWeaver(api, controller);
  const source = agent.contextProviders.list()[0];
  return { api, agent, controller, source };
}
function snapshot(generationId = "draft-one", resourceType: "question" | "question-draft" = "question-draft"): DashboardAuthoringSnapshot {
  const definition = initialDefinition(); definition.query.dataset = { id: "acme.processing", schemaRevision: "1" }; definition.query.metrics = [{ alias: "duration", metric: "processing-duration", revision: "1" }];
  return { generationId, client: { clientId: "client-a", generationId }, target: { moduleId: "Elsa.Studio.Dashboard", resourceType, resourceId: resourceType === "question" ? "saved-question" : generationId, revision: resourceType === "question" ? "saved-7" : "0" }, workingRevision: "0", definition, name: "Processing question", dirty: true, canEdit: true };
}
function toolProps(type: string, data: unknown): StudioAgentResultRendererProps {
  return { proposal: undefined, result: { typeId: type, schemaVersion: "1", data }, resultType: type, toolResult: { toolCallId: "tool-1", toolName: type === "dashboards.clarification" ? "dashboard_request_clarification" : "dashboard_preview_question", succeeded: true, content: "Validated declarations only.", payload: { typeId: type, schemaVersion: "1", data } } };
}
function proposed(origin: DashboardAuthoringSnapshot) { return { target: origin.target, client: origin.client, workingRevision: origin.workingRevision, definition: { ...origin.definition, query: { ...origin.definition.query, groupBy: ["category"] } }, definitionFingerprint: "a".repeat(64), interpretation: "Elapsed processing duration by category." }; }
function result(): QueryResult { return { outcome: "succeeded", interpretation: "Authorized processing duration.", columns: [{ id: "duration", label: "Duration", type: "decimal" }], rows: [{ key: "retail", values: { duration: { type: "decimal", value: "987654321" } } }], coverage: { status: "unknown", description: "Collector coverage is unknown." } }; }
const disposers: (() => void)[] = [];
function render(content: React.ReactNode) { const container = document.createElement("div"); document.body.append(container); const root = createRoot(container); flushSync(() => root.render(content)); disposers.push(() => { flushSync(() => root.unmount()); container.remove(); }); return container; }
function click(container: HTMLElement, text: string) { const target = [...container.querySelectorAll("button")].find(item => item.textContent?.trim() === text); expect(target).toBeDefined(); flushSync(() => target!.click()); }
afterEach(() => { disposers.splice(0).forEach(dispose => dispose()); vi.restoreAllMocks(); });

describe("Dashboard Weaver contribution admission", () => {
  it("gates authoring by current backend capability and provider availability", async () => {
    const unavailable = harness(false); unavailable.controller.publish(snapshot(), vi.fn());
    expect(unavailable.source.requiresBackend).toEqual([{ id: "elsa.api.studio-dashboard-authoring", contractVersion: "1" }]);
    const denied = await unavailable.source.collect({ surface: { route: "/questions/new" } });
    expect(!Array.isArray(denied) && denied.diagnostics[0].required).toBe(true);
    const ready = harness(); ready.controller.publish(snapshot(), vi.fn());
    ready.agent.providerAvailability.set(false);
    const view = render(<DashboardAskWeaver controller={ready.controller} generationId="draft-one" />);
    expect(view.textContent).toContain("Ordinary editing, saving and refresh remain available");
    expect(view.querySelector<HTMLButtonElement>('button[type="submit"]')?.disabled).toBe(true);
    expect(ready.agent.dispatchPrompt).not.toHaveBeenCalled();
  });

  it("collects one exact declarative draft without duplicate prompt attachments or protected result facts", async () => {
    const test = harness(); const origin = snapshot(); test.controller.publish(origin, vi.fn());
    expect(test.controller.weaver?.request(origin.generationId, "Show processing time by category").accepted).toBe(true);
    expect(test.agent.dispatchPrompt.mock.calls[0][0].attachments).toBeUndefined();
    const collection = await test.source.collect({ surface: { route: "/questions/new" } });
    expect(Array.isArray(collection)).toBe(false);
    if (Array.isArray(collection)) throw new Error("Expected a typed context collection");
    expect(collection.attachments).toHaveLength(1); expect(collection.attachments[0].origin).toBe("clientSnapshot");
    expect(collection.attachments[0].client).toEqual(origin.client); expect(collection.attachments[0].agentScopes).toEqual(["/questions", "/dashboards"]);
    expect(Object.keys(collection.attachments[0].content as object).sort()).toEqual(["client", "definition", "dirty", "name", "target", "workingRevision"]);
  });

  it("refuses navigation retargeting and ambiguity, then clears consumed or cancelled dispatch markers", async () => {
    const test = harness(); const first = snapshot(); const remove = test.controller.publish(first, vi.fn());
    test.controller.weaver?.request(first.generationId, "A question about the first draft");
    remove(); test.controller.publish(snapshot("draft-two"), vi.fn());
    const moved = await test.source.collect({ surface: { route: "/questions/new" } });
    expect(!Array.isArray(moved) && moved.diagnostics[0].code).toBe("dashboards.context.target_changed");
    const next = await test.source.collect({ surface: { route: "/questions/new" } });
    expect(!Array.isArray(next) && next.attachments[0].client?.generationId).toBe("draft-two");
    test.controller.weaver?.request("draft-two", "Another question");
    const abort = new AbortController(); abort.abort();
    await expect(test.source.collect({ surface: { route: "/questions/new" }, signal: abort.signal })).rejects.toThrow();
    expect(test.controller.weaver?.request("draft-two", "Not permanently blocked").accepted).toBe(true);
    await test.source.collect({ surface: { route: "/questions/new" } });
    test.controller.publish(snapshot("draft-three"), vi.fn());
    const ambiguous = await test.source.collect({ surface: { route: "/questions/new" } });
    expect(!Array.isArray(ambiguous) && ambiguous.diagnostics[0].code).toBe("dashboards.context.ambiguous");
  });
});

describe("native previews outside the conversation", () => {
  it("previews actual native values but refines with only the exact proposed definition, adapting server SHA on Use", async () => {
    const test = harness(); const origin = snapshot(); const apply = vi.fn(async (_proposal: DashboardAuthoringProposal) => ({ outcome: "applied" as const, summary: "Applied locally." })); test.controller.publish(origin, apply);
    const post = vi.fn(async () => result()); test.api.backend.http.postJson = post as typeof test.api.backend.http.postJson;
    const proposal = proposed(origin);
    const Renderer = test.agent.resultRenderers.list().find(item => item.id === "dashboards.question-preview")!.component;
    const view = render(<Renderer {...toolProps("dashboards.question-preview", proposal)} />);
    click(view, "Preview proposed question"); await waitUntil(() => view.textContent?.includes("987654321") === true);
    click(view, "Refine proposal"); await waitUntil(() => test.agent.dispatchPrompt.mock.calls.length === 1);
    const context = await test.source.collect({ surface: { route: "/questions/new" } });
    if (Array.isArray(context)) throw new Error("Expected typed context");
    expect((context.attachments[0].content as { definition: unknown }).definition).toEqual(proposal.definition);
    expect(JSON.stringify(context)).not.toContain("987654321"); expect(JSON.stringify(test.agent.dispatchPrompt.mock.calls)).not.toContain("987654321");
    click(view, "Use this question"); await waitUntil(() => apply.mock.calls.length === 1);
    expect(apply.mock.calls[0][0]).toMatchObject({ definition: proposal.definition, client: origin.client, target: origin.target, workingRevision: "0" });
    expect(Object.hasOwn(apply.mock.calls[0][0], "definitionFingerprint")).toBe(false);
    expect(post.mock.calls).toHaveLength(1);
  });

  it("uses the saved native preview endpoint with the expected revision", async () => {
    const test = harness(); const origin = snapshot("saved-editor", "question"); test.controller.publish(origin, vi.fn());
    const post = vi.fn(async (_url: string, _body: unknown, _options: unknown) => result()); test.api.backend.http.postJson = post as typeof test.api.backend.http.postJson;
    const Renderer = test.agent.resultRenderers.list().find(item => item.id === "dashboards.question-preview")!.component;
    const view = render(<Renderer {...toolProps("dashboards.question-preview", proposed(origin))} />);
    click(view, "Preview proposed question"); await waitUntil(() => post.mock.calls.length === 1);
    expect(post.mock.calls[0][0]).toBe("https://backend.example/_elsa/studio/questions/saved-question/preview");
    expect(post.mock.calls[0][2]).toMatchObject({ headers: { "If-Match": '"saved-7"' } });
  });

  it("cancels and clears a native preview when its originating editor disappears, ignoring late transport success", async () => {
    const test = harness(); const origin = snapshot(); const remove = test.controller.publish(origin, vi.fn());
    let complete!: (value: QueryResult) => void;
    const post = vi.fn((_url: string, _body: unknown, _options: { signal: AbortSignal }) => new Promise<QueryResult>(resolve => { complete = resolve; })); test.api.backend.http.postJson = post as typeof test.api.backend.http.postJson;
    const Renderer = test.agent.resultRenderers.list().find(item => item.id === "dashboards.question-preview")!.component;
    const view = render(<Renderer {...toolProps("dashboards.question-preview", proposed(origin))} />);
    click(view, "Preview proposed question"); flushSync(remove);
    await waitUntil(() => view.textContent?.includes("Protected preview content has been cleared") === true);
    expect(post.mock.calls[0][2].signal.aborted).toBe(true); complete(result());
    await new Promise(resolve => setTimeout(resolve, 0)); expect(view.textContent).not.toContain("987654321");
  });

  it("does not treat rendering a clarification as user confirmation", async () => {
    const test = harness(); const origin = snapshot(); test.controller.publish(origin, vi.fn());
    const Renderer = test.agent.resultRenderers.list().find(item => item.id === "dashboards.clarification")!.component;
    const data = { target: origin.target, client: origin.client, workingRevision: origin.workingRevision, clarification: { question: "Which period?", choices: [{ id: "week", label: "Calendar week", description: "Monday start in the saved time zone." }] } };
    const view = render(<Renderer {...toolProps("dashboards.clarification", data)} />);
    expect(test.agent.dispatchPrompt).not.toHaveBeenCalled();
    click(view, "Calendar week");
    const context = await test.source.collect({ surface: { route: "/questions/new" } });
    if (Array.isArray(context)) throw new Error("Expected typed context");
    expect((context.attachments[0].content as { confirmedInterpretation?: string }).confirmedInterpretation).toContain("Which period? Calendar week.");
    expect(test.agent.dispatchPrompt.mock.calls[0][0].message).toContain("I choose Calendar week");
  });
});
