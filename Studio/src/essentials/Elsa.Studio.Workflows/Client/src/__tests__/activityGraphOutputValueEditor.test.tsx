import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioActivityDefinitionImplementationEditorProps, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { ActivityGraphOutputValueEditor } from "../ActivityGraphOutputValueEditor";
import type { ActivityOutputContract } from "../activityDefinitionTypes";
import { useWorkflowReferenceAuthoring } from "../workflowReferenceAuthoring";
import { sequenceActivity, sequenceNode } from "./fixtures";

vi.mock("../api/activityDesign", () => ({ useWorkflowActivities: () => ({ data: { activities: [sequenceActivity] } }) }));
vi.mock("../api/expressions", () => ({ listExpressionDescriptors: async () => [
  { type: "ActivityResult", displayName: "Activity result", editingMode: "reference" },
  { type: "Literal", displayName: "Literal", editingMode: "literal" },
  { type: "Variable", displayName: "Variable", editingMode: "reference" }
] }));
const output: ActivityOutputContract = { referenceKey: "sum", name: "Sum", type: { alias: "Double", collectionKind: "Single" }, isRequired: true, isNullable: false, storageDriverKey: "elsa.json", durability: "Durable" };
const setter = (id = "set", name = "Sum") => ({ nodeId: id, activityVersionId: "elsa.intrinsic.set-output@1", inputs: [{ referenceKey: "name", value: { expressionType: "Literal", value: name } }, { referenceKey: "value", value: { expressionType: "Literal", value: 42 } }], outputs: [], intrinsic: { kind: "SetOutput" } });
const mounted: { root: Root; container: HTMLDivElement }[] = [];
afterEach(() => { for (const item of mounted.splice(0)) { flushSync(() => item.root.unmount()); item.container.remove(); } });
const expressionEditor: StudioExpressionEditorContribution = { id: "test.values", supports: () => true, createDefaultValue: () => 0, surfaces: { inline: function TestValueEditor({ disabled, onChange, value }) {
  const authoring = useWorkflowReferenceAuthoring();
  return <><button disabled={disabled} onClick={() => onChange(43)}>Edit {JSON.stringify(value)}</button><span data-visible-variables>{authoring?.visibleVariables.map(variable => variable.name).join(",")}</span></>;
} } };
function renderEditor(options: { nodes?: ReturnType<typeof setter>[]; mapping?: unknown; readOnly?: boolean; outputName?: string } = {}) {
  const container = document.createElement("div"); document.body.append(container); const root = createRoot(container); const onChange = vi.fn(); const onRevealStep = vi.fn(async () => ({ kind: "focused" as const, announcement: "Opened" }));
  const nested = sequenceNode("nested", options.nodes ?? []);
  const props = { context: {}, definitionId: "def", draftId: "draft", revision: 1, providerKey: "elsa.activity-graph", providerSchemaVersion: "2", manifestFingerprint: "test", contract: { contractSchemaVersion: "1", inputs: [], outputs: [{ ...output, name: options.outputName ?? "Sum" }], outcomes: [] }, value: { payload: { rootActivity: sequenceNode("root", [nested]), variables: [{ referenceKey: "total", name: "Total", type: { alias: "Double", collectionKind: "Single" } }], outputMappings: options.mapping ? [{ outputReferenceKey: "sum", source: options.mapping }] : [], outcomeMappings: [] }, layout: [] }, readOnly: options.readOnly ?? false, expressionEditors: [expressionEditor], onChange } as unknown as StudioActivityDefinitionImplementationEditorProps;
  flushSync(() => root.render(<ActivityGraphOutputValueEditor {...props} output={{ ...output, name: options.outputName ?? "Sum" }} onRevealStep={onRevealStep} />)); mounted.push({ root, container });
  return { container, onChange, onRevealStep };
}
const button = (container: HTMLElement, text: string) => [...container.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent === text)!;
const click = (element: HTMLElement) => flushSync(() => element.click());

describe("unified graph output value source", () => {
  it("navigates to a nested setter and source-change guidance cannot mutate the graph or add a conflicting mapping", () => {
    const view = renderEditor({ nodes: [setter()] });
    click(button(view.container, "Go to step")); expect(view.onRevealStep).toHaveBeenCalledWith("set");
    click(button(view.container, "Change value source"));
    expect(view.container.textContent).toContain("remove it or select a different output");
    expect(button(view.container, "Choose value").disabled).toBe(true); expect(view.onChange).not.toHaveBeenCalled();
  });
  it("lists and addresses every setter rather than selecting an arbitrary one", () => {
    const view = renderEditor({ nodes: [setter("first"), setter("second")] });
    const buttons = [...view.container.querySelectorAll<HTMLButtonElement>('button[aria-label^="Go to step"]')];
    expect(buttons).toHaveLength(2); click(buttons[1]); expect(view.onRevealStep).toHaveBeenCalledWith("second");
    expect(view.container.textContent).toContain("Assigned by 2 Set Output steps");
  });
  it("retains navigation while disallowing read-only source changes", () => {
    const view = renderEditor({ nodes: [setter()], readOnly: true });
    expect(button(view.container, "Change value source").disabled).toBe(true);
    expect(button(view.container, "Go to step").disabled).toBe(false);
  });
  it("explains a renamed output and preserves the stale step for explicit repair", () => {
    const view = renderEditor({ nodes: [setter()], outputName: "Renamed" });
    expect(view.container.textContent).toContain("removed, renamed or missing output");
    click(button(view.container, "Go to step")); expect(view.onRevealStep).toHaveBeenCalledWith("set"); expect(view.onChange).not.toHaveBeenCalled();
  });
  it("defaults a new value to supported Literal instead of unavailable ActivityResult browsing", async () => {
    const view = renderEditor(); await vi.waitFor(() => expect(button(view.container, "Choose value").disabled).toBe(false)); click(button(view.container, "Choose value"));
    expect(view.onChange).toHaveBeenCalledWith(expect.objectContaining({ payload: expect.objectContaining({ outputMappings: [{ outputReferenceKey: "sum", source: { syntax: "Literal", value: 0 } }] }) }));
  });
  it("retains private graph variables in mapped expression authoring", async () => {
    const view = renderEditor({ mapping: { syntax: "Variable", value: { referenceKey: "total", declaringScopeId: "workflow" } } });
    await vi.waitFor(() => expect(view.container.querySelector("[data-visible-variables]")?.textContent).toBe("Total"));
  });
  it("preserves a conflicted mapping read-only but allows explicit removal without touching setters", async () => {
    const view = renderEditor({ nodes: [setter()], mapping: { syntax: "Literal", value: 42 } });
    await vi.waitFor(() => expect(button(view.container, "Edit 42")).toBeDefined());
    expect(button(view.container, "Edit 42").disabled).toBe(true); click(button(view.container, "Remove connection"));
    const next = view.onChange.mock.calls[0][0]; expect(next.payload.outputMappings).toEqual([]); expect(next.payload.rootActivity.structure.payload.activities[0].structure.payload.activities[0].nodeId).toBe("set");
  });
});
