import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext, StudioTestStimulusEditorContribution, StudioTestStimulusResultProps } from "@elsa-workflows/studio-sdk";
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

    const loadingAnnouncement = container.querySelector<HTMLElement>(".wf-test-drive-panel [role='status']")?.textContent;
    expect(container.textContent).not.toContain("Test Run setup ready");

    panelChunk.resolveModule({
      WorkflowTestDrivePanel: () => <div>Test Run setup ready</div>
    });
    await vi.waitFor(() => expect(container.textContent).toContain("Test Run setup ready"));
    expect(container.textContent).not.toContain("Loading Test Drive setup…");
    expect(loadingAnnouncement).toBe("Loading Test Run setup…");
  });

  it("announces while Test Run controls load on the Runtime panel", async () => {
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
      .toBe("Loading Test Run controls…");
    expect(container.textContent).not.toContain("Test Run controls ready");

    controlsChunk.resolveModule({
      WorkflowTestRunControls: () => <div>Test Run controls ready</div>
    });
    await vi.waitFor(() => expect(container.textContent).toContain("Test Run controls ready"));
    expect(container.textContent).not.toContain("Loading Test Run controls…");
  });

  it("announces while a module-provided Test Run response suspends", async () => {
    const result = { kind: "http-exchange", body: "response" };
    let resultReady = false;
    let resolveResult!: () => void;
    const resultModule = new Promise<void>(resolve => {
      resolveResult = () => {
        resultReady = true;
        resolve();
      };
    });
    const actionResult = vi.fn(({ result: receivedResult }: StudioTestStimulusResultProps) => {
      if (!resultReady) throw resultModule;
      return <div>Module Test Run response ready: {receivedResult.kind}</div>;
    });
    const moduleEditor: StudioTestStimulusEditorContribution = {
      id: "module-result",
      supports: () => true,
      component: () => null,
      validate: () => undefined,
      serialize: () => null,
      actionResultComponent: actionResult
    };
    const container = render(
      <WorkflowRuntimePanel
        testRun={{
          ...testRunState,
          driverActionResult: { contributionId: moduleEditor.id, value: result }
        }}
        stimulusEditors={[moduleEditor]}
        onOpenRun={() => undefined}
      />
    );

    expect(container.querySelector(".wf-runtime-card [role='status']")?.textContent)
      .toBe("Loading Test Run response…");
    expect(container.textContent).not.toContain("Module Test Run response ready");

    resolveResult();
    await vi.waitFor(() => expect(container.textContent).toContain("Module Test Run response ready: http-exchange"));
    expect(container.textContent).not.toContain("Loading Test Run response…");
    const receivedProps = actionResult.mock.calls[actionResult.mock.calls.length - 1]?.[0];
    expect(receivedProps).toEqual({ action: { kind: result.kind }, result });
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
