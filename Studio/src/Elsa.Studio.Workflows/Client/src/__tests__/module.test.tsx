import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioContributionRegistry, StudioSlotDefinition } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache, isConnectEndOverExistingWorkflowNode, register, resolveConnectEndSource, type WorkflowDesignerPanelContext } from "../module";
import { workflowInspectorCollapsedStorageKey, workflowInspectorWidthStorageKey, workflowSidePanelMaximizedStorageKey } from "../workflow-editor/constants";
import { createDraftSnapshotId, insertSequenceNodeAfter } from "../workflow-editor/editorHelpers";

afterEach(() => {
  clearApiCapabilityCache();
  vi.unstubAllGlobals();
  window.localStorage.removeItem?.(workflowInspectorCollapsedStorageKey);
  window.localStorage.removeItem?.(workflowInspectorWidthStorageKey);
  window.localStorage.removeItem?.(workflowSidePanelMaximizedStorageKey);
});

describe("workflows module", () => {
  it("treats a connect-end captured by the source handle as an empty-canvas release when the pointer is over the pane", () => {
    const sourceHandle = document.createElement("div");
    sourceHandle.className = "react-flow__handle";
    const pane = document.createElement("div");
    pane.className = "react-flow__pane";
    const elementFromPoint = Object.getOwnPropertyDescriptor(document, "elementFromPoint");
    Object.defineProperty(document, "elementFromPoint", { configurable: true, value: vi.fn(() => pane) });

    try {
      const event = new MouseEvent("mouseup", { bubbles: true, clientX: 300, clientY: 240 });
      sourceHandle.dispatchEvent(event);

      expect(isConnectEndOverExistingWorkflowNode(event)).toBe(false);
    } finally {
      if (elementFromPoint) Object.defineProperty(document, "elementFromPoint", elementFromPoint);
      else Reflect.deleteProperty(document, "elementFromPoint");
    }
  });

  it("resolves a connect-end source from React Flow connection state when the start ref is empty", () => {
    expect(resolveConnectEndSource(null, {
      fromNode: { id: "write-line-1" },
      fromHandle: { id: "Done" }
    })).toEqual({ nodeId: "write-line-1", handleId: "Done" });
  });

  it("inserts a picked Sequence activity immediately after the connection source", () => {
    const nodes = [
      { id: "a", position: { x: 40, y: 0 }, data: {} },
      { id: "b", position: { x: 320, y: 0 }, data: {} },
      { id: "c", position: { x: 600, y: 0 }, data: {} }
    ];

    const result = insertSequenceNodeAfter(nodes, "b", {
      id: "new",
      position: { x: 0, y: 0 },
      data: {}
    });

    expect(result.map(node => node.id)).toEqual(["a", "b", "new", "c"]);
    expect(result.map(node => node.position.x)).toEqual([40, 320, 600, 880]);
    expect(nodes[2].position.x).toBe(600);
  });

  it("registers Runs navigation while preserving instance routes", () => {
    const api = testApi();

    register(api);

    expect(api.featureAreas.list()).toEqual([
      expect.objectContaining({ id: "workflows", title: "Workflows", ownedPaths: ["/workflows"], required: true, defaultEnabled: true })
    ]);
    expect(api.navigation.list()).toEqual([
      expect.objectContaining({ id: "workflows", path: "/workflows/definitions", activePathPrefix: "/workflows" }),
      expect.objectContaining({ id: "workflows-definitions", path: "/workflows/definitions", parentId: "workflows" }),
      expect.objectContaining({ id: "workflows-executables", path: "/workflows/executables", parentId: "workflows" }),
      expect.objectContaining({ id: "workflows-runs", label: "Runs", path: "/workflows/instances", parentId: "workflows" }),
      expect.objectContaining({ id: "workflows-runtime diagnostics", label: "Runtime Diagnostics", path: "/workflows/runtime-diagnostics", parentId: "workflows" }),
      expect.objectContaining({ id: "workflows-activity availability", label: "Activity Availability", path: "/workflows/activity-availability", parentId: "workflows" })
    ]);
    expect(api.routes.list()).toEqual([
      expect.objectContaining({ id: "workflows-definitions", path: "/workflows/definitions" }),
      expect.objectContaining({ id: "workflows-executables", path: "/workflows/executables" }),
      expect.objectContaining({ id: "workflows-executable-inspector", label: "Executable Inspector", path: "/workflows/executables/:artifactId" }),
      expect.objectContaining({ id: "workflows-instances", label: "Workflow runs", path: "/workflows/instances" }),
      expect.objectContaining({ id: "workflows-instance-detail", label: "Workflow run", path: "/workflows/instances/:workflowExecutionId" }),
      expect.objectContaining({ id: "workflows-activity-availability", label: "Activity availability", path: "/workflows/activity-availability" }),
      expect.objectContaining({ id: "workflows-runtime-diagnostics", label: "Runtime diagnostics", path: "/workflows/runtime-diagnostics" })
    ]);
    expect(api.expressionEditors.list()).toEqual([
      expect.objectContaining({ id: "studio.workflows.variable-reference", createDefaultValue: expect.any(Function) }),
      expect.objectContaining({ id: "elsa.object-expression-editor", createDefaultValue: expect.any(Function) }),
      expect.objectContaining({ id: "studio.workflows.input-reference", createDefaultValue: expect.any(Function) })
    ]);
  });

  it("renders active definition actions and soft-deletes with confirmation", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "DELETE") return response(null, 204);
      expect(url).toContain("state=active");
      return response({ items: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { api, container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Latest version");

    expect(container.textContent).toContain("Latest version");
    expect(container.textContent).toContain("Open");
    expect(container.textContent).toContain("Artifacts");
    expect(container.textContent).toContain("Delete");

    await click(buttonByText(container, "Delete"));
    await flushPromises();

    expect(api.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ message: expect.stringContaining("Delete workflow definition") }));
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/design/workflows/definitions/definition-1",
      expect.objectContaining({ method: "DELETE" })
    );

    await unmount();
  });

  it("renders deleted definition actions and calls restore and permanent delete endpoints", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST" || init?.method === "DELETE") return response(null, 204);
      return response({ items: url.includes("state=deleted") ? [definition({ deletedAt: "2026-06-18T01:00:00Z" })] : [] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { api, container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "No active workflow definitions");
    await click(buttonByText(container, "Deleted"));
    await waitForText(container, "Delete permanently");

    expect(container.textContent).toContain("Restore");
    expect(container.textContent).toContain("Delete permanently");

    await click(buttonByText(container, "Restore"));
    await flushPromises();

    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/design/workflows/definitions/definition-1/restore",
      expect.objectContaining({ method: "POST" })
    );

    await click(buttonByText(container, "Delete permanently"));
    await flushPromises();

    expect(api.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ message: expect.stringContaining("Permanently delete workflow definition") }));
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/design/workflows/definitions/definition-1/permanent",
      expect.objectContaining({ method: "DELETE" })
    );

    await unmount();
  });

  it("pages the canonical definition collection without extra requests", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL) => response({
      items: [definition(), ...Array.from({ length: 9 }, (_, index) => definition({ id: `definition-${index + 2}`, name: `Workflow ${index + 2}` })), definition({ id: "definition-11", name: "Second page" })]
    }));
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Page 1 of 2");
    await click(buttonByText(container, "Next"));
    await waitForText(container, "Second page");

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls.map(([url]) => String(url))).toEqual([
      "https://server.example/design/workflows/definitions?state=active",
      "https://server.example/design/workflows/definitions?state=active"
    ]);

    await unmount();
  });

  it("opens the workflow editor when a definition row is clicked", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/publishing/workflows/definition-1/slots")) return response({ items: [publicationSlot("artifact-current")] });
      if (url.includes("/activities")) return response({ activities: [] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: null, versions: [] });
      return response({ items: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Hello World");
    await click(rowByLabel(container, "Open workflow definition Hello World"));

    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");

    await unmount();
  });

  it("enables workflow editor autosave by default when not configured", async () => {
    stubWorkflowEditorFetch();

    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Autosave");
    expect(autosaveInput(container)?.checked).toBe(true);

    await unmount();
  });

  it("uses the configured workflow editor autosave default", async () => {
    stubWorkflowEditorFetch();

    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1", api => {
      api.runtime.workflows = { autosaveEnabledByDefault: false };
    });

    await waitForText(container, "Autosave");
    expect(autosaveInput(container)?.checked).toBe(false);

    await unmount();
  });

  it("renders the activity palette as a category tree with activity descriptions", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line",
          description: "Writes a line to the console."
        }),
        activity({
          activityVersionId: "flowchart-v1",
          activityTypeKey: "Elsa.Activities.Flowchart.Activities.Flowchart",
          category: "Composition",
          displayName: "Flowchart",
          description: "Runs activities as a graph."
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: workflowDraft(), versions: [] });
      return response({ items: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Write Line");

    const tree = container.querySelector("[role='tree'][aria-label='Available activities']");
    expect(tree).toBeTruthy();
    expect(tree?.textContent).toContain("Composition");
    expect(tree?.textContent).toContain("Primitives");
    expect(tree?.textContent).toContain("Write Line");
    expect(tree?.textContent).not.toContain("Elsa.Activities.Primitives.Activities.WriteLine");

    const writeLine = Array.from(container.querySelectorAll<HTMLButtonElement>(".wf-palette-activity"))
      .find(button => button.textContent?.includes("Write Line"));
    expect(writeLine?.getAttribute("role")).toBe("treeitem");
    expect(writeLine?.getAttribute("title")).toBe("Writes a line to the console.");
    expect(writeLine?.textContent).toContain("Writes a line to the console.");
    expect(writeLine?.querySelector(".wf-activity-icon")).toBeTruthy();
    expect(writeLine?.querySelector(".wf-palette-activity-grip")).toBeTruthy();

    const primitives = Array.from(container.querySelectorAll<HTMLButtonElement>(".wf-palette-category-toggle"))
      .find(button => button.textContent?.includes("Primitives"));
    expect(primitives?.getAttribute("aria-expanded")).toBe("true");
    await click(primitives ?? null);

    expect(primitives?.getAttribute("aria-expanded")).toBe("false");
    expect(tree?.textContent).not.toContain("Write Line");

    await unmount();
  });

  it("exposes canvas nodes as named controls, synchronizes keyboard selection, and focuses palette insertions", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line",
          executionType: "Action"
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: draftWithFlowchartRoot([
          { nodeId: "write-line-1", activityVersionId: "write-line-v1", inputs: [], outputs: [], structure: null },
          { nodeId: "write-line-2", activityVersionId: "write-line-v1", inputs: [], outputs: [], structure: null }
        ]),
        versions: []
      });
      return response({ items: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForCanvasNode(container, "Write Line");
    const first = container.querySelector<HTMLElement>(".wf-canvas .react-flow__node[data-id='write-line-1']")!;
    expect(first.getAttribute("role")).toBe("button");
    expect(first.getAttribute("aria-label")).toContain("Activity type: Elsa.Activities.Primitives.Activities.WriteLine");
    expect(first.getAttribute("aria-label")).toContain("State: authoring");
    expect(first.getAttribute("aria-pressed")).toBe("false");
    const descriptionId = first.getAttribute("aria-describedby")!;
    expect(document.getElementById(descriptionId)?.textContent).toContain("Press Tab to move between canvas items");

    first.focus();
    flushSync(() => first.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));
    await flushPromises();
    expect(first.getAttribute("aria-pressed")).toBe("true");
    expect(container.querySelector(".wf-inspector")?.textContent).toContain("write-line-1");

    const second = container.querySelector<HTMLElement>(".wf-canvas .react-flow__node[data-id='write-line-2']")!;
    second.focus();
    flushSync(() => second.dispatchEvent(new KeyboardEvent("keydown", { key: " ", bubbles: true })));
    await flushPromises();
    expect(second.getAttribute("aria-pressed")).toBe("true");
    expect(container.querySelector(".wf-inspector")?.textContent).toContain("write-line-2");

    const paletteActivity = container.querySelector<HTMLButtonElement>(".wf-palette-activity")!;
    await click(paletteActivity);
    for (let attempt = 0; attempt < 20 && container.querySelectorAll(".wf-canvas .react-flow__node").length < 3; attempt += 1) {
      await flushPromises();
    }
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
    const focused = document.activeElement as HTMLElement;
    expect(focused.classList.contains("react-flow__node")).toBe(true);
    expect(focused.getAttribute("aria-pressed")).toBe("true");

    await unmount();
  });

  it("supports resizing, collapsing, and maximizing workflow side panels", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line"
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: workflowDraft(), versions: [] });
      return response({ items: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Write Line");

    const editor = container.querySelector(".wf-editor-body");
    expect(editor).toBeTruthy();
    expect(container.querySelectorAll(".wf-side-resize-handle")).toHaveLength(2);
    expect(buttonByLabel(container, "Collapse activities panel")).toBeTruthy();
    expect(buttonByLabel(container, "Maximize inspector panel")).toBeTruthy();

    await click(buttonByLabel(container, "Collapse activities panel"));
    expect(editor?.className).toContain("palette-collapsed");
    expect(buttonByLabel(container, "Expand activities panel")).toBeTruthy();

    await click(buttonByLabel(container, "Expand activities panel"));
    expect(editor?.className).not.toContain("palette-collapsed");

    await click(buttonByLabel(container, "Maximize inspector panel"));
    expect(editor?.className).toContain("inspector-maximized");
    expect(buttonByLabel(container, "Restore inspector panel")).toBeTruthy();

    await click(buttonByLabel(container, "Restore inspector panel"));
    expect(editor?.className).not.toContain("inspector-maximized");

    const inspectorSeparator = container.querySelector<HTMLElement>("[aria-label='Resize inspector panel']");
    inspectorSeparator?.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowLeft", bubbles: true }));
    await flushPromises();
    flushSync(() => {});
    expect(inspectorSeparator?.getAttribute("aria-valuenow")).toBe("336");

    await unmount();
  });

  it("hosts module-contributed workflow side panel tabs", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line"
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: draftWithFlowchartRoot(),
        versions: []
      });
      return response({ items: [definition()] });
    }));
    const captured = capturingPanel();
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1", api => {
      api.workflowDesigner.panels.add({
        id: "custom.left",
        title: "Library",
        side: "left",
        order: 50,
        component: () => <div className="custom-left-panel">Module left panel</div>
      });
      api.workflowDesigner.panels.add({
        id: "custom.right",
        title: "Audit",
        side: "right",
        order: 50,
        component: captured.component
      });
    });

    await waitForText(container, "Write Line");
    expect(container.querySelector("[role='tablist'][aria-label='Activities panel tabs']")?.textContent).toContain("Library");
    expect(container.querySelector("[role='tablist'][aria-label='Inspector panel tabs']")?.textContent).toContain("Audit");

    await click(buttonByText(container, "Library"));
    expect(container.textContent).toContain("Module left panel");

    // With nothing selected the panel context mirrors the inspector's owner fallback: the inspected
    // activity is the root scope owner while selectedActivity stays null.
    await click(buttonByText(container, "Audit"));
    expect(container.textContent).toContain("Module right panel");
    expect(captured.context?.inspectedIsScopeOwner).toBe(true);
    expect(captured.context?.inspectedActivity?.nodeId).toBe("root");
    expect(captured.context?.inspectedActivitySlots).toHaveLength(1);
    expect(captured.context?.selectedActivity).toBeNull();
    expect(captured.context?.currentScopeOwner?.nodeId).toBe("root");

    await unmount();
  });

  it("provides a null inspected context to contributed panels when the draft has no root", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: workflowDraft(), versions: [] });
      return response({ items: [definition()] });
    }));
    const captured = capturingPanel();
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1", api => {
      api.workflowDesigner.panels.add({ id: "custom.right", title: "Audit", side: "right", order: 50, component: captured.component });
    });

    await waitForText(container, "Audit");
    await click(buttonByText(container, "Audit"));

    // No root activity: there is no scope owner to fall back to, so the panel still mounts and the
    // inspected view is empty rather than crashing.
    expect(container.textContent).toContain("Module right panel");
    expect(captured.context?.inspectedActivity).toBeNull();
    expect(captured.context?.inspectedIsScopeOwner).toBe(false);
    expect(captured.context?.inspectedActivitySlots).toHaveLength(0);
    expect(captured.context?.selectedActivity).toBeNull();

    await unmount();
  });

  it("shows current workflow artifacts in the designer right panel", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const writeText = vi.fn(async () => undefined);
    const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, "clipboard");
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST" && url.endsWith("/executables/artifact-current/execute")) {
        return response({ workflowExecutionId: "wfexec-artifacts-1" });
      }
      if (url.includes("/design/workflows/versions/version-1")) {
        const version = workflowDefinitionVersionDetails();
        return response({ ...version, state: { ...version.state, inputs: [workflowInput()] } });
      }
      if (url.includes("/publishing/workflows/definition-1/slots")) return response({ items: [
        publicationSlot("artifact-current"),
        publicationSlot("artifact-no-live", { slotName: "canary" })
      ] });
      if (url.includes("/runtime/workflows/executables")) return response([
        executable({
          artifactId: "artifact-current",
          definitionId: "definition-1",
          artifactVersion: "2.0.0",
          references: [executableReference({
            sourceReferenceId: "reference-current",
            artifactId: "artifact-current",
            definitionVersionId: "version-1"
          })]
        }),
        executable({ artifactId: "artifact-no-live", definitionId: "definition-1", references: [] }),
        executable({ artifactId: "artifact-other", definitionId: "definition-2", sourceId: "definition-2" })
      ]);
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line"
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: workflowDraft(), versions: [] });
      return response({ items: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    try {
      await waitForText(container, "Write Line");
      await click(buttonByText(container, "Artifacts"));
      await waitForText(container, "artifact-current");

      expect(container.textContent).toContain("Version 2.0.0");
      expect(container.textContent).not.toContain("artifact-other");
      const unavailableRun = buttonByLabel(container, "Run executable artifact-no-live") as HTMLButtonElement;
      expect(unavailableRun.disabled).toBe(true);
      expect(unavailableRun.title).toContain("No live published reference");

      await click(buttonByLabel(container, "Copy artifact ID artifact-current"));
      expect(writeText).toHaveBeenCalledWith("artifact-current");
      expect(container.textContent).toContain("Copied artifact ID");

      await click(buttonByLabel(container, "Run executable artifact-current"));
      await waitForText(container, "Provide the workflow inputs for this run.");
      await fill(inputByLabel(container, "Greeting"), "Hello from artifacts");
      await click(buttonByText(container, "Run workflow"));
      await waitForText(container, "Open Run wfexec-artifacts-1");
      expect(fetchMock.mock.calls.map(([url]) => String(url))).toContain(
        "https://server.example/design/workflows/versions/version-1"
      );
      const executeCall = fetchMock.mock.calls.find(([url, init]) => String(url).endsWith("/executables/artifact-current/execute") && init?.method === "POST");
      expect(JSON.parse(String(executeCall?.[1]?.body))).toEqual({
        inputs: { Greeting: "Hello from artifacts" },
        sourceReferenceId: "reference-current"
      });

      await click(buttonByText(container, "Open list"));
      expect(window.location.pathname).toBe("/workflows/executables");
      expect(window.location.search).toBe("?definition=definition-1");
    } finally {
      await unmount();
      if (clipboardDescriptor) Object.defineProperty(navigator, "clipboard", clipboardDescriptor);
      else Reflect.deleteProperty(navigator, "clipboard");
    }
  });

  it("opens the canvas activity picker and inserts an activity into an empty flowchart", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line",
          description: "Writes a line to the console."
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: draftWithFlowchartRoot(),
        versions: []
      });
      return response({ items: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Add activity");
    await click(buttonByText(container, "Add activity"));
    expect(inputByLabel(container, "Search activities")).toBeTruthy();
    await click(optionByText(container, "Write Line"));
    await flushPromises();

    expect(container.querySelector(".wf-node")?.textContent).toContain("Write Line");

    await unmount();
  });

  it("makes Sequence output handles available for drag-to-add gestures", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "sequence-v1",
          activityTypeKey: "Elsa.Activities.Sequence.Activities.Sequence",
          category: "Composition",
          displayName: "Sequence",
          designFacets: [{
            kind: "elsa.sequence.structure",
            schemaVersion: "1.0.0",
            payload: {
              mode: "sequence",
              supportsScopedVariables: true,
              slots: [{ name: "Sequence.Activities", property: "activities", displayName: "Activities", cardinality: "many" }],
              initialPayload: { activities: [] }
            }
          }]
        }),
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line"
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: workflowDraft({
          state: {
            variables: [],
            rootActivity: sequenceRoot([{
              nodeId: "write-line-1",
              activityVersionId: "write-line-v1",
              inputs: [],
              outputs: []
            }]),
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ items: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForCanvasNode(container, "Write Line");

    const outputHandle = container.querySelector(".wf-canvas .react-flow__handle.source");
    expect(outputHandle?.classList.contains("connectablestart")).toBe(true);

    await unmount();
  });

  it("adds one activity for one palette drag onto the canvas", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line",
          description: "Writes a line to the console."
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: draftWithFlowchartRoot(),
        versions: []
      });
      return response({ items: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Write Line");
    const paletteActivity = container.querySelector(".wf-palette-activity");
    const canvas = container.querySelector<HTMLElement>(".wf-canvas");
    const reactFlow = container.querySelector<HTMLElement>(".react-flow");
    if (!canvas || !reactFlow) throw new Error("Canvas not found");
    stubRect(canvas, { left: 100, top: 100, right: 900, bottom: 700, width: 800, height: 600 });
    stubRect(reactFlow, { left: 100, top: 100, right: 900, bottom: 700, width: 800, height: 600 });
    const restoreElementFromPoint = stubElementFromPoint(reactFlow);

    try {
      const dataTransfer = dragDataTransfer();
      dispatchDragEvent(paletteActivity, "dragstart", { dataTransfer, clientX: 220, clientY: 220 });
      dispatchDragEvent(reactFlow, "drop", { dataTransfer, clientX: 360, clientY: 300 });
      dispatchDragEvent(paletteActivity, "dragend", { dataTransfer, clientX: 360, clientY: 300 });
      await flushPromises();
      flushSync(() => {});

      expect(container.querySelectorAll(".wf-canvas .wf-node")).toHaveLength(1);
    } finally {
      restoreElementFromPoint();
      await unmount();
    }
  });

  it("renders an unsupported root activity as a fixed selectable node without flow ports", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line",
          description: "Writes a line to the console."
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: workflowDraft({
          state: {
            variables: [],
            rootActivity: {
              nodeId: "write-line-root",
              activityVersionId: "write-line-v1",
              inputs: [],
              outputs: [],
              structure: null
            },
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ items: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForCanvasNode(container, "Write Line");

    const activityNodes = container.querySelectorAll(".wf-canvas .wf-node");
    expect(activityNodes).toHaveLength(1);
    expect(activityNodes[0].textContent).toContain("Write Line");
    expect(activityNodes[0].textContent).toContain("Primitives");
    expect(activityNodes[0].textContent).not.toContain("Elsa.Activities.Primitives.Activities.WriteLine");
    expect(activityNodes[0].getAttribute("data-icon")).toBe("terminal");
    expect(container.querySelectorAll(".wf-canvas .react-flow__handle")).toHaveLength(0);
    expect(buttonByText(container, "Add activity")).toBeNull();

    await click(container.querySelector(".wf-canvas .react-flow__node"));

    expect(container.querySelector(".wf-inspector")?.textContent).toContain("write-line-root");
    expect(container.querySelector(".wf-inspector")?.textContent).toContain("write-line-v1");
    expect(container.querySelector(".wf-inspector")?.textContent).toContain("Elsa.Activities.Primitives.Activities.WriteLine");

    await unmount();
  });

  it("renders descriptor-driven properties and saves wrapped input edits", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "PUT") return response({ ...workflowDraft(JSON.parse(String(init.body))), validationErrors: [] });
      if (url.includes("/design/activities/catalog")) return response({ activities: [catalogActivity(writeLineDescriptor())] });
      if (url.includes("/expressions/descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "JavaScript", displayName: "JavaScript", editingMode: "text" }
      ] });
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line",
          description: "Writes a line to the console."
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: workflowDraft({
          state: {
            variables: [],
            rootActivity: {
              nodeId: "write-line-root",
              activityVersionId: "write-line-v1",
              inputs: [],
              outputs: [],
              structure: null
            },
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ items: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForCanvasNode(container, "Write Line");
    await click(container.querySelector(".wf-canvas .react-flow__node"));
    const writeLineNode = Array.from(container.querySelectorAll(".react-flow__node"))
      .find(node => node.textContent?.includes("Write Line")) ?? null;
    await click(writeLineNode);
    await waitForText(container, "Text");
    expect(container.querySelector("select.wf-property-syntax")).toBeNull();
    expect(container.querySelector(".wf-expression-field .wf-syntax-picker.inline")).toBeTruthy();
    expect(container.querySelector(".wf-property-row > .wf-syntax-picker:not(.inline)")).toBeNull();

    await click(container.querySelector(".wf-syntax-picker-trigger"));
    expect(document.body.querySelector(".wf-syntax-picker-menu")?.textContent).toContain("Literal");
    expect(document.body.querySelector(".wf-syntax-picker-menu")?.textContent).toContain("JavaScript");

    await fill(container.querySelector<HTMLInputElement>(".wf-property-row input[type='text']"), "Hello from properties");
    await click(buttonByLabel(container, "Open expanded Text editor"));
    expect(container.querySelector("[role='dialog']")?.textContent).toContain("Property editor");
    await fill(textareaByLabel(container, "Text expanded value"), "Hello from expanded editor\nwith more room");
    await click(buttonByText(container, "Close"));

    await click(buttonByText(container, "Save"));

    const putCall = fetchMock.mock.calls.find(([url, init]) => String(url).includes("/drafts/draft-1") && init?.method === "PUT");
    expect(putCall).toBeTruthy();
    const savedRoot = JSON.parse(String(putCall?.[1]?.body)).state.rootActivity;
    // The wire payload carries the authored value in the canonical `inputs` array (ArgumentState),
    // not as a top-level `text` property — that mismatch is what made WriteLine print blank lines.
    expect(savedRoot.text).toBeUndefined();
    expect(savedRoot.inputs).toEqual([
      {
        referenceKey: "Text",
        value: { value: "Hello from expanded editor\nwith more room", expressionType: "Literal" }
      }
    ]);

    await unmount();
  });

  it("renders a collection input as a repeater and saves the authored list", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "PUT") return response({ ...workflowDraft(JSON.parse(String(init.body))), validationErrors: [] });
      if (url.includes("/design/activities/catalog")) return response({ activities: [catalogActivity(writeLinesDescriptor())] });
      if (url.includes("/expressions/descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "JavaScript", displayName: "JavaScript", editingMode: "text" }
      ] });
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-lines-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLines",
          category: "Primitives",
          displayName: "Write Lines"
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: workflowDraft({
          state: {
            variables: [],
            rootActivity: {
              nodeId: "write-lines-root",
              activityVersionId: "write-lines-v1",
              inputs: [],
              outputs: [],
              structure: null
            },
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ items: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForCanvasNode(container, "Write Lines");
    await click(Array.from(container.querySelectorAll(".react-flow__node"))
      .find(node => node.textContent?.includes("Write Lines")) ?? null);
    await waitForText(container, "Lines");

    // The type badge shows a friendly label for the assembly-qualified generic collection type rather
    // than leaking an assembly-qualifier fragment ("0, Culture=neutral, PublicKeyToken=…").
    const typeBadge = container.querySelector(".wf-property-row-header span");
    expect(typeBadge?.textContent).toBe("ICollection<String>");

    // The collection input renders the repeater (not a single text box) and starts empty.
    const repeater = container.querySelector(".wf-collection-editor");
    expect(repeater).toBeTruthy();
    expect(repeater?.querySelector(".wf-collection-empty")).toBeTruthy();

    // Despite the "singleline" hint, the collection does NOT get wrapped in the inline expression-field
    // chrome (whose overlaid picker would stretch over the list and hide the reorder controls). It gets a
    // block syntax picker above the list instead.
    expect(container.querySelector(".wf-expression-field")).toBeNull();
    const syntaxPicker = container.querySelector(".wf-syntax-picker");
    expect(syntaxPicker).toBeTruthy();
    expect(syntaxPicker?.classList.contains("inline")).toBe(false);

    const addButton = buttonByText(container, "Add item");
    await click(addButton);
    await click(buttonByText(container, "Add item"));
    const rows = container.querySelectorAll<HTMLInputElement>(".wf-collection-item input[type='text']");
    expect(rows.length).toBe(2);
    await fill(rows[0], "First line");
    await fill(container.querySelectorAll<HTMLInputElement>(".wf-collection-item input[type='text']")[1], "Second line");

    await click(buttonByText(container, "Save"));

    const putCall = fetchMock.mock.calls.find(([url, init]) => String(url).includes("/drafts/draft-1") && init?.method === "PUT");
    expect(putCall).toBeTruthy();
    const savedRoot = JSON.parse(String(putCall?.[1]?.body)).state.rootActivity;
    // The authored list reaches the wire as a JSON array string (the backend's ArgumentValue.Value is a
    // string) under the "Object" expression type, so the backend JSON-deserializes it into ICollection<T>
    // rather than failing to convert a scalar literal — and `toLiteralCollection` parses it back into rows
    // on load.
    expect(savedRoot.inputs).toEqual([
      {
        referenceKey: "Lines",
        value: { value: '["First line","Second line"]', expressionType: "Object" }
      }
    ]);

    await unmount();
  });

  it("reorders collection items via drag and drop", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "PUT") return response({ ...workflowDraft(JSON.parse(String(init.body))), validationErrors: [] });
      if (url.includes("/design/activities/catalog")) return response({ activities: [catalogActivity(writeLinesDescriptor())] });
      if (url.includes("/expressions/descriptors")) return response({ items: [{ type: "Literal", displayName: "Literal", editingMode: "literal" }] });
      if (url.includes("/activities")) return response({ activities: [
        activity({ activityVersionId: "write-lines-v1", activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLines", category: "Primitives", displayName: "Write Lines" })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: workflowDraft({
          state: {
            variables: [],
            rootActivity: { nodeId: "write-lines-root", activityVersionId: "write-lines-v1", inputs: [], outputs: [], structure: null },
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ items: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForCanvasNode(container, "Write Lines");
    await click(Array.from(container.querySelectorAll(".react-flow__node")).find(node => node.textContent?.includes("Write Lines")) ?? null);
    await waitForText(container, "Lines");

    const rowInputs = () => Array.from(container.querySelectorAll<HTMLInputElement>(".wf-collection-item input[type='text']"));
    await click(buttonByText(container, "Add item"));
    await click(buttonByText(container, "Add item"));
    await click(buttonByText(container, "Add item"));
    await fill(rowInputs()[0], "A");
    await fill(rowInputs()[1], "B");
    await fill(rowInputs()[2], "C");

    // Drag the first row's handle onto the third row; the list reorders A,B,C -> B,C,A.
    const handles = container.querySelectorAll(".wf-collection-item-handle");
    const rows = container.querySelectorAll(".wf-collection-item");
    const transfer = makeDataTransfer();
    await fireDrag(handles[0], "dragstart", transfer);
    await fireDrag(rows[2], "dragover", transfer);
    await fireDrag(rows[2], "drop", transfer);

    expect(rowInputs().map(row => row.value)).toEqual(["B", "C", "A"]);

    await unmount();
  });

  it("filters the canvas activity picker with keyboard search", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [
        activity({ activityVersionId: "write-line-v1", activityTypeKey: "Elsa.Activities.WriteLine", category: "Primitives", displayName: "Write Line" }),
        activity({ activityVersionId: "send-email-v1", activityTypeKey: "Elsa.Activities.SendEmail", category: "Email", displayName: "Send Email" })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: draftWithFlowchartRoot(),
        versions: []
      });
      return response({ items: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Add activity");
    await click(buttonByText(container, "Add activity"));
    await fill(inputByLabel(container, "Search activities"), "email");

    expect(container.querySelector(".wf-connect-menu")?.textContent).toContain("Send Email");
    expect(container.querySelector(".wf-connect-menu")?.textContent).not.toContain("Write Line");

    await unmount();
  });

  it("opens a filtered executable artifacts view for a definition", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response({ items: [definition()] })));
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Hello World");
    await click(buttonByText(container, "Artifacts"));

    expect(window.location.pathname).toBe("/workflows/executables");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");

    await unmount();
  });

  it("scopes an artifact delete to the definition when deleting from the editor's artifacts panel", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "DELETE") return response(null, 204);
      if (url.includes("/publishing/workflows/definition-1/slots")) return response({ items: [publicationSlot("artifact-current")] });
      if (url.includes("/runtime/workflows/executables")) return response([
        executable({ artifactId: "artifact-current", definitionId: "definition-1" })
      ]);
      if (url.includes("/activities")) return response({ activities: [] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: workflowDraft(), versions: [] });
      return response({ items: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { api, container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Autosave");
    await click(buttonByText(container, "Artifacts"));
    await waitForText(container, "artifact-current");

    await click(buttonByLabel(container, "Unpublish publication slot default"));
    await flushPromises();

    expect(api.dialogs.confirm).toHaveBeenCalledWith(expect.objectContaining({ message: expect.stringContaining("Existing workflow runs keep their pinned executable") }));
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/publishing/workflows/definition-1/slots/default",
      expect.objectContaining({ method: "DELETE" })
    );

    await unmount();
  });

  it("creates a workflow definition from catalog-authored initial state without rootKind", async () => {
    let createBody: unknown;
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST" && url.endsWith("/design/workflows/definitions")) {
        createBody = JSON.parse(String(init.body));
        return response({ definition: definition({ id: "created-definition", name: "Customer onboarding" }), draft: null, versions: [] });
      }
      if (url.includes("/activities")) return response({ activities: [
        activity({ activityVersionId: "flowchart-v1", activityTypeKey: "Elsa.Activities.Flowchart", category: "Composition", displayName: "Flowchart", authoringTemplate: { nodeId: "activity", activityVersionId: "flowchart-v1", inputs: {}, outputs: {}, structure: { kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: { activities: [], connections: [] } } } }),
        activity({ activityVersionId: "sequence-v1", activityTypeKey: "Elsa.Activities.Sequence", category: "Composition", displayName: "Sequence", authoringTemplate: { nodeId: "activity", activityVersionId: "sequence-v1", inputs: {}, outputs: {}, structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [] } } } })
      ] });
      if (url.includes("/definitions/created-definition")) return response({ definition: definition({ id: "created-definition", name: "Customer onboarding" }), draft: null, versions: [] });
      return response({ items: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Hello World");
    await click(buttonByText(container, "Create"));
    await waitForText(container, "Create Workflow");
    const rootCards = dialog(container).querySelectorAll(".wf-root-card");
    expect(rootCards).toHaveLength(2);
    expect(Array.from(rootCards).map(card => card.querySelector(".wf-root-card-title")?.textContent)).toEqual(["Flowchart", "Sequence"]);

    await fill(inputByLabel(container, "Display name"), "Customer onboarding");
    await fill(textareaByLabel(container, "Description"), "Creates the first customer workflow.");
    await click(inputByLabel(container, "Sequence"));
    await click(buttonByText(dialog(container), "Create"));

    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/design/workflows/definitions",
      expect.objectContaining({ method: "POST" })
    );
    await waitForUrlParam("definition", "created-definition");
    expect(createBody).toEqual({
      name: "Customer onboarding",
      description: "Creates the first customer workflow.",
      initialState: {
        rootActivity: {
          nodeId: expect.stringMatching(/^sequence-/),
          activityVersionId: "sequence-v1",
          inputs: [],
          outputs: [],
          structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [] } }
        }
      }
    });

    await unmount();
  });

  it("reveals an intent box, requests a metadata suggestion, and applies it to the form", async () => {
    const { container, unmount, publishSuggestion } = await openCreateDialogWithSuggest();
    await fill(textareaByLabel(container, "Workflow intent"), "Approve large orders");
    await click(buttonByText(dialog(container), "Generate"));

    await publishSuggestion({ text: "Sure!\n```json\n{\"name\": \"Order Approval\", \"description\": \"Routes large orders to approvers.\"}\n```" });

    // Non-autopilot result surfaces an Apply button rather than auto-filling.
    await click(buttonByText(dialog(container), "Apply"));

    expect(inputByLabel(container, "Display name")?.value).toBe("Order Approval");
    expect(textareaByLabel(container, "Description")?.value).toBe("Routes large orders to approvers.");

    await unmount();
  });

  it("auto-fills the create form from an Autopilot suggestion without an Apply step", async () => {
    const { container, unmount, publishSuggestion } = await openCreateDialogWithSuggest();
    await click(buttonByText(dialog(container), "Generate"));

    await publishSuggestion({ autoApply: true, text: "{\"name\": \"Auto Workflow\", \"description\": \"Created by Autopilot.\"}" });

    expect(inputByLabel(container, "Display name")?.value).toBe("Auto Workflow");
    expect(buttonByText(dialog(container), "Apply")).toBeNull();

    await unmount();
  });

  it("selects multiple workflow definition rows and clears selection", async () => {
    const fetchMock = vi.fn(async () => response({
      items: [
        definition(),
        definition({ id: "definition-2", name: "Second workflow" })
      ],
      page: 1,
      pageSize: 10,
      totalCount: 2
    }));
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Second workflow");

    await click(checkboxByLabel(container, "Select workflow definition Hello World"));
    await waitForText(container, "1 selected");

    await click(checkboxByLabel(container, "Select workflow definition Second workflow"));
    await waitForText(container, "2 selected");

    await click(buttonByText(container, "Clear selection"));
    await waitForTextToDisappear(container, "2 selected");

    await click(checkboxByLabel(container, "Select visible workflow definitions"));
    await waitForText(container, "2 selected");

    await unmount();
  });

  it("renders workflow executables and runs an artifact", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST") return response({ workflowExecutionId: "wfexec-published-1" });
      if (url.startsWith("https://server.example/runtime/workflows/instances/wfexec-published-1")) {
        return response(workflowInstanceDetails({
          instance: workflowInstance({ workflowExecutionId: "wfexec-published-1" })
        }));
      }
      if (url.startsWith("https://server.example/design/workflows/versions/version-1")) {
        const version = workflowDefinitionVersionDetails();
        return response({
          ...version,
          state: { ...version.state, inputs: [workflowInput()] }
        });
      }
      if (url.startsWith("https://server.example/design/activities/catalog")) {
        return response({ activities: [activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line"
        })] });
      }
      expect(url).toBe("https://server.example/runtime/workflows/executables?scope=Published");
      return response({ executables: [executable({
        rootActivityType: "Elsa.Activities.Flowchart.Activities.Flowchart",
        sourceKind: "WorkflowDefinitionVersion",
        sourceId: "version-1",
        references: [
          executableReference({ sourceReferenceId: "ref-old", definitionVersionId: "version-0", publishedAt: "2026-06-17T01:00:00Z" }),
          executableReference({ sourceReferenceId: "ref-new", definitionVersionId: "version-1", publishedAt: "2026-06-18T01:00:00Z" })
        ]
      })] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables");

    await waitForText(container, "artifact-1");
    expect(container.textContent).toContain("Executables");
    expect(container.querySelector("nav[aria-label='Workflow views']")).toBeNull();
    expect(container.textContent).toContain("Flowchart");
    expect(container.textContent).toContain("Definition version");
    expect(container.textContent).not.toContain("WorkflowDefinitionVersion / version-1 / 1.0.0");
    expect(container.textContent).not.toContain("Elsa.Activities.Flowchart.Activities.Flowchart");

    await click(buttonByText(container, "Run"));
    await waitForText(container, "Provide the workflow inputs for this run.");
    await click(buttonByText(container, "Cancel"));
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
    await click(buttonByText(container, "Run"));
    await waitForText(container, "Provide the workflow inputs for this run.");
    await fill(inputByLabel(container, "Greeting"), "Hello from published run");
    await click(buttonByText(container, "Run workflow"));
    await waitForText(container, "Open Run wfexec-published-1");
    await click(buttonByText(container, "Open Run wfexec-published-1"));

    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/runtime/workflows/executables/artifact-1/execute",
      expect.objectContaining({ method: "POST" })
    );
    expect(fetchMock.mock.calls.map(([url]) => String(url))).toContain(
      "https://server.example/design/workflows/versions/version-1"
    );
    const executeCall = fetchMock.mock.calls.find(([url, init]) => String(url).endsWith("/executables/artifact-1/execute") && init?.method === "POST");
    expect(JSON.parse(String(executeCall?.[1]?.body))).toEqual({
      inputs: { Greeting: "Hello from published run" },
      sourceReferenceId: "ref-new"
    });
    expect(window.location.pathname).toBe("/workflows/instances/wfexec-published-1");

    await unmount();
  });

  it("opens the source definition from the executables grid", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/runtime/workflows/executables")) return response([executable()]);
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: workflowDraft(), versions: [] });
      return response({ items: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables");

    await waitForText(container, "artifact-1");
    await click(buttonByLabel(container, "Open source definition definition-1"));

    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(window.location.search).toBe("?definition=definition-1");

    await unmount();
  });

  it("disables executable-list runs without a live Published reference", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response({ executables: [executable({ references: [] })] })));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables");

    await waitForText(container, "artifact-1");

    const run = buttonByText(container, "Run") as HTMLButtonElement;
    expect(run.disabled).toBe(true);
    expect(run.title).toContain("No live published reference");

    await unmount();
  });

  it("shows an empty executable state when executable endpoints are unavailable", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL) => response(null, 404));
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables");

    await waitForText(container, "Request failed with 404");

    expect(container.textContent).toContain("Something went wrong");
    expect(fetchMock.mock.calls.map(([url]) => String(url))).toEqual([
      "https://server.example/runtime/workflows/executables?scope=Published"
    ]);

    await unmount();
  });

  it("expands an artifact row into its source references", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response({ executables: [executable({
      references: [
        executableReference(),
        executableReference({
          sourceReferenceId: "ref-test",
          artifactVersion: "1.0.0",
          scope: "TestRun",
          publishedAt: null,
          expiresAt: "2026-01-01T00:00:00Z"
        })
      ]
    })] })));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables");

    await waitForText(container, "artifact-1");
    expect(container.querySelector(".wf-reference-list")).toBeNull();

    await click(buttonByLabel(container, "Show references of artifact-1"));

    const referenceList = container.querySelector(".wf-reference-list");
    expect(referenceList?.textContent).toContain("Version 2.0.0");
    expect(referenceList?.textContent).toContain("Published");
    expect(referenceList?.textContent).toContain("Test run");
    expect(referenceList?.textContent).toContain("Expired");
    expect(referenceList?.textContent).toContain("Expires");

    await unmount();
  });

  it("requests the reference scope filter and retired references", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL) => response({ executables: [executable({ deletedAt: "2026-06-19T01:00:00Z" })] }));
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables");

    await waitForText(container, "artifact-1");
    expect(String(fetchMock.mock.calls[0]?.[0])).toBe("https://server.example/runtime/workflows/executables?scope=Published");

    await select(selectByLabel(container, "Executable reference scope"), "test-runs");
    await waitForText(container, "artifact-1");
    expect(String(fetchMock.mock.calls.at(-1)?.[0])).toContain("/executables?scope=TestRuns");

    await click(checkboxByLabel(container, "Include retired references"));
    await waitForText(container, "Retired");
    expect(String(fetchMock.mock.calls.at(-1)?.[0])).toContain("/executables?scope=TestRuns&includeRetired=true");

    // Runtime artifacts are read-only; lifecycle actions belong to Publishing slots.
    expect(container.textContent).toContain("Retained for inspection");
    expect(buttonByLabel(container, "Delete executable artifact-1")).toBeNull();

    await unmount();
  });

  it("opens the Executable Inspector from an executables row", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response({ executables: [executable()] })));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables");

    await waitForText(container, "artifact-1");
    await click(buttonByLabel(container, "Inspect executable artifact-1"));

    expect(window.location.pathname).toBe("/workflows/executables/artifact-1");

    await unmount();
  });

  it("keeps the Runtime executable table read-only", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => response({ executables: [executable()] }));
    vi.stubGlobal("fetch", fetchMock);
    const { api, container, unmount } = await renderRegisteredRoute("/workflows/executables");

    await waitForText(container, "artifact-1");
    expect(buttonByLabel(container, "Delete executable artifact-1")).toBeNull();
    expect(api.dialogs.confirm).not.toHaveBeenCalled();
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === "DELETE")).toBe(false);

    await unmount();
  });

  it("surfaces the structured 409 reference-gate reason when a run is rejected", async () => {
    vi.stubGlobal("fetch", vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === "POST") {
        return response({ error: "Workflow executable artifact 'artifact-1' has no live Published reference: the matching reference has expired." }, 409);
      }
      return response({ executables: [executable({ references: [executableReference()] })] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables");

    await waitForText(container, "artifact-1");
    await click(buttonByText(container, "Run"));
    await waitForText(container, "has no live Published reference");

    expect(container.textContent).toContain("the matching reference has expired");
    expect(container.textContent).toContain("Start a new test run from the definition editor");

    await unmount();
  });

  it("renders the Executable Inspector with sidecar layout, ghost nodes and reference selection", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/executables/artifact-1")) {
        return url.includes("ref=ref-0")
          ? response(executableDetail({ chosenReference: { sourceReferenceId: "ref-0", selection: "requested", layout: [] } }))
          : response(executableDetail());
      }
      if (url.includes("/activities")) return response({ activities: [
        activity({ activityVersionId: "sequence-v1", activityTypeKey: "Elsa.Activities.Sequence.Activities.Sequence", displayName: "Sequence" }),
        activity({ activityVersionId: "write-line-v1", activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine", displayName: "Write Line" })
      ] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition({ latestVersionId: "version-2", latestVersion: "2.0.0" }), draft: null, versions: [] });
      return response(null, 404);
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables/artifact-1");

    await waitForText(container, "Showing newest live reference ref-1");

    // Identity panel: artifact id, hash, node/resume-target counts.
    expect(container.textContent).toContain("sha256:abc");
    expect(container.textContent).toContain("Resume targets");
    expect(container.textContent).toContain("Executable Inspector");

    // Canvas: catalog hit renders its display name; the catalog miss is an honest ghost.
    await waitForText(container, "Write Line");
    await waitForText(container, "Not available in this environment");
    const ghost = container.querySelector(".wf-node-ghost");
    expect(ghost?.textContent).toContain("SendEmail");

    // The reference is current with the definition's latest version: no drift caption, source link enabled.
    expect(container.textContent).not.toContain("the definition's latest is");
    const openSource = buttonByText(container, "Open source definition") as HTMLButtonElement;
    expect(openSource.disabled).toBe(false);

    // Selecting another reference updates the routed selection. Runtime remains the executable source.
    await click(buttonByText(container, "References (2)"));
    await click(buttonByLabel(container, "Show reference ref-0"));
    await waitForText(container, "Showing requested reference ref-0");

    expect(new URLSearchParams(window.location.search).get("ref")).toBe("ref-0");
    expect(fetchMock.mock.calls.map(([url]) => String(url))).toContain("https://server.example/runtime/workflows/executables/artifact-1");

    await unmount();
  });

  it("collects workflow inputs before running from the Executable Inspector", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST" && url.endsWith("/executables/artifact-1/execute")) {
        return response({ workflowExecutionId: "wfexec-inspector-1" });
      }
      if (url.includes("/design/workflows/versions/version-2")) {
        const version = workflowDefinitionVersionDetails({ id: "version-2", version: "2.0.0" });
        return response({ ...version, state: { ...version.state, inputs: [workflowInput()] } });
      }
      if (url.includes("/executables/artifact-1")) return response(executableDetail({
        chosenReference: { sourceReferenceId: "ref-test", selection: "requested", layout: [] },
        references: [
          executableReference(),
          executableReference({
            sourceReferenceId: "ref-test",
            scope: "TestRun",
            definitionVersionId: "draft:snapshot-1",
            artifactVersion: "draft",
            publishedAt: null
          })
        ]
      }));
      if (url.includes("/activities")) return response({ activities: [] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: null, versions: [] });
      return response(null, 404);
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables/artifact-1");

    await waitForText(container, "Executable Inspector");
    await click(buttonByText(container, "Run"));
    await waitForText(container, "Provide the workflow inputs for this run.");
    await fill(inputByLabel(container, "Greeting"), "Hello from inspector");
    await click(buttonByText(container, "Run workflow"));
    await waitForText(container, "Open Run wfexec-inspector-1");

    const executeCall = fetchMock.mock.calls.find(([url, init]) => String(url).endsWith("/executables/artifact-1/execute") && init?.method === "POST");
    expect(JSON.parse(String(executeCall?.[1]?.body))).toEqual({
      inputs: { Greeting: "Hello from inspector" },
      sourceReferenceId: "ref-1"
    });
    expect(fetchMock.mock.calls.map(([url]) => String(url))).toContain(
      "https://server.example/design/workflows/versions/version-2"
    );
    expect(fetchMock.mock.calls.map(([url]) => String(url))).not.toContain(
      "https://server.example/design/workflows/versions/draft%3Asnapshot-1"
    );

    await unmount();
  });

  it("disables inspector execution when no live Published reference exists", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/executables/artifact-1")) return response(executableDetail({
        chosenReference: { sourceReferenceId: "ref-test", selection: "requested", layout: [] },
        references: [
          executableReference({ sourceReferenceId: "ref-retired", deletedAt: "2026-06-19T01:00:00Z" }),
          executableReference({ sourceReferenceId: "ref-test", scope: "TestRun", publishedAt: null })
        ]
      }));
      if (url.includes("/activities")) return response({ activities: [] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: null, versions: [] });
      return response(null, 404);
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables/artifact-1?ref=ref-test");

    await waitForText(container, "Executable Inspector");

    const run = buttonByText(container, "Run") as HTMLButtonElement;
    expect(run.disabled).toBe(true);
    expect(run.title).toContain("No live published reference");

    await unmount();
  });

  it("never dispatches a published run when its selected source version inputs cannot be loaded", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const scenarios = [
      { surface: "list", route: "/workflows/executables", artifactId: "artifact-1", versionId: "version-1" },
      { surface: "artifacts", route: "/workflows/definitions?definition=definition-1", artifactId: "artifact-current", versionId: "version-1" },
      { surface: "inspector", route: "/workflows/executables/artifact-1", artifactId: "artifact-1", versionId: "version-2" }
    ] as const;

    for (const scenario of scenarios) {
      const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = String(input);
        if (url.includes(`/design/workflows/versions/${scenario.versionId}`)) {
          return response({ error: "Workflow input definitions unavailable." }, 503);
        }
        if (scenario.surface === "inspector" && url.includes("/runtime/workflows/executables/artifact-1")) {
          return response(executableDetail());
        }
        if (url.includes("/publishing/workflows/definition-1/slots")) {
          return response({ items: [publicationSlot("artifact-current")] });
        }
        if (url.includes("/runtime/workflows/executables")) {
          return response([executable({
            artifactId: scenario.artifactId,
            references: [executableReference({
              artifactId: scenario.artifactId,
              definitionVersionId: scenario.versionId
            })]
          })]);
        }
        if (url.includes("/activities")) return response({ activities: [] });
        if (url.includes("/definitions/definition-1")) {
          return response({ definition: definition(), draft: workflowDraft(), versions: [] });
        }
        if (init?.method === "POST") return response({ workflowExecutionId: "must-not-dispatch" });
        return response({ items: [definition()] });
      });
      vi.stubGlobal("fetch", fetchMock);
      const { container, unmount } = await renderRegisteredRoute(scenario.route);

      try {
        if (scenario.surface === "artifacts") {
          await waitForText(container, "Autosave");
          await click(buttonByText(container, "Artifacts"));
        }
        await waitForText(container, scenario.surface === "inspector" ? "Executable Inspector" : scenario.artifactId);
        await click(scenario.surface === "artifacts"
          ? buttonByLabel(container, `Run executable ${scenario.artifactId}`)
          : buttonByText(container, "Run"));
        await waitForText(container, "Workflow input definitions unavailable.");

        expect(fetchMock.mock.calls.map(([url]) => String(url))).toContain(
          `https://server.example/design/workflows/versions/${scenario.versionId}`
        );
        expect(fetchMock.mock.calls.some(([url, init]) =>
          init?.method === "POST" && String(url).endsWith(`/executables/${scenario.artifactId}/execute`)
        )).toBe(false);
      } finally {
        await unmount();
      }
    }
  });

  it("captions source drift when the inspected reference is behind the definition's latest version", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/executables/artifact-1")) return response(executableDetail({
        references: [
          executableReference(),
          executableReference({
            sourceReferenceId: "stale-test-run-ref",
            definitionVersionId: "draft:draft-1-stalehash",
            publishedAt: null,
            scope: "TestRun"
          })
        ]
      }));
      if (url.includes("/activities")) return response({ activities: [] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition({ latestVersionId: "version-9", latestVersion: "9.0.0" }),
        draft: workflowDraft(),
        versions: []
      });
      return response(null, 404);
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables/artifact-1");

    await waitForText(container, "the definition's latest is 9.0.0");

    expect(container.textContent).toContain("published from version 2.0.0");
    expect((buttonByText(container, "Open source definition") as HTMLButtonElement).disabled).toBe(false);

    await unmount();
  });

  it("replaces source drift with draft equivalence when the current draft test run produced the inspected artifact", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const draft = workflowDraft();
    const currentDraftVersionId = `draft:${createDraftSnapshotId(draft)}`;
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/executables/artifact-1")) return response(executableDetail({
        references: [
          executableReference(),
          executableReference({
            sourceReferenceId: "test-run-ref",
            definitionVersionId: currentDraftVersionId,
            publishedAt: null,
            scope: "TestRun"
          })
        ]
      }));
      if (url.includes("/activities")) return response({ activities: [] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition({ latestVersionId: "version-9", latestVersion: "9.0.0" }),
        draft,
        versions: []
      });
      return response(null, 404);
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables/artifact-1");

    await waitForText(container, "Current draft is behaviorally identical to this artifact");

    expect(container.querySelector(".wf-executable-equivalent")?.textContent)
      .toContain("Current draft is behaviorally identical to this artifact");
    expect(container.textContent).not.toContain("the definition's latest is 9.0.0");

    await unmount();
  });

  it("disables the source link with a reason when the definition is absent in this environment", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/executables/artifact-1")) return response(executableDetail());
      if (url.includes("/activities")) return response({ activities: [] });
      return response(null, 404);
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables/artifact-1");

    await waitForText(container, "This workflow definition is not available in this environment.");

    const openSource = buttonByText(container, "Open source definition") as HTMLButtonElement;
    expect(openSource.disabled).toBe(true);
    expect(openSource.title).toContain("not available in this environment");

    await unmount();
  });

  it("renders activity availability rows with compact types and helpful descriptions", async () => {
    stubActivityAvailabilityFetch({
      items: [
        availabilityDiagnosticEntry({ displayName: "Activity" }),
        availabilityDiagnosticEntry({
          activityDefinitionId: "send-email-v1",
          activityTypeKey: "Elsa.Activities.Email.SendEmail",
          displayName: "Send Email",
          description: null,
          category: "Messaging",
          state: "BlockedByHostBaseline",
          layer: "HostBaseline",
          referenceName: "Elsa.Activities.Email.SendEmail",
          reason: "Disabled by the host baseline."
        })
      ]
    });

    const { container, unmount } = await renderRegisteredRoute("/workflows/activity-availability");

    await waitForText(container, "Run JavaScript");
    await waitForText(container, "Disabled by the host baseline.");

    const list = container.querySelector(".availability-activity-list");
    expect(list?.textContent).toContain("RunJavaScript.Activity");
    expect(list?.textContent).toContain("Runs JavaScript code inside a workflow.");
    expect(list?.textContent).not.toContain(runJavaScriptTypeKey);
    expect(list?.textContent).not.toContain("Catalog activity is available.");

    // Rows are grouped under category headers instead of repeating a category badge per row.
    const groupTitles = Array.from(container.querySelectorAll(".availability-group-title")).map(title => title.textContent);
    expect(groupTitles).toEqual(["Messaging1", "Scripting1"]);

    // Switches express effective availability: in AllExcept mode an unlisted activity is ON,
    // and a host-blocked activity is forced off and not editable.
    const runJavaScriptSwitch = checkboxByLabel(container, "Run JavaScript available in new workflows");
    const sendEmailSwitch = checkboxByLabel(container, "Send Email available in new workflows");
    expect(runJavaScriptSwitch?.checked).toBe(true);
    expect(sendEmailSwitch?.checked).toBe(false);
    expect(sendEmailSwitch?.disabled).toBe(true);

    // The "Hidden only" quick filter narrows the list to activities that are not effectively available.
    await click(buttonByText(container, "Hidden only"));
    expect(list?.textContent).toContain("Send Email");
    expect(list?.textContent).not.toContain("Run JavaScript");
    await click(buttonByText(container, "Hidden only"));
    expect(list?.textContent).toContain("Run JavaScript");

    await unmount();
  });

  it("saves mode-aware availability toggles and inspects activity metadata in the details panel", async () => {
    let savedBody: { mode: number; rules: { activityTypes: string[]; sets: string[] } } | null = null;
    stubActivityAvailabilityFetch({
      items: [availabilityDiagnosticEntry()],
      sets: [{ name: "Scripting Tools", activityTypeKeys: [runJavaScriptTypeKey] }],
      activities: [{
        activityVersionId: "run-javascript-v1",
        activityTypeKey: runJavaScriptTypeKey,
        version: "1.0.0",
        category: "Scripting",
        displayName: "Run JavaScript",
        description: "Runs JavaScript code inside a workflow.",
        executionType: "Task",
        inputs: [{ name: "script", displayName: "Script", type: { typeName: "String" }, description: "The code to run." }],
        outputs: [],
        designFacets: []
      }],
      onSave: body => { savedBody = body; }
    });

    const { container, unmount } = await renderRegisteredRoute("/workflows/activity-availability");
    await waitForText(container, "Run JavaScript");

    // Clicking the row opens the details inspector with catalog metadata and the raw payload.
    await click(container.querySelector("button[title='Show activity details']"));
    const details = container.querySelector(".availability-details");
    expect(details?.textContent).toContain(runJavaScriptTypeKey);
    expect(details?.textContent).toContain("1.0.0");
    expect(details?.textContent).toContain("Script");
    expect(details?.textContent).toContain("Raw metadata");

    // Excluding a set locks its members' switches: an individual rule can't override the set rule.
    const setSwitch = checkboxByLabel(container, "Activities in the Scripting Tools set available in new workflows");
    expect(setSwitch?.checked).toBe(true);
    await click(setSwitch);
    const lockedSwitch = checkboxByLabel(container, "Run JavaScript available in new workflows");
    expect(lockedSwitch?.checked).toBe(false);
    expect(lockedSwitch?.disabled).toBe(true);
    expect(lockedSwitch?.title).toBe('Controlled by the "Scripting Tools" set rule');
    await click(checkboxByLabel(container, "Activities in the Scripting Tools set available in new workflows"));

    // Turning the switch off marks the draft dirty; saving stores the AllExcept exclusion list.
    const saveButton = buttonByText(container, "Save");
    expect(saveButton?.disabled).toBe(true);
    await click(checkboxByLabel(container, "Run JavaScript available in new workflows"));
    expect(container.textContent).toContain("Unsaved changes");
    await click(buttonByText(container, "Save"));
    await waitForText(container, "Activity availability saved.");
    expect(savedBody).toEqual({ scope: "host-default", mode: 0, rules: { activityTypes: [runJavaScriptTypeKey], sets: [] } });
    expect(container.textContent).not.toContain("Unsaved changes");

    await unmount();
  });

  it("copies executable identifiers from the grid", async () => {
    const writeText = vi.fn(async () => undefined);
    const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, "clipboard");
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    vi.stubGlobal("fetch", vi.fn(async () => response([executable()])));

    const { container, unmount } = await renderRegisteredRoute("/workflows/executables");

    try {
      await waitForText(container, "artifact-1");
      await click(buttonByLabel(container, "Copy artifact ID artifact-1"));

      expect(writeText).toHaveBeenCalledWith("artifact-1");
      expect(container.textContent).toContain("Copied artifact ID");
    } finally {
      await unmount();
      if (clipboardDescriptor) Object.defineProperty(navigator, "clipboard", clipboardDescriptor);
      else Reflect.deleteProperty(navigator, "clipboard");
    }
  });

  it("dispatches executable Explain actions to Weaver", async () => {
    const fetchMock = vi.fn(async () => response([executable()]));
    const dispatchPrompt = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables", api => {
      api.ai.promptActions.add({
        id: "weaver.workflows.explain-executable",
        label: "Explain executable",
        placement: "selection",
        contextKind: "workflow-executable",
        createPrompt: context => ({
          message: "Explain this workflow executable.",
          mode: "enqueue",
          attachments: [{ kind: "workflow-executable", referenceId: (context as { artifactId: string }).artifactId }]
        })
      });
      api.ai.dispatchPrompt = dispatchPrompt;
    });

    await waitForText(container, "artifact-1");
    await click(buttonByText(container, "Explain"));

    expect(dispatchPrompt).toHaveBeenCalledWith(expect.objectContaining({
      message: "Explain this workflow executable.",
      attachments: [expect.objectContaining({ kind: "workflow-executable", referenceId: "artifact-1" })]
    }));
    expect(container.textContent).toContain("Sent artifact-1 to Weaver");

    await unmount();
  });

  it("sends the current designer draft snapshot to the transient test-run endpoint", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST" && urlPath(url) === "/publishing/workflows/drafts/test-runs") return response(testRunView());
      if (init?.method === "POST" && urlPath(url) === "/_elsa/publishing/workflows/drafts/test-runs") return response(null, 404);
      if (init?.method === "POST" && url.includes("/drafts/draft-1/promote")) throw new Error("Designer Run must not promote drafts.");
      if (init?.method === "POST" && url.includes("/versions/")) throw new Error("Designer Run must not publish versions.");
      if (init?.method === "POST" && url.includes("/executables/")) throw new Error("Designer Run must not call durable executable run.");
      if (url.includes("/activities")) return response({ activities: [
        activity({ activityVersionId: "flowchart-v1", activityTypeKey: "Elsa.Activities.Flowchart", category: "Composition", displayName: "Flowchart" }),
        activity({ activityVersionId: "write-line-v1", activityTypeKey: "Elsa.Activities.WriteLine", category: "Primitives", displayName: "Write Line" })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: draftWithFlowchartRoot([], [workflowInput()]),
        versions: []
      });
      return response({ items: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Add activity");
    await click(buttonByText(container, "Add activity"));
    await click(optionByText(container, "Write Line"));
    await click(buttonByText(container, "Write Line"));
    await click(buttonByText(container, "Run"));
    await waitForText(container, "Provide the workflow inputs for this run.");
    expect(fetchMock.mock.calls.some(([url, init]) => init?.method === "POST" && urlPath(String(url)) === "/publishing/workflows/drafts/test-runs")).toBe(false);
    await fill(inputByLabel(container, "Greeting"), "Hello from test run");
    await click(buttonByText(container, "Run workflow"));
    await waitForText(container, "Test run dispatched");

    const calls = fetchMock.mock.calls.map(([url, init]) => ({ url: String(url), method: init?.method ?? "GET", body: String(init?.body ?? "") }));
    const testRunCall = calls.find(call => call.method === "POST" && urlPath(call.url) === "/publishing/workflows/drafts/test-runs");
    expect(testRunCall).toBeTruthy();
    const requestBody = JSON.parse(testRunCall?.body ?? "{}");
    expect(requestBody.definitionId).toBe("definition-1");
    expect(requestBody.snapshotId).toMatch(/^draft-1-[0-9a-f]{8}$/);
    expect(requestBody.inputs).toEqual({ Greeting: "Hello from test run" });
    expect(requestBody.state.rootActivity).toMatchObject({
      nodeId: "root",
      activityVersionId: "flowchart-v1",
      structure: {
        kind: "elsa.flowchart.structure",
        payload: {
          activities: [
            expect.objectContaining({ activityVersionId: "write-line-v1" }),
            expect.objectContaining({ activityVersionId: "write-line-v1" })
          ]
        }
      }
    });
    expect(requestBody.state.rootActivity.structure.payload.startNodeId)
      .toBe(requestBody.state.rootActivity.structure.payload.activities[0].nodeId);
    expect(calls.some(call => call.method === "POST" && urlPath(call.url) === "/_elsa/publishing/workflows/drafts/test-runs")).toBe(false);
    expect(calls.some(call => call.url.includes("/drafts/draft-1/promote") && call.method === "POST")).toBe(false);
    expect(calls.some(call => call.url.includes("/versions/") && call.method === "POST")).toBe(false);
    expect(calls.some(call => call.url.includes("/executables/") && call.method === "POST")).toBe(false);
    expect(container.querySelector(".wf-test-run-capsule")).toBeNull();
    expect(container.querySelector(".wf-test-run-popover")).toBeNull();
    expect(container.querySelector("[aria-label='Inspector panel tabs'] [role='tab'][aria-selected='true']")?.textContent).toContain("Runtime");
    await click(buttonByText(container, "Test run dispatched"));
    await waitForText(container, "test-run-1");
    expect(container.textContent).toContain("Runtime");
    expect(container.textContent).toContain("Ephemeral - not saved, promoted, or published.");
    expect(container.textContent).toContain("artifact-transient-1");
    expect(container.textContent).toContain("wfexec-1");
    expect(container.textContent).toContain("1 activity");
    expect(container.textContent).toContain("0 incidents");

    await unmount();
  });

  it("hides stale designer test-run metadata after the draft changes", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST" && urlPath(url) === "/publishing/workflows/drafts/test-runs") return response(testRunView());
      if (init?.method === "POST" && urlPath(url) === "/_elsa/publishing/workflows/drafts/test-runs") return response(null, 404);
      if (url.includes("/design/activities/catalog")) return response({ activities: [catalogActivity(writeLineDescriptor())] });
      if (url.includes("/expressions/descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal", editingMode: "literal" },
        { type: "JavaScript", displayName: "JavaScript", editingMode: "text" }
      ] });
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line"
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: workflowDraft({
          state: {
            variables: [],
            rootActivity: {
              nodeId: "write-line-root",
              activityVersionId: "write-line-v1",
              inputs: [],
              outputs: [],
              structure: null
            },
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ items: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Run");
    await click(buttonByText(container, "Run"));
    await waitForText(container, "Test run dispatched");
    expect(container.querySelector("[aria-label='Inspector panel tabs'] [role='tab'][aria-selected='true']")?.textContent).toContain("Runtime");
    await waitForText(container, "test-run-1");
    expect(container.textContent).toContain("artifact-transient-1");
    expect(container.textContent).toContain("wfexec-1");

    await click(buttonByText(container, "Inspector"));
    await click(container.querySelector(".wf-canvas .react-flow__node"));
    await waitForText(container, "Text");
    await fill(container.querySelector<HTMLInputElement>(".wf-property-row input[type='text']"), "Stale capsule clearing");

    await waitForTextToDisappear(container, "test-run-1");
    expect(container.textContent).not.toContain("artifact-transient-1");
    expect(container.textContent).not.toContain("wfexec-1");

    await unmount();
  });

  it("shows compile rejection reasons for designer test runs", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST" && urlPath(url) === "/publishing/workflows/drafts/test-runs") return response(testRunView({
        artifactId: null,
        workflowExecutionId: null,
        status: "Rejected",
        commandDispatchStatus: null,
        reason: "Workflow version has no root activity to publish.",
        expiresAt: null
      }), 400);
      if (init?.method === "POST" && urlPath(url) === "/_elsa/publishing/workflows/drafts/test-runs") return response(null, 404);
      if (init?.method === "POST" && url.includes("/drafts/draft-1/promote")) throw new Error("Designer Run must not promote drafts.");
      if (url.includes("/activities")) return response({ activities: [
        activity({ activityVersionId: "flowchart-v1", activityTypeKey: "Elsa.Activities.Flowchart", category: "Composition", displayName: "Flowchart" })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: draftWithFlowchartRoot(),
        versions: []
      });
      return response({ items: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Run");
    await click(buttonByText(container, "Run"));
    await waitForText(container, "Test run rejected");
    expect(container.querySelector("[aria-label='Inspector panel tabs'] [role='tab'][aria-selected='true']")?.textContent).toContain("Runtime");
    await waitForText(container, "Workflow version has no root activity to publish.");

    expect(container.textContent).toContain("Test run rejected");
    expect(container.textContent).toContain("Ephemeral - not saved, promoted, or published.");
    expect(fetchMock.mock.calls.some(([url, init]) => init?.method === "POST" && String(url).includes("/drafts/draft-1/promote"))).toBe(false);

    await unmount();
  });

  it("filters workflow executables by definition query parameter", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => response([
      executable({ artifactId: "artifact-visible", definitionId: "definition-1" }),
      executable({ artifactId: "artifact-hidden", definitionId: "definition-2", definitionVersionId: "version-2", sourceId: "definition-2" })
    ])));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables?definition=definition-1");

    await waitForText(container, "artifact-visible");
    const filterInput = inputByLabel(container, "Filter executables by workflow definition");
    expect(filterInput?.value).toBe("definition-1");
    expect(container.textContent).not.toContain("artifact-hidden");

    await fill(filterInput, "version-2");
    await waitForText(container, "artifact-hidden");
    expect(window.location.search).toBe("?definition=version-2");
    expect(container.textContent).not.toContain("artifact-visible");

    await fill(inputByLabel(container, "Filter executables by workflow definition"), "");
    await waitForText(container, "artifact-visible");
    expect(window.location.search).toBe("");

    await unmount();
  });

  it("navigates from workflow runs to the compatible instance detail route", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.startsWith("https://server.example/runtime/workflows/instances")) {
        if (url.includes("runKind=TestRun")) {
          return response([workflowInstance({
            workflowExecutionId: "wfexec-test",
            artifactId: "artifact-test",
            runKind: "TestRun",
            activityCount: 2,
            incidentCount: 1
          })]);
        }
        return response([workflowInstance({ runKind: "PublishedRun" })]);
      }

      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/instances");

    await waitForText(container, "wfexec-1");
    expect(container.textContent).toContain("Runs");
    expect(container.textContent).toContain("Published Run");
    expect(container.querySelector("nav[aria-label='Workflow views']")).toBeNull();
    await select(selectByLabel(container, "Run Kind"), "TestRun");
    await click(buttonByText(container, "Apply filters"));
    await waitForText(container, "wfexec-test");
    expect(container.textContent).toContain("Test Run");
    expect(container.textContent).toContain("2 activities");
    expect(container.textContent).toContain("1 incidents");
    await click(rowByLabel(container, "Inspect workflow run wfexec-test"));

    expect(window.location.pathname).toBe("/workflows/instances/wfexec-test");
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/runtime/workflows/instances?take=25",
      expect.any(Object)
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/runtime/workflows/instances?runKind=TestRun&take=25",
      expect.any(Object)
    );

    await unmount();
  });

  it("renders direct workflow instance details from the pinned Runtime executable", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const { bookmarkIds: _bookmarkIds, incidentIds: _incidentIds, ...activityWithoutCollectionCounts } = activityExecution();
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.startsWith("https://server.example/runtime/workflows/instances/wfexec-1")) {
        return response(workflowInstanceDetails({
          instance: workflowInstance({ sourceReferenceId: "reference-pinned" }),
          activities: [activityWithoutCollectionCounts]
        }));
      }

      if (url.startsWith("https://server.example/runtime/workflows/executables/artifact-1")) {
        return response(executableDetail());
      }

      if (url.startsWith("https://server.example/design/activities/catalog")) {
        return response({ activities: [activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line"
        })] });
      }

      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/instances/wfexec-1");

    await waitForText(container, "Pinned Runtime executable");
    expect(container.textContent).toContain("Run");
    expect(container.textContent).toContain("Workflow Instance ID");
    await click(buttonByText(container, "Designer"));
    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");
    window.history.replaceState({}, "", "/workflows/instances/wfexec-1");
    // Timeline is the default instance tab and lists executed activities.
    expect(container.textContent).toContain("Timeline");
    expect(container.textContent).toContain("WriteLine");
    expect(buttonByLabel(container, "Collapse run details panel")).toBeTruthy();
    expect(buttonByLabel(container, "Maximize run details panel")).toBeTruthy();
    expect(container.querySelector("[aria-label='Resize run details panel']")).toBeTruthy();
    await click(buttonByLabel(container, "Collapse run details panel"));
    expect(buttonByLabel(container, "Expand run details panel")).toBeTruthy();
    expect(container.textContent).not.toContain("WriteLine");
    await click(buttonByLabel(container, "Expand run details panel"));
    expect(container.textContent).toContain("WriteLine");
    await click(container.querySelector(".wf-timeline-entry"));
    expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true");
    expect(container.textContent).toContain("Activity Execution ID");
    expect(container.textContent).toContain("activity-execution-1");
    expect(container.textContent).toContain("Bookmarks0");
    expect(container.textContent).toContain("Incidents0");
    await click(buttonByLabel(container, "Maximize run details panel"));
    expect(buttonByLabel(container, "Restore run details panel")).toBeTruthy();
    await click(buttonByLabel(container, "Restore run details panel"));
    // Run metadata moved to the Details tab.
    await click(buttonByText(container, "Details"));
    expect(container.textContent).toContain("Published Run");
    // Incidents live under the Issues tab.
    await click(buttonByText(container, "Issues"));
    expect(container.textContent).toContain("No incidents recorded.");
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/runtime/workflows/instances/wfexec-1",
      expect.any(Object)
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/runtime/workflows/executables/artifact-1?ref=reference-pinned",
      expect.any(Object)
    );

    await unmount();
  });

  it("uses the host router from every run tab and preserves browser history", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/runtime/workflows/instances/wfexec-1")) return response(workflowInstanceDetails());
      if (url.includes("/runtime/workflows/executables/artifact-1")) return response(executableDetail());
      if (url.includes("/design/workflows/definitions/definition-1")) {
        return response({ definition: definition(), draft: workflowDraft(), versions: [] });
      }
      if (url.includes("/design/activities/catalog")) return response({ activities: [] });
      return response(null, 404);
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/instances/wfexec-1", undefined, true);

    await waitForText(container, "Pinned Runtime executable");
    await click(buttonByText(container, "Designer"));
    await waitForText(container, "Hello World");

    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");
    expect(container.querySelector(".wf-editor")).toBeTruthy();
    expect(container.querySelector(".wf-instance-detail-workbench")).toBeNull();

    window.history.back();
    await waitForText(container, "Pinned Runtime executable");
    expect(window.location.pathname).toBe("/workflows/instances/wfexec-1");
    expect(container.querySelector(".wf-instance-detail-workbench")).toBeTruthy();

    window.history.forward();
    await waitForText(container, "Hello World");
    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(container.querySelector(".wf-editor")).toBeTruthy();

    window.history.back();
    await waitForText(container, "Pinned Runtime executable");

    const openTab = async (tab: "Activity" | "Issues" | "Details") => {
      if (tab === "Activity") {
        await click(container.querySelector(".wf-timeline-entry"));
      } else {
        await click(buttonByText(container, tab));
      }
      expect(container.querySelector(`[data-tab-id='${tab.toLowerCase()}']`)?.getAttribute("aria-selected")).toBe("true");
    };

    for (const tab of ["Activity", "Issues", "Details"] as const) {
      await openTab(tab);
      await click(buttonByText(container, "Designer"));
      await waitForText(container, "Hello World");
      expect(window.location.pathname).toBe("/workflows/definitions");
      expect(container.querySelector(".wf-editor")).toBeTruthy();
      expect(container.querySelector(".wf-instance-detail-workbench")).toBeNull();

      window.history.back();
      await waitForText(container, "Pinned Runtime executable");
    }

    await unmount();
  });

  it("renders workflow instance canvas from Runtime executable structure", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const writeLineNode = {
      nodeId: "nested-write-line",
      activityVersionId: "write-line-v1",
      inputs: [],
      outputs: [],
      structure: null
    };
    const forEachNode = {
      nodeId: "foreach-1",
      activityVersionId: "foreach-v1",
      inputs: [],
      outputs: [],
      structure: {
        kind: "elsa.foreach.structure",
        schemaVersion: "1.0.0",
        payload: { body: writeLineNode }
      }
    };
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.startsWith("https://server.example/runtime/workflows/instances/wfexec-1")) {
        return response(workflowInstanceDetails({
          instance: workflowInstance({ status: "Failed", activityCount: 2 }),
          activities: [
            activityExecution({
              activityExecutionId: "foreach-execution",
              executableNodeId: "foreach-1",
              authoredActivityId: "foreach-1",
              activityType: "Elsa.Activities.ControlFlow.Activities.ForEach",
              status: "Running",
              completedAt: null
            }),
            activityExecution({
              activityExecutionId: "nested-write-execution",
              executableNodeId: "nested-write-line",
              authoredActivityId: "nested-write-line",
              activityType: "Elsa.Activities.Primitives.Activities.WriteLine",
              status: "Failed",
              subStatus: "Faulted"
            })
          ]
        }));
      }

      if (url.startsWith("https://server.example/runtime/workflows/executables/artifact-1")) {
        return response(executableDetail({
          definition: {
            id: "definition-1",
            name: "Loops",
            description: "Loops over values.",
            createdAt: "2026-06-18T01:00:00Z",
            lastModifiedAt: "2026-06-18T01:10:00Z"
          },
          state: {
            variables: [],
            rootActivity: flowchartRoot([forEachNode]),
            inputs: [],
            outputs: []
          },
          layout: [
            { nodeId: "foreach-1", x: 180, y: 120 },
            { nodeId: "nested-write-line", x: 200, y: 140 }
          ]
        }));
      }

      if (url.startsWith("https://server.example/design/activities/catalog")) {
        return response({ activities: [
          activity({
            activityVersionId: "flowchart-v1",
            activityTypeKey: "Elsa.Activities.Flowchart.Activities.Flowchart",
            category: "Flowchart",
            displayName: "Flowchart"
          }),
          activity({
            activityVersionId: "foreach-v1",
            activityTypeKey: "Elsa.Activities.ControlFlow.Activities.ForEach",
            category: "Control Flow",
            displayName: "For Each",
            designFacets: [{
              kind: "elsa.foreach.structure",
              schemaVersion: "1.0.0",
              payload: {
                mode: "sequence",
                supportsScopedVariables: false,
                slots: [{ name: "Body", property: "body", displayName: "Body", cardinality: "single" }],
                initialPayload: { body: null }
              }
            }]
          }),
          activity({
            activityVersionId: "write-line-v1",
            activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
            category: "Primitives",
            displayName: "Write Line"
          })
        ] });
      }

      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/instances/wfexec-1");

    await waitForText(container, "Pinned Runtime executable");
    const canvasText = () => container.querySelector(".wf-instance-canvas")?.textContent ?? "";
    expect(canvasText()).toContain("Elsa.Activities.Sequence.Activities.Sequence");
    expect(fetchMock.mock.calls.map(([url]) => String(url))).toContain("https://server.example/runtime/workflows/executables/artifact-1");

    await unmount();
  });

  it("copies incidents and hides normal root runtime evidence from the issues tab", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const writeText = vi.fn(async () => undefined);
    const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, "clipboard");
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.startsWith("https://server.example/runtime/workflows/instances/wfexec-1")) {
        return response(workflowInstanceDetails({
          instance: workflowInstance({ status: "Failed", incidentCount: 1 }),
          activities: [
            activityExecution({
              activityExecutionId: "root-execution",
              executableNodeId: "root",
              authoredActivityId: "root",
              activityType: "Elsa.Activities.Flowchart.Activities.Flowchart",
              status: "Faulted",
              subStatus: "InputMaterializationFailed",
              incidentIds: ["incident-1"],
              faultCount: 1
            }),
            activityExecution()
          ],
          incidents: [incidentState({
            incidentId: "incident-1",
            activityExecutionId: "root-execution",
            executableNodeId: null,
            failureType: "InputMaterializationFailed",
            message: "Input 'Collection' failed to evaluate its JavaScript expression."
          })]
        }));
      }

      if (url.startsWith("https://server.example/runtime/workflows/executables/artifact-1")) {
        return response(executableDetail());
      }

      if (url.startsWith("https://server.example/design/activities/catalog")) {
        return response({ activities: [
          activity({
            activityVersionId: "flowchart-v1",
            activityTypeKey: "Elsa.Activities.Flowchart.Activities.Flowchart",
            category: "Flowchart",
            displayName: "Flowchart"
          }),
          activity({
            activityVersionId: "write-line-v1",
            activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
            category: "Primitives",
            displayName: "Write Line"
          })
        ] });
      }

      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/instances/wfexec-1");

    try {
      await waitForText(container, "Pinned Runtime executable");
      await click(buttonByText(container, "Issues (1)"));

      expect(container.textContent).toContain("InputMaterializationFailed");
      expect(container.textContent).toContain("Input 'Collection' failed to evaluate");
      expect(container.textContent).not.toContain("Unmatched runtime evidence");

      await click(buttonByLabel(container, "Copy incident InputMaterializationFailed"));

      expect(writeText).toHaveBeenCalledWith(expect.stringContaining("Incident: InputMaterializationFailed"));
      expect(writeText).toHaveBeenCalledWith(expect.stringContaining("Input 'Collection' failed to evaluate its JavaScript expression."));
      expect(container.textContent).toContain("Copied incident");
    } finally {
      await unmount();
      if (clipboardDescriptor) Object.defineProperty(navigator, "clipboard", clipboardDescriptor);
      else Reflect.deleteProperty(navigator, "clipboard");
    }
  });

  it("shows a not-found state only when the run record is absent", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.startsWith("https://server.example/runtime/workflows/instances/wfexec-missing")) {
        return response({ error: "Workflow run was not found." }, 404);
      }

      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/instances/wfexec-missing");

    await waitForText(container, "Run wfexec-missing was not found.");
    expect(container.textContent).not.toContain("Definition graph unavailable");

    await unmount();
  });

  it("renders transient workflow instance details from its pinned Runtime executable", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const draftVersionId = "draft:01KVWDKVN8HDDM9WPCGCK8QXJ4-2fead0b1";
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.startsWith("https://server.example/runtime/workflows/instances/wfexec-1")) {
        return response(workflowInstanceDetails({
          instance: workflowInstance({
            definitionVersionId: draftVersionId,
            artifactId: "test-artifact-46792146fbed",
            artifactVersion: "draft",
            status: "Running",
            completedAt: null
          })
        }));
      }

      if (url.startsWith("https://server.example/runtime/workflows/executables/test-artifact-46792146fbed")) {
        return response(executableDetail({ artifactId: "test-artifact-46792146fbed" }));
      }

      if (url.startsWith("https://server.example/design/activities/catalog")) {
        return response({ activities: [activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line"
        })] });
      }

      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/instances/wfexec-1");

    await waitForText(container, "Pinned Runtime executable");
    // Timeline is the default tab and renders the executed activity.
    expect(container.textContent).toContain("Timeline");
    expect(container.textContent).toContain("WriteLine");
    expect(fetchMock.mock.calls.map(([url]) => String(url)).some(url => url.includes(encodeURIComponent(draftVersionId)))).toBe(false);
    // Artifact metadata lives under the Details tab.
    await click(buttonByText(container, "Details"));
    expect(container.textContent).toContain("test-artifact-46792146fbed");

    await unmount();
  });
});

function testApi(): ElsaStudioModuleApi {
  const navigation = registry();
  const routes = registry();
  return {
    backend: {
      baseUrl: "https://server.example/",
      http: fetchHttp("https://server.example/")
    },
    runtime: { workflows: {} },
    featureAreas: featureAreaRegistry(navigation, routes),
    navigation,
    routes,
    propertyEditors: registry(),
    expressionEditors: registry(),
    workflowDesigner: {
      panels: registry()
    },
    ai: (() => {
      const resultListeners = new Set<(result: unknown) => void>();
      return {
        promptActions: registry(),
        dispatchPrompt: vi.fn(),
        onPrompt: vi.fn(() => () => {}),
        publishPromptResult: (result: unknown) => { for (const listener of resultListeners) listener(result); },
        onPromptResult: (listener: (result: unknown) => void) => { resultListeners.add(listener); return () => resultListeners.delete(listener); }
      };
    })(),
    dialogs: {
      confirm: vi.fn(async () => true),
      prompt: vi.fn(async () => null),
      alert: vi.fn(async () => {})
    }
  } as unknown as ElsaStudioModuleApi;
}

function featureAreaRegistry(navigation: ReturnType<typeof registry>, routes: ReturnType<typeof registry>) {
  const featureAreas = registry();

  return {
    ...featureAreas,
    add(featureArea: any) {
      featureAreas.add(featureArea);
      navigation.add({
        id: featureArea.id,
        label: featureArea.nav.title,
        path: featureArea.nav.path,
        activePathPrefix: featureArea.ownedPaths[0],
        order: featureArea.order,
        iconColor: featureArea.nav.iconColor
      });
      for (const item of featureArea.nav.items ?? []) {
        navigation.add({
          id: `${featureArea.id}-${item.title.toLowerCase()}`,
          label: item.title,
          path: item.path,
          parentId: featureArea.id,
          activePathPrefix: item.path,
          iconColor: item.iconColor ?? featureArea.nav.iconColor
        });
      }
      for (const route of featureArea.routes) {
        routes.add(route);
      }
    }
  };
}

function fetchHttp(baseUrl: string) {
  async function requestJson<T>(url: string, init?: RequestInit) {
    const response = await fetch(new URL(url, baseUrl).toString(), init);
    const text = await response.text();
    if (!response.ok) {
      throw new Error(text || `Request failed with ${response.status}.`);
    }
    return (text ? JSON.parse(text) : {}) as T;
  }

  return {
    requestJson,
    getJson: <T,>(url: string, init?: RequestInit) => requestJson<T>(url, { ...init, headers: withHeaders(init?.headers) }),
    postJson: <T,>(url: string, body: unknown, init?: RequestInit) => requestJson<T>(url, {
      ...init,
      method: "POST",
      headers: withHeaders(init?.headers, true),
      body: JSON.stringify(body)
    }),
    putJson: <T,>(url: string, body: unknown, init?: RequestInit) => requestJson<T>(url, {
      ...init,
      method: "PUT",
      headers: withHeaders(init?.headers, true),
      body: JSON.stringify(body)
    }),
    deleteJson: <T,>(url: string, init?: RequestInit) => requestJson<T>(url, {
      ...init,
      method: "DELETE",
      headers: withHeaders(init?.headers)
    }),
    postForm: <T,>(url: string, body: FormData, init?: RequestInit) => requestJson<T>(url, {
      ...init,
      method: "POST",
      headers: withHeaders(init?.headers),
      body
    })
  };
}

function withHeaders(headers?: HeadersInit, json = false) {
  const result = new Headers(headers);
  result.set("Accept", "application/json");
  if (json) result.set("Content-Type", "application/json");
  return result;
}

async function renderRegisteredRoute(
  path = "/workflows/definitions",
  configureApi?: (api: ElsaStudioModuleApi) => void,
  followNavigation = false
) {
  if (typeof ResizeObserver === "undefined") {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
  }
  const fetchWithoutCapabilities = globalThis.fetch;
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) =>
    (typeof input === "object" && input && "url" in input ? String(input.url) : String(input)).includes("/capabilities")
      ? response(capabilityDocument())
      : fetchWithoutCapabilities(input, init)));
  window.history.replaceState({}, "", path);
  const api = testApi();
  configureApi?.(api);
  register(api);
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false }
    }
  });

  function RegisteredRouteHost() {
    const [routePath, setRoutePath] = React.useState(() => window.location.pathname);
    React.useEffect(() => {
      if (!followNavigation) return;
      const syncFromLocation = () => setRoutePath(window.location.pathname);
      window.addEventListener("popstate", syncFromLocation);
      return () => window.removeEventListener("popstate", syncFromLocation);
    }, []);
    const route = api.routes.list().find(candidate => candidate.path === routePath) ??
      api.routes.list().find(candidate => routeMatchesPath(candidate.path, routePath)) ??
      api.routes.list()[0];
    const navigate = (nextPath: string) => {
      window.history.pushState({}, "", nextPath);
      if (followNavigation) setRoutePath(window.location.pathname);
      window.dispatchEvent(new PopStateEvent("popstate"));
    };
    const Component = route.component;
    return <Component navigate={navigate} />;
  }

  flushSync(() => {
    root.render(<QueryClientProvider client={queryClient}><RegisteredRouteHost /></QueryClientProvider>);
  });

  return {
    api,
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
      queryClient.clear();
      container.remove();
    }
  };
}

function routeMatchesPath(routePath: string, path: string) {
  const routeSegments = routePath.split("/").filter(Boolean);
  const pathSegments = path.split("/").filter(Boolean);
  return routeSegments.length === pathSegments.length &&
    routeSegments.every((segment, index) => segment.startsWith(":") || segment === pathSegments[index]);
}

// Renders the definitions list with a stub suggest-metadata action, opens the Create dialog, and reveals
// the intent box — the shared arrange for the metadata-suggestion tests. publishSuggestion() feeds a
// Weaver reply back through the real onPromptResult bus using the requestId the dialog dispatched.
async function openCreateDialogWithSuggest() {
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) =>
    String(input).includes("/activities") ? response({ activities: [] }) : response({ items: [definition()] })));

  const captured: { requestId?: string } = {};
  const rendered = await renderRegisteredRoute("/workflows/definitions", api => {
    api.ai.promptActions.add({
      id: "weaver.workflows.suggest-create-metadata",
      label: "Suggest name and description",
      placement: "field-adornment",
      contextKind: "workflow-create-draft",
      createPrompt: () => ({ message: "suggest", mode: "enqueue", attachments: [] })
    });
    api.ai.dispatchPrompt = request => { captured.requestId = request.requestId; };
  });

  await waitForText(rendered.container, "Hello World");
  await click(buttonByText(rendered.container, "Create"));
  await waitForText(rendered.container, "Create Workflow");
  // The button reveals the intent field rather than firing a context-free prompt.
  await click(buttonByText(dialog(rendered.container), "Suggest name and description"));

  const publishSuggestion = async (result: { text: string; status?: "completed" | "cancelled" | "failed"; autoApply?: boolean }) => {
    expect(captured.requestId).toBeTruthy();
    flushSync(() => rendered.api.ai.publishPromptResult({ requestId: captured.requestId!, status: "completed", ...result }));
    await flushPromises();
  };

  return { ...rendered, captured, publishSuggestion };
}

function stubWorkflowEditorFetch() {
  vi.stubGlobal("ResizeObserver", class {
    observe() {}
    unobserve() {}
    disconnect() {}
  });
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.includes("/activities/availability/diagnostics")) return response({ items: [], sets: [] });
    if (url.includes("/activities")) return response({ activities: [] });
    if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: workflowDraft(), versions: [] });
    return response(null, 404);
  }));
}

function capabilityDocument() {
  return {
    capabilities: [
      {
        id: "elsa.api.workflow-design",
        contractVersion: "1",
        links: [
          { rel: "workflow-definitions", href: "design/workflows/definitions" },
          { rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true },
          { rel: "workflow-versions", href: "design/workflows/versions/{versionId}", templated: true },
          { rel: "scoped-variable-analysis", href: "design/workflows/scoped-variables/analyze" },
          { rel: "activity-input-options", href: "design/workflows/activities/{activityVersionId}/inputs/{inputName}/options", templated: true }
        ]
      },
      {
        id: "elsa.api.activity-design",
        contractVersion: "1",
        links: [
          { rel: "activity-catalog", href: "design/activities/catalog" },
          { rel: "activity-availability", href: "design/activities/availability/settings" },
          { rel: "activity-availability-diagnostics", href: "design/activities/availability/diagnostics" }
        ]
      },
      {
        id: "elsa.api.expressions",
        contractVersion: "1",
        links: [
          { rel: "expression-descriptors", href: "expressions/descriptors" },
          { rel: "variable-types", href: "expressions/variable-types" }
        ]
      },
      {
        id: "elsa.api.publishing",
        contractVersion: "1",
        links: [
          { rel: "publication-preflight", href: "publishing/workflows/{versionId}/preflight", templated: true },
          { rel: "workflow-publish", href: "publishing/workflows/{versionId}/publish", templated: true },
          { rel: "publication-slots", href: "publishing/workflows/{definitionId}/slots", templated: true },
          { rel: "publication-policy", href: "publishing/workflows/{definitionId}/policy", templated: true },
          { rel: "workflow-test-runs", href: "publishing/workflows/{versionId}/test-runs", templated: true },
          { rel: "workflow-draft-test-runs", href: "publishing/workflows/drafts/test-runs" }
        ]
      },
      {
        id: "elsa.api.runtime",
        contractVersion: "1",
        links: [
          { rel: "workflow-executables", href: "runtime/workflows/executables" },
          { rel: "workflow-executable", href: "runtime/workflows/executables/{artifactId}", templated: true },
          { rel: "workflow-executable-provenance", href: "runtime/workflows/executables/{artifactId}/provenance", templated: true },
          { rel: "workflow-execute", href: "runtime/workflows/executables/{artifactId}/execute", templated: true },
          { rel: "workflow-instances", href: "runtime/workflows/instances" },
          { rel: "workflow-instance", href: "runtime/workflows/instances/{workflowExecutionId}", templated: true },
          { rel: "activity-execution", href: "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}", templated: true },
          { rel: "runtime-diagnostics", href: "runtime/workflows/diagnostics/settings" }
        ]
      }
    ]
  };
}

function definition(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: "definition-1",
    name: "Hello World",
    description: "Writes text.",
    createdAt: "2026-06-18T01:00:00Z",
    lastModifiedAt: "2026-06-18T01:10:00Z",
    deletedAt: null,
    draftId: "draft-1",
    latestVersionId: "version-1",
    latestVersion: "1.0.0",
    versionCount: 1,
    ...overrides
  };
}

function executable(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    artifactId: "artifact-1",
    artifactVersion: "1.0.0",
    artifactHash: "sha256:abc",
    definitionId: "definition-1",
    definitionVersionId: "version-1",
    createdAt: "2026-06-18T01:00:00Z",
    publishedAt: "2026-06-18T01:10:00Z",
    sourceKind: "definition",
    sourceId: "definition-1",
    sourceVersion: "1.0.0",
    rootActivityType: "Sequence",
    rootActivityVersion: "1.0.0",
    nodeCount: 3,
    resumeTargetCount: 0,
    ...overrides
  };
}

function publicationSlot(artifactId: string, overrides: Partial<Record<string, unknown>> = {}) {
  return {
    definitionId: "definition-1",
    slotName: "default",
    status: "active",
    publication: {
      publicationId: "publication-1",
      definitionId: "definition-1",
      versionId: "version-1",
      artifactId,
      slotName: "default",
      sourceReferenceId: "reference-1",
      status: "active",
      activatedAt: "2026-06-18T01:00:00Z"
    },
    ...overrides
  };
}

function executableReference(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    sourceReferenceId: "ref-1",
    artifactId: "artifact-1",
    sourceKind: "definition",
    sourceId: "definition-1",
    sourceVersion: "2.0.0",
    definitionId: "definition-1",
    definitionVersionId: "version-2",
    artifactVersion: "2.0.0",
    createdAt: "2026-06-18T01:00:00Z",
    publishedAt: "2026-06-18T01:10:00Z",
    scope: "Published",
    expiresAt: null,
    deletedAt: null,
    deletedReason: null,
    ...overrides
  };
}

function executableWireNode(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    executableNodeId: "exec-write-line-1",
    authoredActivityId: "write-line-1",
    activityType: "Elsa.Activities.Primitives.Activities.WriteLine",
    activityTypeVersion: "1.0.0",
    structureKind: null,
    inputBindings: [],
    childSlots: [],
    ...overrides
  };
}

function executableDetail(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    artifactId: "artifact-1",
    artifactHash: "sha256:abc",
    createdAt: "2026-06-18T01:00:00Z",
    rootActivityType: "Elsa.Activities.Sequence.Activities.Sequence",
    rootActivityVersion: "1.0.0",
    nodeCount: 3,
    resumeTargetCount: 1,
    rootActivity: executableWireNode({
      executableNodeId: "exec-root",
      authoredActivityId: "root",
      activityType: "Elsa.Activities.Sequence.Activities.Sequence",
      structureKind: "elsa.sequence.structure",
      childSlots: [{
        name: "Sequence.Activities",
        activities: [
          executableWireNode({ inputBindings: [{ inputName: "Text", source: "Literal", summary: "\"Hello\"" }] }),
          executableWireNode({ executableNodeId: "exec-send-email-1", authoredActivityId: "send-email-1", activityType: "Elsa.Activities.Email.SendEmail" })
        ]
      }]
    }),
    chosenReference: {
      sourceReferenceId: "ref-1",
      selection: "newest-live",
      layout: [{ nodeId: "write-line-1", x: 40, y: 20 }, { nodeId: "send-email-1", x: 320, y: 20 }]
    },
    references: [
      executableReference(),
      executableReference({ sourceReferenceId: "ref-0", artifactVersion: "1.0.0", sourceVersion: "1.0.0", definitionVersionId: "version-1", publishedAt: "2026-06-17T01:00:00Z" })
    ],
    ...overrides
  };
}

function testRunView(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    testRunId: "test-run-1",
    definitionId: "definition-1",
    definitionVersionId: "draft:draft-1-abcdef12",
    artifactId: "artifact-transient-1",
    workflowExecutionId: "wfexec-1",
    status: "DispatchAccepted",
    commandDispatchStatus: "Accepted",
    activityCount: 1,
    incidentCount: 0,
    reason: null,
    expiresAt: "2026-06-24T12:00:00Z",
    ...overrides
  };
}

function workflowInstance(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    workflowExecutionId: "wfexec-1",
    artifactId: "artifact-1",
    artifactVersion: "1.0.0",
    definitionId: "definition-1",
    definitionVersionId: "version-1",
    runKind: "PublishedRun",
    status: "Completed",
    subStatus: null,
    correlationId: "correlation-1",
    parentWorkflowExecutionId: null,
    tenantId: null,
    activityCount: 1,
    incidentCount: 0,
    createdAt: "2026-06-18T01:00:00Z",
    startedAt: "2026-06-18T01:00:01Z",
    completedAt: "2026-06-18T01:00:03Z",
    updatedAt: "2026-06-18T01:00:03Z",
    ...overrides
  };
}

function workflowInstanceDetails(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    instance: workflowInstance(),
    activities: [activityExecution()],
    incidents: [],
    ...overrides
  };
}

function activityExecution(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    activityExecutionId: "activity-execution-1",
    workflowExecutionId: "wfexec-1",
    executableNodeId: "write-line-1",
    authoredActivityId: "write-line-1",
    activityType: "Elsa.Activities.Primitives.Activities.WriteLine",
    activityTypeVersion: "1.0.0",
    status: "Completed",
    subStatus: null,
    scheduledAt: "2026-06-18T01:00:01Z",
    startedAt: "2026-06-18T01:00:01Z",
    completedAt: "2026-06-18T01:00:03Z",
    schedulingActivityExecutionId: null,
    parentActivityExecutionId: null,
    branchId: null,
    iterationId: null,
    callStackDepth: 0,
    bookmarkIds: [],
    incidentIds: [],
    faultCount: 0,
    aggregateFaultCount: 0,
    metadata: {},
    ...overrides
  };
}

function incidentState(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    incidentId: "incident-1",
    workflowExecutionId: "wfexec-1",
    activityExecutionId: "activity-execution-1",
    executableNodeId: "write-line-1",
    severity: "Error",
    status: "Blocking",
    resolutionAction: "None",
    failureType: "InputMaterializationFailed",
    message: "Input failed.",
    createdAt: "2026-06-18T01:00:02Z",
    resolvedAt: null,
    isBlocking: true,
    metadata: {},
    ...overrides
  };
}

function workflowDefinitionVersionDetails(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: "version-1",
    version: "1.0.0",
    definition: {
      id: "definition-1",
      name: "Hello World",
      description: "Writes text.",
      createdAt: "2026-06-18T01:00:00Z",
      lastModifiedAt: "2026-06-18T01:10:00Z"
    },
    state: {
      variables: [],
      rootActivity: flowchartRoot([{
        nodeId: "write-line-1",
        activityVersionId: "write-line-v1",
        inputs: [],
        outputs: [],
        structure: null
      }]),
      inputs: [],
      outputs: []
    },
    layout: [{ nodeId: "write-line-1", x: 180, y: 120 }],
    ...overrides
  };
}

function workflowDraft(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    sourceVersionId: null,
    state: {
      variables: [],
      rootActivity: null,
      inputs: [],
      outputs: []
    },
    layout: [],
    validationErrors: [],
    ...overrides
  };
}

function draftWithFlowchartRoot(activities: unknown[] = [], inputs: unknown[] = []) {
  return workflowDraft({ state: { variables: [], rootActivity: flowchartRoot(activities), inputs, outputs: [] } });
}

function workflowInput(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    referenceKey: "greeting",
    name: "Greeting",
    displayName: "Greeting",
    description: "Greeting to use for this run.",
    category: "General",
    uiHint: "",
    storageDriverType: null,
    type: { alias: "String", collectionKind: "Single" },
    isRequired: true,
    ...overrides
  };
}

// Contributed-panel double that records the latest designer panel context so tests can assert on its
// fields directly instead of round-tripping values through rendered text.
function capturingPanel() {
  const captured = {
    context: null as WorkflowDesignerPanelContext | null,
    component: ({ context }: { context: unknown }) => {
      captured.context = context as WorkflowDesignerPanelContext;
      return <div className="custom-right-panel">Module right panel</div>;
    }
  };
  return captured;
}

function flowchartRoot(activities: unknown[]) {
  return {
    nodeId: "root",
    activityVersionId: "flowchart-v1",
    inputs: [],
    outputs: [],
    structure: {
      kind: "elsa.flowchart.structure",
      schemaVersion: "1.0.0",
      payload: {
        activities,
        connections: [],
        startNodeId: null,
        nodeMetadata: {},
        connectionMetadata: {}
      }
    }
  };
}

function sequenceRoot(activities: unknown[]) {
  return {
    nodeId: "root",
    activityVersionId: "sequence-v1",
    inputs: [],
    outputs: [],
    structure: {
      kind: "elsa.sequence.structure",
      schemaVersion: "1.0.0",
      payload: { activities }
    }
  };
}

function activity(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    activityVersionId: "activity-1",
    activityTypeKey: "Elsa.Activities.Activity",
    version: "1.0.0",
    category: "General",
    displayName: "Activity",
    description: null,
    executionType: "Action",
    inputs: [],
    outputs: [],
    designFacets: [],
    ...overrides
  };
}

function catalogActivity(descriptor: ReturnType<typeof writeLineDescriptor>) {
  const activityVersionId = `${descriptor.name.replace(/([a-z])([A-Z])/g, "$1-$2").toLowerCase()}-v1`;
  return activity({
    activityVersionId,
    activityTypeKey: descriptor.typeName,
    version: String(descriptor.version),
    category: descriptor.category,
    displayName: descriptor.displayName,
    description: null,
    executionType: descriptor.kind,
    inputs: descriptor.inputs,
    outputs: descriptor.outputs,
    ports: descriptor.ports ?? [],
    available: true,
    authoringTemplate: {
      nodeId: "activity",
      activityVersionId,
      inputs: {},
      outputs: {},
      structure: null
    }
  });
}

function writeLineDescriptor() {
  return {
    typeName: "Elsa.Activities.Primitives.Activities.WriteLine",
    namespace: "Elsa.Activities.Primitives",
    name: "WriteLine",
    version: 1,
    category: "Primitives",
    displayName: "Write Line",
    kind: "Action",
    inputs: [{
      name: "Text",
      typeName: "System.String",
      displayName: "Text",
      description: "Text to write.",
      order: 0,
      category: "General",
      isBrowsable: true,
      uiHint: "singleline",
      isWrapped: true,
      defaultSyntax: "Literal"
    }],
    outputs: [],
    ports: []
  };
}

function writeLinesDescriptor() {
  return {
    typeName: "Elsa.Activities.Primitives.Activities.WriteLines",
    namespace: "Elsa.Activities.Primitives",
    name: "WriteLines",
    version: 1,
    category: "Primitives",
    displayName: "Write Lines",
    kind: "Action",
    inputs: [{
      name: "Lines",
      typeName: "System.Collections.Generic.ICollection`1[[System.String, System.Private.CoreLib]], System.Private.CoreLib",
      displayName: "Lines",
      description: "Lines to write.",
      order: 0,
      category: "General",
      isBrowsable: true,
      // Collection list inputs commonly carry a "singleline" hint; the repeater must still win over the
      // single-line inline chrome (regression: the inline picker used to stretch over the whole list).
      uiHint: "singleline",
      isWrapped: true,
      defaultSyntax: "Literal"
    }],
    outputs: [],
    ports: []
  };
}

function response(body: unknown, status = 200) {
  return new Response(body ? JSON.stringify(body) : "", {
    status,
    headers: { "Content-Type": "application/json" }
  });
}

const runJavaScriptTypeKey = "Elsa.Workflows.Runtime.JavaScript.Activities.RunJavaScript.Activity";

function availabilityDiagnosticEntry(overrides: Record<string, unknown> = {}) {
  return {
    activityDefinitionId: "run-javascript-v1",
    activityTypeKey: runJavaScriptTypeKey,
    displayName: "Run JavaScript",
    description: "Runs JavaScript code inside a workflow.",
    category: "Scripting",
    state: "Available",
    layer: "Catalog",
    referenceKind: "ActivityType",
    referenceName: runJavaScriptTypeKey,
    reason: "Catalog activity is available.",
    ...overrides
  };
}

// Shared fetch stub for the activity availability page: settings GET/PUT, diagnostics, and catalog.
function stubActivityAvailabilityFetch(options: {
  items?: unknown[];
  sets?: unknown[];
  activities?: unknown[];
  settings?: unknown;
  onSave?: (body: { scope?: string; mode: number; rules: { activityTypes: string[]; sets: string[] } }) => void;
} = {}) {
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.includes("/activities/availability/settings")) {
      if (init?.method === "PUT") {
        const body = JSON.parse(String(init.body));
        options.onSave?.(body);
        return response({ scope: "host-default", mode: body.mode, rules: body.rules });
      }
      return response(options.settings ?? { scope: "host-default", mode: "AllExcept", rules: { activityTypes: [], sets: [] } });
    }
    if (url.includes("/activities/availability/diagnostics")) return response({ items: options.items ?? [], sets: options.sets ?? [] });
    if (url.includes("/design/activities/catalog")) return response({ activities: options.activities ?? [] });
    return response(null, 404);
  }));
}

async function click(element: Element | null) {
  if (!(element instanceof HTMLElement)) throw new Error("Element not found");
  element.click();
  await flushPromises();
  flushSync(() => {});
}

// jsdom doesn't implement DataTransfer; a minimal stand-in is enough for the drag handlers, which only
// set effectAllowed/dropEffect and a text payload.
function makeDataTransfer() {
  const store: Record<string, string> = {};
  return {
    effectAllowed: "",
    dropEffect: "",
    setData: (format: string, value: string) => { store[format] = value; },
    getData: (format: string) => store[format] ?? ""
  };
}

async function fireDrag(element: Element | null, type: "dragstart" | "dragover" | "drop", dataTransfer: ReturnType<typeof makeDataTransfer>) {
  if (!(element instanceof HTMLElement)) throw new Error("Drag target not found");
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, "dataTransfer", { value: dataTransfer });
  element.dispatchEvent(event);
  await flushPromises();
  flushSync(() => {});
}

function buttonByText(container: HTMLElement, text: string) {
  return Array.from(container.querySelectorAll("button"))
    .find(button => button.textContent?.trim() === text) ?? null;
}

function buttonByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll("button"))
    .find(button => button.getAttribute("aria-label") === label) ?? null;
}

function checkboxByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLInputElement>("input[type='checkbox']"))
    .find(input => input.getAttribute("aria-label") === label) ?? null;
}

function inputByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLInputElement>("input"))
    .find(input => input.getAttribute("aria-label") === label) ?? null;
}

function textareaByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLTextAreaElement>("textarea"))
    .find(input => input.getAttribute("aria-label") === label) ?? null;
}

function autosaveInput(container: HTMLElement) {
  return container.querySelector<HTMLInputElement>(".wf-autosave-toggle .wf-switch-input");
}

function selectByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLSelectElement>("select"))
    .find(input => input.getAttribute("aria-label") === label) ?? null;
}

function dialog(container: HTMLElement) {
  const element = container.querySelector<HTMLElement>(".wf-dialog");
  if (!element) throw new Error("Dialog not found");
  return element;
}

function optionByText(container: HTMLElement, text: string) {
  return Array.from(container.querySelectorAll<HTMLElement>("[role='option']"))
    .find(option => option.textContent?.includes(text)) ?? null;
}

function rowByLabel(container: HTMLElement, label: string) {
  return Array.from(container.querySelectorAll<HTMLElement>("[role='row']"))
    .find(row => row.getAttribute("aria-label") === label) ?? null;
}

function urlPath(url: string) {
  return new URL(url).pathname;
}

async function fill(element: HTMLInputElement | HTMLTextAreaElement | null, value: string) {
  if (!element) throw new Error("Input not found");
  const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  const valueSetter = Object.getOwnPropertyDescriptor(prototype, "value")?.set;
  valueSetter?.call(element, value);
  element.dispatchEvent(new Event("input", { bubbles: true }));
  await flushPromises();
  flushSync(() => {});
}

async function select(element: HTMLSelectElement | null, value: string) {
  if (!element) throw new Error("Select not found");
  const valueSetter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set;
  valueSetter?.call(element, value);
  element.dispatchEvent(new Event("change", { bubbles: true }));
  await flushPromises();
  flushSync(() => {});
}

function dragDataTransfer() {
  const values = new Map<string, string>();
  return {
    dropEffect: "none",
    effectAllowed: "all",
    setData: vi.fn((type: string, value: string) => {
      values.set(type, value);
    }),
    getData: vi.fn((type: string) => values.get(type) ?? ""),
    clearData: vi.fn((type?: string) => {
      if (type) values.delete(type);
      else values.clear();
    })
  };
}

function dispatchDragEvent(
  element: Element | null,
  type: string,
  options: { dataTransfer: ReturnType<typeof dragDataTransfer>; clientX: number; clientY: number }
) {
  if (!(element instanceof HTMLElement)) throw new Error(`Drag target not found: ${type}`);
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperties(event, {
    dataTransfer: { value: options.dataTransfer },
    clientX: { value: options.clientX },
    clientY: { value: options.clientY }
  });
  element.dispatchEvent(event);
}

function stubRect(element: HTMLElement, rect: Partial<DOMRect>) {
  element.getBoundingClientRect = () => ({
    x: rect.left ?? 0,
    y: rect.top ?? 0,
    left: rect.left ?? 0,
    top: rect.top ?? 0,
    right: rect.right ?? 0,
    bottom: rect.bottom ?? 0,
    width: rect.width ?? 0,
    height: rect.height ?? 0,
    toJSON: () => ({})
  } as DOMRect);
}

function stubElementFromPoint(element: HTMLElement) {
  const descriptor = Object.getOwnPropertyDescriptor(document, "elementFromPoint");
  Object.defineProperty(document, "elementFromPoint", { configurable: true, value: vi.fn(() => element) });

  return () => {
    if (descriptor) {
      Object.defineProperty(document, "elementFromPoint", descriptor);
      return;
    }

    Reflect.deleteProperty(document, "elementFromPoint");
  };
}

async function flushPromises() {
  await new Promise(resolve => setTimeout(resolve, 0));
}

async function waitForText(container: HTMLElement, text: string) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    await flushPromises();
    if (container.textContent?.includes(text)) return;
  }

  throw new Error(`Timed out waiting for text: ${text}. Rendered: ${container.textContent}`);
}

// Waits for a CANVAS node containing `text`. waitForText alone is not enough before interacting with
// the canvas: the inspector shows the scope owner as soon as the draft loads, so the activity's name
// can appear in the panel a flush before React Flow commits the node elements.
async function waitForCanvasNode(container: HTMLElement, text: string) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    await flushPromises();
    const node = Array.from(container.querySelectorAll(".wf-canvas .wf-node"))
      .find(candidate => candidate.textContent?.includes(text));
    if (node) return node;
  }

  throw new Error(`Timed out waiting for canvas node: ${text}`);
}

async function waitForTextToDisappear(container: HTMLElement, text: string) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    await flushPromises();
    if (!container.textContent?.includes(text)) return;
  }

  throw new Error(`Timed out waiting for text to disappear: ${text}`);
}

async function waitForUrlParam(name: string, value: string) {
  for (let attempt = 0; attempt < 20; attempt += 1) {
    await flushPromises();
    if (new URLSearchParams(window.location.search).get(name) === value) return;
  }

  throw new Error(`Timed out waiting for URL parameter: ${name}=${value}`);
}

function registry<T>(): StudioContributionRegistry<T> {
  const items: T[] = [];
  const slot: StudioSlotDefinition = { id: "test-slot", kind: "test", owner: { kind: "host", id: "test" } };
  return {
    slot,
    add: item => { items.push(item); },
    list: () => [...items],
    compose: () => items.map((contribution, order) => ({
      contribution,
      slot,
      availability: { state: "available" },
      order,
      stableKey: `test-${order}`
    }))
  };
}
