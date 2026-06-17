import { describe, expect, it } from "vitest";
import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import {
  createApplyPayload,
  FeatureManagementPage,
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
    expect(getErrorMessage(httpError(409, "Expected revision rev-1 but found rev-2.")))
      .toContain("Refresh and review the latest feature state.");
  });

  it("normalizes backend validation errors for display", () => {
    expect(getErrorMessage(httpError(400, "Feature 'A' setting 'Count' must be a JSON integer value.")))
      .toContain("The feature configuration was rejected.");
  });

  it("keeps toggles local until apply sends the full desired state", async () => {
    let postPayload: unknown = null;
    const api = stubApi({
      getJson: async () => ({
        revision: "rev-1",
        features: [
          feature("FeatureA", true, { count: 1 }),
          feature("FeatureB", false, { stale: true })
        ]
      }),
      postJson: async (_url: string, body: unknown) => {
        postPayload = body;
        return {
          catalog: {
            revision: "rev-2",
            features: [
              feature("FeatureA", false, {}),
              feature("FeatureB", false, {})
            ]
          },
          featureDescriptorCount: 2,
          reloadedShellCount: 1
        };
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    expect(container.textContent).toContain("1 enabled of 2 available");
    await click(container.querySelector("[aria-label='Disable FeatureA']"));

    expect(container.textContent).toContain("Unsaved changes");
    expect(postPayload).toBeNull();

    await click(buttonByText(container, "Apply"));

    expect(postPayload).toEqual({
      revision: "rev-1",
      features: [
        { id: "FeatureA", enabled: false, configuration: {} },
        { id: "FeatureB", enabled: false, configuration: {} }
      ]
    });
    expect(container.textContent).toContain("Applied 2 descriptor(s); reloaded 1 shell(s).");

    await unmount();
  });

  it("separates Studio and Server feature catalogs by host tab", async () => {
    const api = stubApi({
      hostGetJson: async () => ({
        revision: "studio-rev",
        features: [feature("StudioFeature", true, {}, ["Studio"])]
      }),
      getJson: async () => ({
        revision: "server-rev",
        features: [feature("ServerFeature", true, {}, ["Runtime"])]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    expect(container.textContent).toContain("Server: 1 enabled of 1 available");
    expect(container.textContent).toContain("ServerFeature");
    expect(container.textContent).not.toContain("StudioFeature");

    await click(buttonContainingText(container, "Studio"));

    expect(container.textContent).toContain("Studio: 1 enabled of 1 available");
    expect(container.textContent).toContain("StudioFeature");
    expect(container.textContent).not.toContain("ServerFeature");
    expect((buttonByText(container, "Apply") as HTMLButtonElement).disabled).toBe(true);
    expect((container.querySelector("[aria-label='Disable StudioFeature']") as HTMLButtonElement).disabled).toBe(true);

    await unmount();
  });

  it("bulk disables selected features before apply", async () => {
    let postPayload: unknown = null;
    const api = stubApi({
      getJson: async () => ({
        revision: "rev-1",
        features: [
          feature("FeatureA", true, {}),
          feature("FeatureB", true, {}),
          feature("FeatureC", false, {})
        ]
      }),
      postJson: async (_url: string, body: unknown) => {
        postPayload = body;
        return {
          catalog: {
            revision: "rev-2",
            features: [
              feature("FeatureA", false, {}),
              feature("FeatureB", false, {}),
              feature("FeatureC", false, {})
            ]
          },
          featureDescriptorCount: 3,
          reloadedShellCount: 1
        };
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Select FeatureA']"));
    await click(container.querySelector("[aria-label='Select FeatureB']"));

    expect(container.textContent).toContain("2 selected");

    await click(buttonByText(container, "Disable"));

    expect(container.textContent).toContain("Unsaved changes");
    expect(postPayload).toBeNull();

    await click(buttonByText(container, "Apply"));

    expect(postPayload).toEqual({
      revision: "rev-1",
      features: [
        { id: "FeatureA", enabled: false, configuration: {} },
        { id: "FeatureB", enabled: false, configuration: {} },
        { id: "FeatureC", enabled: false, configuration: {} }
      ]
    });

    await unmount();
  });

  it("shows conflict errors returned by apply", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "rev-1",
        features: [feature("FeatureA", true, {})]
      }),
      postJson: async () => {
        throw httpError(409, "Expected revision rev-1 but found rev-2.");
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable FeatureA']"));
    await click(buttonByText(container, "Apply"));

    expect(container.textContent).toContain("The feature catalog changed before your changes were applied.");

    await unmount();
  });

  it("filters features by category and marks the selected feature", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "rev-1",
        features: [
          feature("RuntimeA", true, {}, ["Runtime"]),
          feature("StorageA", true, {}, ["Storage"])
        ]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(buttonContainingText(container, "Storage"));

    expect(container.textContent).toContain("1 feature");
    expect(container.textContent).toContain("StorageA");
    expect(container.textContent).not.toContain("RuntimeA");
    expect(container.querySelector(".feature-management-card.selected")?.textContent).toContain("StorageA");

    await unmount();
  });

  it("does not repeat technical ids in feature rows when display name matches", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "rev-1",
        features: [feature("RuntimeA", true, {}, ["Runtime"])]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();
    const selectedCard = container.querySelector(".feature-management-card.selected");
    const metadata = container.querySelector(".feature-management-metadata");

    expect(selectedCard?.querySelector("strong")?.textContent).toBe("RuntimeA");
    expect(selectedCard?.querySelector("code")).toBeNull();
    expect(metadata?.textContent).toContain("Display name");
    expect(metadata?.textContent).toContain("Technical name");
    expect(metadata?.textContent).toContain("RuntimeA");
    expect(metadata?.textContent).toContain("Runtime");

    const settingsSection = container.querySelector("[aria-label='Feature settings']");
    expect(settingsSection?.textContent).toContain("Settings");
    expect(settingsSection?.textContent).toContain("No configurable settings.");
    expect(container.querySelector(".feature-management-inspector-tabs")).toBeNull();
    expect(container.querySelector(".feature-management-sticky-actions")).toBeNull();

    await unmount();
  });

  it("shows full selected feature metadata when the backend provides it", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "rev-1",
        features: [
          feature("RuntimeA", true, { Timeout: 5, IncludeTimestamp: true }, ["Runtime", "Workflows"], {
            displayName: "Runtime A",
            description: "Runs workflow activities.",
            packageId: "Elsa.Runtime.Features",
            packageVersion: "1.2.3",
            advanced: true,
            experimental: true,
            manifestHash: "abc123",
            manifestPath: "/packages/elsa-package.json",
            settings: [
              setting({ name: "IncludeTimestamp", displayName: "Include timestamp", jsonType: "boolean" }),
              setting({ name: "Timeout", displayName: "Timeout", jsonType: "number" })
            ]
          })
        ]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();
    const selectedCard = container.querySelector(".feature-management-card.selected");
    const settingsSection = container.querySelector("[aria-label='Feature settings']");

    expect(selectedCard?.querySelector("strong")?.textContent).toBe("Runtime A");
    expect(selectedCard?.querySelector("code")?.textContent).toBe("RuntimeA");
    expect(container.querySelector(".feature-management-inspector h3")?.textContent).toBe("Runtime A");

    const metadata = container.querySelector(".feature-management-metadata");
    const metadataSection = container.querySelector("[aria-label='Feature metadata']");

    expect(metadataSection).toBeTruthy();
    expect(metadataSection?.textContent).not.toContain("Timeout");
    expect(metadata?.textContent).toContain("Display name");
    expect(metadata?.textContent).toContain("Runtime A");
    expect(metadata?.textContent).toContain("Technical name");
    expect(metadata?.textContent).toContain("RuntimeA");
    expect(metadata?.textContent).toContain("Description");
    expect(metadata?.textContent).toContain("Runs workflow activities.");
    expect(metadata?.textContent).toContain("Package");
    expect(metadata?.textContent).toContain("Elsa.Runtime.Features 1.2.3");
    expect(metadata?.textContent).toContain("Manifest hash");
    expect(metadata?.textContent).toContain("abc123");
    expect(metadata?.textContent).toContain("Manifest path");
    expect(metadata?.textContent).toContain("/packages/elsa-package.json");
    expect(metadata?.textContent).toContain("Advanced");
    expect(metadata?.textContent).toContain("Experimental");
    expect(settingsSection).toBeTruthy();
    expect(settingsSection?.textContent).toContain("Timeout");
    expect(settingsSection?.textContent).toContain("5");
    expect(container.querySelector(".feature-management-inspector-tabs")).toBeNull();
    expect(container.querySelector(".feature-management-sticky-actions")).toBeNull();

    await click(buttonByText(container, "Edit settings"));

    const dialog = container.querySelector(".feature-management-settings-dialog");
    const timeoutInput = dialog?.querySelector("input[type='number']") as HTMLInputElement | null;
    const timestampSwitch = dialog?.querySelector(".feature-management-setting.boolean .feature-management-setting-switch-input") as HTMLInputElement | null;
    expect(dialog?.textContent).toContain("Save draft");
    expect(timestampSwitch?.checked).toBe(true);
    expect(timeoutInput?.value).toBe("5");

    await changeInput(timeoutInput, "12");
    await click(buttonByText(dialog!, "Save draft"));

    const updatedSettingsSection = container.querySelector("[aria-label='Feature settings']");
    expect(container.querySelector(".feature-management-settings-dialog")).toBeNull();
    expect(container.textContent).toContain("Unsaved changes");
    expect(updatedSettingsSection?.textContent).toContain("12");

    await unmount();
  });
});

function stubApi(http?: {
  getJson?: (url: string, init?: RequestInit) => Promise<unknown>;
  postJson?: (url: string, body: unknown, init?: RequestInit) => Promise<unknown>;
  hostGetJson?: (url: string, init?: RequestInit) => Promise<unknown>;
  hostPostJson?: (url: string, body: unknown, init?: RequestInit) => Promise<unknown>;
}) {
  return {
    host: {
      http: {
        getJson: http?.hostGetJson ?? (async () => ({ revision: "studio-rev", features: [] })),
        postJson: http?.hostPostJson ?? (async () => ({ catalog: { revision: "studio-rev", features: [] }, featureDescriptorCount: 0, reloadedShellCount: 0 }))
      }
    },
    backend: {
      http: {
        getJson: http?.getJson ?? (async () => ({ revision: "rev", features: [] })),
        postJson: http?.postJson ?? (async () => ({ catalog: { revision: "rev", features: [] }, featureDescriptorCount: 0, reloadedShellCount: 0 }))
      }
    },
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

function feature(
  id: string,
  enabled: boolean,
  configuration: Record<string, unknown>,
  categories: string[] = [],
  overrides: Record<string, unknown> = {}
) {
  return {
    id,
    displayName: id,
    categories,
    sourceKind: "runtime",
    enabled,
    configuration,
    advanced: false,
    experimental: false,
    settings: [],
    ...overrides
  } as any;
}

function httpError(status: number, message: string) {
  const error = new Error(message) as Error & { status: number };
  error.status = status;
  return error;
}

async function renderFeatureManagementPage() {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  flushSync(() => {
    root.render(React.createElement(FeatureManagementPage));
  });
  await flushPromises();

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      container.remove();
    }
  };
}

async function click(element: Element | null) {
  if (!element) {
    throw new Error("Expected element to exist.");
  }

  flushSync(() => {
    element.dispatchEvent(new MouseEvent("click", { bubbles: true }));
  });
  await flushPromises();
}

function buttonByText(container: Element, text: string) {
  const button = Array.from(container.querySelectorAll("button"))
    .find(candidate => candidate.textContent === text);

  if (!button) {
    throw new Error(`Could not find button '${text}'.`);
  }

  return button;
}

function buttonContainingText(container: Element, text: string) {
  const button = Array.from(container.querySelectorAll("button"))
    .find(candidate => candidate.textContent?.includes(text));

  if (!button) {
    throw new Error(`Could not find button containing '${text}'.`);
  }

  return button;
}

async function changeInput(input: HTMLInputElement | null, value: string) {
  if (!input) {
    throw new Error("Expected input to exist.");
  }

  flushSync(() => {
    const valueSetter = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(input), "value")?.set;
    valueSetter?.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
  });
  await flushPromises();
}

async function flushPromises() {
  await Promise.resolve();
  await Promise.resolve();
  await new Promise(resolve => setTimeout(resolve, 0));
  await Promise.resolve();
}
