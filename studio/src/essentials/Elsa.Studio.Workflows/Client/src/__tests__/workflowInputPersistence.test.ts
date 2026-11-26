import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { clearApiCapabilityCache } from "../api/capabilities";
import { updateDraft } from "../api/workflowDesign";
import { createInput } from "../workflowProperties";
import type { WorkflowDraft } from "../workflowTypes";

afterEach(() => clearApiCapabilityCache());

describe("workflow input persistence", () => {
  it("sends the required nullability flag for newly created and legacy inputs", async () => {
    const putJson = vi.fn(async (_path: string, body: {
      state: WorkflowDraft["state"];
      layout: WorkflowDraft["layout"];
    }) => ({
      id: "workflow-draft-1",
      definitionId: "workflow-definition-1",
      state: body.state,
      layout: body.layout,
      validationErrors: []
    }));
    const context = {
      baseUrl: `test://workflow-input-persistence-${Math.random()}`,
      http: {
        getJson: vi.fn(async (path: string) => {
          if (path === "/capabilities") return capabilities;
          throw new Error(`Unexpected GET ${path}`);
        }),
        putJson
      }
    } as unknown as StudioEndpointContext;

    await updateDraft(context, {
      id: "workflow-draft-1",
      definitionId: "workflow-definition-1",
      state: {
        inputs: [
          createInput({ name: "OrderId" }),
          {
            referenceKey: "legacy-input",
            name: "LegacyInput",
            type: { alias: "String", collectionKind: "Single" }
          }
        ]
      },
      layout: [],
      validationErrors: []
    });

    const request = putJson.mock.calls[0][1];
    expect(request.state.inputs).toEqual([
      expect.objectContaining({ name: "OrderId", isNullable: false }),
      expect.objectContaining({ name: "LegacyInput", isNullable: false })
    ]);
  });
});

const capabilities = {
  capabilities: [{
    id: "elsa.api.workflow-design",
    contractVersion: "1",
    links: [{
      rel: "workflow-drafts",
      href: "design/workflows/drafts/{draftId}",
      templated: true
    }]
  }]
};
