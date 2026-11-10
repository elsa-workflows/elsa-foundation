import React from "react";
import { describe, expect, it } from "vitest";
import type { ElsaStudioModuleApi, StudioWeaverResultRendererProps, StudioContributionRegistry } from "@elsa-workflows/studio-sdk";
import { presentsResourceModule } from "@elsa-workflows/studio-sdk";
import { register, WorkflowBatchResult } from "../module";

describe("weaver workflows module", () => {
  it("registers prompts and presentation on the canonical agent path", () => {
    const api = testApi(); register(api);
    expect(api.weaver.promptActions.list().map(item => item.id)).toContain("weaver.workflows.propose-update");
    expect(api.weaver.resultRenderers.list().map(item => item.id)).toEqual(["weaver.workflows.batch-result", "weaver.workflows.library-result",
      "weaver.workflows.list-state-result", "weaver.workflows.create-dialog-result"]);
  });

  it("reports a library write per workflow and never presents a partial result as done (spec 1193 SC-008)", async () => {
    const { WorkflowLibraryResult } = await import("../module");
    const partial = text(<WorkflowLibraryResult {...({ toolResult: { content: "partial", payload: { typeId: "workflows.library-result", schemaVersion: "1",
      data: { kind: "tags", status: "partial", targets: [{ definitionId: "one", status: "applied" }, { definitionId: "two", status: "conflict", message: "Reload" }] } } } } as unknown as StudioWeaverResultRendererProps)} />);
    expect(partial).toContain("Partly changed");
    expect(partial).not.toContain("Changed in the workflow library.");
    expect(partial).toContain("two: Changed elsewhere; reload");
    const review = text(<WorkflowLibraryResult {...({ preparedInvocation: { preview: { typeId: "workflows.library-result", schemaVersion: "1",
      data: { kind: "tags", status: "proposed", changes: [{ change: "create-and-add", tag: "Brand new" }], targets: [{ definitionId: "one" }] } } } } as unknown as StudioWeaverResultRendererProps)} />);
    expect(review).toContain("Creates the new tag(s) first");
  });

  it("removes the unbound proposal renderer and restores the explain-executable prompt against its registered tool (spec 1193 FR-019)", () => {
    const api = testApi(); register(api);
    expect(api.weaver.resultRenderers.list().flatMap(item => item.resultTypes)).not.toContain("workflows.proposal");
    const executable = api.weaver.promptActions.list().find(item => item.id === "weaver.workflows.explain-executable")!;
    const prompt = executable.createPrompt({ artifactId: "artifact-1", artifactHash: "sha256:abc" });
    expect(prompt.message).toContain("workflow_read_executable");
    expect(prompt.message).toContain("current draft");
    expect(prompt.message).not.toContain("get-executable-detail");
  });

  it("restores the explain-instance prompt against the registered Run read, read-only and never retargeted (spec 1193 slice G)", () => {
    const api = testApi(); register(api);
    const run = api.weaver.promptActions.list().find(item => item.id === "weaver.workflows.explain-instance")!;
    expect(run).toMatchObject({ placement: "selection", contextKind: "workflow-instance", moduleId: "Elsa.Studio.Weaver.Workflows" });
    const prompt = run.createPrompt({ workflowExecutionId: "run-1", activityExecutionId: "act-1", attached: true });
    expect(prompt.message).toContain("workflow_read_run");
    expect(prompt.message).toContain("act-1");
    expect(prompt.message).toContain("retry, cancel, delete or dispatch");
    expect(prompt.message).toContain("run page's own controls");
    expect(prompt.message).toContain("in progress");
    expect(prompt.message).toContain("never guess");
    expect(prompt.attachments).toEqual([]);
  });

  it("asks for one reviewed Operation Batch, never a direct update, and grounds risk findings in the designer's diagnostics", () => {
    const api = testApi(); register(api);
    const update = api.weaver.promptActions.list().find(item => item.id === "weaver.workflows.propose-update")!.createPrompt({ id: "definition-1" });
    expect(update.message).toContain("workflow_apply_operation_batch");
    expect(update.message).not.toContain("graph diff");
    const risks = api.weaver.promptActions.list().find(item => item.id === "weaver.workflows.find-draft-risks")!.createPrompt({ id: "definition-1" });
    expect(risks.message).toContain("validation diagnostics");
    expect(risks.message).toContain("revision");
  });

  it("builds workflow definition prompts with attachments", () => {
    const api = testApi();
    register(api);
    const action = api.weaver.promptActions.list().find(item => item.id === "weaver.workflows.explain-definition");

    const prompt = action?.createPrompt({ id: "definition-1", name: "Onboard customer" });

    expect(prompt).toMatchObject({
      mode: "enqueue",
      attachments: [expect.objectContaining({ contentType: "workflow.request", sourceId: "definition-1" })]
    });
  });

  describe("workflows.batch-result renderer", () => {
    const preview = { typeId: "workflows.batch-result", schemaVersion: "1", data: {
      risk: "destructive", highComplexity: true,
      operations: [
        { index: 0, kind: "add", risk: "reviewRequired", summary: "Add Write Line as 'greet' into 'Main' [flow] / activities." },
        { index: 1, kind: "remove", risk: "destructive", summary: "Remove 'Old step' [old] from the root scope." }
      ]
    } };

    it("registers under its own package for the Workflows module's results", () => {
      const api = testApi(); register(api);
      const renderer = api.weaver.resultRenderers.list().find(item => item.id === "weaver.workflows.batch-result")!;
      expect(renderer).toMatchObject({ moduleId: "Elsa.Studio.Weaver.Workflows", resourceModuleIds: ["Elsa.Studio.Workflows"],
        resultTypes: ["workflows.batch-result"], schemaVersions: ["1"] });
      // The host's own match: the Workflows module's results, never another module's.
      expect(presentsResourceModule(renderer, "Elsa.Studio.Workflows")).toBe(true);
      expect(presentsResourceModule(renderer, "Elsa.Studio.Weaver.Workflows")).toBe(false);
      expect(presentsResourceModule(renderer, null)).toBe(false);
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

    it("reports an applied batch by its receipt and leaves Undo and the saved state to the owning module", () => {
      const applied = render({ proposal: null, result: undefined, preparedInvocation: { preview } as never,
        toolResult: { toolCallId: "call", toolName: "workflow_apply_operation_batch", succeeded: true, content: "Applied all 2 operations.", payload: preview,
          receipt: { invocationId: "i", before: {} as never, after: {} as never, scope: "clientWorkingState", evidence: preview } } });
      expect(applied).toContain("Applied locally to the working draft as one Undo step");
      expect(applied).not.toContain("Undo whole change");
    });

    it("reports a refused batch as nothing changed", () => {
      expect(render({ proposal: { status: "denied" }, result: undefined, preparedInvocation: { preview } as never })).toContain("Refused — nothing changed");
    });
  });

});

function testApi(): ElsaStudioModuleApi {
  return {
    weaver: { promptActions: registry(), resultRenderers: registry() }
  } as unknown as ElsaStudioModuleApi;
}

function registry<T>(): StudioContributionRegistry<T> {
  const items: T[] = [];
  return {
    add: item => items.push(item),
    list: () => [...items]
  };
}

function render(props: StudioWeaverResultRendererProps) {
  return text(<WorkflowBatchResult {...props} />);
}

// The renderer is hook-free, so their element tree is read directly: this package's tests do
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
