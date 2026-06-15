import { describe, expect, it } from "vitest";
import {
  createApplyPayload,
  getErrorMessage,
  isDirty,
  register,
  registerBuiltInSettingEditors,
  selectSettingEditor
} from "../module";

describe("feature management module", () => {
  it("registers navigation, route, and built-in setting editors", () => {
    const api = stubApi();

    register(api);

    expect(api.navigation.items).toHaveLength(1);
    expect(api.routes.items).toHaveLength(1);
    expect(api.settingEditors.items.length).toBeGreaterThanOrEqual(6);
  });

  it("selects a boolean editor by setting type", () => {
    const api = stubApi();
    registerBuiltInSettingEditors(api);

    const editor = selectSettingEditor(api, setting({ jsonType: "boolean" }));

    expect(editor.id).toBe("boolean");
  });

  it("creates apply payload with disabled feature configuration cleared", () => {
    const payload = createApplyPayload("rev-1", [
      feature("Enabled", true, { value: 1 }),
      feature("Disabled", false, { value: 2 })
    ]);

    expect(payload).toEqual({
      revision: "rev-1",
      features: [
        { id: "Enabled", enabled: true, configuration: { value: 1 } },
        { id: "Disabled", enabled: false, configuration: {} }
      ]
    });
  });

  it("detects dirty staged feature state", () => {
    const current = {
      revision: "rev-1",
      features: [feature("FeatureA", true, {})]
    };

    expect(isDirty(current, [feature("FeatureA", false, {})])).toBe(true);
    expect(isDirty(current, [feature("FeatureA", true, {})])).toBe(false);
  });

  it("normalizes backend conflict errors for display", () => {
    expect(getErrorMessage(new Error("Request failed with 409."))).toBe("Request failed with 409.");
  });
});

function stubApi() {
  return {
    backend: { http: { getJson: async () => ({ revision: "rev", features: [] }) } },
    navigation: registry(),
    routes: registry(),
    settingEditors: registry()
  } as any;
}

function registry() {
  const items: any[] = [];
  return {
    items,
    add(item: any) {
      items.push(item);
    },
    list() {
      return items;
    }
  };
}

function setting(overrides: Record<string, unknown>) {
  return {
    name: "Setting",
    displayName: "Setting",
    required: false,
    secret: false,
    sensitive: false,
    restartRequired: false,
    advanced: false,
    experimental: false,
    options: [],
    ...overrides
  } as any;
}

function feature(id: string, enabled: boolean, configuration: Record<string, unknown>) {
  return {
    id,
    displayName: id,
    categories: [],
    sourceKind: "runtime",
    enabled,
    configuration,
    advanced: false,
    experimental: false,
    settings: []
  } as any;
}
