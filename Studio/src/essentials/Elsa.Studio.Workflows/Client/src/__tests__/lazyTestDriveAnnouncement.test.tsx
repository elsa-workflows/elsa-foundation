import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft, WorkflowTestRunView } from "../workflowTypes";
import type { WorkflowTestRunState } from "../workflow-editor/editorTypes";

const panelChunk = vi.hoisted(() => {
  let resolveModule!: (module: Record<string, unknown>) => void;
  const promise = new Promise<Record<string, unknown>>(resolve => { resolveModule = resolve; });
  return { promise, resolveModule: (module: Record<string, unknown>) => resolveModule(module) };
});
const controlsChunk = vi.hoisted(() => {
  let resolveModule!: (module: Record<string, unknown>) => void;
  const promise = new Promise<Record<string, unknown>>(resolve => { resolveModule = resolve; });
  return { promise, resolveModule: (module: Record<string, unknown>) => resolveModule(module) };
});

vi.mock("../workflow-editor/WorkflowTestDrivePanel", () => panelChunk.promise);
vi.mock("../workflow-editor/WorkflowTestRunControls", () => controlsChunk.promise);

import { LazyWorkflowTestDrivePanel } from "../workflow-editor/lazyTestDriveUi";
import { WorkflowRuntimePanel } from "../workflow-editor/editorPanels";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (!active) return;
  flushSync(() => active!.root.unmount());
  active.container.remove();
  active = null;
});

describe("lazy Test Drive UI", () => {
  it("announces while the Test Drive setup panel loads on demand", async () => {
    const container = render(
      <LazyWorkflowTestDrivePanel
        context={backend}
        draft={draft()}
        runInputEditors={[]}
        stimulusEditors={[]}
        setupState={null}
        onSetupStateChange={() => undefined}
        busy={false}
        onStart={async () => testRunView}
        onUpdateRun={() => undefined}
      />
    );

    expect(container.querySelector(".wf-test-drive-panel [role='status']")?.textContent)
      .toBe("Loading Test Drive setup…");
    expect(container.textContent).not.toContain("Test Run setup ready");

    panelChunk.resolveModule({
      WorkflowTestDrivePanel: () => <div>Test Run setup ready</div>
    });
    await vi.waitFor(() => expect(container.textContent).toContain("Test Run setup ready"));
    expect(container.textContent).not.toContain("Loading Test Drive setup…");
  });

  it("announces while Test Drive controls load on the Runtime panel", async () => {
    const container = render(
      <WorkflowRuntimePanel
        testRun={testRunState}
        context={backend}
        stimulusEditors={[]}
        onOpenRun={() => undefined}
        onRunViewUpdated={() => undefined}
      />
    );

    expect(container.querySelector("[aria-label='Test Run controls'] [role='status']")?.textContent)
      .toBe("Loading Test Drive controls…");
    expect(container.textContent).not.toContain("Test Run controls ready");

    controlsChunk.resolveModule({
      WorkflowTestRunControls: () => <div>Test Run controls ready</div>
    });
    await vi.waitFor(() => expect(container.textContent).toContain("Test Run controls ready"));
    expect(container.textContent).not.toContain("Loading Test Drive controls…");
  });
});

const backend = { baseUrl: "https://studio.example/elsa/api", http: {} } as StudioEndpointContext;

const testRunView: WorkflowTestRunView = {
  testRunId: "test-run-1",
  definitionId: "definition-1",
  definitionVersionId: "draft:1",
  status: "DispatchAccepted",
  workflowExecutionId: "execution-1"
};

const testRunState: WorkflowTestRunState = {
  draftSignature: "draft-signature",
  view: testRunView,
  startLabel: "Direct start",
  workflowInputNames: []
};

function draft(): WorkflowDraft {
  return {
    id: "draft-1",
    definitionId: "definition-1",
    state: { rootActivity: { nodeId: "root", activityVersionId: "root-v1", inputs: [], outputs: [] }, inputs: [] },
    layout: [],
    validationErrors: []
  };
}

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  active = { root, container };
  flushSync(() => root.render(ui));
  return container;
}
