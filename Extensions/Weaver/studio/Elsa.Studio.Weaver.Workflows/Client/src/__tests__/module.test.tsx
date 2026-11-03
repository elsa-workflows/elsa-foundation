import React from "react";
import { describe, expect, it, vi } from "vitest";
import type { ElsaStudioModuleApi, StudioAgentResultRendererContribution, StudioAgentResultRendererProps, StudioContributionRegistry, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getExecutableDetail, register, WorkflowBatchResult, workflowBatchUndoControlId } from "../module";

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

  describe("workflows.batch-result renderer", () => {
    const preview = { typeId: "workflows.batch-result", schemaVersion: "1", data: {
      risk: "destructive", highComplexity: true,
      operations: [
        { index: 0, kind: "add", risk: "reviewRequired", summary: "Add Write Line as 'greet' into 'Main' [flow] / activities." },
        { index: 1, kind: "remove", risk: "destructive", summary: "Remove 'Old step' [old] from the root scope." }
      ]
    } };

    it("registers for the batch result kind under the resource-owning module", () => {
      const api = testApi(); register(api);
      expect(api.agent.resultRenderers.list().find(item => item.id === "weaver.workflows.batch-result"))
        .toMatchObject({ moduleId: "Elsa.Studio.Workflows", resultTypes: ["workflows.batch-result"], schemaVersions: ["1"] });
    });

    it("labels each operation's risk, the batch's highest risk and its complexity while proposed", () => {
      const text = render({ proposal: { status: "awaiting-approval" }, result: undefined, preparedInvocation: { preview, risk: "destructive" } as never });
      expect(text).toContain("Proposed — awaiting your review");
      expect(text).toContain("Destructive");
      expect(text).toContain("Review required");
      expect(text).toContain("High complexity");
      expect(text).toContain("Undo does not restore them");
      expect(text).toContain("Saving and publishing remain your explicit actions");
    });

    it("reports an applied batch as local working state and embeds the Studio-owned Undo only then", () => {
      const control = vi.fn(() => <button type="button">Studio undo</button>);
      const undoControl = { id: workflowBatchUndoControlId, displayName: "Undo", component: control } as unknown as StudioAgentResultRendererContribution;
      const proposed = render({ proposal: { status: "awaiting-approval" }, result: undefined, preparedInvocation: { preview } as never }, undoControl);
      expect(proposed).not.toContain("Studio undo");
      const applied = render({ proposal: null, result: undefined, preparedInvocation: { preview } as never,
        toolResult: { toolCallId: "call", toolName: "workflow_apply_operation_batch", succeeded: true, content: "Applied all 2 operations.", payload: preview,
          receipt: { invocationId: "i", before: {} as never, after: {} as never, scope: "clientWorkingState", evidence: preview } } }, undoControl);
      expect(applied).toContain("Applied locally to the unsaved draft — not saved or published");
      expect(applied).toContain("Studio undo");
      expect(control).toHaveBeenCalled();
    });

    it("reports a refused batch as nothing changed", () => {
      expect(render({ proposal: { status: "denied" }, result: undefined, preparedInvocation: { preview } as never })).toContain("Refused — nothing changed");
    });
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

function render(props: StudioAgentResultRendererProps, undoControl?: StudioAgentResultRendererContribution) {
  return text(<WorkflowBatchResult {...props} undoControl={undoControl} />);
}

// The renderer and the embedded control are hook-free, so their element tree is read directly: this package's tests do
// not depend on a DOM renderer.
function text(node: React.ReactNode): string {
  if (node === null || node === undefined || typeof node === "boolean") return "";
  if (typeof node === "string" || typeof node === "number") return String(node);
  if (Array.isArray(node)) return node.map(text).join("");
  if (React.isValidElement(node)) {
    const element = node as React.ReactElement<{ children?: React.ReactNode }>;
    if (typeof element.type === "function") return text((element.type as (props: unknown) => React.ReactNode)(element.props));
    return text(element.props.children);
  }
  return "";
}
