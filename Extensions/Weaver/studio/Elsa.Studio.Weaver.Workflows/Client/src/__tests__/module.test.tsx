import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioContributionRegistry, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getExecutableDetail, register } from "../module";

describe("weaver workflows module", () => {
  it("registers prompts and presentation on the canonical agent path", () => {
    const api = testApi(); register(api);
    expect(api.agent.promptActions.list().map(item => item.id)).toContain("weaver.workflows.propose-update");
    expect(api.agent.resultRenderers.list().map(item => item.id)).toContain("weaver.workflows.workflow-proposal-renderer");
  });

  it("builds workflow definition prompts with attachments", () => {
    const api = testApi();
    register(api);
    const action = api.agent.promptActions.list().find(item => item.id === "weaver.workflows.explain-definition");

    const prompt = action?.createPrompt({ id: "definition-1", name: "Onboard customer" });

    expect(prompt).toMatchObject({
      mode: "enqueue",
      attachments: [expect.objectContaining({ contentType: "workflow.request", sourceId: "definition-1" })]
    });
  });

  it("keeps the executable summary as the explain payload but directs Weaver to the detail tool", () => {
    const api = testApi();
    register(api);
    const action = api.agent.promptActions.list().find(item => item.id === "weaver.workflows.explain-executable");

    const summary = { artifactId: "artifact-1", artifactHash: "hash-1" };
    const prompt = action?.createPrompt(summary);

    expect(prompt?.message).toContain("get-executable-detail");
    expect(prompt?.attachments).toEqual([
      expect.objectContaining({ contentType: "workflow.request", sourceId: "artifact-1", content: { requestKind: "workflow-executable", referenceId: "artifact-1" } })
    ]);
  });

  describe("get-executable-detail tool", () => {
    it("uses the authenticated endpoint context for executable reads", async () => {
      const api = testApi();
      const detail = await getExecutableDetail(api.backend, { artifactId: "artifact/1" });
      expect(api.backend.http.getJson).toHaveBeenCalledWith("/runtime/workflows/executables/artifact%2F1");
      expect(detail).toEqual({ artifactId: "artifact/1" });
    });

    it("pins the layout to a source reference through the ref query parameter", async () => {
      const context = endpointContext();

      await getExecutableDetail(context, { artifactId: "artifact-1", sourceReferenceId: "ref 1" });

      expect(context.http.getJson).toHaveBeenCalledWith("/runtime/workflows/executables/artifact-1?ref=ref%201");
    });

    it("rejects a missing artifact id without calling the endpoint", async () => {
      const context = endpointContext();

      await expect(getExecutableDetail(context, { sourceReferenceId: "ref-1" })).rejects.toThrow(/artifactId/);
      await expect(getExecutableDetail(context, { artifactId: "   " })).rejects.toThrow(/artifactId/);
      expect(context.http.getJson).not.toHaveBeenCalled();
    });

    it("propagates endpoint errors (unknown artifact, denied permission) unmodified", async () => {
      const context = endpointContext();
      (context.http.getJson as ReturnType<typeof vi.fn>).mockRejectedValue(new Error("404 Not Found"));

      await expect(getExecutableDetail(context, { artifactId: "missing" })).rejects.toThrow("404 Not Found");
    });
  });
});

function testApi(): ElsaStudioModuleApi {
  return {
    backend: endpointContext(),
    agent: { promptActions: registry(), resultRenderers: registry() }
  } as unknown as ElsaStudioModuleApi;
}

function endpointContext(): StudioEndpointContext {
  return {
    baseUrl: "",
    http: {
      requestJson: vi.fn(),
      getJson: vi.fn(async (url: string) => {
        if (url === "/capabilities") {
          return {
            capabilities: [{
              id: "elsa.api.runtime",
              contractVersion: "1",
              links: [{ rel: "workflow-executable", href: "runtime/workflows/executables/{artifactId}", templated: true }]
            }]
          };
        }
        const artifactId = decodeURIComponent(url.split("/").pop()!.split("?")[0]);
        return { artifactId };
      }),
      postJson: vi.fn(),
      putJson: vi.fn(),
      deleteJson: vi.fn()
    }
  } as unknown as StudioEndpointContext;
}

function registry<T>(): StudioContributionRegistry<T> {
  const items: T[] = [];
  return {
    add: item => items.push(item),
    list: () => [...items]
  };
}
