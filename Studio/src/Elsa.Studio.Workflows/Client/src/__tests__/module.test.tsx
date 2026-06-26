import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioContributionRegistry } from "@elsa-workflows/studio-sdk";
import { isConnectEndOverExistingWorkflowNode, register, resolveConnectEndSource } from "../module";

afterEach(() => {
  vi.unstubAllGlobals();
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

  it("registers navigation and definitions route", () => {
    const api = testApi();

    register(api);

    expect(api.featureAreas.list()).toEqual([
      expect.objectContaining({ id: "workflows", title: "Workflows", ownedPaths: ["/workflows"], required: true, defaultEnabled: true })
    ]);
    expect(api.navigation.list()).toEqual([
      expect.objectContaining({ id: "workflows", path: "/workflows/definitions", activePathPrefix: "/workflows" }),
      expect.objectContaining({ id: "workflows-definitions", path: "/workflows/definitions", parentId: "workflows" }),
      expect.objectContaining({ id: "workflows-executables", path: "/workflows/executables", parentId: "workflows" }),
      expect.objectContaining({ id: "workflows-instances", path: "/workflows/instances", parentId: "workflows" })
    ]);
    expect(api.routes.list()).toEqual([
      expect.objectContaining({ id: "workflows-definitions", path: "/workflows/definitions" }),
      expect.objectContaining({ id: "workflows-executables", path: "/workflows/executables" }),
      expect.objectContaining({ id: "workflows-instances", path: "/workflows/instances" }),
      expect.objectContaining({ id: "workflows-instance-detail", path: "/workflows/instances/:workflowExecutionId" })
    ]);
  });

  it("renders active definition actions and soft-deletes with confirmation", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "DELETE") return response(null, 204);
      expect(url).toContain("state=active");
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("confirm", vi.fn(() => true));
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Latest version");

    expect(container.textContent).toContain("Latest version");
    expect(container.textContent).toContain("Open");
    expect(container.textContent).toContain("Artifacts");
    expect(container.textContent).toContain("Delete");

    await click(buttonByText(container, "Delete"));
    await flushPromises();

    expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining("Delete workflow definition"));
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/definitions/definition-1",
      expect.objectContaining({ method: "DELETE" })
    );

    await unmount();
  });

  it("renders deleted definition actions and calls restore and permanent delete endpoints", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST" || init?.method === "DELETE") return response(null, 204);
      return response({ definitions: url.includes("state=deleted") ? [definition({ deletedAt: "2026-06-18T01:00:00Z" })] : [] });
    });
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("confirm", vi.fn(() => true));
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "No active workflow definitions found.");
    await click(buttonByText(container, "Deleted"));
    await waitForText(container, "Delete permanently");

    expect(container.textContent).toContain("Restore");
    expect(container.textContent).toContain("Delete permanently");

    await click(buttonByText(container, "Restore"));
    await flushPromises();

    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/definitions/definition-1/restore",
      expect.objectContaining({ method: "POST" })
    );

    await click(buttonByText(container, "Delete permanently"));
    await flushPromises();

    expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining("Permanently delete workflow definition"));
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/definitions/definition-1/permanent",
      expect.objectContaining({ method: "DELETE" })
    );

    await unmount();
  });

  it("requests paged definitions and navigates between pages", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input));
      expect(url.searchParams.get("pageSize")).toBe("10");

      if (url.searchParams.get("page") === "2") {
        return response({
          definitions: [definition({ id: "definition-2", name: "Second page" })],
          page: 2,
          pageSize: 10,
          totalCount: 11
        });
      }

      return response({
        definitions: [definition()],
        page: 1,
        pageSize: 10,
        totalCount: 11
      });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Page 1 of 2");
    await click(buttonByText(container, "Next"));
    await waitForText(container, "Second page");

    expect(String(fetchMock.mock.calls[0]?.[0])).toContain("page=1");
    expect(String(fetchMock.mock.calls.at(-1)?.[0])).toContain("page=2");

    await unmount();
  });

  it("opens the workflow editor when a definition row is clicked", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/activities")) return response({ activities: [] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: null, versions: [] });
      return response({ definitions: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Hello World");
    await click(rowByLabel(container, "Open workflow definition Hello World"));

    expect(window.location.pathname).toBe("/workflows/definitions");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");

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
      return response({ definitions: [definition()] });
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
      return response({ definitions: [definition()] });
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
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: workflowDraft(), versions: [] });
      return response({ definitions: [definition()] });
    }));
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
        component: () => <div className="custom-right-panel">Module right panel</div>
      });
    });

    await waitForText(container, "Write Line");
    expect(container.querySelector("[role='tablist'][aria-label='Activities panel tabs']")?.textContent).toContain("Library");
    expect(container.querySelector("[role='tablist'][aria-label='Inspector panel tabs']")?.textContent).toContain("Audit");

    await click(buttonByText(container, "Library"));
    expect(container.textContent).toContain("Module left panel");

    await click(buttonByText(container, "Audit"));
    expect(container.textContent).toContain("Module right panel");

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
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/_elsa/workflow-management/executables")) return response({ executables: [
        executable({ artifactId: "artifact-current", definitionId: "definition-1", artifactVersion: "2.0.0" }),
        executable({ artifactId: "artifact-other", definitionId: "definition-2", sourceId: "definition-2" })
      ] });
      if (url.includes("/activities")) return response({ activities: [
        activity({
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          category: "Primitives",
          displayName: "Write Line"
        })
      ] });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: workflowDraft(), versions: [] });
      return response({ definitions: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    try {
      await waitForText(container, "Write Line");
      await click(buttonByText(container, "Artifacts"));
      await waitForText(container, "artifact-current");

      expect(container.textContent).toContain("Version 2.0.0");
      expect(container.textContent).not.toContain("artifact-other");

      await click(buttonByLabel(container, "Copy artifact ID artifact-current"));
      expect(writeText).toHaveBeenCalledWith("artifact-current");
      expect(container.textContent).toContain("Copied artifact ID");

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
        draft: workflowDraft({
          state: {
            variables: [],
            rootActivity: flowchartRoot([]),
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ definitions: [definition()] });
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
        draft: workflowDraft({
          state: {
            variables: [],
            rootActivity: flowchartRoot([]),
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ definitions: [definition()] });
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
      return response({ definitions: [definition()] });
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Write Line");

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
      if (url.includes("/descriptors/activities")) return response({ items: [writeLineDescriptor()] });
      if (url.includes("/descriptors/expression-descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal" },
        { type: "JavaScript", displayName: "JavaScript" }
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
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Write Line");
    await click(container.querySelector(".wf-canvas .react-flow__node"));
    const writeLineNode = Array.from(container.querySelectorAll(".react-flow__node"))
      .find(node => node.textContent?.includes("Write Line")) ?? null;
    await click(writeLineNode);
    await waitForText(container, "Text");
    expect(container.querySelector("select.wf-property-syntax")).toBeNull();
    expect(container.querySelector(".wf-expression-field .wf-syntax-picker.inline")).toBeTruthy();
    expect(container.querySelector(".wf-property-row > .wf-syntax-picker:not(.inline)")).toBeNull();

    await click(container.querySelector(".wf-syntax-picker-trigger"));
    expect(container.querySelector(".wf-syntax-picker-menu")?.textContent).toContain("Literal");
    expect(container.querySelector(".wf-syntax-picker-menu")?.textContent).toContain("JavaScript");

    await fill(container.querySelector<HTMLInputElement>(".wf-property-row input[type='text']"), "Hello from properties");
    await click(buttonByLabel(container, "Open expanded Text editor"));
    expect(container.querySelector("[role='dialog']")?.textContent).toContain("Property editor");
    await fill(textareaByLabel(container, "Text expanded value"), "Hello from expanded editor\nwith more room");
    await click(buttonByText(container, "Close"));

    await click(buttonByText(container, "Save"));

    const putCall = fetchMock.mock.calls.find(([url, init]) => String(url).includes("/drafts/draft-1") && init?.method === "PUT");
    expect(putCall).toBeTruthy();
    expect(JSON.parse(String(putCall?.[1]?.body)).state.rootActivity.text).toEqual({
      typeName: "System.String",
      expression: { type: "Literal", value: "Hello from expanded editor\nwith more room" }
    });

    await unmount();
  });

  it("renders the ordered expanded expression editor contribution and preserves wrapper metadata", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "PUT") return response({ ...workflowDraft(JSON.parse(String(init.body))), validationErrors: [] });
      if (url.includes("/descriptors/activities")) return response({ items: [writeLineDescriptor()] });
      if (url.includes("/descriptors/expression-descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal" },
        { type: "JavaScript", displayName: "JavaScript" }
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
            rootActivity: writeLineRoot({
              text: {
                typeName: "System.String",
                expression: { type: "Literal", value: "Before contribution" },
                memoryReference: { id: "text-memory" }
              }
            }),
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);

    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1", api => {
      api.expressionEditors.add({
        id: "late-javascript-expanded",
        order: 200,
        supports: context => context.syntax === "JavaScript",
        surfaces: {
          expanded: ({ value, onChange }) => (
            <textarea
              aria-label="Late JavaScript expanded editor"
              value={value == null ? "" : String(value)}
              onChange={event => onChange(event.target.value)}
            />
          )
        }
      });
      api.expressionEditors.add({
        id: "preferred-javascript-expanded",
        order: 50,
        supports: context => context.syntax === "JavaScript",
        surfaces: {
          expanded: ({ value, onChange }) => (
            <textarea
              aria-label="Preferred JavaScript expanded editor"
              value={value == null ? "" : String(value)}
              onChange={event => onChange(event.target.value)}
            />
          )
        }
      });
    });

    await waitForText(container, "Write Line");
    await click(Array.from(container.querySelectorAll(".react-flow__node")).find(node => node.textContent?.includes("Write Line")) ?? null);
    await waitForText(container, "Text");
    await click(container.querySelector(".wf-syntax-picker-trigger"));
    await click(buttonByText(container, "JavaScript"));
    await click(buttonByLabel(container, "Open expanded Text editor"));

    expect(textareaByLabel(container, "Preferred JavaScript expanded editor")).toBeTruthy();
    expect(textareaByLabel(container, "Late JavaScript expanded editor")).toBeNull();
    expect(container.textContent).not.toContain("No enhanced editor is registered for JavaScript");

    await fill(textareaByLabel(container, "Preferred JavaScript expanded editor"), "return 42;");
    await click(buttonByText(container, "Save"));

    const putCall = fetchMock.mock.calls.find(([url, init]) => String(url).includes("/drafts/draft-1") && init?.method === "PUT");
    expect(putCall).toBeTruthy();
    expect(JSON.parse(String(putCall?.[1]?.body)).state.rootActivity.text).toEqual({
      typeName: "System.String",
      expression: { type: "JavaScript", value: "return 42;" },
      memoryReference: { id: "text-memory" }
    });

    await unmount();
  });

  it("falls back for missing expanded expression editors and preserves values across syntax changes", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "PUT") return response({ ...workflowDraft(JSON.parse(String(init.body))), validationErrors: [] });
      if (url.includes("/descriptors/activities")) return response({ items: [writeLineDescriptor()] });
      if (url.includes("/descriptors/expression-descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal" },
        { type: "JavaScript", displayName: "JavaScript" }
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
            rootActivity: writeLineRoot({
              text: {
                typeName: "System.String",
                expression: { type: "Literal", value: "Before syntax switch" },
                memoryReference: { id: "fallback-memory" }
              }
            }),
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);

    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Write Line");
    await click(Array.from(container.querySelectorAll(".react-flow__node")).find(node => node.textContent?.includes("Write Line")) ?? null);
    await waitForText(container, "Text");
    await click(container.querySelector(".wf-syntax-picker-trigger"));
    await click(buttonByText(container, "JavaScript"));
    await click(buttonByLabel(container, "Open expanded Text editor"));

    expect(container.textContent).toContain("No enhanced editor is registered for JavaScript. Using the generic text editor.");
    expect(container.textContent).not.toContain("Monaco");
    expect(textareaByLabel(container, "Text expanded value")?.value).toBe("Before syntax switch");

    await fill(textareaByLabel(container, "Text expanded value"), "After fallback edit");
    await click(buttonByText(container, "Save"));

    const putCall = fetchMock.mock.calls.find(([url, init]) => String(url).includes("/drafts/draft-1") && init?.method === "PUT");
    expect(putCall).toBeTruthy();
    expect(JSON.parse(String(putCall?.[1]?.body)).state.rootActivity.text).toEqual({
      typeName: "System.String",
      expression: { type: "JavaScript", value: "After fallback edit" },
      memoryReference: { id: "fallback-memory" }
    });

    await unmount();
  });

  it("uses optional expression editor metadata for missing-enhancement fallback hints", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "PUT") return response({ ...workflowDraft(JSON.parse(String(init.body))), validationErrors: [] });
      if (url.includes("/descriptors/activities")) return response({ items: [writeLineDescriptor()] });
      if (url.includes("/descriptors/expression-descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal" },
        { type: "JavaScript", displayName: "JavaScript" }
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
            rootActivity: writeLineRoot({
              text: {
                typeName: "System.String",
                expression: { type: "Literal", value: "Before metadata hint" },
                memoryReference: { id: "metadata-memory" }
              }
            }),
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);

    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1", api => {
      api.expressionEditors.add({
        id: "javascript-metadata-only",
        supports: context => context.syntax === "JavaScript",
        metadata: {
          displayName: "JavaScript expression editor module",
          installHint: "Install the JavaScript editor package to enable the enhanced editor."
        },
        surfaces: {}
      });
    });

    await waitForText(container, "Write Line");
    await click(Array.from(container.querySelectorAll(".react-flow__node")).find(node => node.textContent?.includes("Write Line")) ?? null);
    await waitForText(container, "Text");
    await click(container.querySelector(".wf-syntax-picker-trigger"));
    await click(buttonByText(container, "JavaScript"));
    await click(buttonByLabel(container, "Open expanded Text editor"));

    expect(container.textContent).toContain("No JavaScript expression editor module is registered for JavaScript. Using the generic text editor.");
    expect(container.textContent).toContain("Install the JavaScript editor package to enable the enhanced editor.");
    expect(container.textContent).not.toContain("Monaco");

    await fill(textareaByLabel(container, "Text expanded value"), "Edited with metadata fallback");
    await click(buttonByText(container, "Save"));

    const putCall = fetchMock.mock.calls.find(([url, init]) => String(url).includes("/drafts/draft-1") && init?.method === "PUT");
    expect(putCall).toBeTruthy();
    expect(JSON.parse(String(putCall?.[1]?.body)).state.rootActivity.text).toEqual({
      typeName: "System.String",
      expression: { type: "JavaScript", value: "Edited with metadata fallback" },
      memoryReference: { id: "metadata-memory" }
    });

    await unmount();
  });

  it("renders the ordered inline expression editor contribution and preserves wrapper metadata", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "PUT") return response({ ...workflowDraft(JSON.parse(String(init.body))), validationErrors: [] });
      if (url.includes("/descriptors/activities")) return response({ items: [writeLineDescriptor()] });
      if (url.includes("/descriptors/expression-descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal" },
        { type: "JavaScript", displayName: "JavaScript" }
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
            rootActivity: writeLineRoot({
              text: {
                typeName: "System.String",
                expression: { type: "Literal", value: "Before inline contribution" },
                memoryReference: { id: "inline-memory" }
              }
            }),
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);

    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1", api => {
      api.expressionEditors.add({
        id: "late-javascript-inline",
        order: 200,
        supports: context => context.syntax === "JavaScript",
        surfaces: {
          inline: ({ value, onChange }) => (
            <textarea
              aria-label="Late JavaScript inline editor"
              value={value == null ? "" : String(value)}
              onChange={event => onChange(event.target.value)}
            />
          )
        }
      });
      api.expressionEditors.add({
        id: "preferred-javascript-inline",
        order: 50,
        supports: context => context.syntax === "JavaScript",
        surfaces: {
          inline: ({ value, onChange }) => (
            <textarea
              aria-label="Preferred JavaScript inline editor"
              value={value == null ? "" : String(value)}
              onChange={event => onChange(event.target.value)}
            />
          )
        }
      });
    });

    await waitForText(container, "Write Line");
    await click(Array.from(container.querySelectorAll(".react-flow__node")).find(node => node.textContent?.includes("Write Line")) ?? null);
    await waitForText(container, "Text");
    await click(container.querySelector(".wf-syntax-picker-trigger"));
    await click(buttonByText(container, "JavaScript"));

    expect(textareaByLabel(container, "Preferred JavaScript inline editor")).toBeTruthy();
    expect(textareaByLabel(container, "Late JavaScript inline editor")).toBeNull();

    await fill(textareaByLabel(container, "Preferred JavaScript inline editor"), "return 7;");
    await click(buttonByText(container, "Save"));

    const putCall = fetchMock.mock.calls.find(([url, init]) => String(url).includes("/drafts/draft-1") && init?.method === "PUT");
    expect(putCall).toBeTruthy();
    expect(JSON.parse(String(putCall?.[1]?.body)).state.rootActivity.text).toEqual({
      typeName: "System.String",
      expression: { type: "JavaScript", value: "return 7;" },
      memoryReference: { id: "inline-memory" }
    });

    await unmount();
  });

  it("falls back inline independently when only an expanded expression editor is registered", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "PUT") return response({ ...workflowDraft(JSON.parse(String(init.body))), validationErrors: [] });
      if (url.includes("/descriptors/activities")) return response({ items: [writeLineDescriptor()] });
      if (url.includes("/descriptors/expression-descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal" },
        { type: "JavaScript", displayName: "JavaScript" }
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
            rootActivity: writeLineRoot({
              text: {
                typeName: "System.String",
                expression: { type: "Literal", value: "Before inline fallback" },
                memoryReference: { id: "surface-memory" }
              }
            }),
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);

    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1", api => {
      api.expressionEditors.add({
        id: "javascript-expanded-only",
        supports: context => context.syntax === "JavaScript",
        surfaces: {
          expanded: ({ value, onChange }) => (
            <textarea
              aria-label="Expanded-only JavaScript editor"
              value={value == null ? "" : String(value)}
              onChange={event => onChange(event.target.value)}
            />
          )
        }
      });
    });

    await waitForText(container, "Write Line");
    await click(Array.from(container.querySelectorAll(".react-flow__node")).find(node => node.textContent?.includes("Write Line")) ?? null);
    await waitForText(container, "Text");
    await click(container.querySelector(".wf-syntax-picker-trigger"));
    await click(buttonByText(container, "JavaScript"));

    expect(textareaByLabel(container, "Expanded-only JavaScript editor")).toBeNull();
    expect(container.querySelector<HTMLInputElement>(".wf-property-row input[type='text']")?.value).toBe("Before inline fallback");

    await fill(container.querySelector<HTMLInputElement>(".wf-property-row input[type='text']"), "Inline fallback edit");
    await click(buttonByLabel(container, "Open expanded Text editor"));

    expect(textareaByLabel(container, "Expanded-only JavaScript editor")?.value).toBe("Inline fallback edit");

    await fill(textareaByLabel(container, "Expanded-only JavaScript editor"), "Expanded editor edit");
    await click(buttonByText(container, "Save"));

    const putCall = fetchMock.mock.calls.find(([url, init]) => String(url).includes("/drafts/draft-1") && init?.method === "PUT");
    expect(putCall).toBeTruthy();
    expect(JSON.parse(String(putCall?.[1]?.body)).state.rootActivity.text).toEqual({
      typeName: "System.String",
      expression: { type: "JavaScript", value: "Expanded editor edit" },
      memoryReference: { id: "surface-memory" }
    });

    await unmount();
  });

  it("renders expanded expression editor diagnostics without blocking saves", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "PUT") return response({ ...workflowDraft(JSON.parse(String(init.body))), validationErrors: [] });
      if (url.includes("/descriptors/activities")) return response({ items: [writeLineDescriptor()] });
      if (url.includes("/descriptors/expression-descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal" },
        { type: "JavaScript", displayName: "JavaScript" }
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
            rootActivity: writeLineRoot({
              text: {
                typeName: "System.String",
                expression: { type: "JavaScript", value: "bad expanded expression" },
                memoryReference: { id: "expanded-diagnostic-memory" }
              }
            }),
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);

    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1", api => {
      api.expressionEditors.add({
        id: "diagnostic-javascript-expanded",
        supports: context => context.syntax === "JavaScript",
        diagnostics: (_context, value) => String(value).includes("bad")
          ? [{ severity: "warning", code: "JS001", message: "Review this JavaScript expression." }]
          : [],
        surfaces: {
          expanded: ({ value, onChange }) => (
            <textarea
              aria-label="Diagnostic JavaScript expanded editor"
              value={value == null ? "" : String(value)}
              onChange={event => onChange(event.target.value)}
            />
          )
        }
      });
    });

    await waitForText(container, "Write Line");
    await click(Array.from(container.querySelectorAll(".react-flow__node")).find(node => node.textContent?.includes("Write Line")) ?? null);
    await waitForText(container, "Text");
    await click(buttonByLabel(container, "Open expanded Text editor"));

    expect(container.textContent).toContain("JS001");
    expect(container.textContent).toContain("Review this JavaScript expression.");

    await fill(textareaByLabel(container, "Diagnostic JavaScript expanded editor"), "bad but saveable");
    await click(buttonByText(container, "Save"));

    const putCall = fetchMock.mock.calls.find(([url, init]) => String(url).includes("/drafts/draft-1") && init?.method === "PUT");
    expect(putCall).toBeTruthy();
    expect(JSON.parse(String(putCall?.[1]?.body)).state.rootActivity.text).toEqual({
      typeName: "System.String",
      expression: { type: "JavaScript", value: "bad but saveable" },
      memoryReference: { id: "expanded-diagnostic-memory" }
    });

    await unmount();
  });

  it("renders inline expression editor diagnostics without blocking draft updates", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "PUT") return response({ ...workflowDraft(JSON.parse(String(init.body))), validationErrors: [] });
      if (url.includes("/descriptors/activities")) return response({ items: [writeLineDescriptor()] });
      if (url.includes("/descriptors/expression-descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal" },
        { type: "JavaScript", displayName: "JavaScript" }
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
            rootActivity: writeLineRoot({
              text: {
                typeName: "System.String",
                expression: { type: "JavaScript", value: "warn inline" },
                memoryReference: { id: "inline-diagnostic-memory" }
              }
            }),
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);

    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1", api => {
      api.expressionEditors.add({
        id: "diagnostic-javascript-inline",
        supports: context => context.syntax === "JavaScript",
        diagnostics: (_context, value) => String(value).includes("warn")
          ? [{ severity: "info", message: "Inline advisory diagnostic." }]
          : [],
        surfaces: {
          inline: ({ value, onChange }) => (
            <textarea
              aria-label="Diagnostic JavaScript inline editor"
              value={value == null ? "" : String(value)}
              onChange={event => onChange(event.target.value)}
            />
          )
        }
      });
    });

    await waitForText(container, "Write Line");
    await click(Array.from(container.querySelectorAll(".react-flow__node")).find(node => node.textContent?.includes("Write Line")) ?? null);
    await waitForText(container, "Text");

    expect(container.textContent).toContain("Inline advisory diagnostic.");

    await fill(textareaByLabel(container, "Diagnostic JavaScript inline editor"), "warn and save");
    await click(buttonByText(container, "Save"));

    const putCall = fetchMock.mock.calls.find(([url, init]) => String(url).includes("/drafts/draft-1") && init?.method === "PUT");
    expect(putCall).toBeTruthy();
    expect(JSON.parse(String(putCall?.[1]?.body)).state.rootActivity.text).toEqual({
      typeName: "System.String",
      expression: { type: "JavaScript", value: "warn and save" },
      memoryReference: { id: "inline-diagnostic-memory" }
    });

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
        draft: workflowDraft({
          state: {
            variables: [],
            rootActivity: flowchartRoot([]),
            inputs: [],
            outputs: []
          }
        }),
        versions: []
      });
      return response({ definitions: [definition()] });
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
    vi.stubGlobal("fetch", vi.fn(async () => response({ definitions: [definition()] })));
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Hello World");
    await click(buttonByText(container, "Artifacts"));

    expect(window.location.pathname).toBe("/workflows/executables");
    expect(new URLSearchParams(window.location.search).get("definition")).toBe("definition-1");

    await unmount();
  });

  it("creates a workflow definition from any selected root activity", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "POST" && url.endsWith("/_elsa/workflow-management/definitions")) {
        expect(JSON.parse(String(init.body))).toEqual({
          name: "Customer onboarding",
          description: "Creates the first customer workflow.",
          rootKind: "flowchart",
          rootActivityVersionId: "write-line-v1"
        });
        return response({ definition: definition({ id: "created-definition", name: "Customer onboarding" }), draft: null, versions: [] });
      }
      if (url.includes("/activities")) return response({ activities: [
        activity({ activityVersionId: "flowchart-v1", activityTypeKey: "Elsa.Activities.Flowchart", category: "Composition", displayName: "Flowchart" }),
        activity({ activityVersionId: "sequence-v1", activityTypeKey: "Elsa.Activities.Sequence", category: "Composition", displayName: "Sequence" }),
        activity({ activityVersionId: "write-line-v1", activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine", category: "Primitives", displayName: "Write Line" })
      ] });
      if (url.includes("/definitions/created-definition")) return response({ definition: definition({ id: "created-definition", name: "Customer onboarding" }), draft: null, versions: [] });
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute();

    await waitForText(container, "Hello World");
    await click(buttonByText(container, "Create"));
    await waitForText(container, "Create Workflow");
    await waitForText(container, "Write Line");
    expect(selectByLabel(container, "Root activity")?.querySelector("optgroup[label='Composite roots']")).toBeTruthy();
    expect(selectByLabel(container, "Root activity")?.querySelector("optgroup[label='Primitives']")).toBeTruthy();
    expect(selectByLabel(container, "Root activity")?.querySelector("option[value='write-line-v1']")?.disabled).toBe(false);

    await fill(inputByLabel(container, "Display name"), "Customer onboarding");
    await fill(textareaByLabel(container, "Description"), "Creates the first customer workflow.");
    await select(selectByLabel(container, "Root activity"), "write-line-v1");
    await click(buttonByText(dialog(container), "Create"));

    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/definitions",
      expect.objectContaining({ method: "POST" })
    );
    await waitForUrlParam("definition", "created-definition");

    await unmount();
  });

  it("selects multiple workflow definition rows and clears selection", async () => {
    const fetchMock = vi.fn(async () => response({
      definitions: [
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
      if (init?.method === "POST") return response(null, 204);
      expect(url).toBe("https://server.example/_elsa/workflow-management/executables?state=active");
      return response({ executables: [executable({
        rootActivityType: "Elsa.Activities.Flowchart.Activities.Flowchart",
        sourceKind: "WorkflowDefinitionVersion",
        sourceId: "version-1"
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

    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/executables/artifact-1/run",
      expect.objectContaining({ method: "POST" })
    );

    await unmount();
  });

  it("soft-deletes, restores, and permanently deletes executable artifacts", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (init?.method === "DELETE" || init?.method === "POST") return response(null, 204);
      if (url.includes("state=deleted")) return response({ executables: [
        executable({ artifactId: "artifact-restore", deletedAt: "2026-06-26T00:00:00Z" }),
        executable({ artifactId: "artifact-destroy", deletedAt: "2026-06-26T00:01:00Z" })
      ] });
      return response({ executables: [executable()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("confirm", vi.fn(() => true));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables");

    await waitForText(container, "artifact-1");
    await click(buttonByText(container, "Delete"));

    expect(window.confirm).toHaveBeenCalledWith("Delete executable artifact \"artifact-1\"? You can restore it from the Deleted view.");
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/executables/artifact-1",
      expect.objectContaining({ method: "DELETE" })
    );

    await click(buttonByText(container, "Deleted"));
    await waitForText(container, "artifact-restore");
    expect(container.textContent).toContain("Delete permanently");
    expect(container.textContent).not.toContain("Run");

    await click(buttonByText(container, "Restore"));
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/executables/artifact-restore/restore",
      expect.objectContaining({ method: "POST" })
    );

    await click(buttonByText(container, "Delete permanently"));
    expect(window.confirm).toHaveBeenCalledWith("Permanently delete executable artifact \"artifact-restore\"? This cannot be undone.");
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/executables/artifact-restore/permanent",
      expect.objectContaining({ method: "DELETE" })
    );

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
      if (url.startsWith("https://server.example/runtime/workflows/instances")) return response([
        workflowInstance({ workflowExecutionId: "wfexec-history", artifactId: "artifact-history", definitionId: "definition-1" })
      ]);
      if (url.includes("/activities")) return response({ activities: [
        activity({ activityVersionId: "flowchart-v1", activityTypeKey: "Elsa.Activities.Flowchart", category: "Composition", displayName: "Flowchart" }),
        activity({ activityVersionId: "write-line-v1", activityTypeKey: "Elsa.Activities.WriteLine", category: "Primitives", displayName: "Write Line" })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: workflowDraft({ state: { variables: [], rootActivity: flowchartRoot([]), inputs: [], outputs: [] } }),
        versions: []
      });
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Add activity");
    await click(buttonByText(container, "Add activity"));
    await click(optionByText(container, "Write Line"));
    await click(buttonByText(container, "Run"));
    await waitForText(container, "Test run dispatched");

    const calls = fetchMock.mock.calls.map(([url, init]) => ({ url: String(url), method: init?.method ?? "GET", body: String(init?.body ?? "") }));
    const testRunCall = calls.find(call => call.method === "POST" && urlPath(call.url) === "/publishing/workflows/drafts/test-runs");
    expect(testRunCall).toBeTruthy();
    const requestBody = JSON.parse(testRunCall?.body ?? "{}");
    expect(requestBody.definitionId).toBe("definition-1");
    expect(requestBody.snapshotId).toMatch(/^draft-1-[0-9a-f]{8}$/);
    expect(JSON.stringify(requestBody.state)).toContain("write-line-v1");
    expect(calls.some(call => call.url.includes("/drafts/draft-1/promote") && call.method === "POST")).toBe(false);
    expect(calls.some(call => call.url.includes("/versions/") && call.method === "POST")).toBe(false);
    expect(calls.some(call => call.url.includes("/executables/") && call.method === "POST")).toBe(false);
    expect(container.querySelector(".wf-test-run-capsule")).toBeNull();
    expect(container.querySelector(".wf-test-run-popover")).toBeNull();
    await click(buttonByText(container, "Test run dispatched"));
    await waitForText(container, "test-run-1");
    expect(activeInspectorTab(container)?.textContent).toContain("Test runs");
    expect(container.textContent).toContain("Current draft");
    expect(container.textContent).toContain("Ephemeral - not promoted");
    expect(container.textContent).toContain("artifact-transient-1");
    expect(container.textContent).toContain("wfexec-1");
    expect(container.textContent).toContain("Recent instances");
    expect(container.textContent).toContain("wfexec-history");
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/runtime/workflows/instances?definitionId=definition-1&take=8",
      expect.any(Object)
    );

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
      if (url.startsWith("https://server.example/runtime/workflows/instances")) return response([]);
      if (url.includes("/descriptors/activities")) return response({ items: [writeLineDescriptor()] });
      if (url.includes("/descriptors/expression-descriptors")) return response({ items: [
        { type: "Literal", displayName: "Literal" },
        { type: "JavaScript", displayName: "JavaScript" }
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
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Run");
    await click(buttonByText(container, "Run"));
    await waitForText(container, "Test run dispatched");
    expect(container.textContent).not.toContain("artifact-transient-1");
    expect(container.textContent).not.toContain("wfexec-1");
    await click(buttonByText(container, "Test run dispatched"));
    await waitForText(container, "test-run-1");
    expect(container.textContent).toContain("artifact-transient-1");
    expect(container.textContent).toContain("wfexec-1");

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
      if (url.startsWith("https://server.example/runtime/workflows/instances")) return response([]);
      if (url.includes("/activities")) return response({ activities: [
        activity({ activityVersionId: "flowchart-v1", activityTypeKey: "Elsa.Activities.Flowchart", category: "Composition", displayName: "Flowchart" })
      ] });
      if (url.includes("/definitions/definition-1")) return response({
        definition: definition(),
        draft: workflowDraft({ state: { variables: [], rootActivity: flowchartRoot([]), inputs: [], outputs: [] } }),
        versions: []
      });
      return response({ definitions: [definition()] });
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/definitions?definition=definition-1");

    await waitForText(container, "Run");
    await click(buttonByText(container, "Run"));
    await waitForText(container, "Test run rejected");
    await click(buttonByText(container, "Test run rejected"));
    await waitForText(container, "Workflow version has no root activity to publish.");

    expect(container.textContent).toContain("Test run rejected");
    expect(activeInspectorTab(container)?.textContent).toContain("Test runs");
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

  it("navigates from workflow instances to the wider instance detail route", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.startsWith("https://server.example/runtime/workflows/instances")) {
        return response([workflowInstance()]);
      }

      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container, unmount } = await renderRegisteredRoute("/workflows/instances");

    await waitForText(container, "wfexec-1");
    expect(container.textContent).toContain("Instances");
    expect(container.querySelector("nav[aria-label='Workflow views']")).toBeNull();
    await click(rowByLabel(container, "Inspect workflow instance wfexec-1"));

    expect(window.location.pathname).toBe("/workflows/instances/wfexec-1");
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/runtime/workflows/instances?take=100",
      expect.any(Object)
    );

    await unmount();
  });

  it("renders direct workflow instance details with definition layout on the canvas", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.startsWith("https://server.example/runtime/workflows/instances/wfexec-1")) {
        return response(workflowInstanceDetails());
      }

      if (url.startsWith("https://server.example/_elsa/workflow-management/versions/version-1")) {
        return response(workflowDefinitionVersionDetails());
      }

      if (url.startsWith("https://server.example/_elsa/workflow-management/activities")) {
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

    await waitForText(container, "Definition version");
    expect(container.textContent).toContain("Activity history");
    expect(container.textContent).toContain("WriteLine");
    expect(container.textContent).toContain("No incidents recorded.");
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/runtime/workflows/instances/wfexec-1",
      expect.any(Object)
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "https://server.example/_elsa/workflow-management/versions/version-1",
      expect.any(Object)
    );

    await unmount();
  });

  it("renders transient workflow instance details when the draft definition version is unavailable", async () => {
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

      if (url.startsWith(`https://server.example/_elsa/workflow-management/versions/${encodeURIComponent(draftVersionId)}`)) {
        return response({ error: `Workflow definition version '${draftVersionId}' was not found.` }, 404);
      }

      if (url.startsWith("https://server.example/_elsa/workflow-management/activities")) {
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

    await waitForText(container, "Definition graph unavailable");
    expect(container.textContent).toContain("Activity history");
    expect(container.textContent).toContain("WriteLine");
    expect(container.textContent).toContain("test-artifact-46792146fbed");
    expect(container.textContent).not.toContain("Request failed with 404");

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
    featureAreas: featureAreaRegistry(navigation, routes),
    navigation,
    routes,
    propertyEditors: registry(),
    expressionEditors: registry(),
    workflowDesigner: {
      panels: registry()
    },
    ai: {
      promptActions: registry(),
      dispatchPrompt: vi.fn(),
      onPrompt: vi.fn(() => () => {})
    }
  } as ElsaStudioModuleApi;
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

async function renderRegisteredRoute(path = "/workflows/definitions", configureApi?: (api: ElsaStudioModuleApi) => void) {
  window.history.replaceState({}, "", path);
  const api = testApi();
  configureApi?.(api);
  register(api);
  const routePath = new URL(path, window.location.origin).pathname;
  const route = api.routes.list().find(candidate => candidate.path === routePath) ??
    api.routes.list().find(candidate => routeMatchesPath(candidate.path, routePath)) ??
    api.routes.list()[0];
  const Component = route.component;
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);

  flushSync(() => {
    root.render(<Component />);
  });

  return {
    container,
    unmount: async () => {
      flushSync(() => root.unmount());
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

function testRunView(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    testRunId: "test-run-1",
    definitionId: "definition-1",
    definitionVersionId: "draft:draft-1-abcdef12",
    artifactId: "artifact-transient-1",
    workflowExecutionId: "wfexec-1",
    status: "DispatchAccepted",
    commandDispatchStatus: "Accepted",
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
    activities: [{
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
      metadata: {}
    }],
    incidents: [],
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

function writeLineRoot(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    nodeId: "write-line-root",
    activityVersionId: "write-line-v1",
    inputs: [],
    outputs: [],
    structure: null,
    ...overrides
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

function response(body: unknown, status = 200) {
  return new Response(body ? JSON.stringify(body) : "", {
    status,
    headers: { "Content-Type": "application/json" }
  });
}

async function click(element: Element | null) {
  if (!(element instanceof HTMLElement)) throw new Error("Element not found");
  element.click();
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

function activeInspectorTab(container: HTMLElement) {
  return container.querySelector("[role='tablist'][aria-label='Inspector panel tabs'] [role='tab'][aria-selected='true']");
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

  throw new Error(`Timed out waiting for text: ${text}`);
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
  return {
    add: item => items.push(item),
    list: () => [...items]
  };
}
