import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioContributionRegistry } from "@elsa-workflows/studio-sdk";
import { register } from "../module";

describe("secrets module", () => {
  it("registers feature area and secret picker editor", () => {
    const api = stubApi();

    register(api);

    expect(api.featureAreas.list()).toEqual([
      expect.objectContaining({ id: "secrets", title: "Secrets", navGroup: "Security", ownedPaths: ["/security/secrets", "/secrets"] })
    ]);
    expect(api.propertyEditors.list()).toEqual([
      expect.objectContaining({ id: "secret-picker" })
    ]);
    expect(api.propertyEditors.list()[0].supports({ name: "token", typeName: "String", uiHint: "secret-picker" }, { activity: {}, expressionDescriptors: [] })).toBe(true);
  });
});

function stubApi(): ElsaStudioModuleApi {
  return {
    backend: {
      baseUrl: "https://server.example",
      http: {
        requestJson: vi.fn(),
        getJson: vi.fn(),
        postJson: vi.fn(),
        putJson: vi.fn(),
        deleteJson: vi.fn(),
        postForm: vi.fn()
      }
    },
    featureAreas: registry(),
    propertyEditors: registry()
  };
}

function registry<T>(): StudioContributionRegistry<T> & { items: T[] } {
  const items: T[] = [];
  return {
    items,
    add: item => items.push(item),
    list: () => items
  };
}
