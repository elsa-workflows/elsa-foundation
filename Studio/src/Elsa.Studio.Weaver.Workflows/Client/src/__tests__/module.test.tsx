import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioContributionRegistry } from "@elsa-workflows/studio-sdk";
import { register } from "../module";

describe("weaver workflows module", () => {
  it("registers workflow AI context, prompt action, tool, and proposal contributions", () => {
    const api = testApi();

    register(api);

    expect(api.ai.contextProviders.list().map(item => item.kind)).toEqual([
      "workflow-definition",
      "workflow-executable",
      "workflow-instance"
    ]);
    expect(api.ai.promptActions.list().map(item => item.id)).toContain("weaver.workflows.suggest-create-metadata");
    expect(api.ai.promptActions.list().map(item => item.id)).toContain("weaver.workflows.propose-update");
    expect(api.ai.tools.list()).toEqual([
      expect.objectContaining({ name: "workflow.definition.read", mutability: "read-only" }),
      expect.objectContaining({ name: "workflow.proposal.create", mutability: "proposal" })
    ]);
    expect(api.ai.proposalRenderers.list().map(item => item.kind)).toEqual(["WorkflowCreate", "WorkflowUpdate"]);
  });

  it("builds workflow definition prompts with attachments", () => {
    const api = testApi();
    register(api);
    const action = api.ai.promptActions.list().find(item => item.id === "weaver.workflows.explain-definition");

    const prompt = action?.createPrompt({ id: "definition-1", name: "Onboard customer" });

    expect(prompt).toMatchObject({
      mode: "enqueue",
      attachments: [expect.objectContaining({ kind: "workflow-definition", referenceId: "definition-1" })]
    });
  });
});

function testApi(): ElsaStudioModuleApi {
  return {
    ai: {
      contextProviders: registry(),
      promptActions: registry(),
      tools: registry(),
      proposalRenderers: registry(),
      surfaces: registry(),
      dispatchPrompt: vi.fn(),
      onPrompt: vi.fn()
    }
  } as unknown as ElsaStudioModuleApi;
}

function registry<T>(): StudioContributionRegistry<T> {
  const items: T[] = [];
  return {
    add: item => items.push(item),
    list: () => [...items]
  };
}
