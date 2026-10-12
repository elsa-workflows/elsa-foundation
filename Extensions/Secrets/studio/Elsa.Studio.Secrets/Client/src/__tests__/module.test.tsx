import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioContributionRegistry } from "@elsa-workflows/studio-sdk";
import { register } from "../module";

describe("secrets module", () => {
  it("registers a Secrets-owned Secret Reference Contribution", () => {
    const api = stubApi();

    register(api);

    expect(api.featureAreas.list()).toEqual([
      expect.objectContaining({ id: "secrets", title: "Secrets", navGroup: "Security", ownedPaths: ["/security/secrets", "/secrets"] })
    ]);
    expect(api.propertyEditors.list()).toEqual([]);
    expect(api.expressionEditors.list()).toEqual([
      expect.objectContaining({
        id: "elsa.secret-reference-editor",
        surfaces: expect.objectContaining({ inline: expect.any(Function) }),
        createDefaultValue: expect.any(Function)
      })
    ]);
    const contribution = api.expressionEditors.list()[0];
    const context = {
      syntax: "Secret",
      surface: "inline" as const,
      descriptor: { name: "token", typeName: "System.String" },
      activity: {},
      expressionDescriptors: [{ type: "Secret", displayName: "Secret", editingMode: "reference" as const }]
    };
    expect(contribution.supports(context)).toBe(true);
    expect(contribution.createDefaultValue?.(context)).toBeNull();
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
    propertyEditors: registry(),
    expressionEditors: registry()
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
