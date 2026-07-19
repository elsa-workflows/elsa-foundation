import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { getActivityDefinitionVersion } from "../api/activityDesign";
import { getDraft, updateDraft } from "../api/workflowDesign";
import { buildUnsupportedActivityCanvas, createActivityNode } from "../workflowAdapter";
import { decorateReusableCatalog } from "../workflow-editor/useWorkflowEditorData";
import type { ActivityCatalogItem, WorkflowDraft } from "../workflowTypes";

afterEach(() => clearApiCapabilityCache());

describe("reusable activity draft persistence", () => {
  it("derives identity and presentation after the server saves and reloads only the exact activityVersionId", async () => {
    const catalog = [
      reusableCatalogItem("invoice-version-2", "2.0.0"),
      reusableCatalogItem("invoice-version-3", "3.0.0")
    ];
    const placed = createActivityNode(catalog[0], "invoice-boundary");
    let persisted: WorkflowDraft = {
      id: "workflow-draft-1",
      definitionId: "workflow-definition-1",
      state: {},
      layout: [],
      validationErrors: []
    };
    const putJson = vi.fn(async (_path: string, body: { state: WorkflowDraft["state"]; layout: WorkflowDraft["layout"] }) => {
      persisted = {
        ...persisted,
        state: JSON.parse(JSON.stringify(body.state)),
        layout: body.layout
      };
      return persisted;
    });
    const context = {
      baseUrl: `test://reusable-persistence-${Math.random()}`,
      http: {
        getJson: vi.fn(async (path: string) => {
          if (path === "/capabilities") return capabilities;
          if (path === "/design/workflows/drafts/workflow-draft-1") return persisted;
          if (path === "/design/activities/versions/invoice-version-2") return exactVersion();
          throw new Error(`Unexpected GET ${path}`);
        }),
        putJson
      }
    } as unknown as StudioEndpointContext;

    const saved = await updateDraft(context, {
      ...persisted,
      state: { rootActivity: placed }
    });
    const wireRoot = (putJson.mock.calls[0][1] as { state: WorkflowDraft["state"] }).state.rootActivity!;
    expect(wireRoot).toEqual({
      nodeId: "invoice-boundary",
      activityVersionId: "invoice-version-2",
      inputs: [],
      outputs: [],
      structure: null
    });
    expect(JSON.stringify(wireRoot)).not.toContain("activityDefinition");
    expect(saved.state.rootActivity).toEqual(wireRoot);

    const reloaded = await getDraft(context, "workflow-draft-1");
    const decoratedCatalog = decorateReusableCatalog(catalog, [{
      definitionId: "invoice-definition",
      activityTypeKey: "Contoso.InvoiceEvaluator",
      category: "Finance",
      displayName: "Invoice evaluator",
      versionId: "invoice-version-3",
      version: "3.0.0",
      isAvailable: true
    }]);
    const canvas = buildUnsupportedActivityCanvas(reloaded.state.rootActivity!, decoratedCatalog, []);
    const immutableVersion = await getActivityDefinitionVersion(context, reloaded.state.rootActivity!.activityVersionId);

    expect(canvas.nodes[0].data).toMatchObject({
      icon: "reusable",
      activityDefinitionVersion: "2.0.0"
    });
    expect(immutableVersion).toMatchObject({
      definition: { definitionId: "invoice-definition" },
      versionId: "invoice-version-2",
      version: "2.0.0",
      lifecycle: "Active",
      provider: { providerKey: "elsa.activity-graph", schemaVersion: "1" }
    });
  });
});

function reusableCatalogItem(activityVersionId: string, version: string): ActivityCatalogItem {
  return {
    activityVersionId,
    activityTypeKey: "Contoso.InvoiceEvaluator",
    version,
    category: "Finance",
    displayName: "Invoice evaluator",
    description: null,
    executionType: "Action",
    available: true,
    inputs: [],
    outputs: [],
    designFacets: [],
    authoringTemplate: {
      nodeId: "template",
      activityVersionId,
      inputs: [],
      outputs: [],
      structure: null
    }
  };
}

function exactVersion() {
  return {
    definition: {
      definitionId: "invoice-definition",
      activityTypeKey: "Contoso.InvoiceEvaluator",
      tenantId: null,
      category: "Finance",
      displayName: "Invoice evaluator",
      description: null,
      contentAuthority: { kind: "Design", authorityKey: "elsa.activity-design", sourceId: null },
      forkedFrom: null,
      headVersionId: "invoice-version-3",
      recommendedVersionId: "invoice-version-3"
    },
    versionId: "invoice-version-2",
    version: "2.0.0",
    sourceDraftId: "invoice-draft-2",
    sourceVersionId: null,
    contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [] },
    provider: {
      providerKey: "elsa.activity-graph",
      schemaVersion: "1",
      manifestFingerprint: "sha256:invoice-v2"
    },
    lifecycle: "Active",
    publishedAt: "2026-07-17T10:00:00Z"
  };
}

const capabilities = {
  capabilities: [
    {
      id: "elsa.api.workflow-design",
      contractVersion: "1",
      links: [{ rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true }]
    },
    {
      id: "elsa.api.activity-design",
      contractVersion: "1",
      links: [{ rel: "activity-definition-version", href: "design/activities/versions/{versionId}", templated: true }]
    }
  ]
};
