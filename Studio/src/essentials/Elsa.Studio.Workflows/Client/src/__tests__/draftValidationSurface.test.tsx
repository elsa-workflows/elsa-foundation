import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  STRUCTURAL_DANGLING_TYPE,
  STRUCTURAL_NO_START_TYPE,
  STRUCTURAL_START_TRIGGER_TYPE,
  combineValidationErrors,
  detectStructuralValidationErrors,
  fromDraftValidationError
} from "../draftValidation";
import { getDraftValidations, isDraftValidationsAvailable } from "../api/draftValidations";
import { clearApiCapabilityCache } from "../api/capabilities";
import { parsePromotionValidationErrors } from "../workflow-editor/useWorkflowOperations";
import { ValidationPanel } from "../workflow-editor/editorPanels";
import { flowchartStructureKind } from "../flowchartStartNode";
import type { ActivityNode, DraftValidationError, WorkflowDraft } from "../workflowTypes";

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

  it("appends designer-side local contribution errors after structural checks", () => {
    const combined = combineValidationErrors({
      structuralErrors: [{ type: STRUCTURAL_DANGLING_TYPE, message: "dangling", path: "n3" }],
      localErrors: [{ type: "Decision/InputMembership", message: "notes is not in this table.", path: "eval-1/inputs/Inputs" }]
    });
    expect(combined.map(e => e.message)).toEqual(["dangling", "notes is not in this table."]);
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

describe("start-trigger detection", () => {
  const startTrigger = (nodeId: string) => node(nodeId, {
    canStartWorkflow: { typeName: "System.Boolean", expression: { type: "Literal", value: true } }
  });

  it("flags a start trigger that something connects into and that is not the start node", () => {
    // Spec 238: publishing refuses this placement, so the designer surfaces it.
    const state = { rootActivity: flowchart([node("if"), startTrigger("http")], [{ from: "if", to: "http" }], "if") };

    const errors = detectStructuralValidationErrors(state);

    const misplaced = errors.find(error => error.type === STRUCTURAL_START_TRIGGER_TYPE);
    expect(misplaced?.path).toBe("http");
  });

  it("stops flagging it once it is the start node", () => {
    const state = { rootActivity: flowchart([node("if"), startTrigger("http")], [{ from: "if", to: "http" }], "http") };

    expect(detectStructuralValidationErrors(state).some(error => error.type === STRUCTURAL_START_TRIGGER_TYPE))
      .toBe(false);
  });

  it("accepts a second start trigger heading its own branch: neither misplaced nor unreachable", () => {
    const state = {
      rootActivity: flowchart(
        [startTrigger("http-a"), node("after-a"), startTrigger("http-b"), node("after-b")],
        [{ from: "http-a", to: "after-a" }, { from: "http-b", to: "after-b" }],
        "http-a")
    };

    const errors = detectStructuralValidationErrors(state);

    expect(errors.some(error => error.type === STRUCTURAL_START_TRIGGER_TYPE)).toBe(false);
    expect(errors.filter(error => error.type === STRUCTURAL_NO_START_TYPE)).toEqual([]);
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

  async function endpointErrors(errors: DraftValidationError[]) {
    const context = {
      baseUrl: `test://validation-panel-${Math.random()}`,
      http: {
        getJson: vi.fn(async (url: string) => url === "/capabilities"
          ? { capabilities: [{ id: "elsa.api.workflow-design", contractVersion: "1", links: [
            { rel: "workflow-draft-validations", href: "design/workflows/drafts/{draftId}/validations", templated: true }
          ] }] }
          : { draftId: "draft-1", errors })
      }
    } as unknown as StudioEndpointContext;
    return getDraftValidations(context, "draft-1");
  }

  it("renders mixed endpoint error and warning severities after authoritative projection and de-duplication", async () => {
    const missingRead = {
      code: "Scripts/UnresolvedVariableRead", message: "VF-READ-002: The script reads variable 'missingForValidation', but no variable with that name is visible.",
      nodeId: "message", member: "inputs/text", severity: "Error"
    };
    const computedRead = {
      code: "Scripts/ComputedVariableRead", message: "VF-READ-004: The script reads variables by a computed name. It could read: 'order'.",
      nodeId: "message", member: "inputs/text", severity: "Warning"
    };
    const errors = combineValidationErrors({
      serverErrors: await endpointErrors([missingRead, computedRead]),
      // A stale reconciled severity must not override the authoritative endpoint's warning or add another finding.
      draftErrors: [{ type: computedRead.code, message: computedRead.message, path: "message/inputs/text", severity: "Error" }]
    });
    flushSync(() => root.render(<ValidationPanel draft={draft} errors={errors} onRepair={() => {}} />));

    const summary = container.querySelector(".wf-validation-summary")?.textContent ?? "";
    expect(summary).toBe("1 error · 1 warning");
    expect(summary).not.toContain("publishing is not blocked");
    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-validation-toggle")!.click());
    expect(Array.from(container.querySelectorAll(".wf-validation-severity")).map(badge => badge.textContent)).toEqual(["Error", "Warning"]);
    expect(container.querySelector('[data-severity="error"]')?.closest("li")?.textContent).toContain("VF-READ-002");
    expect(container.querySelector('[data-severity="warning"]')?.closest("li")?.textContent).toContain("VF-READ-004");
    expect(container.querySelectorAll(".wf-validation-item")).toHaveLength(2);
  });

  it("renders warnings-only endpoint findings as non-blocking after projection and de-duplication", async () => {
    const computedRead = {
      code: "Scripts/ComputedVariableRead", message: "VF-READ-004: The script reads variables by a computed name. It could read: 'order'.",
      nodeId: "message", member: "inputs/text", severity: "Warning"
    };
    const errors = combineValidationErrors({
      serverErrors: await endpointErrors([computedRead]),
      draftErrors: [{ type: computedRead.code, message: computedRead.message, path: "message/inputs/text", severity: "Error" }]
    });
    const selected: Array<{ id: string; path?: string }> = [];
    flushSync(() => root.render(
      <ValidationPanel draft={draft} errors={errors} onRepair={() => {}} onSelectNode={(id, path) => selected.push({ id, path })} />
    ));

    expect(container.querySelector(".wf-validation-summary")?.textContent).toBe("1 warning · publishing is not blocked");
    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-validation-toggle")!.click());
    expect(container.querySelectorAll('[data-severity="error"]')).toHaveLength(0);
    expect(container.querySelector('[data-severity="warning"]')?.textContent).toBe("Warning");
    expect(container.querySelectorAll(".wf-validation-item")).toHaveLength(1);
    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-validation-node-action")!.click());
    expect(selected).toEqual([{ id: "message", path: "message/inputs/text" }]);
  });

  it("keeps full diagnostics collapsed until expanded and offers input navigation", () => {
    const selected: Array<{ id: string; path?: string }> = [];
    const message = 'Required input "Text" on Write Line is empty. Check the Conversion setting on this input and confirm that the selected profile supports the source representation.';
    flushSync(() => root.render(
      <ValidationPanel
        draft={draft}
        errors={[{ type: "InputOutput/MissingRequired", message, path: "orphan/inputs/Text" }]}
        onRepair={() => {}}
        onSelectNode={(id, path) => selected.push({ id, path })}
      />
    ));
    expect(container.querySelector(".wf-validation-summary")?.textContent).toContain("1 validation issue");
    expect(container.querySelector<HTMLButtonElement>(".wf-validation-toggle")?.getAttribute("aria-expanded")).toBe("false");
    expect(container.querySelector(".wf-validation-details")?.hasAttribute("hidden")).toBe(true);
    const toggle = container.querySelector<HTMLButtonElement>(".wf-validation-toggle")!;
    flushSync(() => toggle.click());
    expect(container.querySelector(".wf-validation-message")?.textContent).toBe(message);
    expect(container.querySelector(".wf-validation-location")?.textContent).toContain("Inputs → Text");
    expect(container.querySelector(".wf-validation-hint")?.textContent).toContain("Set a value for this required input");
    const goTo = container.querySelector<HTMLButtonElement>(".wf-validation-node-action")!;
    expect(goTo.textContent).toContain("Go to input");
    flushSync(() => goTo.click());
    expect(selected).toEqual([{ id: "orphan", path: "orphan/inputs/Text" }]);
  });

  it("keeps unresolved-variable Repair path-aware so the affected input can receive focus", () => {
    const selected: Array<{ id: string; path?: string }> = [];
    const repair = vi.fn();
    flushSync(() => root.render(
      <ValidationPanel
        draft={draft}
        errors={[{ type: "Expressions/UnresolvedVariable", message: "Variable is not visible from this activity's scope.", path: "orphan/inputs/Text" }]}
        onRepair={repair}
        onSelectNode={(id, path) => selected.push({ id, path })}
      />
    ));

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-validation-toggle")!.click());
    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-validation-repair")!.click());
    expect(selected).toEqual([{ id: "orphan", path: "orphan/inputs/Text" }]);
    expect(repair).not.toHaveBeenCalled();
  });

  it("identifies non-blocking warnings and keeps their node navigation available", () => {
    const selected: string[] = [];
    flushSync(() => root.render(
      <ValidationPanel
        draft={draft}
        errors={[{
          type: "CompensationScope/ReplaySafeStep",
          message: "Demo logging needs no action; use an External activity for real operations.",
          path: "scope/steps/0",
          severity: " Warning "
        }]}
        onRepair={() => {}}
        onSelectNode={id => selected.push(id)}
      />
    ));

    expect(container.querySelector(".wf-validation-summary")?.textContent).toContain("1 warning · publishing is not blocked");
    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-validation-toggle")!.click());
    expect(container.querySelector(".wf-validation-message")?.textContent).toContain("Demo logging needs no action");
    const goTo = container.querySelector<HTMLButtonElement>(".wf-validation-node-action")!;
    expect(goTo.textContent).toContain("Go to");
    flushSync(() => goTo.click());
    expect(selected).toEqual(["scope"]);
  });

  it("counts missing-severity validations as errors and does not reassure when errors and warnings coexist", () => {
    flushSync(() => root.render(
      <ValidationPanel
        draft={draft}
        errors={[
          { type: "CompensationScope/ReplaySafeStep", message: "A non-blocking warning.", path: "scope/steps/0", severity: "Warning" },
          { type: "InputOutput/MissingRequired", message: "A blocking issue.", path: "write-line/inputs/text" }
        ]}
        onRepair={() => {}}
        onSelectNode={() => {}}
      />
    ));

    const summary = container.querySelector(".wf-validation-summary")?.textContent ?? "";
    expect(summary).toContain("1 error · 1 warning");
    expect(summary).not.toContain("publishing is not blocked");
  });

  it("offers a one-click start-node fix for both start faults", () => {
    const madeStart: string[] = [];
    flushSync(() => root.render(
      <ValidationPanel
        draft={draft}
        errors={[
          { type: STRUCTURAL_START_TRIGGER_TYPE, message: "Activity 'http' can start a workflow…", path: "http" },
          { type: STRUCTURAL_NO_START_TYPE, message: "Activity 'write' cannot be reached from the start node.", path: "write" }
        ]}
        onRepair={() => {}}
        onSetAsStartNode={id => madeStart.push(id)}
      />
    ));

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-validation-toggle")!.click());
    const fixes = Array.from(container.querySelectorAll<HTMLButtonElement>(".wf-validation-repair"))
      .filter(button => button.textContent?.includes("Set as start"));
    expect(fixes).toHaveLength(2);
    flushSync(() => fixes[0].click());
    flushSync(() => fixes[1].click());
    expect(madeStart).toEqual(["http", "write"]);
  });

  it("offers no start-node fix for unrelated errors, or when the canvas has no start", () => {
    flushSync(() => root.render(
      <ValidationPanel
        draft={draft}
        errors={[{ type: STRUCTURAL_DANGLING_TYPE, message: "Activity 'orphan' is not connected.", path: "orphan" }]}
        onRepair={() => {}}
        onSetAsStartNode={() => {}}
      />
    ));
    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-validation-toggle")!.click());
    expect(container.textContent).not.toContain("Set as start");

    flushSync(() => root.render(
      <ValidationPanel
        draft={draft}
        errors={[{ type: STRUCTURAL_START_TRIGGER_TYPE, message: "Activity 'http' can start a workflow…", path: "http" }]}
        onRepair={() => {}}
      />
    ));
    expect(container.textContent).not.toContain("Set as start");
  });

  it("notes when server validation is unavailable on the empty state", () => {
    flushSync(() => root.render(<ValidationPanel draft={draft} errors={[]} onRepair={() => {}} unavailable />));
    expect(container.textContent).toMatch(/server validation unavailable/i);
    expect(container.textContent).not.toContain("No validation issues found");
  });

  it("does not claim no validation errors while local contribution checks are pending", () => {
    flushSync(() => root.render(<ValidationPanel draft={draft} errors={[]} onRepair={() => {}} pending />));
    expect(container.textContent).toContain("Checking validation");
    expect(container.textContent).not.toContain("No validation issues found");
  });

  it("keeps known findings visible while server validation is still checking or unavailable", () => {
    const message = "This warning remains available while validation updates.";
    flushSync(() => root.render(
      <ValidationPanel
        draft={draft}
        errors={[{ type: "CompensationScope/ReplaySafeStep", message, path: "scope/steps/0", severity: "Warning" }]}
        onRepair={() => {}}
        pending
        unavailable
      />
    ));

    expect(container.querySelector(".wf-validation-summary")?.textContent).toContain("Still checking");
    expect(container.textContent).toMatch(/server validation unavailable/i);
    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-validation-toggle")!.click());
    expect(container.querySelector(".wf-validation-message")?.textContent).toBe(message);
  });
});
