import { describe, expect, it } from "vitest";
import { createStudioRegistry } from "../../registry";
import { createEndpointContext } from "../../../sdk";
import {
  getActiveAgentCapabilities,
  getActiveAgentToolContracts,
  getActiveAgentToolSlots,
  getActivePromptStarters
} from "../agentContributions";

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

  it("filters and orders resource-agnostic tool contracts by slot and contract order", () => {
    const api = createStudioRegistry({
      hostVersion: "1.0.0",
      sdkVersion: "1.0.0",
      ...createEndpointContext("https://studio.example/")
    });

    api.agent.toolSlots.add({
      id: "extension.tools",
      moduleId: "extensions",
      displayName: "Extension tools",
      surfaces: ["/extensions"],
      invocationModes: ["read-only", "proposal"],
      order: 20
    });
    api.agent.toolSlots.add({
      id: "workflow.tools",
      moduleId: "workflows",
      displayName: "Workflow tools",
      surfaces: ["/workflows"],
      invocationModes: ["read-only", "direct", "proposal"],
      order: 10
    });
    api.agent.toolSlots.add({
      id: "workflow.audit.tools",
      moduleId: "workflows",
      displayName: "Workflow audit tools",
      surfaces: ["/workflows"],
      invocationModes: ["read-only"],
      order: 5
    });

    api.agent.toolContracts.add({
      id: "workflow.repair",
      slotId: "workflow.tools",
      moduleId: "workflows",
      displayName: "Repair workflow",
      description: "Prepare a reviewable workflow repair proposal.",
      surfaces: ["/workflows"],
      order: 20,
      inputSchema: { type: "object" },
      resourceTargetSchema: { type: "object" },
      resultSchema: { type: "object" },
      risk: "review-required",
      requiredPermissions: ["workflows.write"],
      availability: { status: "available" },
      invocationModes: ["proposal"],
      resultRendererIds: ["workflow.diff"]
    });
    api.agent.toolContracts.add({
      id: "workflow.inspect",
      slotId: "workflow.tools",
      moduleId: "workflows",
      displayName: "Inspect workflow",
      description: "Inspect a workflow without mutating it.",
      surfaces: ["/workflows"],
      order: 10,
      inputSchema: { type: "object" },
      resourceTargetSchema: { type: "object" },
      resultSchema: { type: "object" },
      risk: "read-only",
      requiredPermissions: ["workflows.read"],
      availability: { status: "available" },
      invocationModes: ["read-only"]
    });
    api.agent.toolContracts.add({
      id: "workflow.audit.inspect",
      slotId: "workflow.audit.tools",
      moduleId: "workflows",
      displayName: "Inspect workflow audit",
      description: "Inspect workflow audit metadata.",
      surfaces: ["/workflows"],
      order: 30,
      inputSchema: { type: "object" },
      resourceTargetSchema: { type: "object" },
      resultSchema: { type: "object" },
      risk: "read-only",
      requiredPermissions: ["workflows.read"],
      availability: { status: "available" },
      invocationModes: ["read-only"]
    });
    api.agent.toolContracts.add({
      id: "workflow.disabled",
      slotId: "workflow.tools",
      moduleId: "workflows",
      displayName: "Disabled workflow tool",
      description: "Unavailable by policy.",
      surfaces: ["/workflows"],
      inputSchema: {},
      resourceTargetSchema: {},
      resultSchema: {},
      risk: "admin",
      availability: { status: "policy-denied", reason: "Disabled by host policy." },
      invocationModes: ["privileged"]
    });
    api.agent.toolContracts.add({
      id: "extension.inspect",
      slotId: "extension.tools",
      moduleId: "extensions",
      displayName: "Inspect extension",
      description: "Inspect an extension resource.",
      surfaces: ["/extensions"],
      inputSchema: {},
      resourceTargetSchema: {},
      resultSchema: {},
      risk: "read-only",
      availability: { status: "available" },
      invocationModes: ["read-only"]
    });

    const options = {
      moduleStates: [
        { id: "workflows", status: "loaded", compatibility: "compatible" },
        { id: "extensions", status: "loaded", compatibility: "compatible" }
      ],
      permissions: ["workflows.read", "workflows.write"],
      policy: { deniedToolIds: ["workflow.disabled"] }
    };

    expect(getActiveAgentToolSlots(api, { route: "/workflows/order" }, options).map(slot => slot.id)).toEqual([
      "workflow.audit.tools",
      "workflow.tools"
    ]);
    expect(getActiveAgentToolContracts(api, { route: "/workflows/order" }, options).map(contract => contract.id)).toEqual([
      "workflow.audit.inspect",
      "workflow.inspect",
      "workflow.repair"
    ]);
  });
});
