import { afterEach, describe, expect, it, vi } from "vitest";
import { createStudioRegistry, findFeatureAreaForPath } from "../app/registry";
import { createEndpointContext, describeApiError, StudioHttpError, tryExtractValidationErrors, type StudioFeatureAreaContribution } from "../sdk";

describe("studio registry", () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("tracks typed bottom panel contributions", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });

    api.panels.add({ id: "second", title: "Second", order: 20, component: () => null });
    api.panels.add({ id: "first", title: "First", order: 10, component: () => null });

    expect(api.panels.list().map(panel => panel.id)).toEqual(["second", "first"]);
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
