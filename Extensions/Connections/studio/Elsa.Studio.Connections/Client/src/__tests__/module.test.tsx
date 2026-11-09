import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioContributionRegistry } from "@elsa-workflows/studio-sdk";
import { register } from "../module";

describe("connections module", () => {
  it("registers a Connections page and Connection expression editor", () => {
    const api = stubApi();

    register(api);

    expect(api.featureAreas.list()).toEqual([
      expect.objectContaining({ id: "connections", title: "Connections", navGroup: "Security", ownedPaths: ["/security/connections"] })
    ]);
    expect(api.propertyEditors.list()).toEqual([]);
    expect(api.expressionEditors.list()).toEqual([
      expect.objectContaining({
        id: "elsa.connection-reference-editor",
        surfaces: expect.objectContaining({ inline: expect.any(Function) }),
        createDefaultValue: expect.any(Function)
      })
    ]);
    const contribution = api.expressionEditors.list()[0];
    const context = {
      syntax: "Connection",
      surface: "inline" as const,
      descriptor: { name: "Connection", typeName: "ConnectionReference", uiHint: "connection-picker" },
      activity: {},
      expressionDescriptors: [{ type: "Connection", displayName: "Connection", editingMode: "reference" as const }]
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
        deleteJson: vi.fn()
      }
    },
    featureAreas: registry(),
    propertyEditors: registry(),
    expressionEditors: registry(),
    dialogs: { confirm: vi.fn(), prompt: vi.fn(), alert: vi.fn() }
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
