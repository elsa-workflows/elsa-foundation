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

afterEach(() => {
  const errors: unknown[] = [];
  for (const cleanup of Array.from(mountedRoots)) {
    try {
      cleanup();
    } catch (error) {
      errors.push(error);
    }
  }
  document.body.replaceChildren();
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
    expect((buttonByText(container, "Apply") as HTMLButtonElement).disabled).toBe(true);
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
          feature("Shared", false, {})
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
      firstToggle: "Disable B",
      secondToggle: "Enable B",
      residualFeature: "A",
      netZeroFeature: "B",
      requiredPath: ["B", "A"]
    }
  ] as const)("keeps a residual cascade truthful after its original root returns to baseline: $scenario", async scenario => {
    const api = stubApi({
      getJson: async () => ({
        revision: "server-rev-1",
        features: [
          feature("A", scenario.aEnabled, {}, [], { dependencies: ["B"] }),
          feature("B", scenario.bEnabled, {})
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
      expectNoImpactRow(resetReview, "B");
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
    const api = stubApi({
      getJson: async () => ({ revision: "server-rev-1", features: [feature("FeatureA", false, {}, [], { settings: [setting({ name: "Mode", displayName: "Mode" })] })] })
    });
    register(api);
    const page = await renderFeatureManagementPage(authenticatedProvider(["module-management:read", "module-management:manage"]));

    await click(page.container.querySelector("[aria-label='Enable FeatureA']"));
    page.rerender(authenticatedProvider(["module-management:read"]));
    await flushPromises();

    expect(page.container.textContent).toContain("FeatureA");
    expectControlUnavailable(page.container, "[aria-label='Disable FeatureA']");
    expectControlUnavailable(page.container, "[aria-label='Select FeatureA']");
    expectControlUnavailable(page.container, "[aria-label='Enable selected features']");
    expectControlUnavailable(page.container, "[aria-label='Disable selected features']");
    expectControlUnavailableByText(page.container, "Edit settings");
    expectControlUnavailableByText(page.container, "Reset");
    expectControlUnavailableByText(page.container, "Apply");
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
    dependencies: [],
    ...overrides
  };

  // Normalize dependency overrides so a bare string means a mandatory dependency and an object can flag `optional`.
  built.dependencies = (built.dependencies as Array<string | { id: string; optional?: boolean }>).map(dependency =>
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
  return children => React.createElement(AuthProvider, { manager }, children);
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
  return Array.from(review.querySelectorAll("tr, [role='row'], li, [role='listitem'], article, section, [data-impact-feature-id], [data-feature-id]"))
    .find(row => {
      const exactId = row.getAttribute("data-impact-feature-id")
        ?? row.querySelector("[data-impact-feature-id]")?.getAttribute("data-impact-feature-id");
      if (exactId) {
        return exactId === featureId;
      }

      const identity = row.querySelector("th, td:first-child, [role='rowheader'], h3, h4, strong, code")?.textContent?.trim();
      return identity ? new RegExp(`(^|\\b)${escapeRegExp(featureId)}(\\b|$)`).test(identity) : false;
    }) ?? null;
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

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

async function editSettingValue(container: Element, featureId: string, settingLabel: string, value: string) {
  const card = Array.from(container.querySelectorAll(".feature-management-feature-button"))
    .find(button => button.textContent?.includes(featureId));
  await click(card ?? null);
  await click(buttonByText(container, "Edit settings"));

  const dialog = container.querySelector("[role='dialog']");
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
