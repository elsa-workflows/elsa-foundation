import { describe, expect, it } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext } from "../../../sdk";
import { getActiveAgentCapabilities, getActivePromptStarters } from "../agentContributions";

describe("agent contribution filtering", () => {
  it("filters capabilities and prompt starters by active surface", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });

    api.agent.capabilities.add({
      id: "workflow.explain",
      displayName: "Explain workflow",
      description: "Explain the workflow.",
      kind: "answer",
      risk: "read-only",
      surfaces: ["/workflows"]
    });
    api.agent.promptStarters.add({
      id: "workflow.prompt",
      label: "Explain",
      prompt: "Explain this workflow.",
      surfaces: ["/workflows"],
      requiredCapabilities: ["workflow.explain"]
    });
    api.agent.promptStarters.add({
      id: "missing.prompt",
      label: "Missing",
      prompt: "Missing capability.",
      surfaces: ["/workflows"],
      requiredCapabilities: ["workflow.missing"]
    });

    expect(getActiveAgentCapabilities(api, { route: "/modules" })).toHaveLength(0);
    expect(getActiveAgentCapabilities(api, { route: "/workflows/order" })).toHaveLength(1);
    expect(getActivePromptStarters(api, { route: "/workflows/order" }).map(prompt => prompt.id)).toEqual(["workflow.prompt"]);
  });

  it("filters module contributions by module state, permissions, and policy", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });

    api.agent.capabilities.add({
      id: "workflow.explain",
      moduleId: "workflows",
      displayName: "Explain workflow",
      description: "Explain the workflow.",
      kind: "answer",
      risk: "read-only",
      surfaces: ["/workflows"],
      requiredPermissions: ["workflows.read"]
    });
    api.agent.capabilities.add({
      id: "workflow.admin",
      moduleId: "workflows",
      displayName: "Admin workflow",
      description: "Administer the workflow.",
      kind: "action",
      risk: "admin",
      surfaces: ["/workflows"],
      requiredPermissions: ["workflows.admin"]
    });
    api.agent.capabilities.add({
      id: "module.disabled",
      moduleId: "disabled-module",
      displayName: "Disabled",
      description: "Disabled module capability.",
      kind: "answer",
      risk: "read-only",
      surfaces: ["/workflows"]
    });
    api.agent.promptStarters.add({
      id: "workflow.prompt",
      moduleId: "workflows",
      label: "Explain",
      prompt: "Explain this workflow.",
      surfaces: ["/workflows"],
      requiredCapabilities: ["workflow.explain"]
    });
    api.agent.promptStarters.add({
      id: "disabled.prompt",
      moduleId: "disabled-module",
      label: "Disabled",
      prompt: "Explain disabled module.",
      surfaces: ["/workflows"],
      requiredCapabilities: ["module.disabled"]
    });

    const capabilities = getActiveAgentCapabilities(api, { route: "/workflows/order" }, {
      moduleStates: [
        { id: "workflows", status: "loaded", compatibility: "compatible" },
        { id: "disabled-module", status: "disabled", compatibility: "compatible" }
      ],
      permissions: ["workflows.read"],
      policy: { deniedCapabilityIds: ["workflow.admin"] }
    });

    expect(capabilities.map(capability => capability.id)).toEqual(["workflow.explain"]);
    expect(getActivePromptStarters(api, { route: "/workflows/order" }, capabilities).map(prompt => prompt.id)).toEqual(["workflow.prompt"]);
  });
});
