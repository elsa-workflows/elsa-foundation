import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { register } from "../module";

describe("weaver chat module", () => {
  it("retires its duplicate chat UI — Weaver is now the host-owned assistant dock", () => {
    const api = testApi();

    register(api);

    expect(api.navigation.list()).toEqual([]);
    expect(api.routes.list()).toEqual([]);
    expect(api.panels.list()).toEqual([]);
    expect(api.ai.surfaces.list()).toEqual([]);
  });
});

function testApi(): ElsaStudioModuleApi {
  const lists = {
    navigation: [] as unknown[],
    routes: [] as unknown[],
    panels: [] as unknown[],
    surfaces: [] as unknown[]
  };
  return {
    navigation: { add: vi.fn(item => lists.navigation.push(item)), list: () => lists.navigation },
    routes: { add: vi.fn(item => lists.routes.push(item)), list: () => lists.routes },
    panels: { add: vi.fn(item => lists.panels.push(item)), list: () => lists.panels },
    ai: {
      surfaces: { add: vi.fn(item => lists.surfaces.push(item)), list: () => lists.surfaces },
      onPrompt: vi.fn(() => () => undefined)
    }
  } as unknown as ElsaStudioModuleApi;
}
