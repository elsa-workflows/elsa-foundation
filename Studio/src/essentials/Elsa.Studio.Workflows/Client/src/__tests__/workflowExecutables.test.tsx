import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioAiContributionApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { listExecutables } from "../api/runtime";
import { listDefinitions } from "../api/workflowDesign";
import type { WorkflowDefinitionSummary, WorkflowExecutableSummary } from "../workflowTypes";
import { WorkflowExecutables } from "../workflow-editor/WorkflowExecutables";

vi.mock("../api/runtime", () => ({ listExecutables: vi.fn() }));
vi.mock("../api/workflowDesign", () => ({ listDefinitions: vi.fn() }));
vi.mock("../workflow-editor/useExecutableWorkflowRun", () => ({
  createExecutableWorkflowRunFeedback: () => ({}),
  createExecutableWorkflowRunTarget: () => null,
  useExecutableWorkflowRun: () => ({ pending: null, runningArtifactId: null, request: vi.fn(), confirm: vi.fn(), cancel: vi.fn() })
}));
vi.mock("../workflow-editor/useExecutableArtifactExport", () => ({
  useExecutableArtifactDownload: () => ({ supported: false, exportingArtifactId: null, exportArtifact: vi.fn() })
}));

let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  vi.clearAllMocks();
});

describe("WorkflowExecutables", () => {
  it("shows a name-loading state before name filters can match, then exposes source metadata", async () => {
    const executable = executableSummary();
    type DefinitionListResult = Awaited<ReturnType<typeof listDefinitions>>;
    let resolveDefinitions!: (response: DefinitionListResult) => void;
    const definitions = new Promise<DefinitionListResult>(resolve => { resolveDefinitions = resolve; });
    vi.mocked(listExecutables).mockResolvedValue([executable]);
    vi.mocked(listDefinitions).mockReturnValue(definitions);

    flushSync(() => root.render(<WorkflowExecutables
      context={{} as StudioEndpointContext}
      ai={emptyAi()}
      runInputEditors={[]}
      definitionFilter="Nightly orders"
      onDefinitionFilterChange={() => {}}
    />));
    await flushAsyncUpdates();

    expect(listDefinitions).toHaveBeenCalledTimes(1);
    expect(container.textContent).toContain("Loading workflow names");
    expect(container.textContent).not.toContain("No executables match this definition filter.");

    resolveDefinitions({ definitions: [definitionSummary("Nightly orders")], page: 1, pageSize: 100, totalCount: 1 });
    await definitions;
    await flushAsyncUpdates();

    const artifactCell = container.querySelector(".wf-artifact-cell");
    expect(artifactCell?.textContent).toContain("Definition version");
    expect(artifactCell?.textContent).toContain("Version 12.4");
    expect(container.textContent).not.toContain("Loading workflow names");
  });
});

function executableSummary(): WorkflowExecutableSummary {
  return {
    artifactId: "artifact-1",
    artifactVersion: "12.4",
    artifactHash: "sha256:artifact-hash",
    definitionId: "definition-1",
    definitionVersionId: "definition-version-1",
    sourceKind: "WorkflowDefinitionVersion",
    sourceId: "source-1",
    sourceVersion: "12.4",
    createdAt: "2026-10-18T00:00:00Z",
    rootActivityType: "Elsa.Workflows.WriteLine",
    rootActivityVersion: "1.0.0",
    nodeCount: 1,
    resumeTargetCount: 0,
    references: []
  };
}

function definitionSummary(name: string): WorkflowDefinitionSummary {
  return {
    id: "definition-1",
    name,
    createdAt: "2026-10-18T00:00:00Z",
    lastModifiedAt: "2026-10-18T00:00:00Z",
    versionCount: 1
  };
}

function emptyAi() {
  return {
    promptActions: { list: () => [] },
    dispatchPrompt: () => {}
  } as unknown as StudioAiContributionApi;
}

async function flushAsyncUpdates() {
  for (let i = 0; i < 3; i += 1) await new Promise<void>(resolve => setTimeout(resolve, 0));
  flushSync(() => {});
}
