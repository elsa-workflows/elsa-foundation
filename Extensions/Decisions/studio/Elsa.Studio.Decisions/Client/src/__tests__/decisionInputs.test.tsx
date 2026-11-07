import React, { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioExpressionEditorContext } from "@elsa-workflows/studio-sdk";
import { DecisionInputsEditor } from "../DecisionInputsEditor";
import { DecisionVersionEditor } from "../DecisionVersionEditor";
import {
  asDecisionValueMap,
  extraDecisionInputs,
  isDecisionInputsContext,
  parseDecisionInputsJson,
  parseDecisionPin,
  readDecisionPin,
  seedDecisionInputs
} from "../decisionInputs";
import { editTable, newDecisionXml, parseTable } from "../decisionXml";
import { asset, click, render, select, stubApi, type } from "./helpers";

const disposals: Array<() => void> = [];
afterEach(() => { disposals.splice(0).forEach(dispose => dispose()); });
function mount(content: React.ReactNode) { const result = render(content); disposals.push(result.dispose); return result; }

const descriptor = { name: "Inputs", referenceKey: "Inputs", typeName: "Elsa.Decisions.Core.DecisionValue" };
const published = { id: "v1", assetId: asset.id, number: 1, name: asset.name, xml: asset.xml, sourceHash: "hash1", publishedAt: asset.createdAt };

function expressionContext(activity: unknown): StudioExpressionEditorContext {
  return { syntax: "Literal", surface: "inline", descriptor, activity, expressionDescriptors: [] };
}

describe("decision input seeding helpers", () => {
  it("reads a wrapped Decision pin and seeds missing tagged clauses without rewriting existing values", () => {
    expect(parseDecisionPin("approval@v1")).toEqual({ assetId: "approval", selection: "v1" });
    expect(readDecisionPin({ Decision: { expression: { type: "Literal", value: "approval@v1" } } })).toBe("approval@v1");
    const table = parseTable(editTable(newDecisionXml(), { kind: "add-clause", output: false }));
    const seeded = seedDecisionInputs({ amount: { kind: "number", value: "950" } }, table.inputs)!;
    expect(seeded.amount).toEqual({ kind: "number", value: "950" });
    expect(seeded[table.inputs[1].name]?.kind).toBe("string");
    expect(seedDecisionInputs(seeded, table.inputs)).toBeNull();
    expect(extraDecisionInputs({ ...seeded, leftover: { kind: "string", value: "x" } }, table.inputs)).toEqual([{ name: "leftover", value: { kind: "string", value: "x" } }]);
  });

  it("claims Evaluate Decision Inputs under Object or Literal and parses tagged JSON immediately", () => {
    expect(isDecisionInputsContext({ syntax: "Object", descriptor })).toBe(true);
    expect(isDecisionInputsContext({ syntax: "Literal", descriptor })).toBe(true);
    expect(isDecisionInputsContext({ syntax: "JavaScript", descriptor })).toBe(false);
    expect(isDecisionInputsContext({ syntax: "Object", descriptor: { name: "Headers", typeName: "System.String" } })).toBe(false);
    expect(parseDecisionInputsJson('{"amount":{"kind":"number","value":"950"}}')).toEqual({ valid: true, value: { amount: { kind: "number", value: "950" } } });
    expect(parseDecisionInputsJson("[1]").valid).toBe(false);
    expect(asDecisionValueMap('{"amount":{"kind":"number","value":"1"}}').amount).toEqual({ kind: "number", value: "1" });
  });
});

describe("Evaluate Decision Inputs editor", () => {
  it("seeds amount from the selected published table and commits typed values without JSON", async () => {
    const api = stubApi(), onChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions/v1") ? published : { ...asset, publishedVersionId: "v1" });
    function Harness() {
      const [value, setValue] = useState<unknown>({});
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={value} context={expressionContext({ Decision: { expression: { type: "Literal", value: "approval@v1" } } })} onChange={next => { onChange(next); setValue(next); }} />;
    }
    const { host } = mount(<Harness />);
    await vi.waitFor(() => expect(host.querySelector<HTMLInputElement>('[aria-label="amount"]')).not.toBeNull());
    expect(onChange).toHaveBeenCalledWith({ amount: { kind: "number", value: "0" } });
    type(host.querySelector<HTMLInputElement>('[aria-label="amount"]')!, "950");
    expect(onChange).toHaveBeenLastCalledWith({ amount: { kind: "number", value: "950" } });
    expect(host.querySelector(".wf-dictionary-editor")).toBeNull();
  });

  it("keeps JSON as an advanced surface that commits on valid tagged objects", async () => {
    const api = stubApi(), onChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions/v1") ? published : { ...asset, publishedVersionId: "v1" });
    const { host } = mount(<DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={{ amount: { kind: "number", value: "0" } }} context={expressionContext({ Decision: { expression: { type: "Literal", value: "approval@v1" } } })} onChange={onChange} />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).not.toBeNull());
    click(host.querySelector("summary")!);
    const json = host.querySelector<HTMLTextAreaElement>('[aria-label="Decision inputs JSON"]')!;
    type(json, '{"amount":{"kind":"number","value":"15000"}}');
    expect(onChange).toHaveBeenLastCalledWith({ amount: { kind: "number", value: "15000" } });
    type(json, "[");
    expect(host.textContent).toContain("JSON is not valid.");
  });
});

describe("Evaluate Decision version picker", () => {
  it("explains that an unpublished draft cannot be pinned before Review & publish", async () => {
    const api = stubApi(), onChange = vi.fn(), onValidationChange = vi.fn();
    const { host, update } = mount(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="" disabled={false} onChange={onChange} onValidationChange={onValidationChange} />);
    await vi.waitFor(() => expect(host.textContent).toContain("Approval"));
    select(host.querySelector<HTMLSelectElement>('[aria-label="Business decision"]')!, asset.id);
    update(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="approval@latest" disabled={false} onChange={onChange} onValidationChange={onValidationChange} />);
    await vi.waitFor(() => expect(host.textContent).toContain("no published version"));
    expect(onValidationChange).toHaveBeenCalledWith(true);
  });
});
