import { describe, expect, it, vi } from "vitest";
import { definition, renderRegisteredRoute, response } from "./routeRenderingHelpers";

describe("Executable Inspector toolbar", () => {
  it("renders Run as the filled primary StudioButton and the rest as default StudioButtons", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      unobserve() {}
      disconnect() {}
    });
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/executables/artifact-1")) return response(executableDetail());
      if (url.includes("/activities")) return response({
        activities: [{
          activityVersionId: "write-line-v1",
          activityTypeKey: "Elsa.Activities.Primitives.Activities.WriteLine",
          version: "1.0.0",
          category: "Primitives",
          displayName: "Write Line",
          description: null,
          executionType: "Action",
          inputs: [],
          outputs: [],
          designFacets: []
        }]
      });
      if (url.includes("/definitions/definition-1")) return response({ definition: definition(), draft: null, versions: [] });
      return response(null, 404);
    }));

    const { container, unmount } = await renderRegisteredRoute("/workflows/executables/artifact-1", api => {
      api.weaver.promptActions.add({
        id: "weaver.workflows.explain-executable",
        label: "Explain executable",
        placement: "selection",
        contextKind: "workflow-executable",
        createPrompt: context => ({ message: "Explain", mode: "enqueue", attachments: [], context } as never)
      });
    });

    await vi.waitFor(() => {
      if (!toolbarButton(container, "Run")) throw new Error("Timed out waiting for Run");
    });

    const labels = ["Executables", "Refresh", "Run", "Explain", "Open source definition"];
    for (const label of labels) {
      const button = toolbarButton(container, label);
      expect(button, label).toBeTruthy();
      expect(button?.classList.contains("studio-button"), label).toBe(true);
    }

    expect(toolbarButton(container, "Run")?.getAttribute("data-variant")).toBe("primary");
    for (const label of ["Executables", "Refresh", "Explain", "Open source definition"]) {
      expect(toolbarButton(container, label)?.getAttribute("data-variant")).toBe("default");
    }

    expect(container.querySelector(".wf-instance-inspector .wf-panel-tab-list button")).toBeTruthy();
    expect(container.querySelector(".wf-instance-inspector .wf-panel-action-button")).toBeTruthy();

    await unmount();
  });
});

function toolbarButton(container: HTMLElement, text: string) {
  return [...container.querySelectorAll<HTMLButtonElement>(".wf-toolbar button")]
    .find(button => button.textContent?.replace(/\s+/g, " ").trim().includes(text)) ?? null;
}

function executableDetail() {
  return {
    artifactId: "artifact-1",
    artifactHash: "sha256:abc",
    createdAt: "2026-06-18T01:00:00Z",
    rootActivityType: "Elsa.Activities.Primitives.Activities.WriteLine",
    rootActivityVersion: "1.0.0",
    nodeCount: 1,
    resumeTargetCount: 0,
    rootActivity: {
      executableNodeId: "exec-write-line-1",
      authoredActivityId: "write-line-1",
      activityType: "Elsa.Activities.Primitives.Activities.WriteLine",
      activityTypeVersion: "1.0.0",
      structureKind: null,
      inputBindings: [],
      childSlots: []
    },
    chosenReference: { sourceReferenceId: "ref-1", selection: "newest-live", layout: [] },
    references: [{
      sourceReferenceId: "ref-1",
      artifactId: "artifact-1",
      sourceKind: "definition",
      sourceId: "definition-1",
      sourceVersion: "1.0.0",
      definitionId: "definition-1",
      definitionVersionId: "version-1",
      artifactVersion: "1.0.0",
      createdAt: "2026-06-18T01:00:00Z",
      publishedAt: "2026-06-18T01:10:00Z",
      scope: "Published",
      expiresAt: null,
      deletedAt: null,
      deletedReason: null
    }]
  };
}
