import { afterEach, describe, expect, it } from "vitest";
import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { AnonymousAuthProvider, AuthProvider, type AuthProviderManager } from "@elsa-workflows/studio-sdk";
import {
  applyFeatureToggle,
  buildDependentsIndex,
  computeFeatureCascade,
  createApplyPayload,
  FeatureManagementPage,
  getErrorMessage,
  isDirty,
  register,
  registerBuiltInSettingEditors,
  selectSettingEditor
} from "../module";

const mountedRoots = new Set<() => void>();
const eventCleanups = new Set<() => void>();

afterEach(() => {
  const errors: unknown[] = [];
  const previousUrl = `${window.location.pathname}${window.location.search}${window.location.hash}`;
  setBrowserPath("/__feature-management-test-cleanup__");
  for (const cleanup of Array.from(mountedRoots)) {
    try {
      cleanup();
    } catch (error) {
      errors.push(error);
    }
  }
  for (const cleanup of Array.from(eventCleanups)) {
    try {
      cleanup();
    } catch (error) {
      errors.push(error);
    }
  }
  document.body.replaceChildren();
  window.history.replaceState(null, "", previousUrl);
  if (errors.length > 0) {
    throw new AggregateError(errors, "One or more feature-management test roots failed to unmount.");
  }
});

describe("feature management module", () => {
  it("registers navigation, route, and shared setting editors", () => {
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

  it("enabling a feature cascades to its transitive dependencies", () => {
    const draft = [
      feature("Api", false, {}, [], { dependencies: ["FastEndpoints"] }),
      feature("FastEndpoints", false, {}, [], { dependencies: ["Runtime"] }),
      feature("Runtime", false, {}),
      feature("Unrelated", false, {})
    ];

    const result = applyFeatureToggle(draft, "Api", true);

    expect(enabledIds(result)).toEqual(["Api", "FastEndpoints", "Runtime"]);
  });

  it("disabling a feature cascades to its transitive dependants", () => {
    const draft = [
      feature("Api", true, {}, [], { dependencies: ["FastEndpoints"] }),
      feature("FastEndpoints", true, {}, [], { dependencies: ["Runtime"] }),
      feature("Runtime", true, {}),
      feature("Unrelated", true, {})
    ];

    const result = applyFeatureToggle(draft, "Runtime", false);

    expect(enabledIds(result)).toEqual(["Unrelated"]);
  });

  it("matches dependency ids case-insensitively", () => {
    const draft = [
      feature("Api", false, {}, [], { dependencies: ["fastendpoints"] }),
      feature("FastEndpoints", false, {})
    ];

    expect(Array.from(computeFeatureCascade(draft, "Api", true)).sort()).toEqual(["Api", "FastEndpoints"]);
  });

  it("enabling a feature does not cascade to its optional dependencies", () => {
    const draft = [
      feature("Api", false, {}, [], { dependencies: [{ id: "FastEndpoints", optional: false }, { id: "Telemetry", optional: true }] }),
      feature("FastEndpoints", false, {}),
      feature("Telemetry", false, {})
    ];

    const result = applyFeatureToggle(draft, "Api", true);

    // The mandatory dependency is pulled in; the optional one is left untouched.
    expect(enabledIds(result)).toEqual(["Api", "FastEndpoints"]);
  });

  it("disabling a feature does not cascade to features that depend on it optionally", () => {
    const draft = [
      feature("Api", true, {}, [], { dependencies: [{ id: "Telemetry", optional: true }] }),
      feature("Telemetry", true, {})
    ];

    const result = applyFeatureToggle(draft, "Telemetry", false);

    // Api only optionally uses Telemetry, so disabling Telemetry must not force Api off.
    expect(enabledIds(result)).toEqual(["Api"]);
  });

  it("excludes optional dependencies from the dependants index", () => {
    const draft = [
      feature("Api", true, {}, [], { dependencies: [{ id: "Telemetry", optional: true }] }),
      feature("Ui", true, {}, [], { dependencies: ["Telemetry"] }),
      feature("Telemetry", true, {})
    ];

    expect(buildDependentsIndex(draft).get("telemetry")).toEqual(["Ui"]);
  });

  it("indexes direct dependants by required feature id", () => {
    const draft = [
      feature("Api", true, {}, [], { dependencies: ["FastEndpoints"] }),
      feature("Ui", true, {}, [], { dependencies: ["FastEndpoints"] }),
      feature("FastEndpoints", true, {})
    ];

    expect(buildDependentsIndex(draft).get("fastendpoints")).toEqual(["Api", "Ui"]);
  });

  it("detects dirty staged feature state", () => {
    const current = {
      revision: "rev-1",
      features: [feature("FeatureA", true, {})]
    };

    expect(isDirty(current, [feature("FeatureA", false, {})])).toBe(true);
    expect(isDirty(current, [feature("FeatureA", true, {})])).toBe(false);
  });

  it("keeps undefined configuration properties omitted in dirty checks and outgoing JSON", () => {
    const current = { revision: "rev-1", features: [feature("FeatureA", true, { optional: undefined })] };
    const draft = [feature("FeatureA", true, {})];

    expect(isDirty(current, draft)).toBe(false);
    expect(JSON.stringify(createApplyPayload("rev-1", current.features))).toBe(JSON.stringify(createApplyPayload("rev-1", draft)));
  });

  it.each([
    ["stale configuration revision", "Expected revision rev-1 but found rev-2."],
    ["activation guard refusal", "Feature 'FeatureA' cannot be enabled while its runtime is unavailable."]
  ] as const)("preserves the backend reason for a 409 %s", (_scenario, reason) => {
    const message = getErrorMessage(httpError(409, reason));

    expect(message).toContain(reason);
    expect(message).not.toContain("The feature catalog changed");
    expect(message).not.toContain("Refresh and review the latest feature state.");
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

    const review = await openImpactReview(container);
    expect(review.textContent).toContain("Server");
    await click(buttonByText(review, "Apply"));

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
    expect((buttonByText(container, "Review changes") as HTMLButtonElement).disabled).toBe(true);
    expect((container.querySelector("[aria-label='Disable StudioFeature']") as HTMLButtonElement).disabled).toBe(false);

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
          feature("FeatureC", false, {}),
          feature("FeatureAChild", true, {}, [], { dependencies: ["FeatureA"] }),
          feature("FeatureBChild", true, {}, [], { dependencies: ["FeatureB"] })
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
              feature("FeatureC", false, {}),
              feature("FeatureAChild", false, {}, [], { dependencies: ["FeatureA"] }),
              feature("FeatureBChild", false, {}, [], { dependencies: ["FeatureB"] })
            ]
          },
          featureDescriptorCount: 5,
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

    const review = await openImpactReview(container);
    expect(impactRow(review, "FeatureA")?.textContent).toContain("Direct");
    expect(impactRow(review, "FeatureB")?.textContent).toContain("Direct");
    expect(impactRowText(review, "FeatureAChild")).toContain("Automatic");
    expect(impactRowText(review, "FeatureBChild")).toContain("Automatic");
    await click(buttonByText(review, "Apply"));

    expect(postPayload).toEqual({
      revision: "rev-1",
      features: [
        { id: "FeatureA", enabled: false, configuration: {} },
        { id: "FeatureB", enabled: false, configuration: {} },
        { id: "FeatureC", enabled: false, configuration: {} },
        { id: "FeatureAChild", enabled: false, configuration: {} },
        { id: "FeatureBChild", enabled: false, configuration: {} }
      ]
    });

    await unmount();
  });

  it("enables dependencies when a feature is toggled on in the list", async () => {
    let postPayload: unknown = null;
    const api = stubApi({
      getJson: async () => ({
        revision: "rev-1",
        features: [
          feature("Api", false, {}, [], { dependencies: ["FastEndpoints"] }),
          feature("FastEndpoints", false, {})
        ]
      }),
      postJson: async (_url: string, body: unknown) => {
        postPayload = body;
        return {
          catalog: { revision: "rev-2", features: [feature("Api", true, {}), feature("FastEndpoints", true, {})] },
          featureDescriptorCount: 2,
          reloadedShellCount: 1
        };
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Enable Api']"));

    expect(container.textContent).toContain("2 enabled of 2 available");

    const review = await openImpactReview(container);
    expect(impactRow(review, "FastEndpoints")?.textContent).toContain("Automatic");
    await click(buttonByText(review, "Apply"));

    expect(postPayload).toEqual({
      revision: "rev-1",
      features: [
        { id: "Api", enabled: true, configuration: {} },
        { id: "FastEndpoints", enabled: true, configuration: {} }
      ]
    });

    await unmount();
  });

  it.each([
    ["stale revision", "Expected revision rev-1 but found rev-2."],
    ["activation guard", "Feature 'FeatureA' cannot be enabled while its runtime is unavailable."]
  ] as const)("shows the backend's reason for a %s Apply failure without reporting success", async (_scenario, reason) => {
    const api = stubApi({
      getJson: async () => ({
        revision: "rev-1",
        features: [feature("FeatureA", true, {})]
      }),
      postJson: async () => {
        throw httpError(409, reason);
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable FeatureA']"));
    const review = await openImpactReview(container);
    await click(buttonByText(review, "Apply"));

    expect(container.textContent).toContain(reason);
    expect(container.textContent).not.toContain("The feature catalog changed");
    expect(container.textContent).not.toContain("Applied 1 descriptor");

    await unmount();
  });

  it("filters features by category through the searchable dropdown and marks the selected feature", async () => {
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

    await click(container.querySelector("[aria-label='Filter by category']"));

    const searchInput = container.querySelector("[aria-label='Search categories']") as HTMLInputElement | null;
    expect(searchInput).toBeTruthy();

    // The category search narrows the option list before a category is picked.
    await changeInput(searchInput, "stor");
    const listbox = container.querySelector("[role='listbox']");
    expect(listbox?.textContent).toContain("Storage");
    expect(listbox?.textContent).not.toContain("Runtime");

    await click(buttonContainingText(listbox as Element, "Storage"));

    expect(container.querySelector(".feature-management-category-popover")).toBeNull();
    expect(container.querySelector("[aria-label='Filter by category']")?.textContent).toContain("Storage");
    expect(container.textContent).toContain("1 feature");
    expect(container.textContent).toContain("StorageA");
    expect(container.textContent).not.toContain("RuntimeA");
    expect(container.querySelector(".feature-management-card.selected")?.textContent).toContain("StorageA");

    await unmount();
  });

  it("filters features by search text", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "rev-1",
        features: [
          feature("ActivitiesRuntime", true, {}, ["Runtime"], { description: "Runs workflow activities." }),
          feature("DiagnosticsStructuredLogs", true, {}, ["Diagnostics"], { packageId: "Elsa.Diagnostics.StructuredLogs" }),
          feature("StorageSqlite", true, {}, ["Storage"], { settings: [setting({ name: "ConnectionString", displayName: "Connection string" })] })
        ]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();
    const filter = container.querySelector("[aria-label='Filter features']") as HTMLInputElement | null;

    await changeInput(filter, "structured");

    expect(container.textContent).toContain("1 feature");
    expect(container.textContent).toContain("DiagnosticsStructuredLogs");
    expect(container.textContent).not.toContain("ActivitiesRuntime");
    expect(container.textContent).not.toContain("StorageSqlite");

    await changeInput(filter, "connection");

    expect(container.textContent).toContain("StorageSqlite");
    expect(container.textContent).not.toContain("DiagnosticsStructuredLogs");

    await unmount();
  });

  it("does not repeat technical ids in feature rows when display name matches", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "rev-1",
        features: [feature("RuntimeA", true, {}, ["Runtime"])]
      })
    });
    registerBuiltInSettingEditors(api);
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();
    const selectedCard = container.querySelector(".feature-management-card.selected");
    const metadata = container.querySelector(".feature-management-metadata");

    expect(selectedCard?.querySelector("strong")?.textContent).toBe("RuntimeA");
    expect(selectedCard?.querySelector("code")).toBeNull();
    expect(metadata?.textContent).toContain("Technical name");
    expect(metadata?.textContent).toContain("RuntimeA");
    expect(metadata?.textContent).toContain("Runtime");
    // Status and source render once as inspector heading badges, not repeated in metadata.
    expect(metadata?.textContent).not.toContain("Display name");
    expect(metadata?.textContent).not.toContain("Status");
    expect(metadata?.textContent).not.toContain("Source");

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
    expect(metadata?.textContent).toContain("Technical name");
    expect(metadata?.textContent).toContain("RuntimeA");
    expect(metadata?.textContent).toContain("Package");
    expect(metadata?.textContent).toContain("Elsa.Runtime.Features 1.2.3");
    expect(metadata?.textContent).toContain("Manifest hash");
    expect(metadata?.textContent).toContain("abc123");
    expect(metadata?.textContent).toContain("Manifest path");
    expect(metadata?.textContent).toContain("/packages/elsa-package.json");
    // Display name, description, status, source, and the advanced/experimental flags render
    // once in the inspector heading, so the metadata list must not repeat them.
    expect(metadata?.textContent).not.toContain("Display name");
    expect(metadata?.textContent).not.toContain("Description");
    expect(metadata?.textContent).not.toContain("Advanced");
    expect(metadata?.textContent).not.toContain("Experimental");
    const heading = container.querySelector(".feature-management-inspector-heading");
    expect(heading?.textContent).toContain("Advanced");
    expect(heading?.textContent).toContain("Experimental");
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

describe("feature impact review", () => {
  it("shows overlapping required paths beyond the active filter and excludes optional edges", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [
          feature("Api", false, {}, [], { dependencies: ["FastEndpoints", { id: "Telemetry", optional: true }] }),
          feature("Ui", false, {}, [], { dependencies: ["FastEndpoints"] }),
          feature("FastEndpoints", false, {}, [], { dependencies: ["Runtime"] }),
          // This required back-edge keeps the cascade and explanation-path projection cycle-safe.
          feature("Runtime", false, {}, [], { dependencies: ["Api"] }),
          feature("Telemetry", false, {})
        ]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Enable Api']"));
    await click(container.querySelector("[aria-label='Enable Ui']"));
    await changeInput(container.querySelector("[aria-label='Filter features']"), "Api");

    const visibleFeatureNames = Array.from(container.querySelectorAll(".feature-management-card strong"))
      .map(name => name.textContent?.trim() ?? "");
    expect(visibleFeatureNames).toContain("Api");
    expect(visibleFeatureNames).not.toContain("Ui");
    expect(visibleFeatureNames).not.toContain("FastEndpoints");
    expect(visibleFeatureNames).not.toContain("Runtime");

    const review = await openImpactReview(container);
    expect(review.textContent).toContain("Server");
    expect(impactRowText(review, "Api")).toContain("Direct");
    expect(impactRowText(review, "Ui")).toContain("Direct");

    const fastEndpoints = impactRowText(review, "FastEndpoints");
    expect(fastEndpoints).toContain("Automatic");
    expect(fastEndpoints).toContain("Api");
    expect(fastEndpoints).toContain("Ui");

    const runtime = impactRowText(review, "Runtime");
    expect(runtime).toContain("Automatic");
    expectOrderedPath(runtime, ["Api", "FastEndpoints", "Runtime"]);
    expectOrderedPath(runtime, ["Ui", "FastEndpoints", "Runtime"]);
    expectNoImpactRow(review, "Telemetry");
    await unmount();
  });

  it("shows a bulk enable cascade as direct roots with shared automatic dependencies", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [
          feature("Api", false, {}, [], { dependencies: ["Shared"] }),
          feature("Ui", false, {}, [], { dependencies: ["Shared"] }),
          feature("Shared", false, { Mode: "safe" }, [], {
            settings: [setting({ name: "Mode", displayName: "Mode" })]
          })
        ]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Select Api']"));
    await click(container.querySelector("[aria-label='Select Ui']"));
    await click(container.querySelector("[aria-label='Enable selected features']"));
    expect(container.textContent).toContain("3 enabled of 3 available");

    const review = await openImpactReview(container);
    expect(impactRowText(review, "Api")).toContain("Direct");
    expect(impactRowText(review, "Ui")).toContain("Direct");
    const shared = impactRowText(review, "Shared");
    expect(shared).toContain("Automatic");
    expect(shared).not.toContain("Direct");
    expect(shared).toContain("Mode");
    expect(shared).toContain("safe");
    expect(shared).toContain("Api");
    expect(shared).toContain("Ui");
    expectOrderedPath(shared, ["Api", "Shared"]);
    expectOrderedPath(shared, ["Ui", "Shared"]);
    await unmount();
  });

  it.each([
    ["without a setting edit", false],
    ["with a still-pending setting edit", true]
  ] as const)("does not let a superseded disable-B root explain the final state %s", async (_scenario, settingChanged) => {
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [
          feature("A", false, {}, [], { dependencies: ["B"] }),
          feature("B", true, settingChanged ? { Mode: "safe" } : {}, [], {
            settings: settingChanged ? [setting({ name: "Mode", displayName: "Mode" })] : []
          })
        ]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    if (settingChanged) {
      await editSettingValue(container, "B", "Mode", "fast");
    }
    await click(container.querySelector("[aria-label='Disable B']"));
    await click(container.querySelector("[aria-label='Enable A']"));
    expect(container.textContent).toContain("2 enabled of 2 available");

    const review = await openImpactReview(container);
    expect(impactRowText(review, "A")).toContain("Direct");

    if (settingChanged) {
      // The same feature is present only when its independent direct setting edit remains non-net-zero.
      const settingRow = impactRowText(review, "B");
      expect(settingRow).toContain("Direct");
      expect(settingRow).toContain("Mode");
      expect(settingRow).not.toContain("Automatic");
      expect(settingRow).not.toMatch(/B\s*(?:was|:|,|→)\s*(?:off|disabled)/i);
    } else {
      expectNoImpactRow(review, "B");
      expect(review.textContent).not.toMatch(/B\s*(?:was|:|,|→)\s*(?:off|disabled)/i);
    }
    await unmount();
  });

  it.each([
    {
      scenario: "enabling A then disabling A leaves B enabled",
      aEnabled: false,
      bEnabled: false,
      bId: "B",
      firstToggle: "Enable A",
      secondToggle: "Disable A",
      residualFeature: "B",
      netZeroFeature: "A",
      requiredPath: ["A", "B"]
    },
    {
      scenario: "disabling B then enabling B leaves A disabled",
      aEnabled: true,
      bEnabled: true,
      bId: "B",
      firstToggle: "Disable B",
      secondToggle: "Enable B",
      residualFeature: "A",
      netZeroFeature: "B",
      requiredPath: ["B", "A"]
    },
    {
      scenario: "enabling A then disabling A retains the __proto__ feature cause",
      aEnabled: false,
      bEnabled: false,
      bId: "__proto__",
      firstToggle: "Enable A",
      secondToggle: "Disable A",
      residualFeature: "__proto__",
      netZeroFeature: "A",
      requiredPath: ["A", "__proto__"]
    }
  ] as const)("keeps a residual cascade truthful after its original root returns to baseline: $scenario", async scenario => {
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [
          feature("A", scenario.aEnabled, {}, [], { dependencies: [scenario.bId] }),
          feature(scenario.bId, scenario.bEnabled, {})
        ]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector(`[aria-label='${scenario.firstToggle}']`));
    await click(container.querySelector(`[aria-label='${scenario.secondToggle}']`));
    expect(container.textContent).toContain("1 enabled of 2 available");

    const review = await openImpactReview(container);
    expectNoImpactRow(review, scenario.netZeroFeature);
    const residual = impactRowText(review, scenario.residualFeature);
    expect(residual).toContain("Automatic");
    expect(residual).not.toContain("Direct");
    expectOrderedPath(residual, [...scenario.requiredPath]);
    expect(residual).toMatch(/historical|earlier|prior/i);

    await click(buttonByText(container, "Reset"));
    expect(impactReviewRegion(container)).toBeNull();
    expect(container.textContent).not.toContain("Unsaved changes");
    const reviewButton = Array.from(container.querySelectorAll("button"))
      .find(button => button.textContent === "Review changes");
    if (reviewButton && !reviewButton.disabled) {
      const resetReview = await openImpactReview(container);
      expectNoImpactRow(resetReview, "A");
      expectNoImpactRow(resetReview, scenario.bId);
      expect(resetReview.textContent).not.toMatch(/historical|earlier|prior/i);
    } else {
      expectControlUnavailableByText(container, "Review changes");
    }
    await unmount();
  });

  it("masks sensitive edits and separates known reload metadata from unknown impact", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [
          feature("Configured", true, { Token: "credential-before", Mode: "safe" }, [], {
            settings: [
              setting({ name: "Token", displayName: "Access token", jsonType: "string", secret: true, sensitive: true }),
              setting({ name: "Mode", displayName: "Mode", jsonType: "string", restartRequired: true })
            ]
          }),
          feature("UnknownReload", false, {})
        ]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    expect(container.textContent).not.toContain("credential-before");
    expect(container.textContent).toContain("********");
    await editSettingValue(container, "Configured", "Access token", "credential-after");
    await editSettingValue(container, "Configured", "Mode", "fast");
    await click(container.querySelector("[aria-label='Enable UnknownReload']"));

    const review = await openImpactReview(container);
    const configured = impactRowText(review, "Configured");
    expect(configured).toContain("Direct");
    expect(configured).toContain("Access token");
    expect(configured).toContain("Mode");
    expect(configured).toMatch(/reload|restart/i);
    expect(review.textContent).not.toContain("credential-before");
    expect(review.textContent).not.toContain("credential-after");
    expect(configured).toContain("********");
    expect(impactRowText(review, "UnknownReload")).toMatch(/unknown/i);
    await unmount();
  });

  it.each([
    {
      scenario: "direct disable",
      configuredFeature: "Configured",
      rootFeature: "Configured",
      automatic: false,
      features: [feature("Configured", true, { Mode: "safe" }, [], {
        settings: [setting({ name: "Mode", displayName: "Mode" })]
      })]
    },
    {
      scenario: "required cascade disable",
      configuredFeature: "Api",
      rootFeature: "Runtime",
      automatic: true,
      features: [
        feature("Api", true, { Mode: "safe" }, [], {
          settings: [setting({ name: "Mode", displayName: "Mode" })],
          dependencies: ["Runtime"]
        }),
        feature("Runtime", true, {})
      ]
    }
  ] as const)("previews the applied configuration when a configured feature is disabled by $scenario", async scenario => {
    const api = stubApi({
      getJson: async () => ({ revision: "server-rev-1", features: scenario.features })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector(`[aria-label='Disable ${scenario.rootFeature}']`));
    const review = await openImpactReview(container);
    const configured = impactRowText(review, scenario.configuredFeature);

    expect(configured).toContain("Mode");
    expect(configured).toContain("safe");
    expect(configured).toContain("Not set");
    if (scenario.automatic) {
      expect(configured).toContain("Automatic");
      expect(configured).not.toContain("Direct");
    } else {
      expect(configured).toContain("Direct");
    }
    await unmount();
  });

  it("does not preview an edited value after disabling a feature when apply clears its configuration", async () => {
    let submitted: unknown = null;
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [feature("Configured", true, { Mode: "safe" }, [], {
          settings: [setting({ name: "Mode", displayName: "Mode" })]
        }), feature("Other", false, {})]
      }),
      postJson: async (_url, body) => {
        submitted = body;
        return { catalog: { revision: "server-rev-2", features: [feature("Configured", false, {}), feature("Other", true, {})] }, featureDescriptorCount: 2, reloadedShellCount: 0 };
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await editSettingValue(container, "Configured", "Mode", "fast");
    await click(container.querySelector("[aria-label='Disable Configured']"));
    await click(container.querySelector("[aria-label='Enable Other']"));
    const review = await openImpactReview(container);
    const configured = impactRowText(review, "Configured");

    expect(configured).not.toContain("fast");
    expect(configured).toContain("Not set");
    expect(impactRowText(review, "Other")).toContain("Direct");
    await click(buttonByText(review, "Apply"));
    expect(submitted).toEqual({
      revision: "server-rev-1",
      features: [
        { id: "Configured", enabled: false, configuration: {} },
        { id: "Other", enabled: true, configuration: {} }
      ]
    });
    await unmount();
  });

  it("does not preview stale configuration for an initially-disabled feature when another feature is applied", async () => {
    let submitted: unknown = null;
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [
          feature("Disabled", false, { Mode: "safe" }, [], {
            settings: [setting({ name: "Mode", displayName: "Mode" })]
          }),
          feature("Other", false, {})
        ]
      }),
      postJson: async (_url, body) => {
        submitted = body;
        return { catalog: { revision: "server-rev-2", features: [feature("Disabled", false, {}), feature("Other", true, {})] }, featureDescriptorCount: 1, reloadedShellCount: 0 };
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    expectControlUnavailableByText(container, "Edit settings");
    await click(container.querySelector("[aria-label='Enable Other']"));
    const review = await openImpactReview(container);
    expectNoImpactRow(review, "Disabled");
    await click(buttonByText(review, "Apply"));

    expect(submitted).toEqual({
      revision: "server-rev-1",
      features: [
        { id: "Disabled", enabled: false, configuration: {} },
        { id: "Other", enabled: true, configuration: {} }
      ]
    });
    await unmount();
  });

  it("masks a secret value while previewing configuration clearing", async () => {
    const secret = "credential-to-clear";
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [feature("Configured", true, { Token: secret }, [], {
          settings: [setting({ name: "Token", displayName: "Access token", secret: true, sensitive: true })]
        })]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable Configured']"));
    const review = await openImpactReview(container);
    const configured = impactRowText(review, "Configured");

    expect(configured).toContain("Access token");
    expect(configured).toContain("********");
    expect(configured).toContain("Not set");
    expect(review.textContent).not.toContain(secret);
    await unmount();
  });

  it("keeps an explicit setting edit direct when a later required cascade enables the feature", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [
          feature("A", false, {}, [], { settings: [setting({ name: "Mode", displayName: "Mode" })] }),
          feature("B", false, {}, [], { dependencies: ["A"] })
        ]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Enable A']"));
    await editSettingValue(container, "A", "Mode", "fast");
    await click(container.querySelector("[aria-label='Disable A']"));
    await click(container.querySelector("[aria-label='Enable B']"));

    const review = await openImpactReview(container);
    const a = impactRowText(review, "A");
    expect(a).toContain("Direct");
    expect(a).toContain("Automatic");
    expect(a).toContain("fast");
    expectOrderedPath(a, ["B", "A"]);
    await unmount();
  });

  it("does not stage an object-setting change when only JSON key order changes", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [feature("Configured", true, { Options: { first: 1, second: 2 } }, [], {
          settings: [setting({ name: "Options", displayName: "Options", jsonType: "object" })]
        })]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await editSettingValue(container, "Configured", "Options", '{"second":2,"first":1}');

    expect(container.textContent).not.toContain("Unsaved changes");
    expectControlUnavailableByText(container, "Review changes");
    await unmount();
  });

  it("preserves an object setting's own __proto__ key in dirty and impact review state", async () => {
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [feature("Configured", true, { Options: {} }, [], {
          settings: [setting({ name: "Options", displayName: "Options", jsonType: "object" })]
        })]
      })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();
    try {
      await editSettingValue(container, "Configured", "Options", '{"__proto__":{"polluted":true}}');

      expect(container.textContent).toContain("Unsaved changes");
      const review = await openImpactReview(container);
      const configured = impactRowText(review, "Configured");
      expect(configured).toContain("{}");
      expect(configured).toContain('{"__proto__":{"polluted":true}}');
    } finally {
      await unmount();
    }
  });

  it("moves focus into the review dialog when it opens", async () => {
    const api = stubApi({ getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", true, {})] }) });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable FeatureA']"));
    const opener = buttonByText(container, "Review changes") as HTMLButtonElement;
    opener.focus();
    await click(opener);
    const review = impactReviewRegion(container);
    const close = buttonByText(review!, "Close");

    expect(document.activeElement).toBe(close);
    await unmount();
  });

  it("closes the review dialog on Escape and returns focus to its opener", async () => {
    const api = stubApi({ getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", true, {})] }) });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable FeatureA']"));
    const opener = buttonByText(container, "Review changes") as HTMLButtonElement;
    opener.focus();
    await click(opener);
    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));
    expect(impactReviewRegion(container)).toBeNull();
    expect(document.activeElement).toBe(opener);
    await unmount();
  });

  it("wraps Tab navigation inside the review dialog", async () => {
    const api = stubApi({ getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", true, {})] }) });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable FeatureA']"));
    const review = await openImpactReview(container);
    const close = buttonByText(review, "Close");
    const apply = buttonByText(review, "Apply") as HTMLButtonElement;

    apply.focus();
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(close);
    close.focus();
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", shiftKey: true, bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(apply);
    await unmount();
  });

  it("cancels review without applying and retains the selected host draft", async () => {
    let postCount = 0;
    const api = stubApi({
      getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", true, {})] }),
      postJson: async () => {
        postCount++;
        return { catalog: { revision: "server-rev-2", features: [feature("FeatureA", false, {})] }, featureDescriptorCount: 1, reloadedShellCount: 0 };
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable FeatureA']"));
    const review = await openImpactReview(container);
    await click(buttonByText(review, "Cancel"));

    expect(impactReviewRegion(container)).toBeNull();
    expect(container.textContent).toContain("Unsaved changes");
    expect(container.textContent).toContain("0 enabled of 1 available");
    expect(postCount).toBe(0);
    await unmount();
  });

  it("resets only the selected host draft and clears its open review", async () => {
    const api = stubApi({
      hostGetJson: async () => ({ revision: "studio-rev-1", features: [feature("StudioFeature", true, {})] }),
      getJson: async () => ({ revision: "server-rev-1", features: [feature("ServerFeature", true, {})] })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable ServerFeature']"));
    await click(buttonContainingText(container, "Studio"));
    await click(container.querySelector("[aria-label='Disable StudioFeature']"));
    await click(buttonContainingText(container, "Server"));
    const review = await openImpactReview(container);
    await click(buttonByText(container, "Reset"));

    expect(impactReviewRegion(container)).toBeNull();
    expect(container.textContent).toContain("Server: 1 enabled of 1 available");
    expect(container.textContent).not.toContain("Server: 1 enabled of 1 available - Unsaved changes");
    await click(buttonContainingText(container, "Studio"));
    expect(container.textContent).toContain("Studio: 0 enabled of 1 available - Unsaved changes");
    expect(review.textContent).toContain("Server");
    await unmount();
  });

  it.each([
    ["Server", "ServerFeature", "server-rev-1"],
    ["Studio", "StudioFeature", "studio-rev-1"]
  ])("applies the complete reviewed state and revision to %s only", async (host, featureId, revision) => {
    let submitted: { host: string; url: string; body: unknown } | null = null;
    const serverFeatures = [feature("ServerFeature", true, { value: 1 }), feature("ServerUnchanged", false, { keep: true })];
    const studioFeatures = [feature("StudioFeature", true, { value: 2 }, ["Studio"]), feature("StudioUnchanged", false, { keep: true }, ["Studio"] )];
    const api = stubApi({
      hostGetJson: async () => ({ revision: "studio-rev-1", features: studioFeatures }),
      getJson: async () => ({ revision: "server-rev-1", features: serverFeatures }),
      hostPostJson: async (url, body) => {
        submitted = { host: "Studio", url, body };
        return { catalog: { revision: "studio-rev-2", features: studioFeatures.map(item => item.id === "StudioFeature" ? feature("StudioFeature", false, {}, ["Studio"]) : item) }, featureDescriptorCount: 2, reloadedShellCount: 1 };
      },
      postJson: async (url, body) => {
        submitted = { host: "Server", url, body };
        return { catalog: { revision: "server-rev-2", features: serverFeatures.map(item => item.id === "ServerFeature" ? feature("ServerFeature", false, {}) : item) }, featureDescriptorCount: 2, reloadedShellCount: 1 };
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    if (host === "Studio") {
      await click(buttonContainingText(container, "Studio"));
    }
    await click(container.querySelector(`[aria-label='Disable ${featureId}']`));
    const review = await openImpactReview(container);
    expect(review.textContent).toContain(host);
    expect(impactRowText(review, featureId)).toContain("Direct");
    await click(buttonByText(review, "Apply"));

    const expectedFeatures = (host === "Studio" ? studioFeatures : serverFeatures).map(item => ({
      id: item.id,
      enabled: item.id === featureId ? false : item.enabled,
      configuration: item.id === featureId || !item.enabled ? {} : item.configuration
    }));
    expect(submitted).toEqual({
      host,
      url: "/modularity/features/apply",
      body: { revision, features: expectedFeatures }
    });
    expect(container.textContent).toContain("Applied 2 descriptor(s); reloaded 1 shell(s).");
    await unmount();
  });

  it.each([
    ["draft mutation", "retained"],
    ["host change", "retained"],
    ["catalog refresh", "same"],
    ["catalog replacement", "replaced"]
  ])("invalidates an open review after %s", async (trigger, replacement) => {
    let serverRead = 0;
    let postCount = 0;
    const original = [feature("FeatureA", true, {}), feature("FeatureB", false, {})];
    const countPost = async () => {
      postCount++;
      return { catalog: { revision: "server-rev-2", features: original }, featureDescriptorCount: 0, reloadedShellCount: 0 };
    };
    const api = stubApi({
      hostGetJson: async () => ({ revision: "studio-rev-1", features: [feature("StudioFeature", true, {})] }),
      hostPostJson: countPost,
      getJson: async () => {
        serverRead++;
        return replacement === "replaced" && serverRead > 1
          ? { revision: "server-rev-2", features: [...original, feature("FeatureC", false, {})] }
          : { revision: "server-rev-1", features: original };
      },
      postJson: countPost
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable FeatureA']"));
    await openImpactReview(container);

    if (trigger === "draft mutation") {
      await click(container.querySelector("[aria-label='Enable FeatureB']"));
    } else if (trigger === "host change") {
      await click(buttonContainingText(container, "Studio"));
    } else {
      await click(container.querySelector("[aria-label='Refresh features']"));
      await flushPromises();
      if (replacement === "replaced") {
        expect(container.textContent).toContain("FeatureC");
      }
    }

    expect(impactReviewRegion(container)).toBeNull();
    expect(postCount).toBe(0);
    await unmount();
  });

  it("invalidates the review when a raw settings draft changes", async () => {
    let postCount = 0;
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [
          feature("Configured", true, { Mode: "slow" }, [], { settings: [setting({ name: "Mode", displayName: "Mode" })] }),
          feature("Other", false, {})
        ]
      }),
      postJson: async () => {
        postCount++;
        return { catalog: { revision: "server-rev-2", features: [] }, featureDescriptorCount: 0, reloadedShellCount: 0 };
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Enable Other']"));
    await openImpactReview(container);
    await click(buttonByText(container, "Edit settings"));
    const settingsDialog = Array.from(container.querySelectorAll("[role='dialog']"))
      .find(dialog => dialog.textContent?.includes("Save draft"));
    const modeLabel = Array.from(settingsDialog?.querySelectorAll("label") ?? [])
      .find(label => label.textContent?.includes("Mode"));
    await changeInput(modeLabel?.querySelector("input, textarea") as HTMLInputElement | null, "fast");
    await click(buttonByText(settingsDialog!, "Save draft"));

    expect(impactReviewRegion(container)).toBeNull();
    expect(container.textContent).toContain("Unsaved changes");
    expect(postCount).toBe(0);
    await unmount();
  });

  it("saves only changed settings and does not materialize untouched defaults", async () => {
    const featureId = "WorkflowsRuntimeEntityFrameworkCore";
    const settings = [
      setting({ name: "WorkflowExecutableCacheCapacity", displayName: "Workflow executable cache capacity", jsonType: "integer", defaultValue: 256 }),
      setting({ name: "ConnectionName", displayName: "Connection name", jsonType: "string" }),
      setting({ name: "ConnectionString", displayName: "Connection string", jsonType: "string" }),
      setting({ name: "PooledContexts", displayName: "Pooled contexts", jsonType: "boolean" }),
      setting({ name: "Provider", displayName: "Provider", jsonType: "string", defaultValue: "Sqlite" }),
      setting({ name: "Schema", displayName: "Schema", jsonType: "string" }),
      setting({ name: "ExistingSetting", displayName: "Existing setting", jsonType: "string", defaultValue: "default" })
    ];
    const initialFeature = feature(featureId, true, {
      WorkflowExecutableCacheCapacity: 256,
      ExistingSetting: "preserve me"
    }, ["Runtime"], { settings });
    let submitted: unknown;
    const api = stubApi({
      getJson: async () => ({ revision: "server-rev-1", features: [initialFeature] }),
      postJson: async (_url, body) => {
        submitted = body;
        return {
          catalog: {
            revision: "server-rev-2",
            features: [feature(featureId, true, {
              WorkflowExecutableCacheCapacity: 257,
              ExistingSetting: "preserve me"
            }, ["Runtime"], { settings })]
          },
          featureDescriptorCount: 1,
          reloadedShellCount: 1
        };
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    const openSettingsDialog = async () => {
      await click(buttonByText(container, "Edit settings"));
      const dialog = container.querySelector(".feature-management-settings-dialog");
      if (!dialog) throw new Error("The feature settings dialog should open.");
      return dialog;
    };

    try {
      let dialog = await openSettingsDialog();
      await click(buttonByText(dialog, "Save draft"));
      expect(container.textContent).not.toContain("Unsaved changes");
      expectControlUnavailableByText(container, "Review changes");

      dialog = await openSettingsDialog();
      let capacityInput = dialog.querySelector("input[type='number']") as HTMLInputElement | null;
      await changeInput(capacityInput, "257");
      await click(buttonByText(dialog, "Reset changes"));
      expect(capacityInput?.value).toBe("256");
      await click(buttonByText(dialog, "Save draft"));
      expect(container.textContent).not.toContain("Unsaved changes");
      expectControlUnavailableByText(container, "Review changes");

      dialog = await openSettingsDialog();
      capacityInput = dialog.querySelector("input[type='number']") as HTMLInputElement | null;
      await changeInput(capacityInput, "257");
      await click(buttonByText(dialog, "Save draft"));
      expect(container.textContent).toContain("Unsaved changes");

      const review = await openImpactReview(container);
      const changedFeature = impactRow(review, featureId);
      const changedSettings = Array.from(changedFeature?.querySelectorAll(".feature-management-impact-section") ?? [])
        .find(section => section.querySelector("h5")?.textContent === "Changed settings");
      expect(Array.from(changedSettings?.querySelectorAll("dt") ?? []).map(item => item.textContent)).toEqual([
        "Workflow executable cache capacity"
      ]);

      await click(buttonByText(review, "Apply"));
      expect(submitted).toEqual({
        revision: "server-rev-1",
        features: [{
          id: featureId,
          enabled: true,
          configuration: {
            WorkflowExecutableCacheCapacity: 257,
            ExistingSetting: "preserve me"
          }
        }]
      });
    } finally {
      await unmount();
    }
  });

  it("invalidates the review as soon as a catalog refresh starts", async () => {
    let serverRead = 0;
    let finishRefresh: ((value: unknown) => void) | undefined;
    let postCount = 0;
    const original = [feature("FeatureA", true, {})];
    const api = stubApi({
      getJson: async () => {
        serverRead++;
        if (serverRead === 1) {
          return { revision: "server-rev-1", features: original };
        }
        return new Promise(resolve => { finishRefresh = resolve; });
      },
      postJson: async () => {
        postCount++;
        return { catalog: { revision: "server-rev-2", features: original }, featureDescriptorCount: 0, reloadedShellCount: 0 };
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable FeatureA']"));
    await openImpactReview(container);
    await click(container.querySelector("[aria-label='Refresh features']"));

    expect(impactReviewRegion(container)).toBeNull();
    expect(postCount).toBe(0);
    finishRefresh?.({ revision: "server-rev-2", features: original });
    await flushPromises();
    await unmount();
  });

  it("keeps the review open and blocks duplicate Apply or dismissal while submitting", async () => {
    let postCount = 0;
    let finishPost: ((value: unknown) => void) | undefined;
    const api = stubApi({
      getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", true, {})] }),
      postJson: async () => {
        postCount++;
        return new Promise(resolve => { finishPost = resolve; });
      }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable FeatureA']"));
    const review = await openImpactReview(container);
    const applyButton = buttonByText(review, "Apply");
    flushSync(() => {
      applyButton.dispatchEvent(new MouseEvent("click", { bubbles: true }));
      applyButton.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });
    await flushPromises();

    expect(postCount).toBe(1);
    expect(buttonByText(review, "Close").hasAttribute("disabled")).toBe(true);
    expect(buttonByText(review, "Cancel").hasAttribute("disabled")).toBe(true);
    flushSync(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true })));
    expect(impactReviewRegion(container)).not.toBeNull();

    finishPost?.({
      catalog: { revision: "server-rev-2", features: [feature("FeatureA", false, {})] },
      featureDescriptorCount: 1,
      reloadedShellCount: 0
    });
    await flushPromises();
    expect(impactReviewRegion(container)).toBeNull();
    await unmount();
  });

  it("shows backend Apply failures inside the review without stale success claims", async () => {
    const reason = "Feature 'FeatureA' cannot be enabled while its runtime is unavailable.";
    const api = stubApi({
      getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", false, {})] }),
      postJson: async () => { throw httpError(409, reason); }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Enable FeatureA']"));
    const review = await openImpactReview(container);
    await click(buttonByText(review, "Apply"));

    expect(impactReviewRegion(container)).not.toBeNull();
    expect(impactReviewRegion(container)?.textContent).toContain(reason);
    expect(impactReviewRegion(container)?.textContent).not.toContain("Applied ");
    expect(impactReviewRegion(container)?.textContent).not.toContain("configuration was unchanged");
    await unmount();
  });

  it("moves focus to a rejected Apply reason when the review list is scrolled", async () => {
    const reason = "Feature changes were rejected by the host.";
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: Array.from({ length: 16 }, (_, index) => feature(`Feature${index + 1}`, false, {}))
      }),
      postJson: async () => { throw httpError(409, reason); }
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    for (let index = 1; index <= 16; index++) {
      await click(container.querySelector(`[aria-label='Select Feature${index}']`));
    }
    await click(container.querySelector("[aria-label='Enable selected features']"));
    const review = await openImpactReview(container);
    const body = review.querySelector(".feature-management-dialog-body") as HTMLElement | null;
    if (!body) {
      throw new Error("The feature impact review should expose its scrollable body.");
    }
    body.scrollTop = 1200;
    expect(body.scrollTop).toBe(1200);

    await click(buttonByText(review, "Apply"));

    const error = review.querySelector("[role='alert']");
    expect(review.textContent).toContain(reason);
    expect(error).not.toBeNull();
    expect(error?.parentElement?.getAttribute("tabindex")).toBe("-1");
    expect(document.activeElement).toBe(error?.parentElement);
    await unmount();
  });

  it("clears a prior Apply success when a new draft is staged", async () => {
    const api = stubApi({
      getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", true, {})] }),
      postJson: async () => ({ catalog: { revision: "server-rev-2", features: [feature("FeatureA", false, {})] }, featureDescriptorCount: 1, reloadedShellCount: 0 })
    });
    register(api);
    const { container, unmount } = await renderFeatureManagementPage();

    await click(container.querySelector("[aria-label='Disable FeatureA']"));
    const review = await openImpactReview(container);
    await click(buttonByText(review, "Apply"));
    expect(container.textContent).toContain("Applied 1 descriptor(s); reloaded 0 shell(s).");

    await click(container.querySelector("[aria-label='Enable FeatureA']"));
    expect(container.textContent).not.toContain("Applied 1 descriptor(s); reloaded 0 shell(s).");
    await unmount();
  });

  it("retains the Studio target, success result, and focus through repeated registry remounts", async () => {
    setBrowserPath("/features");
    const harness = studioApplyHarness();
    const registryChanges = observeRegistryChanges();
    register(harness.createApi());
    const strictAnonymousProvider: AuthWrapper = children => anonymousProvider()(
      React.createElement(React.StrictMode, null, children)
    );
    let page = await renderFeatureManagementPage(strictAnonymousProvider);

    await applyStudioFeature(page.container);

    expect(registryChanges.count).toBe(1);
    expect(page.container.textContent).toContain("Applied 1 descriptor(s); reloaded 1 shell(s).");

    for (let rebuild = 0; rebuild < 2; rebuild++) {
      await page.unmount();
      register(harness.createApi());
      page = await renderFeatureManagementPage(strictAnonymousProvider);

      expect(page.container.textContent).toContain("Studio: 0 enabled of 1 available");
      expect(page.container.textContent).not.toContain("Server: 1 enabled of 1 available");
      const completion = featureCompletion(page.container);
      expect(completion).not.toBeNull();
      expect(completion?.textContent ?? "").toContain("Applied 1 descriptor(s); reloaded 1 shell(s).");
      expect(completion?.getAttribute("tabindex")).toBe("-1");
      expect(document.activeElement).toBe(completion);
    }

    await page.unmount();
  });

  it("focuses an ordinary Server Apply result after the review closes", async () => {
    setBrowserPath("/features");
    const api = stubApi({
      getJson: async () => ({ revision: "server-rev-1", features: [feature("ServerFeature", true, {})] }),
      postJson: async () => ({
        catalog: { revision: "server-rev-2", features: [feature("ServerFeature", false, {})] },
        featureDescriptorCount: 1,
        reloadedShellCount: 0
      })
    });
    register(api);
    const page = await renderFeatureManagementPage();

    await click(page.container.querySelector("[aria-label='Disable ServerFeature']"));
    const review = await openImpactReview(page.container);
    await click(buttonByText(review, "Apply"));

    const completion = featureCompletion(page.container);
    expect(page.container.textContent).toContain("Applied 1 descriptor(s); reloaded 0 shell(s).");
    expect(completion).not.toBeNull();
    expect(completion?.textContent ?? "").toContain("Applied 1 descriptor(s); reloaded 0 shell(s).");
    expect(completion?.getAttribute("tabindex")).toBe("-1");
    expect(document.activeElement).toBe(completion);
    await page.unmount();
  });

  it("clears Studio completion on host, draft, reset, and manual-refresh actions", async () => {
    const actions = ["host selection", "draft mutation", "reset", "manual refresh"] as const;

    for (const action of actions) {
      setBrowserPath("/features");
      const harness = studioApplyHarness();
      register(harness.createApi());
      let page = await renderFeatureManagementPage();
      await applyStudioFeature(page.container);

      if (action === "host selection") {
        await click(buttonContainingText(page.container, "Server"));
      } else if (action === "draft mutation") {
        await click(page.container.querySelector("[aria-label='Enable StudioFeature']"));
      } else if (action === "reset") {
        await click(page.container.querySelector("[aria-label='Enable StudioFeature']"));
        await click(buttonByText(page.container, "Reset"));
      } else {
        await click(page.container.querySelector("[aria-label='Refresh features']"));
      }
      expect(featureCompletion(page.container), action).toBeNull();

      await page.unmount();
      register(harness.createApi());
      page = await renderFeatureManagementPage();
      expect(featureCompletion(page.container)).toBeNull();
      expect(page.container.textContent).not.toContain("Applied 1 descriptor(s); reloaded 1 shell(s).");
      await page.unmount();
    }
  });

  it("does not hand off success after a new Studio Apply is rejected", async () => {
    setBrowserPath("/features");
    const harness = studioApplyHarness({ rejectApplyNumber: 2 });
    const registryChanges = observeRegistryChanges();
    register(harness.createApi());
    let page = await renderFeatureManagementPage();
    await applyStudioFeature(page.container);
    expect(registryChanges.count).toBe(1);

    await page.unmount();
    register(harness.createApi());
    page = await renderFeatureManagementPage();
    expect(page.container.textContent).toContain("Studio: 0 enabled of 1 available");
    expect(featureCompletion(page.container)?.textContent).toContain("Applied 1 descriptor(s); reloaded 1 shell(s).");

    await click(page.container.querySelector("[aria-label='Enable StudioFeature']"));
    expect(featureCompletion(page.container)).toBeNull();
    const review = await openImpactReview(page.container);
    await click(buttonByText(review, "Apply"));
    expect(impactReviewRegion(page.container)?.textContent).toContain("Studio feature activation was rejected.");
    expect(featureCompletion(page.container)).toBeNull();
    expect(registryChanges.count).toBe(1);

    await page.unmount();
    register(harness.createApi());
    page = await renderFeatureManagementPage();
    expect(featureCompletion(page.container)).toBeNull();
    expect(page.container.textContent).not.toContain("Applied 1 descriptor(s); reloaded 1 shell(s).");
    await page.unmount();
  });

  it("clears Studio completion when leaving during a registry-loading unmount", async () => {
    setBrowserPath("/features");
    const harness = studioApplyHarness();
    register(harness.createApi());
    let page = await renderFeatureManagementPage();
    await applyStudioFeature(page.container);
    expect(page.container.textContent).toContain("Applied 1 descriptor(s); reloaded 1 shell(s).");

    // App temporarily removes the route while the modules-changed registry is loading.
    await page.unmount();
    setBrowserPath("/settings/diagnostics");
    setBrowserPath("/features");
    register(harness.createApi());
    page = await renderFeatureManagementPage();

    expect(page.container.textContent).toContain("Server: 1 enabled of 1 available");
    expect(featureCompletion(page.container)).toBeNull();
    expect(page.container.textContent).not.toContain("Applied 1 descriptor(s); reloaded 1 shell(s).");
    await page.unmount();
  });

  it("clears Studio completion after a mounted Features route is left and revisited", async () => {
    setBrowserPath("/features");
    const harness = studioApplyHarness();
    register(harness.createApi());
    let page = await renderFeatureManagementPage();
    await applyStudioFeature(page.container);
    expect(page.container.textContent).toContain("Applied 1 descriptor(s); reloaded 1 shell(s).");

    setBrowserPath("/settings/diagnostics");
    await page.unmount();
    setBrowserPath("/features");
    register(harness.createApi());
    page = await renderFeatureManagementPage();

    expect(page.container.textContent).toContain("Server: 1 enabled of 1 available");
    expect(featureCompletion(page.container)).toBeNull();
    expect(page.container.textContent).not.toContain("Applied 1 descriptor(s); reloaded 1 shell(s).");
    await page.unmount();
  });

  it("does not replay an Apply completion when the request resolves after leaving and returning", async () => {
    setBrowserPath("/features");
    let studioCatalog = { revision: "studio-rev-1", features: [feature("StudioFeature", true, {}, ["Studio"])] };
    let finishApply: ((result: { catalog: typeof studioCatalog; featureDescriptorCount: number; reloadedShellCount: number }) => void) | undefined;
    const registryChanges = observeRegistryChanges();
    const createApi = () => stubApi({
      hostGetJson: async () => studioCatalog,
      getJson: async () => ({ revision: "server-rev-1", features: [feature("ServerFeature", true, {})] }),
      hostPostJson: async () => new Promise(resolve => {
        finishApply = result => {
          studioCatalog = result.catalog;
          resolve(result);
        };
      })
    });
    register(createApi());
    let page = await renderFeatureManagementPage();
    await applyStudioFeature(page.container);
    expect(buttonByText(impactReviewRegion(page.container)!, "Applying").hasAttribute("disabled")).toBe(true);

    // A pending dialog does not prevent ordinary route navigation. The old route is gone when Apply completes.
    await page.unmount();
    setBrowserPath("/settings/diagnostics");
    setBrowserPath("/features");
    register(createApi());
    page = await renderFeatureManagementPage();
    expect(page.container.textContent).toContain("Server: 1 enabled of 1 available");

    finishApply?.({
      catalog: { revision: "studio-rev-2", features: [feature("StudioFeature", false, {}, ["Studio"])] },
      featureDescriptorCount: 1,
      reloadedShellCount: 1
    });
    await flushPromises();
    expect(registryChanges.count).toBe(1);

    // The resulting same-version registry reload must not bring a previous-visit completion into this visit.
    await page.unmount();
    register(createApi());
    page = await renderFeatureManagementPage();
    expect(page.container.textContent).toContain("Server: 1 enabled of 1 available");
    expect(featureCompletion(page.container)).toBeNull();
    expect(page.container.textContent).not.toContain("Applied 1 descriptor(s); reloaded 1 shell(s).");
    await page.unmount();
  });

  it("does not let a stale Studio Apply clear a newer Server completion", async () => {
    setBrowserPath("/features");
    const studioCatalog = { revision: "studio-rev-1", features: [feature("StudioFeature", true, {}, ["Studio"])] };
    let serverCatalog = { revision: "server-rev-1", features: [feature("ServerFeature", true, {})] };
    let finishStudioApply: ((result: { catalog: typeof studioCatalog; featureDescriptorCount: number; reloadedShellCount: number }) => void) | undefined;
    const registryChanges = observeRegistryChanges();
    const api = stubApi({
      hostGetJson: async () => studioCatalog,
      getJson: async () => serverCatalog,
      hostPostJson: async () => new Promise(resolve => {
        finishStudioApply = result => resolve(result);
      }),
      postJson: async () => {
        serverCatalog = { revision: "server-rev-2", features: [feature("ServerFeature", false, {})] };
        return { catalog: serverCatalog, featureDescriptorCount: 1, reloadedShellCount: 0 };
      }
    });
    register(api);
    let page = await renderFeatureManagementPage();
    await applyStudioFeature(page.container);

    await page.unmount();
    setBrowserPath("/settings/diagnostics");
    setBrowserPath("/features");
    register(api);
    page = await renderFeatureManagementPage();
    await click(page.container.querySelector("[aria-label='Disable ServerFeature']"));
    const review = await openImpactReview(page.container);
    await click(buttonByText(review, "Apply"));

    const serverCompletion = featureCompletion(page.container);
    expect(serverCompletion?.textContent ?? "").toContain("Applied 1 descriptor(s); reloaded 0 shell(s).");
    expect(document.activeElement).toBe(serverCompletion);
    expect(registryChanges.count).toBe(0);

    finishStudioApply?.({
      catalog: { revision: "studio-rev-2", features: [feature("StudioFeature", false, {}, ["Studio"])] },
      featureDescriptorCount: 1,
      reloadedShellCount: 1
    });
    await flushPromises();
    expect(registryChanges.count).toBe(1);

    await page.unmount();
    register(api);
    page = await renderFeatureManagementPage();
    expect(page.container.textContent).toContain("Server: 0 enabled of 1 available");
    expect(page.container.textContent).not.toContain("Studio: 0 enabled of 1 available");
    expect(featureCompletion(page.container)?.textContent ?? "").toContain("Applied 1 descriptor(s); reloaded 0 shell(s).");
    expect(document.activeElement).toBe(featureCompletion(page.container));
    await page.unmount();
  });

  it("mounts authenticated inspect-only users without editable controls", async () => {
    const api = stubApi({
      getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", false, {}, [], { settings: [setting({ name: "Mode", displayName: "Mode" })] })] })
    });
    register(api);
    const page = await renderFeatureManagementPage(authenticatedProvider(["module-management:read"]));

    expect(page.container.textContent).toContain("FeatureA");
    expect(page.container.textContent).toContain("0 enabled of 1 available");
    expectControlUnavailable(page.container, "[aria-label='Enable FeatureA']");
    expectControlUnavailable(page.container, "[aria-label='Select FeatureA']");
    expectControlUnavailable(page.container, "[aria-label='Enable selected features']");
    expectControlUnavailable(page.container, "[aria-label='Disable selected features']");
    expectControlUnavailableByText(page.container, "Edit settings");
    expectControlUnavailableByText(page.container, "Reset");
    expectControlUnavailableByText(page.container, "Review changes");
    expectControlUnavailableByText(page.container, "Apply");
    await page.unmount();
  });

  it("keeps authenticated inspect-only users out of every draft-changing control", async () => {
    let postCount = 0;
    const api = stubApi({
      getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", false, {}, [], { settings: [setting({ name: "Mode", displayName: "Mode" })] })] }),
      postJson: async () => {
        postCount++;
        return { catalog: { revision: "server-rev-2", features: [] }, featureDescriptorCount: 0, reloadedShellCount: 0 };
      }
    });
    register(api);
    const page = await renderFeatureManagementPage(authenticatedProvider(["module-management:read", "module-management:manage"]));

    await click(page.container.querySelector("[aria-label='Enable FeatureA']"));
    await openImpactReview(page.container);
    page.rerender(authenticatedProvider(["module-management:read"]));
    await flushPromises();

    expect(page.container.textContent).toContain("FeatureA");
    const review = impactReviewRegion(page.container);
    expect(review).not.toBeNull();
    expectControlUnavailableByText(review!, "Apply");
    expectControlUnavailable(page.container, "[aria-label='Disable FeatureA']");
    expectControlUnavailable(page.container, "[aria-label='Select FeatureA']");
    expectControlUnavailable(page.container, "[aria-label='Enable selected features']");
    expectControlUnavailable(page.container, "[aria-label='Disable selected features']");
    expectControlUnavailableByText(page.container, "Edit settings");
    expectControlUnavailableByText(page.container, "Reset");
    expect(postCount).toBe(0);
    await page.unmount();
  });

  it("keeps authenticated managers able to stage and review changes", async () => {
    const api = stubApi({ getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", false, {})] }) });
    register(api);
    const manager = await renderFeatureManagementPage(authenticatedProvider(["module-management:read", "module-management:manage"]));
    await click(manager.container.querySelector("[aria-label='Enable FeatureA']"));
    expect(manager.container.textContent).toContain("Unsaved changes");
    await openImpactReview(manager.container);
    await manager.unmount();
  });

  it("preserves writable staging for the anonymous demo provider", async () => {
    let submitted: unknown;
    const api = stubApi({
      getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", false, {})] }),
      postJson: async (_url, body) => {
        submitted = body;
        return { catalog: { revision: "server-rev-2", features: [feature("FeatureA", true, {})] }, featureDescriptorCount: 1, reloadedShellCount: 0 };
      }
    });
    register(api);
    const demo = await renderFeatureManagementPage();
    await click(demo.container.querySelector("[aria-label='Enable FeatureA']"));
    expect(demo.container.textContent).toContain("Unsaved changes");
    expect(submitted).toBeUndefined();
    const review = await openImpactReview(demo.container);
    expect(submitted).toBeUndefined();
    await click(buttonByText(review, "Apply"));
    expect(submitted).toEqual({
      revision: "server-rev-1",
      features: [{ id: "FeatureA", enabled: true, configuration: {} }]
    });
    await demo.unmount();
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

function studioApplyHarness(options: { rejectApplyNumber?: number } = {}) {
  let studioCatalog = {
    revision: "studio-rev-1",
    features: [feature("StudioFeature", true, {}, ["Studio"])]
  };
  let applyCount = 0;

  return {
    createApi() {
      return stubApi({
        hostGetJson: async () => studioCatalog,
        getJson: async () => ({ revision: "server-rev-1", features: [feature("ServerFeature", true, {})] }),
        hostPostJson: async () => {
          applyCount++;
          if (applyCount === options.rejectApplyNumber) {
            throw httpError(409, "Studio feature activation was rejected.");
          }
          studioCatalog = {
            revision: `studio-rev-${applyCount + 1}`,
            features: [feature("StudioFeature", false, {}, ["Studio"])]
          };
          return { catalog: studioCatalog, featureDescriptorCount: 1, reloadedShellCount: 1 };
        }
      });
    }
  };
}

function featureCompletion(container: Element) {
  return container.querySelector("[data-feature-apply-completion]");
}

async function applyStudioFeature(container: Element) {
  await click(buttonContainingText(container, "Studio"));
  await click(container.querySelector("[aria-label='Disable StudioFeature']"));
  const review = await openImpactReview(container);
  await click(buttonByText(review, "Apply"));
}

function setBrowserPath(path: string) {
  window.history.replaceState(null, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
}

function observeRegistryChanges() {
  let count = 0;
  const listener = () => { count++; };
  window.addEventListener("elsa-studio:modules-changed", listener);
  const cleanup = () => {
    window.removeEventListener("elsa-studio:modules-changed", listener);
    eventCleanups.delete(cleanup);
  };
  eventCleanups.add(cleanup);
  return { get count() { return count; } };
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
  const dependencies = (overrides.dependencies as Array<string | { id: string; optional?: boolean }> | undefined) ?? [];
  const built = {
    id,
    displayName: id,
    categories,
    sourceKind: "runtime",
    enabled,
    configuration,
    advanced: false,
    experimental: false,
    settings: [],
    ...overrides,
    dependencies
  };

  // Normalize dependency overrides so a bare string means a mandatory dependency and an object can flag `optional`.
  built.dependencies = built.dependencies.map(dependency =>
    typeof dependency === "string" ? { id: dependency, optional: false } : { id: dependency.id, optional: dependency.optional === true });

  return built as any;
}

function enabledIds(features: Array<{ id: string; enabled: boolean }>) {
  return features.filter(feature => feature.enabled).map(feature => feature.id);
}

function httpError(status: number, message: string) {
  const error = new Error(message) as Error & { status: number };
  error.status = status;
  return error;
}

type AuthWrapper = (children: React.ReactNode) => React.ReactElement;

async function renderFeatureManagementPage(provider: AuthWrapper = anonymousProvider()) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const page = React.createElement(FeatureManagementPage);
  let mounted = true;

  function renderWith(nextProvider: AuthWrapper) {
    flushSync(() => {
      root.render(nextProvider(page));
    });
  }

  const cleanup = () => {
    if (!mounted) {
      return;
    }
    mounted = false;
    mountedRoots.delete(cleanup);
    flushSync(() => root.unmount());
    container.remove();
  };

  mountedRoots.add(cleanup);
  renderWith(provider);
  await flushPromises();

  return {
    container,
    rerender(nextProvider: AuthWrapper) {
      renderWith(nextProvider);
      return flushPromises();
    },
    unmount: async () => {
      cleanup();
    }
  };
}

function anonymousProvider(): AuthWrapper {
  return children => React.createElement(AnonymousAuthProvider, null, children);
}

function authenticatedProvider(permissions: string[]): AuthWrapper {
  const session = { status: "authenticated" as const, roles: [], permissions };
  const manager: AuthProviderManager = {
    getSession: () => session,
    getCapabilities: async () => ({ ownershipMode: "foundation-owned", providers: [] }),
    initialize: async () => session,
    login: async () => undefined,
    handleCallback: async () => session,
    logout: async () => undefined,
    getAccessToken: async () => null,
    refresh: async () => session
  };
  return children => React.createElement(AuthProvider, { manager, children });
}

async function openImpactReview(container: Element) {
  await click(buttonByText(container, "Review changes"));
  const review = impactReviewRegion(container);
  if (!review) {
    throw new Error("Opening review should expose the accessible Feature impact review region.");
  }
  return review;
}

function impactReviewRegion(container: Element) {
  return container.querySelector("[aria-label='Feature impact review']");
}

function impactRow(review: Element, featureId: string) {
  return Array.from(review.querySelectorAll("article[data-impact-feature-id]"))
    .find(row => row.getAttribute("data-impact-feature-id") === featureId) ?? null;
}

function impactRowText(review: Element, featureId: string) {
  const row = impactRow(review, featureId);
  if (!row) {
    throw new Error(`Expected the impact review to contain a row for '${featureId}'.`);
  }
  return row.textContent ?? "";
}

function expectNoImpactRow(review: Element, featureId: string) {
  expect(impactRow(review, featureId)).toBeNull();
}

function expectOrderedPath(text: string, path: string[]) {
  let previousIndex = -1;
  for (const featureId of path) {
    const currentIndex = text.indexOf(featureId, previousIndex + 1);
    expect(currentIndex, `Expected required path ${path.join(" -> ")} in '${text}'.`).toBeGreaterThan(previousIndex);
    previousIndex = currentIndex;
  }
}

async function editSettingValue(container: Element, featureId: string, settingLabel: string, value: string) {
  const card = Array.from(container.querySelectorAll(".feature-management-feature-button"))
    .find(button => button.textContent?.includes(featureId));
  await click(card ?? null);
  await click(buttonByText(container, "Edit settings"));

  const dialog = Array.from(container.querySelectorAll("[role='dialog']"))
    .find(candidate => Array.from(candidate.querySelectorAll("button")).some(button => button.textContent === "Save draft")) ?? null;
  const settingField = Array.from(dialog?.querySelectorAll("label") ?? [])
    .find(label => label.textContent?.includes(settingLabel));
  const input = settingField?.querySelector("input, textarea") as HTMLInputElement | null;
  await changeInput(input, value);
  await click(buttonByText(dialog!, "Save draft"));
}

function expectControlUnavailable(container: Element, selector: string) {
  const control = container.querySelector(selector) as (HTMLButtonElement | HTMLInputElement | null);
  expect(!control || control.disabled).toBe(true);
}

function expectControlUnavailableByText(container: Element, text: string) {
  const control = Array.from(container.querySelectorAll("button"))
    .find(button => button.textContent === text);
  expect(!control || control.disabled).toBe(true);
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
