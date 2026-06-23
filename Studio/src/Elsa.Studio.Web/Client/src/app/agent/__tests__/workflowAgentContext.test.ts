import { describe, expect, it } from "vitest";
import { createWorkflowAgentContextProvider } from "../workflowAgentContext";
import { workflowAgentCapabilities, workflowPromptStarters } from "../workflowPromptStarters";

describe("workflow agent context", () => {
  it("collects bounded workflow context only on workflow surfaces", async () => {
    const provider = createWorkflowAgentContextProvider(() => ({
      workflowId: "order-approval",
      selectedActivityId: "send-email",
      summary: "Order approval workflow",
      diagnostics: [{ severity: "warning", message: "No timeout branch is configured." }]
    }));

    await expect(provider.collect({ surface: { route: "/" }, mode: "explain" })).resolves.toEqual([]);
    await expect(provider.collect({ surface: { route: "/workflows/order-approval" }, mode: "explain" })).resolves.toMatchObject([
      {
        id: "workflow:order-approval",
        source: "workflow",
        label: "Order approval workflow",
        contentType: "workflow.definition",
        sensitivity: "internal",
        scope: "selection"
      }
    ]);
  });

  it("defines workflow-first MVP capabilities and prompt starters", () => {
    expect(workflowAgentCapabilities.map(capability => capability.id)).toEqual([
      "workflow.explain",
      "workflow.troubleshoot",
      "workflow.propose-change"
    ]);
    expect(workflowPromptStarters.every(prompt => prompt.surfaces.includes("/workflows"))).toBe(true);
  });
});
