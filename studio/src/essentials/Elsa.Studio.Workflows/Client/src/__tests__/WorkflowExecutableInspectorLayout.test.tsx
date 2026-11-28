import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioWeaverRegistry, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowExecutablesPage, WorkflowManagementPage } from "../workflow-editor/pages";
import { WorkflowExecutableInspectorPage } from "../workflow-editor/executableInspectorPage";

vi.mock("../workflow-editor/WorkflowExecutableInspector", () => ({
  WorkflowExecutableInspectorWorkbench: () => (
    <>
      <header className="wf-editor-toolbar wf-executable-toolbar"><h2>Executable artifact-1</h2><button>Refresh</button></header>
      <div className="studio-result-state" data-tone="danger" role="alert">The executable could not be run</div>
      <div className="wf-status-line">Started artifact-1</div>
      <div className="graph-authoring-workspace wf-editor-body palette-absent wf-executable-workbench">
        inspector workbench
      </div>
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
  it("uses the shared Definition toolbar and inset workbench without a page heading", async () => {
    window.history.replaceState({}, "", "/workflows/executables/artifact-1");
    flushSync(() => root.render(
      <WorkflowExecutableInspectorPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioWeaverRegistry}
        runInputEditors={[]}
      />
    ));

    const loadingPage = container.querySelector(".wf-page.wf-page--run-workbench");
    expect(loadingPage?.getAttribute("data-studio-workspace-backdrop")).toBe("none");
    expect(loadingPage?.classList.contains("wf-page--executable-inspector")).toBe(true);
    expect(loadingPage?.querySelector(".wf-lazy-state")).toBeTruthy();
    await vi.waitFor(() => expect(container.textContent).toContain("inspector workbench"));
    const page = container.querySelector(".wf-page.wf-page--run-workbench");
    expect(page).toBeTruthy();
    expect(page?.querySelector(":scope > .wf-executable-toolbar.wf-editor-toolbar")).toBeTruthy();
    expect(page?.querySelector(":scope > .studio-page-header")).toBeNull();
    expect(page?.querySelector(":scope > .wf-executable-workbench.graph-authoring-workspace")).toBeTruthy();
  });

  it("keeps the workbench in the last grid track when a run status and run error are present", async () => {
    window.history.replaceState({}, "", "/workflows/executables/artifact-1");
    flushSync(() => root.render(
      <WorkflowExecutableInspectorPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioWeaverRegistry}
        runInputEditors={[]}
      />
    ));

    await vi.waitFor(() => expect(container.textContent).toContain("inspector workbench"));
    const page = container.querySelector(".wf-page.wf-page--run-workbench");
    const workbench = page?.querySelector(":scope > .wf-executable-workbench");
    const status = page?.querySelector(":scope > .wf-status-line");
    const runError = page?.querySelector(":scope > .studio-result-state[data-tone='danger']");
    const children = [...(page?.children ?? [])];

    expect(status?.textContent).toContain("Started artifact-1");
    expect(runError?.textContent).toContain("The executable could not be run");
    expect(workbench).toBeTruthy();
    expect(children.at(-1)).toBe(workbench);
    expect(children.indexOf(status as Element)).toBeLessThan(children.indexOf(workbench as Element));
    expect(children.indexOf(runError as Element)).toBeLessThan(children.indexOf(workbench as Element));
  });

  it("does not add a page heading around the workbench identity", async () => {
    window.history.replaceState({}, "", "/workflows/executables/artifact-95bc4f13586a");
    flushSync(() => root.render(
      <WorkflowExecutableInspectorPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioWeaverRegistry}
        runInputEditors={[]}
      />
    ));

    await vi.waitFor(() => expect(container.textContent).toContain("inspector workbench"));
    expect(container.querySelector(".wf-executable-toolbar h2")?.textContent).toBe("Executable artifact-1");
    expect(container.querySelector(".studio-page-header")).toBeNull();
    expect(container.querySelector(".studio-page-eyebrow")).toBeNull();
  });

  it("does not put the executables list on the workbench grid", async () => {
    window.history.replaceState({}, "", "/workflows/executables");
    flushSync(() => root.render(
      <WorkflowExecutablesPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioWeaverRegistry}
        runInputEditors={[]}
      />
    ));

    await vi.waitFor(() => expect(container.textContent).toContain("Executables list"));
    expect(container.querySelector(".wf-page--run-workbench")).toBeNull();
    expect(container.querySelector(".wf-page")).toBeTruthy();
  });

  it("opts the full-bleed editor fallback out before its lazy designer loads", () => {
    window.history.replaceState({}, "", "/workflows/definitions?definition=designer-1");
    flushSync(() => root.render(
      <WorkflowManagementPage
        context={{ baseUrl: "workflow-editor-test" } as StudioEndpointContext}
        ai={{} as StudioWeaverRegistry}
        propertyEditors={[]}
        expressionEditors={[]}
        runInputEditors={[]}
        workflowDesignerPanels={[]}
      />
    ));

    const fallback = container.querySelector(".wf-lazy-state");
    expect(fallback?.getAttribute("data-studio-workspace-backdrop")).toBe("none");
  });
});
