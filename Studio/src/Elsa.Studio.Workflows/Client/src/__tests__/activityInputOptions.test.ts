import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getActivityInputOptions } from "../api/workflows";
import { clearApiCapabilityCache } from "../api/capabilities";
import type { WorkflowDefinitionState } from "../workflowTypes";

describe("activity input options API", () => {
  afterEach(clearApiCapabilityCache);

  it("posts canonical current workflow context to the routed descriptor input and propagates cancellation", async () => {
    const getJson = vi.fn(async () => ({ capabilities: [{
      id: "elsa.api.workflow-design",
      contractVersion: "1",
      links: [{
        rel: "activity-input-options",
        href: "design/workflows/activities/{activityVersionId}/inputs/{inputName}/options",
        templated: true
      }]
    }] }));
    const postJson = vi.fn(async () => ({ options: [{ label: "Name", value: "name" }] }));
    const context = { baseUrl: "test://activity-options-1", http: { getJson, postJson } } as unknown as StudioEndpointContext;
    const workflowState: WorkflowDefinitionState = {
      variables: [],
      rootActivity: {
        nodeId: "activity-1",
        activityVersionId: "activity/version 1",
        inputs: [],
        outputs: [],
        field: { typeName: "System.String", expression: { type: "Literal", value: "name" } }
      },
      inputs: [],
      outputs: []
    };
    const controller = new AbortController();

    const options = await getActivityInputOptions(context, "activity/version 1", "Field Name", "activity-1", workflowState, controller.signal);

    expect(options).toEqual([{ label: "Name", value: "name" }]);
    expect(postJson).toHaveBeenCalledWith(
      "/design/workflows/activities/activity%2Fversion%201/inputs/Field%20Name/options",
      {
        nodeId: "activity-1",
        workflowState: expect.objectContaining({
          rootActivity: expect.objectContaining({
            inputs: [{ referenceKey: "Field", value: { value: "name", expressionType: "Literal" } }]
          })
        })
      },
      { signal: controller.signal }
    );
  });

  it("filters malformed response entries without changing provider order", async () => {
    const context = {
      baseUrl: "test://activity-options-2",
      http: {
        getJson: vi.fn(async () => ({ capabilities: [{
          id: "elsa.api.workflow-design",
          contractVersion: "1",
          links: [{ rel: "activity-input-options", href: "design/workflows/activities/{activityVersionId}/inputs/{inputName}/options" }]
        }] })),
        postJson: vi.fn(async () => ({ options: [
        { label: "One", value: 1 },
        { label: "", value: 2 },
        { label: "Three", value: { complex: true } },
        { label: "Enabled", value: true }
        ] }))
      }
    } as unknown as StudioEndpointContext;

    const options = await getActivityInputOptions(context, "v1", "Choice", "n1", {}, new AbortController().signal);

    expect(options).toEqual([{ label: "One", value: 1 }, { label: "Enabled", value: true }]);
  });
});
