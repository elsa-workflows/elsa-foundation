import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioAgentRegistry, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
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

    expect(container.textContent).toContain("Nightly orders");
    expect(container.querySelector(".wf-executable-workflow-name")?.getAttribute("title")).toBe("Nightly orders");
    expect(container.querySelector('footer[aria-label="Workflow executable pagination"]')?.textContent).toContain("Showing 1-1 of 1");

    const identifiersTrigger = container.querySelector<HTMLButtonElement>('button[aria-label="Identifiers for Nightly orders"]');
    expect(identifiersTrigger).not.toBeNull();
    identifiersTrigger!.click();
    flushSync(() => {});

    const identifiersDialog = document.body.querySelector('[role="dialog"][aria-label="Workflow executable identifiers for Nightly orders"]');
    expect(identifiersDialog?.textContent).toContain("Definition version");
    expect(identifiersDialog?.textContent).toContain("Version 12.4");
    expect(container.textContent).not.toContain("Loading workflow names");
  });

  it("keeps long workflow names on one line and offers an empty-state action", async () => {
    const longName = "Customer-onboarding-and-KYC-refresh-for-regulated-markets-with-manual-exception-review-and-multi-region-escalation";
    vi.mocked(listExecutables).mockResolvedValue([executableSummary()]);
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [definitionSummary(longName)], page: 1, pageSize: 100, totalCount: 1 });

    flushSync(() => root.render(<WorkflowExecutables
      context={{} as StudioEndpointContext}
      ai={emptyAi()}
      runInputEditors={[]}
      definitionFilter={null}
      onDefinitionFilterChange={() => {}}
    />));
    await flushAsyncUpdates();

    const name = container.querySelector(".wf-executable-workflow-name");
    expect(name?.textContent).toBe(longName);
    expect(name?.getAttribute("title")).toBe(longName);
    expect(container.querySelector(".studio-collection-table")).not.toBeNull();
    expect(container.textContent).toContain("Inspect");
    expect(container.textContent).toContain("Run");
    expect(Array.from(container.querySelectorAll(".wf-row-actions button"), button => button.textContent?.replace(/\s+/g, " ").trim()))
      .toEqual(expect.arrayContaining(["Inspect", "Run"]));
    expect(container.querySelector(".wf-row-actions .studio-overflow-menu")).toBeNull();

    const identifiers = container.querySelector(".wf-executable-identifiers");
    expect(identifiers?.querySelector(".wf-list-primary-id")).not.toBeNull();
    expect(identifiers?.querySelector(`button[aria-label="Identifiers for ${longName}"]`)).not.toBeNull();

    const published = container.querySelector<HTMLTimeElement>(".wf-executable-published time");
    const publishedAt = new Date("2026-10-18T00:00:00Z");
    expect(published?.textContent?.trim()).toBe(publishedAt.toLocaleDateString());
    expect(published?.getAttribute("title")).toBe(publishedAt.toLocaleString());
  });

  it("shows a primary empty-state action and the pager footer when there are no executables", async () => {
    vi.mocked(listExecutables).mockResolvedValue([]);
    vi.mocked(listDefinitions).mockResolvedValue({ definitions: [], page: 1, pageSize: 100, totalCount: 0 });

    flushSync(() => root.render(<WorkflowExecutables
      context={{} as StudioEndpointContext}
      ai={emptyAi()}
      runInputEditors={[]}
      definitionFilter={null}
      onDefinitionFilterChange={() => {}}
    />));
    await flushAsyncUpdates();

    expect(container.textContent).toContain("No workflow executables");
    expect(container.textContent).toContain("Open workflow definitions");
    expect(container.querySelector('footer[aria-label="Workflow executable pagination"]')?.textContent).toContain("Showing 0-0 of 0");
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
  } as unknown as StudioAgentRegistry;
}

async function flushAsyncUpdates() {
  for (let i = 0; i < 3; i += 1) await new Promise<void>(resolve => setTimeout(resolve, 0));
  flushSync(() => {});
}
