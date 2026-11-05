import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DecisionsPage } from "../DecisionsPage";
import { CasePanel } from "../CasePanel";
import { DecisionVersionEditor } from "../DecisionVersionEditor";
import { EvaluationPanel } from "../EvaluationPanel";
import { studioNavigationRequestedEvent } from "@elsa-workflows/studio-sdk";
import { newDecisionXml } from "../decisionXml";
import { asset, button, click, render, select, stubApi, type } from "./helpers";
const disposals: Array<() => void> = [];
afterEach(() => { disposals.splice(0).forEach(dispose => dispose()); });
function mount(content: React.ReactNode) { const result = render(content); disposals.push(result.dispose); return result; }
describe("Decisions Studio journeys", () => {
  it("distinguishes a populated collection, an unmatched search and an empty collection", async () => {
    const api = stubApi(), navigate = vi.fn(), { host, update } = mount(<DecisionsPage api={api} navigate={navigate} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Purchase approval"));
    type(host.querySelector<HTMLInputElement>('[aria-label="Search decisions"]')!, "unknown");
    expect(host.textContent).toContain("No matching decisions");
    click(button(host, "Clear search")); expect(host.textContent).toContain("Purchase approval");
    const empty = stubApi(); empty.backend.http.getJson.mockResolvedValue({ items: [] } as never); update(<DecisionsPage api={empty} navigate={navigate} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No decisions yet"));
  });
  it("creates a decision through the authenticated API and navigates after the write settles", async () => {
    const api = stubApi(), navigate = vi.fn(), { host } = mount(<DecisionsPage api={api} navigate={navigate} isNew />);
    api.backend.http.postJson.mockResolvedValue(asset as never);
    await vi.waitFor(() => expect(host.querySelector('input[aria-label="Input 1 name"]')).not.toBeNull());
    const name = Array.from(host.querySelectorAll<HTMLInputElement>("input")).find(input => input.parentElement?.textContent === "Name")!;
    type(name, "Purchase approval"); click(button(host, "Save decision"));
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith("/decisions?decision=approval"));
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions", expect.objectContaining({ name: "Purchase approval", xml: expect.stringContaining("decisionTable") }));
  });
  it("preserves unsaved table work after a conflicting save and supports explicit reload", async () => {
    const api = stubApi(), { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.querySelector('input[aria-label="Input 1 name"]')).not.toBeNull());
    const condition = host.querySelector<HTMLInputElement>('[aria-label="Rule 1, input amount"]')!; type(condition, "<= 1000");
    const failure = Object.assign(new Error("The draft changed."), { status: 409 }); api.backend.http.putJson.mockRejectedValue(failure as never);
    click(button(host, "Save decision"));
    await vi.waitFor(() => expect(host.textContent).toContain("Your unsaved work is still here"));
    expect(condition.value).toBe("<= 1000");
    click(button(host, "Reload server draft"));
    await vi.waitFor(() => expect(host.querySelector<HTMLInputElement>('[aria-label="Rule 1, input amount"]')?.value).toBe("-"));
  });
  it("does not render protected source or enable writes for a read-only principal", async () => {
    const api = stubApi(["read"]); api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions") ? { items: [] } : { ...asset, xml: null } as never);
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Decision source is protected"));
    expect(button(host, "Save decision").disabled).toBe(true); expect(button(host, "Publish").disabled).toBe(true); expect(button(host, "Export DMN").disabled).toBe(true);
    expect(host.querySelector(".decision-table")).toBeNull();
  });
  it("allows Test+Read without Source to run cases while keeping source and protected values unavailable", async () => {
    const api = stubApi(["read", "test"]), saved = { id: "case1", name: "Boundary", revision: "c1", inputs: null, expectedStatus: "Success", expectedResult: null, isSensitive: true };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions") ? { items: [] } : path.endsWith("/cases") ? { items: [saved] } : { ...asset, xml: null });
    api.backend.http.postJson.mockResolvedValue({ id: "run1", candidateId: "draft-r1", candidateHash: "hash1", passed: true, hasDetailedPayload: false, comparisons: [{ caseId: "case1", name: "Boundary", passed: true, status: "Success", expected: null, actual: null, matchedRules: [], selectedRules: [], trace: [] }], completedAt: asset.createdAt, evaluatedAt: asset.createdAt } as never);
    const { host } = mount(<DecisionsPage api={api} navigate={vi.fn()} assetId={asset.id} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Protected inputs are unavailable"));
    expect(button(host, "Add case").disabled).toBe(true); expect(button(host, "Edit Boundary").disabled).toBe(true); expect(button(host, "Run saved cases").disabled).toBe(false);
    click(button(host, "Run saved cases")); await vi.waitFor(() => expect(host.textContent).toContain("All cases passed"));
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/test", expect.objectContaining({ candidateRevision: "r1", candidateHash: "hash1", cases: [{ id: "case1", revision: "c1" }] }));
    expect(host.textContent).toContain("Detailed values were not retained or are unavailable with your permissions");
    expect(host.querySelector(".decision-table")).toBeNull(); expect(host.querySelector(".decision-value-field")).toBeNull(); expect(host.textContent).not.toContain("Expected:"); expect(host.textContent).not.toContain("Actual:");
  });
  it("ignores a stale response after the backend identity changes", async () => {
    const old = stubApi(), next = stubApi(); let complete!: (result: unknown) => void;
    old.backend.http.getJson.mockImplementation(() => new Promise(resolve => { complete = resolve; }) as never);
    next.backend.http.getJson.mockResolvedValue({ items: [{ ...asset, name: "Tenant B policy" }] } as never);
    const { host, update } = mount(<DecisionsPage api={old} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(complete).toBeTypeOf("function")); update(<DecisionsPage api={next} navigate={vi.fn()} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Tenant B policy")); complete({ items: [{ ...asset, name: "Tenant A private policy" }] });
    await new Promise(resolve => setTimeout(resolve, 0)); expect(host.textContent).not.toContain("Tenant A private policy");
  });
  it("sends 34-digit case inputs and outputs as exact tagged text", async () => {
    const api = stubApi(), xml = newDecisionXml().replace('name="result" typeRef="string"', 'name="result" typeRef="number"');
    api.backend.http.getJson.mockResolvedValue({ items: [] } as never); api.backend.http.postJson.mockResolvedValue({} as never);
    const { host } = mount(<CasePanel api={api} asset={{ ...asset, xml }} xml={xml} dirty={false} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet")); click(button(host, "Add case"));
    const form = host.querySelector("form")!, name = form.querySelector<HTMLInputElement>("input")!, number = "1234567890123456789012345678901234";
    type(name, "Exact boundary"); type(form.querySelector<HTMLInputElement>('[aria-label="Case input amount"]')!, number); type(form.querySelector<HTMLInputElement>('[aria-label="Expected result"]')!, number);
    click(button(host, "Save case"));
    await vi.waitFor(() => expect(api.backend.http.postJson).toHaveBeenCalled());
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/cases", expect.objectContaining({ inputs: { amount: { kind: "number", value: number } }, expectedResult: { kind: "number", value: number } }));
  });
  it("keeps published tables read only and clones through revision-checked API", async () => {
    const api = stubApi(), published = { id: "v1", assetId: asset.id, number: 1, name: asset.name, xml: asset.xml, sourceHash: "hash1", publishedAt: asset.createdAt };
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions") ? { items: [published] } : path.endsWith("/cases") ? { items: [] } : path.endsWith("/versions/v1") ? published : asset);
    api.backend.http.postJson.mockResolvedValue(asset as never); const navigate = vi.fn(), { host } = mount(<DecisionsPage api={api} navigate={navigate} assetId={asset.id} versionId="v1" />);
    await vi.waitFor(() => expect(host.querySelector('input[aria-label="Input 1 name"]')).not.toBeNull());
    expect(host.querySelector<HTMLInputElement>('[aria-label="Rule 1, input amount"]')!.disabled).toBe(true);
    click(button(host, "Clone to draft"));
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith("/decisions?decision=approval"));
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/clone-to-draft", expect.objectContaining({ expectedRevision: "r1", versionId: "v1" }));
  });
  it("offers latest-at-publication and exact immutable versions to workflow authors", async () => {
    const api = stubApi(), onChange = vi.fn(), published = { id: "v1", assetId: asset.id, number: 1, name: asset.name, xml: asset.xml, sourceHash: "hash1", publishedAt: asset.createdAt };
    api.backend.http.getJson.mockImplementation(async path => ({ items: path.endsWith("/versions") ? [published] : [asset] } as never));
    const { host, update } = mount(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="" disabled={false} onChange={onChange} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Approval")); select(host.querySelector<HTMLSelectElement>('[aria-label="Business decision"]')!, asset.id);
    expect(onChange).toHaveBeenLastCalledWith("approval@latest");
    update(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="approval@latest" disabled={false} onChange={onChange} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Version 1")); select(host.querySelector<HTMLSelectElement>('[aria-label="Business decision version"]')!, "v1"); expect(onChange).toHaveBeenLastCalledWith("approval@v1");
  });
  it("renders actual matched and selected rules for exact published evaluation", async () => {
    const api = stubApi(); api.backend.http.postJson.mockResolvedValue({ status: "Success", decisionId: "approval", contentHash: "exact-content", result: { kind: "string", value: "Automatic" }, diagnostics: [], trace: [{ ruleId: "rule1", matched: true, selected: true, cells: [{ inputId: "amount", truth: "True" }] }, { ruleId: "rule2", matched: true, selected: false, cells: [{ inputId: "amount", truth: "True" }] }] } as never);
    const { host } = mount(<EvaluationPanel api={api} id={asset.id} versionId="v1" xml={asset.xml!} />);
    type(host.querySelector<HTMLInputElement>('[aria-label="Evaluate input amount"]')!, "9007199254740993"); click(button(host, "Evaluate decision"));
    await vi.waitFor(() => expect(host.textContent).toContain("Automatic"));
    expect(api.backend.http.postJson).toHaveBeenCalledWith("/tenant/decisions/approval/evaluate", { versionId: "v1", inputs: { amount: { kind: "number", value: "9007199254740993" } } });
    const rows = host.querySelectorAll("tbody tr"); expect(rows[0].textContent).toContain("rule1YesYes"); expect(rows[1].textContent).toContain("rule2YesNo"); expect(host.textContent).toContain("exact-content");
  });
  it("blocks dirty navigation probes without showing a dialog and confirms an actual leave", async () => {
    const api = stubApi(), navigate = vi.fn(), { host } = mount(<DecisionsPage api={api} navigate={navigate} isNew />);
    await vi.waitFor(() => expect(host.querySelector('input[aria-label="Input 1 name"]')).not.toBeNull());
    type(host.querySelector<HTMLInputElement>('[aria-label="Rule 1, input amount"]')!, "<= 1000");
    const probe = new CustomEvent(studioNavigationRequestedEvent, { cancelable: true, detail: { fromPath: "/decisions?new=1", toPath: "/workflows", intent: "probe" } }); window.dispatchEvent(probe);
    expect(probe.defaultPrevented).toBe(true); expect(api.dialogs.confirm).not.toHaveBeenCalled();
    window.dispatchEvent(new CustomEvent(studioNavigationRequestedEvent, { cancelable: true, detail: { fromPath: "/decisions?new=1", toPath: "/workflows", intent: "navigate" } }));
    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith("/workflows")); expect(api.dialogs.confirm).toHaveBeenCalledTimes(1);
  });
  it("cancels case editing with Escape and restores its opener focus", async () => {
    const api = stubApi(), { host } = mount(<CasePanel api={api} asset={asset} xml={asset.xml!} dirty={false} />);
    await vi.waitFor(() => expect(host.textContent).toContain("No saved cases yet")); const add = button(host, "Add case"); click(add);
    await vi.waitFor(() => expect(host.querySelector("form")).not.toBeNull());
    host.querySelector("form")!.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    await vi.waitFor(() => expect(host.querySelector("form")).toBeNull()); await vi.waitFor(() => expect(document.activeElement).toBe(add)); expect(api.backend.http.postJson).not.toHaveBeenCalled();
  });
});
