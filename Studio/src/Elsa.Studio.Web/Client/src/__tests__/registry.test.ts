import { afterEach, describe, expect, it, vi } from "vitest";
import { createStudioRegistry } from "../app/registry";
import { createEndpointContext } from "../sdk";

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
});
