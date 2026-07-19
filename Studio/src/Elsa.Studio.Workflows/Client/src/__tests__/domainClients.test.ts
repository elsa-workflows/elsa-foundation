import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import {
  createWorkflowFolder,
  deleteEmptyWorkflowFolder,
  getWorkflowFolderMutationSupport,
  listDefinitions,
  listWorkflowFolders,
  moveWorkflowFolder,
  renameWorkflowFolder
} from "../api/workflowDesign";
import { listActivities } from "../api/activityDesign";
import { preflightPublication, preflightPublicationSnapshot, startWorkflowDraftTestRun } from "../api/publishing";
import { getExecutable, getExecutableInputSources, listExecutables, runExecutable } from "../api/runtime";
import { parseWorkflowRunInputs } from "../workflowRunInputs";
import type { WorkflowInput } from "../workflowTypes";

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
        { rel: "publication-snapshot-preflight", href: "publishing/workflows/preflight" },
        { rel: "workflow-draft-test-runs", href: "publishing/workflows/drafts/test-runs" }
      ]
    },
    {
      id: "elsa.api.runtime",
      contractVersion: "1",
      links: [
        { rel: "workflow-executables", href: "runtime/workflows/executables" },
        { rel: "workflow-executable", href: "runtime/workflows/executables/{artifactId}", templated: true },
        { rel: "workflow-executable-input-sources", href: "runtime/workflows/executables/{artifactId}/source-references/{sourceReferenceId}/input-sources", templated: true },
        { rel: "workflow-execute", href: "runtime/workflows/executables/{artifactId}/execute", templated: true }
      ]
    }
  ]
};

const pagedDefinitionCapabilities = {
  capabilities: [{
    id: "elsa.api.workflow-design",
    contractVersion: "1",
    links: [
      { rel: "workflow-definitions", href: "design/workflows/definitions" },
      { rel: "workflow-definitions-page", href: "design/workflows/definition-pages" }
    ]
  }]
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

  it("uses the advertised bounded definition page without downloading the legacy collection", async () => {
    const items = Array.from({ length: 5 }, (_, index) => ({
      id: `definition-${index + 1}`,
      name: `Definition ${index + 1}`,
      createdAt: "2026-07-13T00:00:00Z",
      lastModifiedAt: "2026-07-13T00:00:00Z",
      versionCount: 0
    }));
    const getJson = vi.fn(async (url: string) => url === "/capabilities"
      ? pagedDefinitionCapabilities
      : { items, nextContinuationToken: "opaque-next-page" });
    const context = createContext({ getJson });

    const result = await listDefinitions(context, {
      search: "Definition",
      state: "all",
      page: 2,
      pageSize: 5,
      continuationToken: "opaque-current-page"
    });

    expect(result).toMatchObject({
      definitions: items,
      page: 2,
      pageSize: 5,
      nextContinuationToken: "opaque-next-page",
      isPaged: true
    });
    expect(getJson).toHaveBeenLastCalledWith(
      "/design/workflows/definition-pages?state=all&search=Definition&pageSize=5&continuationToken=opaque-current-page"
    );
    expect(getJson).not.toHaveBeenCalledWith(expect.stringContaining("/design/workflows/definitions?"));
  });

  it("uses the advertised folder root for direct children and never invents a host path", async () => {
    const folderCapabilities = {
      capabilities: [{
        id: "elsa.api.workflow-design",
        contractVersion: "1",
        links: [{ rel: "workflow-folders", href: "design/workflows/folders" }]
      }]
    };
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? folderCapabilities : { items: [] });
    const postJson = vi.fn(async () => ({ id: "folder-a", name: "Operations", normalizedName: "operations", createdAt: "", lastModifiedAt: "" }));
    const context = createContext({ getJson, postJson });

    await listWorkflowFolders(context, { parentId: "folder-parent" });
    await createWorkflowFolder(context, { name: "Operations", parentId: "folder-parent" });

    expect(getJson).toHaveBeenCalledWith("/design/workflows/folders?pageSize=100&parentId=folder-parent");
    expect(postJson).toHaveBeenCalledWith("/design/workflows/folders", { name: "Operations", parentId: "folder-parent" });
  });

  it("sends direct-folder and Unfiled selectors only through the paged relation", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities"
      ? pagedDefinitionCapabilities
      : { items: [], nextContinuationToken: null });
    const context = createContext({ getJson });

    await listDefinitions(context, { search: "", page: 1, pageSize: 10, folderId: "folder/a" });
    await listDefinitions(context, { search: "", page: 1, pageSize: 10, unfiled: true });

    expect(getJson).toHaveBeenCalledWith("/design/workflows/definition-pages?state=active&pageSize=10&folderId=folder%2Fa");
    expect(getJson).toHaveBeenCalledWith("/design/workflows/definition-pages?state=active&pageSize=10&unfiled=true");
  });

  it("does not probe a folder endpoint when the relation is absent", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : { items: [] });
    const context = createContext({ getJson });

    expect(await listWorkflowFolders(context)).toBeNull();
    expect(getJson).toHaveBeenCalledTimes(1);
  });

  it("keeps the opaque folder continuation available to a lazy caller", async () => {
    const folderCapabilities = { capabilities: [{ id: "elsa.api.workflow-design", contractVersion: "1", links: [{ rel: "workflow-folders", href: "design/workflows/folders" }] }] };
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? folderCapabilities : { items: [{ id: "folder-2" }], nextContinuationToken: null });
    const context = createContext({ getJson });

    const page = await listWorkflowFolders(context, { parentId: "folder-parent", continuationToken: "opaque-next" });

    expect(page?.items).toEqual([{ id: "folder-2" }]);
    expect(getJson).toHaveBeenCalledWith("/design/workflows/folders?pageSize=100&parentId=folder-parent&continuationToken=opaque-next");
  });

  it("uses only advertised folder mutation templates and sends the exact mutation contracts", async () => {
    const folderCapabilities = {
      capabilities: [{
        id: "elsa.api.workflow-design",
        contractVersion: "1",
        links: [
          { rel: "workflow-folder-rename", href: "design/workflows/folders/{folderId}/rename", templated: true },
          { rel: "workflow-folder-move", href: "design/workflows/folders/{folderId}/move", templated: true },
          { rel: "workflow-folder-delete-empty", href: "design/workflows/folders/{folderId}", templated: true }
        ]
      }]
    };
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? folderCapabilities : {});
    const postJson = vi.fn(async () => ({}));
    const deleteJson = vi.fn(async () => ({}));
    const context = createContext({ getJson, postJson, deleteJson });

    expect(await getWorkflowFolderMutationSupport(context)).toEqual({ rename: true, move: true, deleteEmpty: true });
    await renameWorkflowFolder(context, "folder/a", "Operations");
    await moveWorkflowFolder(context, "folder/a", null);
    await deleteEmptyWorkflowFolder(context, "folder/a");

    expect(postJson).toHaveBeenCalledWith("/design/workflows/folders/folder%2Fa/rename", { name: "Operations" });
    expect(postJson).toHaveBeenCalledWith("/design/workflows/folders/folder%2Fa/move", { parentId: null });
    expect(deleteJson).toHaveBeenCalledWith("/design/workflows/folders/folder%2Fa");
    expect(getJson).toHaveBeenCalledTimes(1);
  });

  it("reports absent folder mutation relations without probing guessed endpoints", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : {});
    const context = createContext({ getJson });

    expect(await getWorkflowFolderMutationSupport(context)).toEqual({ rename: false, move: false, deleteEmpty: false });
    expect(getJson).toHaveBeenCalledTimes(1);
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
    await preflightPublicationSnapshot(context, {
      definitionId: "definition-1",
      state: { rootActivity: null },
      layout: [],
      action: "replace",
      slotName: "default",
      expectedPublicationId: "publication-1"
    });

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
    expect(postJson).toHaveBeenCalledWith(
      "/publishing/workflows/preflight",
      {
        definitionId: "definition-1",
        state: { rootActivity: null },
        layout: [],
        action: "replace",
        slotName: "default",
        expectedPublicationId: "publication-1"
      });
    expect(getJson).toHaveBeenCalledTimes(3);
  });

  it("sends the same keyed input dictionary for transient and published dispatches", async () => {
    const inputs = { Greeting: "Hello", Attempts: 3 };
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : {});
    const requestJson = vi.fn(async (url: string) => url.includes("drafts/test-runs")
      ? { testRunId: "test-1", definitionId: "definition-1", definitionVersionId: "version-1", status: "Dispatched" }
      : { workflowExecutionId: "execution-1" });
    const context = createContext({ getJson, requestJson });

    await startWorkflowDraftTestRun(context, {
      definitionId: "definition-1",
      snapshotId: "snapshot-1",
      state: { rootActivity: null },
      inputs
    });
    await runExecutable(context, "artifact/1", inputs, "reference-1");

    expect(requestJson).toHaveBeenCalledWith(
      "/publishing/workflows/drafts/test-runs",
      expect.objectContaining({ body: expect.stringContaining('"inputs":{"Greeting":"Hello","Attempts":3}') })
    );
    expect(requestJson).toHaveBeenCalledWith(
      "/runtime/workflows/executables/artifact%2F1/execute",
      expect.objectContaining({ body: '{"inputs":{"Greeting":"Hello","Attempts":3},"sourceReferenceId":"reference-1"}' })
    );
  });

  it("emits exact Int64, UInt64, and Decimal JSON number tokens for transient and published execution", async () => {
    const parsed = parseWorkflowRunInputs([
      workflowInput("signed", "Signed", "Int64"),
      workflowInput("unsigned", "Unsigned", "UInt64"),
      workflowInput("amount", "Amount", "Decimal")
    ], {
      signed: "-9223372036854775808",
      unsigned: "18446744073709551615",
      amount: "0.1234567890123456789012345678"
    });
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : {});
    const requestJson = vi.fn(async (url: string) => url.includes("drafts/test-runs")
      ? { testRunId: "test-1", definitionId: "definition-1", definitionVersionId: "version-1", status: "Dispatched" }
      : { workflowExecutionId: "execution-1" });
    const context = createContext({ getJson, requestJson });

    await startWorkflowDraftTestRun(context, {
      definitionId: "definition-1",
      snapshotId: "snapshot-1",
      state: { rootActivity: null },
      inputs: parsed.values
    });
    await runExecutable(context, "artifact/1", parsed.values, "reference-1");

    expect(requestJson).toHaveBeenNthCalledWith(1, "/publishing/workflows/drafts/test-runs", expect.objectContaining({
      method: "POST",
      body: '{"definitionId":"definition-1","snapshotId":"snapshot-1","state":{"rootActivity":null},"inputs":{"Signed":-9223372036854775808,"Unsigned":18446744073709551615,"Amount":0.1234567890123456789012345678}}'
    }));
    expect(requestJson).toHaveBeenNthCalledWith(2, "/runtime/workflows/executables/artifact%2F1/execute", expect.objectContaining({
      method: "POST",
      body: '{"inputs":{"Signed":-9223372036854775808,"Unsigned":18446744073709551615,"Amount":0.1234567890123456789012345678},"sourceReferenceId":"reference-1"}'
    }));
  });

  it("requests the exact historical executable reference when one is selected", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : {});
    const context = createContext({ getJson });

    await getExecutable(context, "artifact/1", "reference/old");

    expect(getJson).toHaveBeenLastCalledWith(
      "/runtime/workflows/executables/artifact%2F1?ref=reference%2Fold"
    );
  });

  it("requests pinned authored and compiled input source through its separately protected relation", async () => {
    const getJson = vi.fn(async (url: string) => url === "/capabilities" ? capabilities : {});
    const context = createContext({ getJson });

    await getExecutableInputSources(context, "artifact/1", "reference/old");

    expect(getJson).toHaveBeenLastCalledWith(
      "/runtime/workflows/executables/artifact%2F1/source-references/reference%2Fold/input-sources"
    );
  });
});

function createContext(http: Record<string, unknown>) {
  return {
    baseUrl: `test://domain-clients-${Math.random()}`,
    http
  } as unknown as StudioEndpointContext;
}

function workflowInput(referenceKey: string, name: string, alias: string): WorkflowInput {
  return {
    referenceKey,
    name,
    displayName: name,
    description: "",
    category: "",
    uiHint: "",
    storageDriverType: null,
    type: { alias, collectionKind: "Single" },
    isRequired: true
  };
}
