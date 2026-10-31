import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioAgentRegistry, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowExecutableInspectorPage, WorkflowExecutablesPage } from "../workflow-editor/pages";

vi.mock("../workflow-editor/WorkflowExecutableInspector", () => ({
  WorkflowExecutableInspectorWorkbench: () => (
    <>
      <div className="wf-toolbar">toolbar</div>
      <div className="wf-instance-detail-workbench">inspector workbench</div>
    </>
  )
}));

vi.mock("../workflow-editor/WorkflowExecutables", () => ({
  WorkflowExecutables: () => <div>Executables list</div>
}));

let container: HTMLDivElement;
let root: Root;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  window.history.replaceState({}, "", "/");
});

describe("Executable Inspector page layout", () => {
  it("reuses the run-detail workbench grid and keeps the toolbar a page-frame child", async () => {
    window.history.replaceState({}, "", "/workflows/executables/artifact-1");
    flushSync(() => root.render(
      <WorkflowExecutableInspectorPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioAgentRegistry}
        runInputEditors={[]}
      />
    ));

    await vi.waitFor(() => expect(container.textContent).toContain("inspector workbench"));
    const page = container.querySelector(".wf-page.wf-page--run-workbench");
    expect(page).toBeTruthy();
    expect(page?.querySelector(":scope > .wf-toolbar")).toBeTruthy();
    expect(page?.querySelector(":scope > .wf-instance-detail-workbench")).toBeTruthy();
  });

  it("does not put the executables list on the workbench grid", async () => {
    window.history.replaceState({}, "", "/workflows/executables");
    flushSync(() => root.render(
      <WorkflowExecutablesPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioAgentRegistry}
        runInputEditors={[]}
      />
    ));

    await vi.waitFor(() => expect(container.textContent).toContain("Executables list"));
    expect(container.querySelector(".wf-page--run-workbench")).toBeNull();
    expect(container.querySelector(".wf-page")).toBeTruthy();
  });
});
