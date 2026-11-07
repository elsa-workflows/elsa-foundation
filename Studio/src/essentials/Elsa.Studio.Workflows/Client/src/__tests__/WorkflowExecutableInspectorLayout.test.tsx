import React from "react";
import { createRoot, type Root } from "react-dom/client";
import { flushSync } from "react-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioAgentRegistry, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowExecutableInspectorPage, WorkflowExecutablesPage } from "../workflow-editor/pages";
import type { ExecutableInspectorIdentity } from "../workflow-editor/executableInspectorIdentity";

vi.mock("../workflow-editor/WorkflowExecutableInspector", () => ({
  WorkflowExecutableInspectorWorkbench: ({
    onIdentityChange
  }: {
    onIdentityChange?(identity: ExecutableInspectorIdentity): void;
  }) => (
    <>
      <div className="wf-toolbar">toolbar</div>
      <div className="studio-result-state" data-tone="danger" role="alert">The executable could not be run</div>
      <div className="wf-status-line">Started artifact-1</div>
      <div className="wf-instance-detail-workbench">
        inspector workbench
        <button
          type="button"
          onClick={() => onIdentityChange?.({
            definitionName: "Invoice settlement",
            artifactId: "artifact-1",
            version: "1.0.0"
          })}
        >
          resolve identity
        </button>
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

  it("keeps the workbench in the last grid track when a run status and run error are present", async () => {
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
    const workbench = page?.querySelector(":scope > .wf-instance-detail-workbench");
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

  it("uses Executable plus the artifact ID until the source definition name arrives", async () => {
    window.history.replaceState({}, "", "/workflows/executables/artifact-95bc4f13586a");
    flushSync(() => root.render(
      <WorkflowExecutableInspectorPage
        context={{} as StudioEndpointContext}
        ai={{} as StudioAgentRegistry}
        runInputEditors={[]}
      />
    ));

    await vi.waitFor(() => expect(container.textContent).toContain("inspector workbench"));
    expect(container.querySelector("h2")?.textContent).toBe("Executable artifact-95bc4f13586a");
    expect(container.querySelector(".studio-page-eyebrow")).toBeNull();

    flushSync(() => {
      const resolve = [...container.querySelectorAll("button")].find(button => button.textContent === "resolve identity");
      resolve?.click();
    });

    expect(container.querySelector("h2")?.textContent).toBe("Invoice settlement · 1.0.0");
    expect(container.querySelector(".studio-page-eyebrow")?.textContent).toBe("Inspector");
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
