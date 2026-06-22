import { afterEach, describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../app/registry";
import { createEndpointContext, StudioHttpError } from "../sdk";

describe("studio registry", () => {
  afterEach(() => {
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

  it("preserves problem detail messages on http failures", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(
      JSON.stringify({ detail: "Revision is stale." }),
      { status: 409, headers: { "content-type": "application/json" } })));
    const client = createEndpointContext("https://foundation.example/").http;

    await expect(client.getJson("/modularity/features")).rejects.toMatchObject({
      status: 409,
      message: "Revision is stale."
    } satisfies Partial<StudioHttpError>);
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
