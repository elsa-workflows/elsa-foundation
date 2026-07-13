import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { listDefinitions } from "../api/workflowDesign";
import { listActivities } from "../api/activityDesign";
import { preflightPublication, startWorkflowDraftTestRun } from "../api/publishing";
import { getExecutable, listExecutables, runExecutable } from "../api/runtime";

afterEach(clearApiCapabilityCache);

const capabilities = {
  capabilities: [
    {
      id: "elsa.api.workflow-design",
      contractVersion: "1",
      links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }]
    },
    {
      id: "elsa.api.activity-design",
      contractVersion: "1",
      links: [{ rel: "activity-catalog", href: "design/activities/catalog" }]
    },
    {
      id: "elsa.api.publishing",
      contractVersion: "1",
      links: [
        { rel: "publication-preflight", href: "publishing/workflows/{versionId}/preflight", templated: true },
        { rel: "workflow-draft-test-runs", href: "publishing/workflows/drafts/test-runs" }
      ]
    },
    {
      id: "elsa.api.runtime",
      contractVersion: "1",
      links: [
        { rel: "workflow-executables", href: "runtime/workflows/executables" },
        { rel: "workflow-executable", href: "runtime/workflows/executables/{artifactId}", templated: true },
        { rel: "workflow-execute", href: "runtime/workflows/executables/{artifactId}/execute", templated: true }
      ]
    }
  ]
};

describe("canonical domain clients", () => {
  it("normalizes the unpaged Design items response for Studio paging", async () => {
    const items = Array.from({ length: 12 }, (_, index) => ({
      id: `definition-${index + 1}`,
      name: `Definition ${index + 1}`,
      createdAt: "2026-07-13T00:00:00Z",
      lastModifiedAt: "2026-07-13T00:00:00Z",
      versionCount: 0
    }));
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : { items });
    const context = createContext({ getJson });

    const result = await listDefinitions(context, { search: "Definition", state: "all", page: 2, pageSize: 5 });

    expect(result.definitions.map(item => item.id)).toEqual(["definition-6", "definition-7", "definition-8", "definition-9", "definition-10"]);
    expect(result).toMatchObject({ page: 2, pageSize: 5, totalCount: 12 });
    expect(getJson).toHaveBeenLastCalledWith("/design/workflows/definitions?state=all&search=Definition");
  });

  it("uses canonical Activity, Publishing, and Runtime links without probing alternates", async () => {
    const getJson = vi.fn(async (url: string) => {
      if (url === "/capabilities") return capabilities;
      if (url === "/design/activities/catalog") return { activities: [{
        activityVersionId: "activity-v1",
        activityTypeKey: "Elsa.WriteLine",
        version: "1",
        displayName: "Write Line",
        category: null,
        executionType: "Action",
        available: true,
        inputs: [{ name: "Text", type: "String" }],
        outputs: [],
        ports: [],
        containerStructure: null,
        authoringTemplate: { nodeId: "activity", activityVersionId: "activity-v1", inputs: {}, outputs: {}, structure: null }
      }] };
      if (url === "/runtime/workflows/executables?scope=All&includeRetired=true") return { items: [] };
      throw new Error(`Unexpected GET ${url}`);
    });
    const postJson = vi.fn(async () => ({
      definitionId: "definition-1",
      versionId: "version-1",
      slotName: "blue",
      resolvedAction: "sideBySide",
      policySource: "request",
      canActivate: true,
      conflicts: []
    }));
    const context = createContext({ getJson, postJson });

    const catalog = await listActivities(context);
    await listExecutables(context, { scope: "all", includeRetired: true });
    await preflightPublication(context, "version/1", { action: "sideBySide", slotName: "blue" });

    expect(getJson).toHaveBeenCalledWith("/capabilities");
    expect(catalog.activities[0]).toMatchObject({
      category: "Uncategorized",
      designFacets: [],
      inputs: [{ name: "Text", type: "String", typeName: "String" }],
      authoringTemplate: { inputs: [], outputs: [] }
    });
    expect(getJson).toHaveBeenCalledWith("/design/activities/catalog");
    expect(getJson).toHaveBeenCalledWith("/runtime/workflows/executables?scope=All&includeRetired=true");
    expect(postJson).toHaveBeenCalledWith(
      "/publishing/workflows/version%2F1/preflight",
      { action: "sideBySide", slotName: "blue" });
    expect(getJson).toHaveBeenCalledTimes(3);
  });

  it("sends the same keyed input dictionary for transient and published dispatches", async () => {
    const inputs = { Greeting: "Hello", Attempts: 3 };
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : {});
    const postJson = vi.fn(async (url: string) => url.includes("drafts/test-runs")
      ? { testRunId: "test-1", definitionId: "definition-1", definitionVersionId: "version-1", status: "Dispatched" }
      : { workflowExecutionId: "execution-1" });
    const context = createContext({ getJson, postJson });

    await startWorkflowDraftTestRun(context, {
      definitionId: "definition-1",
      snapshotId: "snapshot-1",
      state: { rootActivity: null },
      inputs
    });
    await runExecutable(context, "artifact/1", inputs, "reference-1");

    expect(postJson).toHaveBeenCalledWith(
      "/publishing/workflows/drafts/test-runs",
      expect.objectContaining({ inputs })
    );
    expect(postJson).toHaveBeenCalledWith(
      "/runtime/workflows/executables/artifact%2F1/execute",
      { inputs, sourceReferenceId: "reference-1" }
    );
  });

  it("requests the exact historical executable reference when one is selected", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : {});
    const context = createContext({ getJson });

    await getExecutable(context, "artifact/1", "reference/old");

    expect(getJson).toHaveBeenLastCalledWith(
      "/runtime/workflows/executables/artifact%2F1?ref=reference%2Fold"
    );
  });
});

function createContext(http: Record<string, unknown>) {
  return {
    baseUrl: `test://domain-clients-${Math.random()}`,
    http
  } as unknown as StudioEndpointContext;
}
