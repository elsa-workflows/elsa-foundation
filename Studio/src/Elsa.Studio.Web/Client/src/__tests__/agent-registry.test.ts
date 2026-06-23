import { describe, expect, it } from "vitest";
import { createStudioRegistry } from "../app/registry";
import { createEndpointContext } from "../sdk";

describe("agent registry", () => {
  it("tracks module contributed agent context, prompts, capabilities, and actions", async () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });

    api.agent.contextProviders.add({
      id: "workflow.context",
      displayName: "Workflow",
      surfaces: ["/workflows"],
      sensitivity: "internal",
      collect: async () => [{
        id: "ctx",
        source: "workflow",
        label: "Workflow",
        contentType: "summary",
        sensitivity: "internal",
        scope: "screen"
      }]
    });
    api.agent.promptStarters.add({ id: "prompt", label: "Explain", prompt: "Explain this.", surfaces: ["/workflows"] });
    api.agent.capabilities.add({
      id: "workflow.explain",
      displayName: "Explain workflow",
      description: "Explain the active workflow.",
      kind: "answer",
      risk: "read-only",
      surfaces: ["/workflows"]
    });
    api.agent.actions.add({
      id: "workflow.propose",
      capabilityId: "workflow.propose-change",
      displayName: "Propose change",
      description: "Create a workflow proposal.",
      risk: "review-required",
      surfaces: ["/workflows"],
      proposalSchema: {}
    });

    expect(api.agent.contextProviders.list()).toHaveLength(1);
    expect(api.agent.promptStarters.list()).toHaveLength(1);
    expect(api.agent.capabilities.list()).toHaveLength(1);
    expect(api.agent.actions.list()).toHaveLength(1);
  });
});
