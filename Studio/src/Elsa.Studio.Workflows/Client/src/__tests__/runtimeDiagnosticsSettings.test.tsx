import React from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { saveRuntimeDiagnosticsSettings } from "../api/workflows";
import { RuntimeDiagnosticsSettingsPage } from "../RuntimeDiagnosticsSettingsPage";
import type { RuntimeDiagnosticsSettingsView } from "../workflowTypes";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
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
      expect(container.textContent).toContain("Effective: Diagnostic snapshot");
      expect(container.textContent).toContain("Full payload capture is disabled by host policy.");
    });

    expect(findButton(container, "Save").hasAttribute("disabled")).toBe(true);
    expect(Array.from(container.querySelectorAll("option")).some(option => option.textContent === "Payload")).toBe(false);
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

    expect(putJson).toHaveBeenCalledWith("/_elsa/workflow-management/runtime-diagnostics/settings", {
      scope: "host-default",
      defaultLevel: "Metadata",
      subjectOverrides: { activityOutputs: "Off" }
    });
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
    baseUrl: "",
    http: {
      getJson: vi.fn(async () => settings),
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
