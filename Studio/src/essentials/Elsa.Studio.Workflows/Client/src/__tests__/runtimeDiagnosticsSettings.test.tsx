import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { saveRuntimeDiagnosticsSettings } from "../api/workflows";
import { clearApiCapabilityCache } from "../api/capabilities";
import { RuntimeDiagnosticsSettingsPage } from "../RuntimeDiagnosticsSettingsPage";
import type { RuntimeDiagnosticsSettingsView } from "../workflowTypes";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  clearApiCapabilityCache();
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
});

describe("RuntimeDiagnosticsSettingsPage", () => {
  it("renders requested and effective runtime diagnostics settings", async () => {
    const context = stubContext(defaultSettingsView());
    const container = render(<RuntimeDiagnosticsSettingsPage context={context} />);

    await waitFor(() => {
      expect(container.textContent).toContain("Runtime diagnostics");
      expect(container.textContent).toContain("New capture events use");
      expect(container.textContent).toContain("Diagnostic snapshots");
      expect(container.textContent).toContain("Choose the level Studio should request.");
      expect(container.textContent).toContain("Full payload capture is disabled by host policy.");
    });

    expect(findButton(container, "Save").hasAttribute("disabled")).toBe(true);
    expect(Array.from(container.querySelectorAll("option")).some(option => option.value === "Payload")).toBe(false);
  });

  it("explains when Host Policy lowers the saved request", async () => {
    const settings = defaultSettingsView();
    settings.effective = {
      defaultLevel: "Metadata",
      subjectOverrides: settings.effective.subjectOverrides,
      limitationReasons: ["Host Policy limits the default to metadata."]
    };
    settings.hostPolicy.maximumLevel = "Metadata";
    const container = render(<RuntimeDiagnosticsSettingsPage context={stubContext(settings)} />);

    await waitFor(() => {
      expect(container.querySelector(".runtime-diagnostics-effective strong")?.textContent).toBe("Metadata only");
      expect(container.textContent).toContain("Saved request: Diagnostic snapshots. Host Policy applies a lower level.");
      expect(container.textContent).toContain("Host Policy limits the default to metadata.");
    });
  });

  it("saves requested settings through the runtime diagnostics API", async () => {
    const putJson = vi.fn(async (_url: string, body: unknown) => ({
      ...defaultSettingsView(),
      requested: body,
      effective: {
        defaultLevel: "Metadata",
        subjectOverrides: { activityOutputs: "Off" },
        limitationReasons: []
      }
    }));
    const context = stubContext(defaultSettingsView({ canManage: true }), putJson);

    await saveRuntimeDiagnosticsSettings(context, {
      scope: "host-default",
      defaultLevel: "Metadata",
      subjectOverrides: { activityOutputs: "Off" }
    });

    expect(putJson).toHaveBeenCalledWith("/runtime/workflows/diagnostics/settings", {
      scope: "host-default",
      defaultLevel: "Metadata",
      subjectOverrides: { activityOutputs: "Off" }
    });
  });

  it("normalizes camel-case evidence levels and allows changing away from Off", async () => {
    const settings = camelCaseSettingsView();
    const putJson = vi.fn(async (_url: string, body: unknown) => ({ ...settings, requested: body }));
    const context = stubContext(settings, putJson);
    const container = render(<RuntimeDiagnosticsSettingsPage context={context} />);

    await waitFor(() => {
      expect(container.textContent).toContain("New capture events use");
      expect(container.textContent).toContain("Diagnostic snapshots");
      expect(container.querySelectorAll("input[name='runtime-diagnostics-default-level']")).toHaveLength(3);
      expect(findLevelInput(container, "DiagnosticSnapshot").checked).toBe(true);
      expect(findLevelInput(container, "Off").disabled).toBe(false);
      expect(Array.from(container.querySelectorAll("select")).some(select => select.value === "Metadata")).toBe(true);
      expect(findButton(container, "Save changes").hasAttribute("disabled")).toBe(true);
    });

    const off = findLevelInput(container, "Off");
    const metadata = findLevelInput(container, "Metadata");

    clickLevelCard(off);
    await waitFor(() => {
      expect(off.checked).toBe(true);
      expect(container.textContent).toContain("Unsaved request: Off");
      expect(container.querySelector(".runtime-diagnostics-effective strong")?.textContent).toBe("Diagnostic snapshots");
    });

    clickLevelCard(metadata);
    await waitFor(() => {
      expect(off.checked).toBe(false);
      expect(metadata.checked).toBe(true);
      expect(container.textContent).toContain("Unsaved request: Metadata only");
      expect(container.textContent).toContain("Save changes to apply this request to new capture events.");
      expect(findButton(container, "Save changes").hasAttribute("disabled")).toBe(false);
    });

    findButton(container, "Save").click();
    await waitFor(() => expect(putJson).toHaveBeenCalledWith("/runtime/workflows/diagnostics/settings", expect.objectContaining({
      defaultLevel: "Metadata"
    })));
  });
});

function render(ui: React.ReactElement) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false }
    }
  });
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>));
  active = { root, container };
  return container;
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;
  for (let attempt = 0; attempt < 30; attempt++) {
    try {
      assertion();
      return;
    } catch (error) {
      lastError = error;
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }
  throw lastError;
}

function stubContext(
  settings: RuntimeDiagnosticsSettingsView,
  putJson: (url: string, body: unknown) => Promise<unknown> = async () => settings
): StudioEndpointContext {
  return {
    baseUrl: `test://runtime-diagnostics-${Math.random()}`,
    http: {
      getJson: vi.fn(async (url: string) => url === "/capabilities"
        ? {
            capabilities: [{
              id: "elsa.api.runtime",
              contractVersion: "1",
              links: [{ rel: "runtime-diagnostics", href: "runtime/workflows/diagnostics/settings" }]
            }]
          }
        : settings),
      putJson,
      requestJson: vi.fn(),
      postJson: vi.fn(),
      deleteJson: vi.fn(),
      postForm: vi.fn()
    }
  } as unknown as StudioEndpointContext;
}

function defaultSettingsView(options: { canManage?: boolean } = {}): RuntimeDiagnosticsSettingsView {
  return {
    requested: {
      scope: "host-default",
      defaultLevel: "DiagnosticSnapshot",
      subjectOverrides: { durableValues: "Metadata" }
    },
    effective: {
      defaultLevel: "DiagnosticSnapshot",
      subjectOverrides: { durableValues: "Metadata" },
      limitationReasons: []
    },
    hostPolicy: {
      maximumLevel: "DiagnosticSnapshot",
      subjectMaximums: { durableValues: "Metadata" },
      limitationReasons: ["Full payload capture is disabled by host policy."],
      snapshotLimits: {
        maxDepth: 6,
        maxObjectProperties: 32,
        maxArrayItems: 32,
        maxStringLength: 256,
        maxTotalBytes: 16384
      }
    },
    permissions: {
      canManage: options.canManage ?? false,
      canEnableFullPayloads: false
    }
  };
}

function findButton(container: HTMLElement, label: string) {
  const button = Array.from(container.querySelectorAll("button")).find(candidate => candidate.textContent?.includes(label));
  if (!button) throw new Error(`Could not find button ${label}.`);
  return button;
}

function findLevelInput(container: HTMLElement, level: string) {
  const input = container.querySelector<HTMLInputElement>(`input[name='runtime-diagnostics-default-level'][value='${level}']`);
  if (!input) throw new Error(`Could not find runtime diagnostics level ${level}.`);
  return input;
}

function clickLevelCard(input: HTMLInputElement) {
  const label = input.closest("label");
  if (!label) throw new Error(`Could not find the card for runtime diagnostics level ${input.value}.`);
  label.click();
}

function camelCaseSettingsView(): RuntimeDiagnosticsSettingsView {
  return {
    ...defaultSettingsView({ canManage: true }),
    requested: {
      scope: "host-default",
      defaultLevel: "diagnosticSnapshot",
      subjectOverrides: { durableValues: "metadata" }
    },
    effective: {
      defaultLevel: "diagnosticSnapshot",
      subjectOverrides: { durableValues: "metadata" },
      limitationReasons: []
    },
    hostPolicy: {
      ...defaultSettingsView().hostPolicy,
      maximumLevel: "diagnosticSnapshot",
      subjectMaximums: { durableValues: "metadata" }
    }
  } as unknown as RuntimeDiagnosticsSettingsView;
}
