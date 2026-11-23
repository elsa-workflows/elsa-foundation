import React from "react";
import { flushSync } from "react-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CasePanel } from "../CasePanel";
import { RuleExplanation, labelsForXml } from "../RuleExplanation";
import { editTable, newDecisionXml, parseTable } from "../decisionXml";
import type { Comparison, DecisionCase, TestRun } from "../decisionsApi";
import { asset, button, click, render, stubApi, type } from "./helpers";

const disposals: Array<() => void> = [];
afterEach(() => { disposals.splice(0).forEach(dispose => dispose()); });
function mount(content: React.ReactNode) { const result = render(content); disposals.push(result.dispose); return result; }

function caseMenuItem(host: HTMLElement, name: string, label: string) {
  const trigger = host.querySelector<HTMLButtonElement>(`button[aria-label="Actions for ${name}"]`)!;
  if (trigger.getAttribute("aria-expanded") !== "true") click(trigger);
  const menu = document.getElementById(trigger.getAttribute("aria-controls")!)!;
  return Array.from(menu.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')).find(item => item.textContent === label)!;
}

function twoRuleXml() {
  let xml = editTable(newDecisionXml(), { kind: "add-clause", output: false });
  const secondInput = parseTable(xml).inputs[1];
  xml = editTable(xml, { kind: "clause", id: secondInput.id, output: false, name: "gender", type: "string" });
  xml = editTable(xml, { kind: "add-rule" });
  return xml;
}

function makeCase(id: string, name: string, amount: string, gender: string, expectedResult = "Approved", expectedStatus = "Success"): DecisionCase {
  return {
    id,
    name,
    revision: `${id}-r1`,
    inputs: { amount: { kind: "number", value: amount }, gender: { kind: "string", value: gender } },
    expectedStatus,
    expectedResult: expectedStatus === "Success" ? { kind: "string", value: expectedResult } : null
  };
}

describe("Master-detail Decision test runner", () => {
  it("renders one selected case detail at a time, auto-selects the first failure, floats failures first, and supports keyboard navigation", async () => {
    const xml = twoRuleXml(), table = parseTable(xml), api = stubApi();
    const cases = [
      makeCase("c1", "Passed A", "10", "female", "Approved"),
      makeCase("c2", "Failed output", "25", "male", "Approved"),
      makeCase("c3", "Passed B", "30", "female", "Approved"),
      makeCase("c4", "Failed hit policy", "12", "male", "Approved")
    ];
    const comparisons: Comparison[] = [
      { caseId: "c1", name: "Passed A", passed: true, status: "Success", expected: { kind: "string", value: "Approved" }, actual: { kind: "string", value: "Approved" }, trace: [{ ruleId: table.rules[0].id, matched: true, selected: true, cells: [{ inputId: table.inputs[0].id, truth: "true" }] }] },
      { caseId: "c2", name: "Failed output", passed: false, status: "Success", failurePath: "$.value", expected: { kind: "string", value: "Approved" }, actual: { kind: "string", value: "Denied" }, trace: [
        { ruleId: table.rules[0].id, matched: false, selected: false, cells: [{ inputId: table.inputs[0].id, truth: "true" }, { inputId: table.inputs[1].id, truth: "false" }] },
        { ruleId: table.rules[1].id, matched: true, selected: true, cells: [{ inputId: table.inputs[0].id, truth: "true" }, { inputId: table.inputs[1].id, truth: "true" }] }
      ] },
      { caseId: "c3", name: "Passed B", passed: true, status: "Success", expected: { kind: "string", value: "Approved" }, actual: { kind: "string", value: "Approved" }, trace: [{ ruleId: table.rules[0].id, matched: true, selected: true, cells: [{ inputId: table.inputs[0].id, truth: "true" }] }] },
      { caseId: "c4", name: "Failed hit policy", passed: false, status: "HitPolicyViolation", failurePath: "$.status", trace: [
        { ruleId: table.rules[0].id, matched: true, selected: false, cells: [{ inputId: table.inputs[0].id, truth: "true" }] },
        { ruleId: table.rules[1].id, matched: true, selected: false, cells: [{ inputId: table.inputs[0].id, truth: "true" }] }
      ] }
    ];
    const runResult: TestRun = { id: "run-1", candidateId: "r1", candidateHash: asset.sourceHash, passed: false, hasDetailedPayload: true, comparisons, evaluatedAt: asset.createdAt, completedAt: asset.createdAt };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: cases } : { items: [] });
    api.backend.http.postJson.mockResolvedValue(runResult as never);

    const { host } = mount(<CasePanel api={api} asset={{ ...asset, xml }} xml={xml} dirty={false} />);
    await vi.waitFor(() => expect(host.querySelectorAll(".decision-case-row")).toHaveLength(4));

    click(button(host, "Run saved cases"));
    await vi.waitFor(() => expect(host.textContent).toContain("Cases failed"));

    // Only one detail card is rendered (O(1) height regardless of suite size).
    expect(host.querySelectorAll(".decision-case-detail")).toHaveLength(1);
    expect(host.querySelector(".decision-case-detail h4")?.textContent).toBe("Failed output");
    expect(host.textContent).toContain("2 passed");
    expect(host.textContent).toContain("2 failed");
    expect(host.textContent).toContain("Output mismatch at $.value");
    expect(host.textContent).toContain("Expected: Approved");
    expect(host.textContent).toContain("Actual: Denied");
    // Inputs for the selected case are surfaced in the inspector.
    expect(host.querySelector(".decision-inspector-inputs")?.textContent).toContain("amount25");
    expect(host.querySelector(".decision-inspector-inputs")?.textContent).toContain("gendermale");

    // Failed cases float to the top while preserving order within groups.
    const orderedNames = Array.from(host.querySelectorAll(".decision-case-row-name")).map(el => el.textContent);
    expect(orderedNames).toEqual(["Failed output", "Failed hit policy", "Passed A", "Passed B"]);

    // ArrowDown moves selection & focus to the next row (Failed hit policy) and shows its plain-language status verdict.
    const firstSelect = host.querySelectorAll<HTMLButtonElement>(".decision-case-select")[0];
    firstSelect.focus();
    flushSync(() => firstSelect.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true, cancelable: true })));
    expect(host.querySelector(".decision-case-detail h4")?.textContent).toBe("Failed hit policy");
    expect(host.textContent).toContain("Outcome mismatch: expected Success, got HitPolicyViolation");
    expect(host.textContent).toContain("Multiple rules matched under a hit policy that allows at most one.");
    expect(document.activeElement).toBe(host.querySelectorAll<HTMLButtonElement>(".decision-case-select")[1]);

    // End / Home jump to the last / first visible row.
    flushSync(() => (document.activeElement as HTMLElement).dispatchEvent(new KeyboardEvent("keydown", { key: "End", bubbles: true, cancelable: true })));
    expect(host.querySelector(".decision-case-detail h4")?.textContent).toBe("Passed B");
    expect(host.textContent).toContain("Outcome matched (Success)");
  });

  it("filters cases by result status and search query with a Clear filters recovery action", async () => {
    const xml = twoRuleXml(), api = stubApi();
    const cases = [
      makeCase("c1", "Adult approval", "100", "female"),
      makeCase("c2", "Minor review", "10", "male")
    ];
    const runResult: TestRun = {
      id: "run-filter",
      candidateId: "r1",
      candidateHash: asset.sourceHash,
      passed: false,
      hasDetailedPayload: true,
      comparisons: [
        { caseId: "c1", name: "Adult approval", passed: true, status: "Success", expected: { kind: "string", value: "Approved" }, actual: { kind: "string", value: "Approved" } },
        { caseId: "c2", name: "Minor review", passed: false, status: "NoMatch", failurePath: "$.status" }
      ],
      evaluatedAt: asset.createdAt,
      completedAt: asset.createdAt
    };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: cases } : { items: [] });
    api.backend.http.postJson.mockResolvedValue(runResult as never);

    const { host } = mount(<CasePanel api={api} asset={{ ...asset, xml }} xml={xml} dirty={false} />);
    await vi.waitFor(() => expect(host.querySelectorAll(".decision-case-row")).toHaveLength(2));
    click(button(host, "Run saved cases"));
    await vi.waitFor(() => expect(host.textContent).toContain("Failed (1)"));

    click(button(host, "Failed (1)"));
    expect(Array.from(host.querySelectorAll(".decision-case-row-name")).map(el => el.textContent)).toEqual(["Minor review"]);

    click(button(host, "Passed (1)"));
    expect(Array.from(host.querySelectorAll(".decision-case-row-name")).map(el => el.textContent)).toEqual(["Adult approval"]);

    click(button(host, "All (2)"));
    const search = host.querySelector<HTMLInputElement>('input[aria-label="Filter saved cases"]')!;
    type(search, "nonexistent");
    expect(host.textContent).toContain("No cases match the current filter.");
    click(button(host, "Clear filters"));
    expect(host.querySelectorAll(".decision-case-row")).toHaveLength(2);
  });

  it("merges single-case runs and Re-run failed results into the active run without losing other cases", async () => {
    const xml = twoRuleXml(), api = stubApi();
    const cases = [
      makeCase("c1", "Case 1", "10", "female"),
      makeCase("c2", "Case 2", "20", "male"),
      makeCase("c3", "Case 3", "30", "male")
    ];
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: cases } : { items: [] });
    api.backend.http.postJson
      .mockResolvedValueOnce({
        id: "run-1", candidateId: "r1", candidateHash: asset.sourceHash, passed: false, hasDetailedPayload: true,
        comparisons: [
          { caseId: "c1", name: "Case 1", passed: true, status: "Success", expected: { kind: "string", value: "Approved" }, actual: { kind: "string", value: "Approved" } },
          { caseId: "c2", name: "Case 2", passed: false, status: "Success", failurePath: "$.value", expected: { kind: "string", value: "Approved" }, actual: { kind: "string", value: "Denied" } },
          { caseId: "c3", name: "Case 3", passed: false, status: "NoMatch", failurePath: "$.status" }
        ],
        evaluatedAt: asset.createdAt, completedAt: asset.createdAt
      } as never)
      .mockResolvedValueOnce({
        id: "run-2", candidateId: "r1", candidateHash: asset.sourceHash, passed: true, hasDetailedPayload: true,
        comparisons: [
          { caseId: "c2", name: "Case 2", passed: true, status: "Success", expected: { kind: "string", value: "Approved" }, actual: { kind: "string", value: "Approved" } }
        ],
        evaluatedAt: asset.createdAt, completedAt: asset.createdAt
      } as never)
      .mockResolvedValueOnce({
        id: "run-3", candidateId: "r1", candidateHash: asset.sourceHash, passed: true, hasDetailedPayload: true,
        comparisons: [
          { caseId: "c3", name: "Case 3", passed: true, status: "Success", expected: { kind: "string", value: "Approved" }, actual: { kind: "string", value: "Approved" } }
        ],
        evaluatedAt: asset.createdAt, completedAt: asset.createdAt
      } as never);

    const { host } = mount(<CasePanel api={api} asset={{ ...asset, xml }} xml={xml} dirty={false} />);
    await vi.waitFor(() => expect(host.querySelectorAll(".decision-case-row")).toHaveLength(3));

    click(button(host, "Run saved cases"));
    await vi.waitFor(() => expect(host.textContent).toContain("Re-run failed (2)"));
    expect(host.querySelector(".decision-case-detail h4")?.textContent).toBe("Case 2");

    // Run single failing case (Case 2) via inspector Run button.
    click(button(host, "Run"));
    await vi.waitFor(() => expect(host.textContent).toContain("2 passed"));
    expect(api.backend.http.postJson).toHaveBeenNthCalledWith(2, "/tenant/decisions/approval/test", expect.objectContaining({
      cases: [{ id: "c2", revision: "c2-r1" }]
    }));
    expect(host.textContent).toContain("1 failed");
    expect(host.textContent).toContain("Re-run failed (1)");

    // Filter to Failed (1), then click Re-run failed (1) — when it passes, filter resets to All and summary says All cases passed.
    click(button(host, "Failed (1)"));
    click(button(host, "Re-run failed (1)"));
    await vi.waitFor(() => expect(host.textContent).toContain("All cases passed"));
    expect(api.backend.http.postJson).toHaveBeenNthCalledWith(3, "/tenant/decisions/approval/test", expect.objectContaining({
      cases: [{ id: "c3", revision: "c3-r1" }]
    }));
    expect(host.textContent).toContain("3 passed");
    expect(host.querySelectorAll(".decision-case-row")).toHaveLength(3);
  });

  it("duplicates a saved case from the row menu with prefilled inputs and expectations", async () => {
    const xml = twoRuleXml(), api = stubApi();
    const original = makeCase("c1", "Adult male", "25", "male", "Approved");
    let items: DecisionCase[] = [original];
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items } : { items: [] });
    api.backend.http.postJson.mockImplementation(async (_path, body) => {
      const created = { ...(body as Omit<DecisionCase, "id" | "revision">), id: "c2", revision: "c2-r1" };
      items = [...items, created];
      return created as never;
    });

    const { host } = mount(<CasePanel api={api} asset={{ ...asset, xml }} xml={xml} dirty={false} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Adult male"));

    click(caseMenuItem(host, "Adult male", "Duplicate"));
    await vi.waitFor(() => expect(host.querySelector('form[aria-label="New decision case"]')).not.toBeNull());
    expect(host.querySelector<HTMLInputElement>('input[data-dialog-initial-focus]')?.value).toBe("Adult male copy");
    expect(host.querySelector<HTMLInputElement>('[aria-label="Case input amount"]')?.value).toBe("25");
    expect(host.querySelector<HTMLInputElement>('[aria-label="Case input gender"]')?.value).toBe("male");

    click(button(host, "Save case"));
    await vi.waitFor(() => expect(host.querySelector('[role="dialog"]')).toBeNull());
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/cases", expect.objectContaining({
      name: "Adult male copy",
      inputs: original.inputs,
      expectedStatus: "Success",
      expectedResult: original.expectedResult
    }));
    expect(host.querySelectorAll(".decision-case-row")).toHaveLength(2);
  });

  it("supports Matched-only filtering and table-level Rule IDs toggle in RuleExplanation", () => {
    const xml = twoRuleXml(), table = parseTable(xml);
    const trace = [
      { ruleId: table.rules[0].id, matched: false, selected: false, cells: [{ inputId: table.inputs[0].id, truth: "false" }] },
      { ruleId: table.rules[1].id, matched: true, selected: true, cells: [{ inputId: table.inputs[0].id, truth: "true" }] }
    ];
    const { host } = mount(<RuleExplanation trace={trace} labels={labelsForXml(xml)} />);

    expect(host.querySelectorAll("tbody tr")).toHaveLength(2);
    expect(host.querySelector("details")).toBeNull();
    expect(host.textContent).toContain("1 matched · 1 selected");
    expect(host.textContent).not.toContain(table.rules[0].id);

    click(button(host, "Matched (1)"));
    expect(host.querySelectorAll("tbody tr")).toHaveLength(1);
    expect(host.querySelector("tbody tr th")?.textContent).toBe("Rule 2");

    click(button(host, "Rule IDs"));
    expect(host.querySelector("tbody tr th")?.textContent).toContain(table.rules[1].id);

    click(button(host, "All (2)"));
    expect(host.querySelectorAll("tbody tr")).toHaveLength(2);
  });

  it("switches cleanly between Evaluate and Case results when an outputSupplement is provided", async () => {
    const xml = twoRuleXml(), api = stubApi();
    const saved = makeCase("c1", "Boundary", "20", "male");
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/cases") ? { items: [saved] } : { items: [] });

    const { host } = mount(
      <CasePanel
        api={api}
        asset={{ ...asset, xml }}
        candidate={{ id: "v1", assetId: asset.id, number: 1, name: asset.name, xml, sourceHash: asset.sourceHash, publishedAt: asset.createdAt }}
        xml={xml}
        dirty={false}
        outputSupplement={<section data-testid="eval-panel">Evaluate surface</section>}
      />
    );
    await vi.waitFor(() => expect(host.textContent).toContain("Boundary"));

    // Published version defaults to Evaluate view so ad-hoc evaluation is immediately accessible.
    expect(host.querySelector('[data-testid="eval-panel"]')).not.toBeNull();

    // Clicking a case row switches the right pane to Case results.
    click(host.querySelector<HTMLButtonElement>(".decision-case-select")!);
    expect(host.querySelector('[data-testid="eval-panel"]')).toBeNull();
    expect(host.querySelector(".decision-case-detail h4")?.textContent).toBe("Boundary");

    // Switching back to Evaluate restores the evaluation panel.
    click(host.querySelector<HTMLButtonElement>('[role="tab"][data-tab-id="evaluate"]')!);
    expect(host.querySelector('[data-testid="eval-panel"]')).not.toBeNull();
  });
});
