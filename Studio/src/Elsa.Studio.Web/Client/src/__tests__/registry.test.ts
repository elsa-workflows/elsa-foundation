import { afterEach, describe, expect, it, vi } from "vitest";
import { createStudioRegistry, findFeatureAreaForPath } from "../app/registry";
import { registerBuiltInPropertyEditors } from "../app/propertyEditors";
import {
  createContributionRegistry,
  createEndpointContext,
  defineStudioSlot,
  describeApiError,
  moduleSlotOwner,
  StudioHttpError,
  studioSlotIds,
  tryExtractValidationErrors,
  type StudioActivityInputDescriptor,
  type StudioActivityPropertyEditorContribution,
  type StudioActivityPropertyEditorContext,
  type StudioExpressionEditorContribution,
  type StudioFeatureAreaContribution
} from "../sdk";

describe("studio registry", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("tracks typed bottom panel contributions with deterministic ordering", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });

    api.panels.add({ id: "second", title: "Second", order: 20, component: () => null });
    api.panels.add({ id: "first", title: "First", order: 10, component: () => null });

    expect(api.panels.slot.id).toBe(studioSlotIds.panels);
    expect(api.panels.list().map(panel => panel.id)).toEqual(["first", "second"]);
  });

  it("uses stable fallback ordering instead of module load order", () => {
    const first = createContributionRegistry<{ id: string }>();
    const second = createContributionRegistry<{ id: string }>();

    first.add({ id: "zebra" });
    first.add({ id: "alpha" });
    second.add({ id: "alpha" });
    second.add({ id: "zebra" });

    expect(first.list().map(item => item.id)).toEqual(["alpha", "zebra"]);
    expect(second.list().map(item => item.id)).toEqual(["alpha", "zebra"]);
  });

  it("maps current registries to public Slot IDs", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });

    expect(api.routes.slot.id).toBe(studioSlotIds.routes);
    expect(api.dashboardWidgets.slot.id).toBe(studioSlotIds.dashboardWidgets);
    expect(api.workflowDesigner.panels.slot.id).toBe(studioSlotIds.workflowDesignerPanels);
    expect(api.propertyEditors.slot.id).toBe(studioSlotIds.propertyEditors);
    expect(api.agent.promptStarters.slot.id).toBe(studioSlotIds.agentPromptStarters);
    expect(api.agent.toolSlots.slot.id).toBe(studioSlotIds.agentToolSlots);
    expect(api.agent.toolContracts.slot.id).toBe(studioSlotIds.agentToolContracts);
    expect(api.agent.resultRenderers.slot.id).toBe(studioSlotIds.agentResultRenderers);
    expect(api.ai.tools.slot.id).toBe(studioSlotIds.aiTools);
  });

  it("supports module-owned nested Slots", () => {
    const weatherCardsSlot = defineStudioSlot({
      id: "weather.dashboard.cards",
      kind: "dashboard-widget",
      title: "Weather dashboard cards",
      parentId: studioSlotIds.dashboardWidgets,
      owner: moduleSlotOwner("weather")
    });
    const registry = createContributionRegistry<{ id: string; component: () => null }>({ slot: weatherCardsSlot });

    registry.add({ id: "weather.forecast", component: () => null });

    expect(registry.slot).toMatchObject({
      id: "weather.dashboard.cards",
      parentId: studioSlotIds.dashboardWidgets,
      owner: { kind: "module", moduleId: "weather" }
    });
    expect(registry.compose()[0]).toMatchObject({
      slot: weatherCardsSlot,
      availability: { state: "available" }
    });
  });

  it("runs Slot Owner rules before Host Policy vetoes", () => {
    const registry = createContributionRegistry<{ id: string; moduleId: string; component?: () => null }>({
      slot: defineStudioSlot({
        id: "weather.dashboard.cards",
        kind: "dashboard-widget",
        parentId: studioSlotIds.dashboardWidgets,
        owner: moduleSlotOwner("weather")
      }),
      slotOwner: ({ contribution }) => contribution.id.startsWith("weather.")
        ? true
        : { state: "hidden", reason: "Only weather cards are accepted." },
      hostPolicy: ({ contribution }) => contribution.moduleId === "blocked"
        ? { state: "unavailable", reason: "Host policy blocked this module." }
        : true
    });

    registry.add({ id: "other.card", moduleId: "weather", component: () => null });
    registry.add({ id: "weather.blocked", moduleId: "blocked", component: () => null });
    registry.add({ id: "weather.forecast", moduleId: "weather", component: () => null });

    expect(registry.list().map(item => item.id)).toEqual(["weather.forecast"]);
    expect(registry.compose({ includeHidden: true, includeUnavailable: true }).map(item => ({
      id: item.contribution.id,
      state: item.availability.state,
      source: item.availability.source,
      reason: item.availability.reason
    }))).toEqual([
      { id: "other.card", state: "hidden", source: "slot-owner", reason: "Only weather cards are accepted." },
      { id: "weather.blocked", state: "unavailable", source: "host-policy", reason: "Host policy blocked this module." },
      { id: "weather.forecast", state: "available", source: undefined, reason: undefined }
    ]);
  });

  it("hides disabled module and feature Contributions by default", () => {
    const registry = createContributionRegistry<{ id: string; moduleId?: string; featureId?: string }>();

    registry.add({ id: "weather.forecast", moduleId: "weather" });
    registry.add({ id: "secrets.picker", featureId: "secrets" });
    registry.add({ id: "workflows.definitions", moduleId: "workflows" });

    expect(registry.list({ disabledModuleIds: ["weather"], disabledFeatureIds: ["secrets"] }).map(item => item.id)).toEqual(["workflows.definitions"]);
    expect(registry.compose({
      disabledModuleIds: ["weather"],
      disabledFeatureIds: ["secrets"],
      includeHidden: true
    }).map(item => [item.contribution.id, item.availability.state, item.availability.source])).toEqual([
      ["secrets.picker", "hidden", "feature"],
      ["weather.forecast", "hidden", "module"],
      ["workflows.definitions", "available", undefined]
    ]);
  });

  it("can surface runtime-unavailable Contributions with disabled reasons", () => {
    const registry = createContributionRegistry<{
      id: string;
      availability?: { state: "unavailable"; reason: string };
    }>();

    registry.add({ id: "weaver.prompt.build", availability: { state: "unavailable", reason: "Weaver backend is offline." } });
    registry.add({ id: "weaver.prompt.explain" });

    expect(registry.list().map(item => item.id)).toEqual(["weaver.prompt.explain"]);
    expect(registry.compose({ includeUnavailable: true }).map(item => ({
      id: item.contribution.id,
      availability: item.availability
    }))).toEqual([
      { id: "weaver.prompt.build", availability: { state: "unavailable", reason: "Weaver backend is offline.", source: "runtime" } },
      { id: "weaver.prompt.explain", availability: { state: "available" } }
    ]);
  });

  it("tracks diagnostics widget contributions through the public SDK registry", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });
    const widget = {
      id: "runtime-health",
      title: "Runtime health",
      component: () => null
    };

    api.diagnosticsWidgets.add(widget);

    expect(api.diagnosticsWidgets.list()).toEqual([widget]);
  });

  it("registers descriptor-driven property editors with fallback and order-based overrides", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });
    const customEditor: StudioActivityPropertyEditorContribution = {
      id: "custom.singleline",
      order: 1,
      supports: descriptor => descriptor.uiHint === "singleline",
      component: () => null
    };
    const context: StudioActivityPropertyEditorContext = { activity: {}, expressionDescriptors: [] };

    registerBuiltInPropertyEditors(api);
    api.propertyEditors.add(customEditor);

    expect(resolveEditor(api.propertyEditors.list(), input({ uiHint: "checkbox", typeName: "System.Boolean" }), context)?.id).toBe("studio.property.checkbox");
    expect(resolveEditor(api.propertyEditors.list(), input({ uiHint: "unknown", typeName: "Acme.Custom" }), context)?.id).toBe("studio.property.text-fallback");
    expect(resolveEditor(api.propertyEditors.list(), input({ uiHint: "singleline", typeName: "System.String" }), context)?.id).toBe("custom.singleline");

    // Collection scope: an option set is owned by the multi-select; without options nothing claims it, so
    // the property panel falls back to its repeater. Element scope is unaffected by either rule.
    const collectionContext: StudioActivityPropertyEditorContext = { ...context, scope: "collection" };
    const optionInput = input({ typeName: "System.Collections.Generic.ICollection`1", uiSpecifications: { options: ["a", "b"] } });
    expect(resolveEditor(api.propertyEditors.list(), optionInput, collectionContext)?.id).toBe("studio.property.multiselect");
    expect(resolveEditor(api.propertyEditors.list(), input({ typeName: "System.Collections.Generic.ICollection`1" }), collectionContext)).toBeUndefined();
    expect(resolveEditor(api.propertyEditors.list(), optionInput, context)?.id).toBe("studio.property.dropdown");
  });

  it("tracks expression editor contributions through the public SDK registry", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });
    const expandedEditor: StudioExpressionEditorContribution = {
      id: "javascript.expanded",
      order: 20,
      supports: context => context.syntax === "JavaScript",
      surfaces: { expanded: () => null }
    };
    const inlineEditor: StudioExpressionEditorContribution = {
      id: "javascript.inline",
      order: 10,
      supports: context => context.syntax === "JavaScript",
      surfaces: { inline: () => null }
    };

    api.expressionEditors.add(expandedEditor);
    api.expressionEditors.add(inlineEditor);

    expect(api.expressionEditors.list()).toEqual([inlineEditor, expandedEditor]);
  });

  it("preserves navigation icon colors", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });

    api.navigation.add({ id: "weather", label: "Weather", path: "/weather", iconColor: "#14b8a6" });

    expect(api.navigation.list()[0]).toMatchObject({ iconColor: "#14b8a6" });
  });

  it("expands feature areas into navigation and route contributions", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });
    const Definitions = () => null;
    const Instances = () => null;

    api.featureAreas.add({
      id: "workflows",
      title: "Workflows",
      description: "Workflow management.",
      navGroup: "Workspace",
      ownedPaths: ["/workflows"],
      required: true,
      defaultEnabled: true,
      order: 20,
      nav: {
        title: "Workflows",
        path: "/workflows/definitions",
        iconColor: "#0ea5e9",
        items: [
          { title: "Definitions", path: "/workflows/definitions" },
          { title: "Instances", path: "/workflows/instances" }
        ]
      },
      routes: [
        { id: "workflows-definitions", path: "/workflows/definitions", label: "Definitions", component: Definitions },
        { id: "workflows-instances", path: "/workflows/instances", label: "Instances", component: Instances }
      ]
    });

    expect(api.featureAreas.list()).toEqual([
      expect.objectContaining({ id: "workflows", ownedPaths: ["/workflows"], required: true, defaultEnabled: true })
    ]);
    expect(api.navigation.list()).toEqual([
      expect.objectContaining({ id: "workflows", path: "/workflows/definitions", activePathPrefix: "/workflows" }),
      expect.objectContaining({ id: "workflows-definitions", path: "/workflows/definitions", parentId: "workflows" }),
      expect.objectContaining({ id: "workflows-instances", path: "/workflows/instances", parentId: "workflows" })
    ]);
    expect(api.routes.list().map(route => route.id)).toEqual(["workflows-definitions", "workflows-instances"]);
    expect(findFeatureAreaForPath(api.featureAreas.list(), "/workflows/instances/42")?.id).toBe("workflows");
  });

  it("tracks AI contributions and dispatches prompts", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });
    const listener = vi.fn();

    api.ai.promptActions.add({
      id: "explain-resource",
      label: "Explain",
      placement: "selection",
      contextKind: "resource",
      createPrompt: () => ({ message: "Explain this resource." })
    });
    const unsubscribe = api.ai.onPrompt(listener);
    api.ai.dispatchPrompt({ message: "Explain this workflow." });
    unsubscribe();
    api.ai.dispatchPrompt({ message: "This should not be observed." });

    expect(api.ai.promptActions.list()).toEqual([expect.objectContaining({ id: "explain-resource" })]);
    expect(listener).toHaveBeenCalledTimes(1);
    expect(listener).toHaveBeenCalledWith({ message: "Explain this workflow." });
  });

  it("selects the most specific feature area owner for nested paths", () => {
    const parent = featureArea("workflows", ["/workflows"]);
    const child = featureArea("workflow-instances", ["/workflows/instances"]);

    expect(findFeatureAreaForPath([parent, child], "/workflows/instances/42")?.id).toBe("workflow-instances");
  });

  it("creates a backend client that can target a separate base url", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");

    await api.backend.http.getJson("/api/workflows");

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0]?.[0]).toBe("https://foundation.example/api/workflows");
  });

  it("applies configured backend headers to backend client requests", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/", { "X-Elsa-Module-Management-Key": "secret" });

    await api.backend.http.getJson("/_elsa/module-management/registry");

    const headers = new Headers(fetchMock.mock.calls[0]?.[1]?.headers);
    expect(headers.get("X-Elsa-Module-Management-Key")).toBe("secret");
    expect(headers.get("Accept")).toBe("application/json");
  });

  it("times out stalled backend client requests with a configuration-focused error", async () => {
    vi.useFakeTimers();
    vi.stubGlobal("fetch", vi.fn((_input: RequestInfo | URL, init?: RequestInit) => new Promise((_resolve, reject) => {
      init?.signal?.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")));
    })));
    const client = createEndpointContext("https://foundation.example/").http;

    const request = client.getJson("/_elsa/workflow-management/definitions");
    const expectation = expect(request).rejects.toThrow("backend API is responding");
    await vi.advanceTimersByTimeAsync(10000);

    await expectation;
  });

  it("posts json through the backend client", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ ok: true }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    }, "https://foundation.example/");

    await api.backend.http.postJson("/modularity/features/apply", { revision: "rev", features: [] });

    expect(fetchMock.mock.calls[0]?.[0]).toBe("https://foundation.example/modularity/features/apply");
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: "POST",
      body: JSON.stringify({ revision: "rev", features: [] })
    });
  });

  it("supports put, delete, and form requests through the backend client", async () => {
    const fetchMock = vi.fn(async () => new Response("", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    const client = createEndpointContext("https://foundation.example/").http;
    const form = new FormData();
    form.append("package", new Blob(["package"]), "module.nupkg");

    await client.putJson("/settings", { enabled: true });
    await client.deleteJson("/settings/old");
    await client.postForm("/packages/upload", form);

    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      method: "PUT",
      body: JSON.stringify({ enabled: true })
    });
    expect(new Headers(fetchMock.mock.calls[0]?.[1]?.headers).get("Content-Type")).toBe("application/json");
    expect(fetchMock.mock.calls[1]?.[1]).toMatchObject({ method: "DELETE" });
    expect(fetchMock.mock.calls[2]?.[1]).toMatchObject({ method: "POST", body: form });
    expect(new Headers(fetchMock.mock.calls[2]?.[1]?.headers).get("Content-Type")).toBeNull();
  });

  it("preserves problem detail messages on http failures", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(
      JSON.stringify({ detail: "Revision is stale." }),
      { status: 409, headers: { "content-type": "application/problem+json" } })));
    const client = createEndpointContext("https://foundation.example/").http;

    await expect(client.getJson("/modularity/features")).rejects.toMatchObject({
      status: 409,
      message: "Revision is stale."
    } satisfies Partial<StudioHttpError>);
  });

  it("preserves reason payloads on structured http failures", async () => {
    const payload = {
      testRunId: "test-run-1",
      status: "Rejected",
      reason: "Workflow version has no root activity to publish."
    };
    vi.stubGlobal("fetch", vi.fn(async () => new Response(
      JSON.stringify(payload),
      { status: 400, headers: { "content-type": "application/json" } })));
    const client = createEndpointContext("https://foundation.example/").http;

    await expect(client.postJson("/publishing/workflows/version-1/test-runs", {})).rejects.toMatchObject({
      status: 400,
      message: payload.reason,
      payload
    } satisfies Partial<StudioHttpError>);
  });

  it("extracts validation errors from http failures", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(
      JSON.stringify({ errors: { Name: ["Name is required."] } }),
      { status: 422, headers: { "content-type": "application/json" } })));
    const client = createEndpointContext("https://foundation.example/").http;

    const error = await client.postJson("/workflow-definitions", {}).catch(e => e);

    await expect(describeApiError(error)).resolves.toContain("Name is required.");
    await expect(tryExtractValidationErrors(error)).resolves.toEqual({ Name: ["Name is required."] });
  });

  it("reports successful non-json responses as endpoint configuration errors", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(
      "<!doctype html><title>Fallback</title>",
      { status: 200, headers: { "content-type": "text/html" } })));
    const client = createEndpointContext("https://foundation.example/").http;

    await expect(client.getJson("/modularity/features")).rejects.toMatchObject({
      status: 200,
      message: expect.stringContaining("Expected JSON from https://foundation.example/modularity/features")
    } satisfies Partial<StudioHttpError>);
  });
});

function resolveEditor(
  editors: StudioActivityPropertyEditorContribution[],
  descriptor: StudioActivityInputDescriptor,
  context: StudioActivityPropertyEditorContext
) {
  return [...editors]
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500))
    .find(editor => editor.supports(descriptor, context));
}

function input(overrides: Partial<StudioActivityInputDescriptor>): StudioActivityInputDescriptor {
  return {
    name: "Text",
    displayName: "Text",
    typeName: "System.String",
    inputs: [],
    ...overrides
  } as StudioActivityInputDescriptor;
}

function featureArea(id: string, ownedPaths: string[]): StudioFeatureAreaContribution {
  return {
    id,
    title: id,
    ownedPaths,
    nav: {
      title: id,
      path: ownedPaths[0] ?? `/${id}`
    },
    routes: []
  };
}
