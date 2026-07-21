import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  STRUCTURAL_DANGLING_TYPE,
  STRUCTURAL_NO_START_TYPE,
  combineValidationErrors,
  detectStructuralValidationErrors,
  fromDraftValidationError
} from "../draftValidation";
import { getDraftValidations, isDraftValidationsAvailable } from "../api/workflowDesign";
import { clearApiCapabilityCache } from "../api/capabilities";
import { parsePromotionValidationErrors } from "../workflow-editor/useWorkflowOperations";
import { ValidationPanel } from "../workflow-editor/editorPanels";
import { flowchartStructureKind } from "../flowchartStartNode";
import type { ActivityNode, WorkflowDraft } from "../workflowTypes";

function node(nodeId: string, extra: Record<string, unknown> = {}): ActivityNode {
  return { nodeId, activityVersionId: `${nodeId}-v`, inputs: [], outputs: [], ...extra };
}

function flowchart(activities: ActivityNode[], connections: Array<{ from: string; to: string }>, startNodeId?: string): ActivityNode {
  return node("root", {
    structure: {
      kind: flowchartStructureKind,
      schemaVersion: "1",
      payload: {
        activities,
        connections: connections.map(c => ({ source: { nodeId: c.from }, target: { nodeId: c.to } })),
        startNodeId: startNodeId ?? activities[0]?.nodeId ?? null
      }
    }
  });
}

describe("detectStructuralValidationErrors", () => {
  it("flags a flowchart activity with no connections as dangling", () => {
    const state = { rootActivity: flowchart([node("a"), node("b"), node("orphan")], [{ from: "a", to: "b" }]) };
    const errors = detectStructuralValidationErrors(state);
    expect(errors.map(e => e.path)).toContain("orphan");
    expect(errors.find(e => e.path === "orphan")?.type).toBe(STRUCTURAL_DANGLING_TYPE);
    expect(errors.some(e => e.path === "a" || e.path === "b")).toBe(false);
  });

  it("flags a connected-but-unreachable activity relative to the start node", () => {
    // a -> b (start a); c -> d is a separate island: connected to each other but unreachable from a.
    const state = { rootActivity: flowchart([node("a"), node("b"), node("c"), node("d")], [
      { from: "a", to: "b" },
      { from: "c", to: "d" }
    ], "a") };
    const errors = detectStructuralValidationErrors(state);
    const unreachable = errors.filter(e => e.type === STRUCTURAL_NO_START_TYPE).map(e => e.path).sort();
    expect(unreachable).toEqual(["c", "d"]);
  });

  it("returns no structural errors for a single-activity flowchart or a non-flowchart root", () => {
    expect(detectStructuralValidationErrors({ rootActivity: flowchart([node("only")], []) })).toEqual([]);
    expect(detectStructuralValidationErrors({ rootActivity: node("seq") })).toEqual([]);
    expect(detectStructuralValidationErrors(null)).toEqual([]);
  });
});

describe("fromDraftValidationError", () => {
  it("rebuilds a node/member path and carries the code through", () => {
    expect(fromDraftValidationError({ code: "InputOutput/MissingRequired", message: "missing", nodeId: "n1", member: "inputs/x" }))
      .toMatchObject({ type: "InputOutput/MissingRequired", message: "missing", path: "n1/inputs/x" });
    expect(fromDraftValidationError({ code: "Root/Missing", message: "no root", nodeId: null, member: null }).path).toBe("$workflow");
    expect(fromDraftValidationError({ code: "Wf/Var", message: "bad var", nodeId: null, member: "variables/v" }).path).toBe("$workflow/variables/v");
  });
});

describe("combineValidationErrors", () => {
  it("merges server, draft, and structural errors and de-duplicates identical entries", () => {
    const combined = combineValidationErrors({
      serverErrors: [{ code: "C1", message: "server issue", nodeId: "n1", member: null }],
      draftErrors: [{ type: "C1", message: "server issue", path: "n1" }, { type: "C2", message: "draft issue", path: "n2" }],
      structuralErrors: [{ type: STRUCTURAL_DANGLING_TYPE, message: "dangling", path: "n3" }]
    });
    // "C1 n1 server issue" appears in both server + draft form and collapses to one.
    expect(combined.filter(e => e.message === "server issue")).toHaveLength(1);
    expect(combined.map(e => e.message)).toEqual(["server issue", "draft issue", "dangling"]);
  });
});

describe("parsePromotionValidationErrors", () => {
  it("reads the FastEndpoints array shape and drops the summary line", () => {
    const error = { status: 409, payload: { errors: [
      { name: "$", reason: "Cannot promote draft 'd' to a version: 2 validation error(s) present." },
      { name: "n1/inputs/x", reason: "Required input 'x' is missing." },
      { name: "n2", reason: "Activity 'n2' is not connected." }
    ] } };
    expect(parsePromotionValidationErrors(error)).toEqual([
      "Required input 'x' is missing.",
      "Activity 'n2' is not connected."
    ]);
  });

  it("reads the dictionary shape and returns [] for an unstructured error", () => {
    const dict = { payload: { errors: { "n1": ["one"], "n2": ["two", "three"] } } };
    expect(parsePromotionValidationErrors(dict)).toEqual(["one", "two", "three"]);
    expect(parsePromotionValidationErrors({ payload: { detail: "boom" } })).toEqual([]);
    expect(parsePromotionValidationErrors(new Error("plain"))).toEqual([]);
  });
});

describe("draft validations API capability handling", () => {
  const draftId = "draft-1";
  function contextWith(links: Array<{ rel: string; href: string; templated?: boolean }>, getJson = vi.fn()) {
    return {
      baseUrl: `test://validations-${Math.random()}`,
      http: {
        getJson: vi.fn(async (url: string) => {
          if (url === "/capabilities") return { capabilities: [{ id: "elsa.api.workflow-design", contractVersion: "1", links }] };
          return getJson(url);
        })
      }
    } as unknown as StudioEndpointContext;
  }

  beforeEach(() => clearApiCapabilityCache());

  it("returns null (unavailable) when the backend omits the relation", async () => {
    const context = contextWith([{ rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true }]);
    expect(await isDraftValidationsAvailable(context)).toBe(false);
    expect(await getDraftValidations(context, draftId)).toBeNull();
  });

  it("fetches and filters validations when the relation is advertised", async () => {
    const inner = vi.fn(async () => ({ draftId, errors: [
      { code: "C1", message: "bad", nodeId: "n1", member: "inputs/x", severity: "Error" },
      { code: "C2", message: 42 } // malformed — filtered out
    ] }));
    const context = contextWith([
      { rel: "workflow-drafts", href: "design/workflows/drafts/{draftId}", templated: true },
      { rel: "workflow-draft-validations", href: "design/workflows/drafts/{draftId}/validations", templated: true }
    ], inner);

    expect(await isDraftValidationsAvailable(context)).toBe(true);
    const errors = await getDraftValidations(context, draftId);
    expect(errors).toEqual([{ code: "C1", message: "bad", nodeId: "n1", member: "inputs/x", severity: "Error" }]);
    expect(inner).toHaveBeenCalledWith("/design/workflows/drafts/draft-1/validations");
  });
});

describe("ValidationPanel", () => {
  let root: Root;
  let container: HTMLDivElement;
  beforeEach(() => {
    container = document.createElement("div");
    document.body.appendChild(container);
    root = createRoot(container);
  });
  afterEach(() => {
    flushSync(() => root.unmount());
    container.remove();
  });

  const draft = { validationErrors: [] } as unknown as WorkflowDraft;

  it("shows the combined errors and offers node navigation, overriding the draft's empty list", () => {
    const selected: string[] = [];
    flushSync(() => root.render(
      <ValidationPanel
        draft={draft}
        errors={[{ type: STRUCTURAL_DANGLING_TYPE, message: "Activity 'orphan' is not connected.", path: "orphan" }]}
        onRepair={() => {}}
        onSelectNode={id => selected.push(id)}
      />
    ));
    expect(container.querySelector(".wf-validation-summary")?.textContent).toContain("1 validation issue");
    const goTo = container.querySelector<HTMLButtonElement>(".wf-validation-repair")!;
    flushSync(() => goTo.click());
    expect(selected).toEqual(["orphan"]);
  });

  it("notes when server validation is unavailable on the empty state", () => {
    flushSync(() => root.render(<ValidationPanel draft={draft} errors={[]} onRepair={() => {}} unavailable />));
    expect(container.querySelector(".wf-validation.ok")?.textContent).toContain("server validation unavailable");
  });
});
