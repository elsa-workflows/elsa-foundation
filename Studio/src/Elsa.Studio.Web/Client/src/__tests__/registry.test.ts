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
