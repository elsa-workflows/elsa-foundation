import { describe, expect, it, vi } from "vitest";
import { definition, renderRegisteredRoute, response } from "./routeRenderingHelpers";

describe("Executable Inspector toolbar", () => {
  it.each([200, 500])("keeps the refreshed executable when an older response returns %s", async status => {
    let finishFirst!: (response: Response) => void;
    const first = new Promise<Response>(resolve => { finishFirst = resolve; });
    let requests = 0;
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/executables/artifact-1")) {
        requests++;
        return requests === 1 ? first : response({ ...executableDetail(), artifactHash: "sha256:current" });
      }
      if (url.includes("/activities")) return response({ activities: [] });
      return response(null, 404);
    }));
    const { container, unmount } = await renderRegisteredRoute("/workflows/executables/artifact-1");
    await vi.waitFor(() => expect(requests).toBe(1));
    toolbarButton(container, "Refresh")!.click();
    await vi.waitFor(() => expect(container.textContent).toContain("sha256:current"));
    finishFirst(response(executableDetail(), status));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(container.textContent).toContain("sha256:current");
    expect(container.textContent).not.toContain("sha256:abc");
    expect(container.querySelector("time")?.dateTime).toBe(executableDetail().createdAt);
    await unmount();
  });

  it("renders Run as the filled primary StudioButton, quiet return navigation and default actions", async () => {
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

    const returnLink = container.querySelector<HTMLAnchorElement>(".wf-executable-toolbar nav[aria-label='Breadcrumb'] a");
    expect(returnLink?.textContent).toBe("Executables");
    expect(returnLink?.getAttribute("href")).toBe("/workflows/executables");

    const labels = ["Refresh", "Run", "Explain", "Open source definition"];
    for (const label of labels) {
      const button = toolbarButton(container, label);
      expect(button, label).toBeTruthy();
      expect(button?.classList.contains("studio-button"), label).toBe(true);
    }

    expect(toolbarButton(container, "Run")?.getAttribute("data-variant")).toBe("primary");
    for (const label of ["Refresh", "Explain", "Open source definition"]) {
      expect(toolbarButton(container, label)?.getAttribute("data-variant")).toBe("default");
    }
    for (const label of ["Refresh"]) {
      expect(toolbarButton(container, label)?.getAttribute("data-size")).toBe("sm");
    }

    expect(container.querySelectorAll(".wf-instance-inspector .wf-panel-tab-list button")).toHaveLength(3);
    expect(container.querySelector(".wf-instance-inspector .wf-panel-action-button")).toBeTruthy();
    expect(container.querySelector(".wf-instance-inspector [role='tab'][aria-selected='true']")?.textContent).toContain("Identity");

    await unmount();
  });
});

function toolbarButton(container: HTMLElement, text: string) {
  return [...container.querySelectorAll<HTMLButtonElement>(".wf-executable-toolbar button")]
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
