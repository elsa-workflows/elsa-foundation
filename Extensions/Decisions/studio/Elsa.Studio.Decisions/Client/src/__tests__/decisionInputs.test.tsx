import React, { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioExpressionEditorContext } from "@elsa-workflows/studio-sdk";
import { DecisionInputsEditor } from "../DecisionInputsEditor";
import { DecisionVersionEditor } from "../DecisionVersionEditor";
import {
  collectEvaluateDecisionValidation,
  evaluateDecisionActivityType,
  loadDecisionTable
} from "../decisionDraftValidation";
import {
  asDecisionValueMap,
  extraDecisionInputs,
  invalidDecisionInputs,
  isDecisionInputsContext,
  isScalar,
  isValidDecisionNumber,
  matchDecisionInputTypes,
  mismatchedDecisionInputs,
  parseDecisionInputsJson,
  parseDecisionPin,
  readDecisionInputs,
  readDecisionPin,
  removeDecisionInputs,
  seedDecisionInputs,
  writtenDecisionInput
} from "../decisionInputs";
import { editTable, newDecisionXml, parseTable } from "../decisionXml";
import { asset, blur, button, click, render, select, stubApi, type } from "./helpers";

const disposals: Array<() => void> = [];
afterEach(() => { disposals.splice(0).forEach(dispose => dispose()); });
function mount(content: React.ReactNode) { const result = render(content); disposals.push(result.dispose); return result; }

const descriptor = { name: "Inputs", referenceKey: "Inputs", typeName: "Elsa.Decisions.Core.DecisionValue" };
const published = { id: "v1", assetId: asset.id, number: 1, name: asset.name, xml: asset.xml, sourceHash: "hash1", publishedAt: asset.createdAt };
const scoreXml = `<?xml version="1.0" encoding="utf-8"?><definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="score_definitions" name="Score" namespace="urn:elsa:decisions"><decision id="score" name="Score"><decisionTable id="score_table" hitPolicy="UNIQUE"><input id="score_input"><inputExpression id="score_expression" typeRef="number"><text>score</text></inputExpression></input><output id="score_output" label="result" typeRef="string"/><rule id="score_rule"><inputEntry id="score_c1"><text>-</text></inputEntry><outputEntry id="score_o1"><text>"Ok"</text></outputEntry></rule></decisionTable></decision></definitions>`;
const scorePublished = { id: "v1", assetId: "scoring", number: 1, name: "Scoring", xml: scoreXml, sourceHash: "hash-score", publishedAt: asset.createdAt };
const approvalTwoXml = `<?xml version="1.0" encoding="utf-8"?><definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="approval_definitions" name="Approval" namespace="urn:elsa:decisions"><decision id="approval" name="Approval"><decisionTable id="approval_table" hitPolicy="UNIQUE"><input id="approval_amount"><inputExpression id="approval_amount_expression" typeRef="number"><text>amount</text></inputExpression></input><input id="approval_category"><inputExpression id="approval_category_expression" typeRef="string"><text>customerCategory</text></inputExpression></input><output id="approval_result" label="result" typeRef="string"/><rule id="approval_rule"><inputEntry id="approval_c1"><text>-</text></inputEntry><inputEntry id="approval_c2"><text>-</text></inputEntry><outputEntry id="approval_o1"><text>"Automatic"</text></outputEntry></rule></decisionTable></decision></definitions>`;
const regionXml = approvalTwoXml
  .replace('<output id="approval_result"', '<input id="approval_region"><inputExpression id="approval_region_expression" typeRef="string"><text>region</text></inputExpression></input><output id="approval_result"')
  .replace('<outputEntry id="approval_o1"', '<inputEntry id="approval_c3"><text>-</text></inputEntry><outputEntry id="approval_o1"');
const twoPublished = { ...published, xml: approvalTwoXml };
const regionPublished = { ...published, id: "v2", number: 2, xml: regionXml, sourceHash: "hash-region" };
const authoredTwo = {
  amount: { kind: "number", value: "950" },
  customerCategory: { kind: "string", value: "Standard" }
};
const amountTextXml = (asset.xml ?? "").replace('typeRef="number"', 'typeRef="string"');
const textPublished = { id: "v2", assetId: asset.id, number: 2, name: asset.name, xml: amountTextXml, sourceHash: "hash2", publishedAt: asset.createdAt };

function expressionContext(activity: unknown): StudioExpressionEditorContext {
  return { syntax: "Literal", surface: "inline", descriptor, activity, expressionDescriptors: [] };
}

function pin(value: string) {
  return { Decision: { expression: { type: "Literal", value } } };
}

const evaluateContext = {
  typeName: evaluateDecisionActivityType,
  inputs: [
    { name: "Decision", referenceKey: "Decision", typeName: "System.String", uiHint: "decision-version" },
    { name: "Inputs", referenceKey: "Inputs", typeName: "Elsa.Decisions.Core.DecisionValue" }
  ]
};

function evaluateActivity(extra: Record<string, unknown> = {}) {
  return {
    nodeId: "eval-1",
    activityTypeKey: evaluateDecisionActivityType,
    Decision: { expression: { type: "Literal", value: "approval@v1" } },
    ...extra
  };
}

function collect(api: ReturnType<typeof stubApi>, activity: unknown) {
  return collectEvaluateDecisionValidation(api, activity, evaluateContext);
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

  it("rejects non-string wire values the runtime contract refuses", () => {
    expect(isScalar({ kind: "number", value: 950 })).toBe(false);
    expect(isScalar({ kind: "string", value: true })).toBe(false);
    expect(isScalar({ kind: "boolean", value: true })).toBe(false);
    expect(isScalar({ kind: "null", value: "null" })).toBe(false);
    expect(parseDecisionInputsJson('{"amount":{"kind":"number","value":950}}').valid).toBe(false);
  });

  it("keeps extras across a table switch and rewrites displayed kind on edit", () => {
    const score = parseTable(scoreXml);
    const extras = extraDecisionInputs({
      amount: { kind: "number", value: "950" },
      customerCategory: { kind: "string", value: "Standard" },
      score: { kind: "number", value: "0" }
    }, score.inputs);
    expect(extras.map(item => item.name)).toEqual(["amount", "customerCategory"]);
    expect(removeDecisionInputs({ amount: { kind: "number", value: "950" }, score: { kind: "number", value: "0" } }, ["amount"])).toEqual({
      score: { kind: "number", value: "0" }
    });
    const textAmount = parseTable(amountTextXml).inputs[0];
    expect(mismatchedDecisionInputs({ amount: { kind: "number", value: "950" } }, [textAmount])).toEqual([
      { name: "amount", storedKind: "number", tableType: "string" }
    ]);
    expect(writtenDecisionInput(textAmount, { kind: "number", value: "950" })).toEqual({ kind: "string", value: "950" });
    expect(matchDecisionInputTypes({ amount: { kind: "number", value: "950" } }, [textAmount])).toEqual({
      amount: { kind: "string", value: "950" }
    });
  });

  it("flags empty or non-numeric required numbers the runtime would refuse as InvalidValue", () => {
    const amount = parseTable(asset.xml ?? "").inputs[0];
    expect(isValidDecisionNumber("")).toBe(false);
    expect(isValidDecisionNumber("-")).toBe(false);
    expect(isValidDecisionNumber("950")).toBe(true);
    expect(invalidDecisionInputs({ amount: { kind: "number", value: "" } }, [amount])).toEqual([{ name: "amount", tableType: "number" }]);
    expect(invalidDecisionInputs({ amount: { kind: "number", value: "0" } }, [amount])).toEqual([]);
    expect(readDecisionInputs({
      nodeId: "n1",
      Inputs: { expression: { type: "Object", value: { amount: { kind: "number", value: "950" } } } }
    })).toEqual({ amount: { kind: "number", value: "950" } });
  });
});

describe("Evaluate Decision Inputs editor", () => {
  it("leaves amount unset until entered and commits typed values without JSON", async () => {
    const api = stubApi(), onChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions/v1") ? published : { ...asset, publishedVersionId: "v1" });
    function Harness() {
      const [value, setValue] = useState<unknown>({});
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={value} context={expressionContext(pin("approval@v1"))} onChange={next => { onChange(next); setValue(next); }} />;
    }
    const { host } = mount(<Harness />);
    await vi.waitFor(() => expect(host.querySelector<HTMLInputElement>('[aria-label="amount"]')).not.toBeNull());
    expect(onChange).not.toHaveBeenCalled();
    expect(host.querySelector<HTMLSelectElement>('[aria-label="amount value kind"]')?.value).toBe("unset");
    expect(host.querySelector<HTMLInputElement>('[aria-label="amount"]')?.placeholder).toBe("Not set");
    expect(host.textContent).toContain("MissingInput");
    type(host.querySelector<HTMLInputElement>('[aria-label="amount"]')!, "950");
    expect(onChange).toHaveBeenLastCalledWith({ amount: { kind: "number", value: "950" } });
    expect(host.querySelector<HTMLSelectElement>('[aria-label="amount value kind"]')?.value).toBe("number");
    expect(host.querySelector(".wf-dictionary-editor")).toBeNull();
  });

  it("keeps JSON as an advanced surface that commits on valid tagged objects", async () => {
    const api = stubApi(), onChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions/v1") ? published : { ...asset, publishedVersionId: "v1" });
    const { host } = mount(<DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={{ amount: { kind: "number", value: "0" } }} context={expressionContext(pin("approval@v1"))} onChange={onChange} />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).not.toBeNull());
    click(host.querySelector("summary")!);
    const json = host.querySelector<HTMLTextAreaElement>('[aria-label="Decision inputs JSON"]')!;
    type(json, '{"amount":{"kind":"number","value":"15000"}}');
    expect(onChange).toHaveBeenLastCalledWith({ amount: { kind: "number", value: "15000" } });
    type(json, "[");
    expect(host.textContent).toContain("JSON is not valid.");
    expect(json.getAttribute("aria-invalid")).toBe("true");
    expect(json.getAttribute("aria-describedby")).toBeTruthy();
  });

  it("keeps cleared Advanced JSON unset and restores stored JSON on invalid blur", async () => {
    const api = stubApi(), onChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions/v1") ? published : { ...asset, publishedVersionId: "v1" });
    function Harness() {
      const [value, setValue] = useState<unknown>({ amount: { kind: "number", value: "950" } });
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={value} context={expressionContext(pin("approval@v1"))} onChange={next => { onChange(next); setValue(next); }} />;
    }
    const { host } = mount(<Harness />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).not.toBeNull());
    click(host.querySelector("summary")!);
    const json = host.querySelector<HTMLTextAreaElement>('[aria-label="Decision inputs JSON"]')!;
    type(json, "{}");
    await vi.waitFor(() => expect(onChange).toHaveBeenCalledWith({}));
    expect(host.querySelector<HTMLSelectElement>('[aria-label="amount value kind"]')?.value).toBe("unset");
    expect(host.textContent).toContain("MissingInput");
    type(host.querySelector<HTMLInputElement>('[aria-label="amount"]')!, "950");
    json.focus();
    type(json, "{");
    expect(host.textContent).toContain("JSON is not valid.");
    blur(json);
    expect(host.textContent).not.toContain("JSON is not valid.");
    expect(json.getAttribute("aria-invalid")).toBeNull();
    expect(json.value).toContain('"950"');
  });

  it("keeps authored extras when switching tables and removes them only on request", async () => {
    const api = stubApi(), onChange = vi.fn(), onValidationChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.includes("scoring") && path.includes("/versions/")) return scorePublished;
      if (path.includes("/versions/v1")) return published;
      if (path.includes("scoring")) return { id: "scoring", name: "Scoring", publishedVersionId: "v1", xml: scoreXml };
      return { ...asset, publishedVersionId: "v1" };
    });
    function Harness({ decision }: { decision: string }) {
      const [value, setValue] = useState<unknown>({
        amount: { kind: "number", value: "950" },
        customerCategory: { kind: "string", value: "Standard" }
      });
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={value} context={expressionContext(pin(decision))} onChange={next => { onChange(next); setValue(next); }} onValidationChange={onValidationChange} />;
    }
    const { host, update } = mount(<Harness decision="approval@v1" />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).not.toBeNull());
    update(<Harness decision="scoring@v1" />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="score"]')).not.toBeNull());
    expect(host.textContent).toContain("InputMembership");
    expect(host.querySelector('[aria-label="amount (not in this table)"]')).not.toBeNull();
    expect(host.querySelector('[aria-label="customerCategory (not in this table)"]')).not.toBeNull();
    expect(onValidationChange).toHaveBeenCalledWith(true);
    click(button(host, "Remove inputs not in this table"));
    expect(onChange.mock.calls.at(-1)?.[0]).toEqual({});
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).toBeNull());
    expect(host.querySelector<HTMLSelectElement>('[aria-label="score value kind"]')?.value).toBe("unset");
  });

  it("flags a type switch and writes the displayed table kind on edit", async () => {
    const api = stubApi(), onChange = vi.fn(), onValidationChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions/v2") ? textPublished : path.endsWith("/versions/v1") ? published : { ...asset, publishedVersionId: "v1" });
    function Harness({ pinValue }: { pinValue: string }) {
      const [value, setValue] = useState<unknown>({ amount: { kind: "number", value: "950" } });
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={value} context={expressionContext(pin(pinValue))} onChange={next => { onChange(next); setValue(next); }} onValidationChange={onValidationChange} />;
    }
    const { host, update } = mount(<Harness pinValue="approval@v1" />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).not.toBeNull());
    update(<Harness pinValue="approval@v2" />);
    await vi.waitFor(() => expect(host.textContent).toContain("InputTypeMismatch"));
    expect(onValidationChange).toHaveBeenCalledWith(true);
    expect(host.querySelector<HTMLSelectElement>('[aria-label="amount value kind"]')?.value).toBe("string");
    type(host.querySelector<HTMLInputElement>('[aria-label="amount"]')!, "951");
    expect(onChange.mock.calls.at(-1)?.[0]).toEqual({ amount: { kind: "string", value: "951" } });
    const amount = host.querySelector<HTMLInputElement>('[aria-label="amount"]')!;
    expect(amount.getAttribute("inputmode")).toBe("text");
  });

  it("flags an empty required number as InvalidValue until a valid value is entered", async () => {
    const api = stubApi(), onChange = vi.fn(), onValidationChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions/v1") ? published : { ...asset, publishedVersionId: "v1" });
    function Harness() {
      const [value, setValue] = useState<unknown>({ amount: { kind: "number", value: "950" } });
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={value} context={expressionContext(pin("approval@v1"))} onChange={next => { onChange(next); setValue(next); }} onValidationChange={onValidationChange} />;
    }
    const { host } = mount(<Harness />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).not.toBeNull());
    type(host.querySelector<HTMLInputElement>('[aria-label="amount"]')!, "");
    expect(host.textContent).toContain("InvalidValue");
    expect(onValidationChange).toHaveBeenCalledWith(true);
  });

  it("writes explicit 0 when the Number kind is confirmed and keeps that distinct from missing", async () => {
    const api = stubApi(), onChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions/v1") ? published : { ...asset, publishedVersionId: "v1" });
    function Harness() {
      const [value, setValue] = useState<unknown>({});
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={value} context={expressionContext(pin("approval@v1"))} onChange={next => { onChange(next); setValue(next); }} />;
    }
    const { host } = mount(<Harness />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).not.toBeNull());
    expect(host.textContent).toContain("MissingInput");
    select(host.querySelector<HTMLSelectElement>('[aria-label="amount value kind"]')!, "number");
    expect(onChange).toHaveBeenLastCalledWith({ amount: { kind: "number", value: "0" } });
    expect(host.querySelector<HTMLInputElement>('[aria-label="amount"]')?.value).toBe("0");
    expect(host.textContent).not.toContain("MissingInput");
  });

  it("writes explicit empty text when the Text kind is confirmed", async () => {
    const api = stubApi(), onChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions/v2") ? textPublished : path.endsWith("/versions/v1") ? published : { ...asset, publishedVersionId: "v1" });
    function Harness() {
      const [value, setValue] = useState<unknown>({});
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={value} context={expressionContext(pin("approval@v2"))} onChange={next => { onChange(next); setValue(next); }} />;
    }
    const { host } = mount(<Harness />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).not.toBeNull());
    expect(host.querySelector<HTMLSelectElement>('[aria-label="amount value kind"]')?.value).toBe("unset");
    select(host.querySelector<HTMLSelectElement>('[aria-label="amount value kind"]')!, "string");
    expect(onChange).toHaveBeenLastCalledWith({ amount: { kind: "string", value: "" } });
    expect(host.querySelector<HTMLSelectElement>('[aria-label="amount value kind"]')?.value).toBe("string");
    expect(host.textContent).not.toContain("MissingInput");
  });

  it("distinguishes an unset boolean from an explicit false", async () => {
    const booleanXml = (asset.xml ?? "").replace('typeRef="number"', 'typeRef="boolean"').replace(">amount<", ">approved<");
    const booleanPublished = { ...published, xml: booleanXml };
    const api = stubApi(), onChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions/v1") ? booleanPublished : { ...asset, publishedVersionId: "v1", xml: booleanXml });
    function Harness() {
      const [value, setValue] = useState<unknown>({});
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={value} context={expressionContext(pin("approval@v1"))} onChange={next => { onChange(next); setValue(next); }} />;
    }
    const { host } = mount(<Harness />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="approved"]')).not.toBeNull());
    expect(host.querySelector<HTMLSelectElement>('[aria-label="approved"]')?.value).toBe("");
    expect(host.textContent).toContain("MissingInput");
    select(host.querySelector<HTMLSelectElement>('[aria-label="approved"]')!, "false");
    expect(onChange).toHaveBeenLastCalledWith({ approved: { kind: "boolean", value: "false" } });
    expect(host.querySelector<HTMLSelectElement>('[aria-label="approved"]')?.value).toBe("false");
    expect(host.textContent).not.toContain("MissingInput");
  });

  it("does not label a version-fetch failure as unpublished", async () => {
    const api = stubApi(), onChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.endsWith("/versions/v1")) throw new Error("version lookup failed");
      return { ...asset, publishedVersionId: "v1" };
    });
    const { host } = mount(<DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={{}} context={expressionContext(pin("approval@v1"))} onChange={onChange} />);
    await vi.waitFor(() => expect(host.textContent).toContain("version lookup failed"));
    expect(host.textContent).not.toContain("no published version");
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

  it("shows a version-fetch failure instead of an unpublished draft warning", async () => {
    const api = stubApi(), onChange = vi.fn(), onValidationChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.endsWith("/versions")) throw new Error("versions unavailable");
      if (path.endsWith("/decisions")) return { items: [asset] };
      return asset;
    });
    const { host } = mount(<DecisionVersionEditor api={api} descriptor={{ name: "Decision", typeName: "String" }} context={{} as never} value="approval@latest" disabled={false} onChange={onChange} onValidationChange={onValidationChange} />);
    await vi.waitFor(() => expect(host.textContent).toContain("versions unavailable"));
    expect(host.textContent).not.toContain("no published version");
    expect(onValidationChange).toHaveBeenCalledWith(true);
  });
});

describe("Evaluate Decision draft validation collection", () => {
  it("reports extras, type mismatch, empty number, and unpublished pins for Review & publish", async () => {
    const api = stubApi();
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.endsWith("/versions/v2")) return textPublished;
      if (path.endsWith("/versions/v1")) return published;
      if (path.endsWith("/versions")) return { items: [] };
      return { ...asset, publishedVersionId: null, xml: asset.xml };
    });
    const extras = await collect(api, evaluateActivity({
      Inputs: { expression: { type: "Object", value: {
        amount: { kind: "number", value: "950" },
        notes: { kind: "string", value: "extra" }
      } } }
    }));
    expect(extras.map(item => item.type)).toContain("Decision/InputMembership");
    expect(extras.some(item => item.message.includes("notes"))).toBe(true);
    expect(extras.every(item => item.path === "eval-1/inputs/Inputs" || item.path === "eval-1/inputs/Decision")).toBe(true);

    const mismatch = await collect(api, evaluateActivity({
      Decision: { expression: { type: "Literal", value: "approval@v2" } },
      Inputs: { expression: { type: "Object", value: { amount: { kind: "number", value: "950" } } } }
    }));
    expect(mismatch.map(item => item.type)).toContain("Decision/InputTypeMismatch");

    const empty = await collect(api, evaluateActivity({
      Inputs: { expression: { type: "Object", value: { amount: { kind: "number", value: "" } } } }
    }));
    expect(empty.map(item => item.type)).toContain("Decision/InvalidValue");

    const unpublished = await collect(api, evaluateActivity({
      Decision: { expression: { type: "Literal", value: "approval@latest" } },
      Inputs: { expression: { type: "Object", value: { amount: { kind: "number", value: "950" } } } }
    }));
    expect(unpublished.map(item => item.type)).toContain("Decision/UnpublishedLatest");
    expect(unpublished.some(item => item.message.includes("Review & publish cannot pin"))).toBe(true);
    expect(unpublished[0]?.path).toBe("eval-1/inputs/Decision");

    const missing = await collect(api, evaluateActivity({
      Inputs: { expression: { type: "Object", value: {} } }
    }));
    expect(missing.map(item => item.type)).toContain("Decision/MissingInput");

    const explicitZero = await collect(api, evaluateActivity({
      Inputs: { expression: { type: "Object", value: { amount: { kind: "number", value: "0" } } } }
    }));
    expect(explicitZero.map(item => item.type)).not.toContain("Decision/MissingInput");
    expect(explicitZero.map(item => item.type)).not.toContain("Decision/InvalidValue");
  });

  it("does not treat a custom activity Decision mailbox as a table pin", async () => {
    const api = stubApi();
    const issues = await collectEvaluateDecisionValidation(api, {
      nodeId: "notify-1",
      activityTypeKey: "Contoso.Notify",
      Decision: { expression: { type: "Literal", value: "ops@example.com" } }
    }, {
      typeName: "Contoso.Notify",
      inputs: [{ name: "Decision", referenceKey: "Decision", typeName: "System.String" }]
    });
    expect(issues).toEqual([]);
    expect(api.backend.http.getJson).not.toHaveBeenCalled();
  });

  it("re-resolves @latest after an unpublished table is published in the same session", async () => {
    const api = stubApi();
    let publishedVersionId: string | null = null;
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.endsWith("/versions/v1")) return published;
      if (path.endsWith("/approval") || path.endsWith("/decisions/approval")) return { ...asset, publishedVersionId, xml: asset.xml };
      return { ...asset, publishedVersionId, xml: asset.xml };
    });
    const activity = evaluateActivity({
      Decision: { expression: { type: "Literal", value: "approval@latest" } },
      Inputs: { expression: { type: "Object", value: { amount: { kind: "number", value: "950" } } } }
    });
    const unpublished = await collect(api, activity);
    expect(unpublished.map(item => item.type)).toContain("Decision/UnpublishedLatest");
    publishedVersionId = "v1";
    const publishedLatest = await collect(api, activity);
    expect(publishedLatest.map(item => item.type)).not.toContain("Decision/UnpublishedLatest");
    expect(publishedLatest.filter(item => item.type.startsWith("Decision/"))).toEqual([]);
  });

  it("re-resolves a new @latest published table that added a required input", async () => {
    const api = stubApi();
    const regionXml = editTable(asset.xml ?? "", { kind: "add-clause", output: false });
    const added = parseTable(regionXml).inputs.find(clause => clause.name !== "amount")!;
    let latest = published;
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.endsWith("/versions/v1") || path.endsWith("/versions/v2")) return latest;
      return { ...asset, publishedVersionId: latest.id, xml: latest.xml };
    });
    const activity = evaluateActivity({
      Decision: { expression: { type: "Literal", value: "approval@latest" } },
      Inputs: { expression: { type: "Object", value: { amount: { kind: "number", value: "950" } } } }
    });
    const first = await collect(api, activity);
    expect(first.map(item => item.type)).not.toContain("Decision/MissingInput");
    latest = { id: "v2", assetId: asset.id, number: 2, name: asset.name, xml: regionXml, sourceHash: "hash-region", publishedAt: asset.createdAt };
    const second = await collect(api, activity);
    expect(second.map(item => item.type)).toContain("Decision/MissingInput");
    expect(second.some(item => item.message.includes(added.name))).toBe(true);
  });

  it("retries a transient table load instead of caching the failure", async () => {
    const api = stubApi();
    let fail = true;
    api.backend.http.getJson.mockImplementation(async path => {
      if (fail) throw new Error("temporary lookup failed");
      if (path.endsWith("/versions/v1")) return published;
      return { ...asset, publishedVersionId: "v1", xml: asset.xml };
    });
    const activity = evaluateActivity({
      Inputs: { expression: { type: "Object", value: { amount: { kind: "number", value: "950" } } } }
    });
    const failed = await collect(api, activity);
    expect(failed.map(item => item.type)).toContain("Decision/Unavailable");
    expect(failed.some(item => item.message.includes("temporary lookup failed"))).toBe(true);
    fail = false;
    const recovered = await collect(api, activity);
    expect(recovered.map(item => item.type)).not.toContain("Decision/Unavailable");
  });

  it("shares one in-flight table load and does not reuse it after it settles", async () => {
    const api = stubApi();
    let inflight = 0;
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.endsWith("/versions/v1")) {
        inflight += 1;
        await new Promise(resolve => setTimeout(resolve, 20));
        return published;
      }
      return { ...asset, publishedVersionId: "v1" };
    });
    const [first, second] = await Promise.all([
      loadDecisionTable(api, "approval", "v1"),
      loadDecisionTable(api, "approval", "v1")
    ]);
    expect(first.table).not.toBeNull();
    expect(second.table).toBe(first.table);
    expect(inflight).toBe(1);
    await loadDecisionTable(api, "approval", "v1");
    expect(inflight).toBe(2);
  });

  it("reloads @latest when the Studio tab is shown again after a same-session publish", async () => {
    const api = stubApi();
    let publishedVersionId: string | null = null;
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.endsWith("/versions/v1")) return published;
      return { ...asset, publishedVersionId, xml: asset.xml };
    });
    const { host } = mount(<DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={{ amount: { kind: "number", value: "950" } }} context={expressionContext(pin("approval@latest"))} onChange={vi.fn()} />);
    await vi.waitFor(() => expect(host.textContent).toContain("no published version"));
    publishedVersionId = "v1";
    window.dispatchEvent(new Event("focus"));
    await vi.waitFor(() => expect(host.textContent).not.toContain("no published version"));
  });

  it("does not silently seed a new @latest clause after a foreground table refresh", async () => {
    const { host, onChange, added } = await mountLatestThenGrow();
    expect(host.querySelector(`[aria-label="${added.name}"]`)).not.toBeNull();
    expect(onChange.mock.calls.some(call => added.name in (call[0] as Record<string, unknown>))).toBe(false);
  });

  it("does not seed a new @latest clause after an edit following a foreground refresh", async () => {
    const { host, onChange, added } = await mountLatestThenGrow();
    onChange.mockClear();
    type(host.querySelector<HTMLInputElement>('[aria-label="customerCategory"]')!, "VIP");
    expect(onChange.mock.calls.at(-1)?.[0]).toEqual({
      amount: { kind: "number", value: "950" },
      customerCategory: { kind: "string", value: "VIP" }
    });
    expect(onChange.mock.calls.some(call => added.name in (call[0] as Record<string, unknown>))).toBe(false);
    expect(host.querySelector(`[aria-label="${added.name}"]`)).not.toBeNull();
  });

  it("does not seed a new @latest clause after reselecting the same pin", async () => {
    const { host, update, onChange, added, Harness } = await mountLatestThenGrow();
    onChange.mockClear();
    update(<Harness decision="approval@v1" />);
    await vi.waitFor(() => expect(host.querySelector(`[aria-label="${added.name}"]`)).toBeNull());
    update(<Harness decision="approval@latest" />);
    await vi.waitFor(() => expect(host.querySelector(`[aria-label="${added.name}"]`)).not.toBeNull());
    expect(onChange.mock.calls.some(call => added.name in (call[0] as Record<string, unknown>))).toBe(false);
  });

  it("does not seed a new @latest clause after remounting the same pin", async () => {
    const { host, onChange, added, authored, Harness } = await mountLatestThenGrow();
    type(host.querySelector<HTMLInputElement>('[aria-label="customerCategory"]')!, "VIP");
    expect(authored.current).toEqual({
      amount: { kind: "number", value: "950" },
      customerCategory: { kind: "string", value: "VIP" }
    });
    onChange.mockClear();
    disposals.splice(0).forEach(dispose => dispose());
    const remounted = mount(<Harness decision="approval@latest" />);
    await vi.waitFor(() => expect(remounted.host.querySelector(`[aria-label="${added.name}"]`)).not.toBeNull());
    expect(onChange.mock.calls.some(call => added.name in (call[0] as Record<string, unknown>))).toBe(false);
    expect(remounted.host.querySelector<HTMLInputElement>('[aria-label="customerCategory"]')?.value).toBe("VIP");
  });

  it("does not seed a new @latest clause when Advanced JSON is cleared after table growth", async () => {
    const { host, onChange, added } = await mountLatestThenGrow();
    click(host.querySelector("summary")!);
    type(host.querySelector<HTMLTextAreaElement>('[aria-label="Decision inputs JSON"]')!, "{}");
    await vi.waitFor(() => expect(onChange).toHaveBeenCalledWith({}));
    expect(onChange.mock.calls.some(call => added.name in (call[0] as Record<string, unknown>))).toBe(false);
    expect(host.querySelector<HTMLSelectElement>(`[aria-label="${added.name} value kind"]`)?.value).toBe("unset");
    expect(host.textContent).toContain("MissingInput");
  });

  it("keeps InvalidValue and field focus while a same-pin table refresh is in flight", async () => {
    const api = stubApi();
    const regionXml = editTable(asset.xml ?? "", { kind: "add-clause", output: false });
    let latest = published;
    let release: (() => void) | undefined;
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.includes("/versions/")) {
        if (latest.id === "v2") await new Promise<void>(resolve => { release = resolve; });
        return latest;
      }
      return { ...asset, publishedVersionId: latest.id, xml: latest.xml };
    });
    function Harness() {
      const [value, setValue] = useState<unknown>({ amount: { kind: "number", value: "950" } });
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Object" value={value} context={expressionContext(pin("approval@latest"))} onChange={next => setValue(next)} />;
    }
    const { host } = mount(<Harness />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).not.toBeNull());
    const amount = host.querySelector<HTMLInputElement>('[aria-label="amount"]')!;
    type(amount, "");
    amount.focus();
    expect(host.textContent).toContain("InvalidValue");
    latest = { ...published, id: "v2", number: 2, xml: regionXml, sourceHash: "hash-region" };
    window.dispatchEvent(new Event("focus"));
    await vi.waitFor(() => expect(release).toEqual(expect.any(Function)));
    expect(host.textContent).toContain("InvalidValue");
    expect(host.textContent).not.toContain("Loading input fields");
    expect(host.querySelector("fieldset")?.disabled).toBe(false);
    expect(document.activeElement).toBe(amount);
    release!();
    await vi.waitFor(() => expect(host.querySelector(`[aria-label="${parseTable(regionXml).inputs.find(clause => clause.name !== "amount")!.name}"]`)).not.toBeNull());
    expect(host.textContent).toContain("InvalidValue");
    expect(host.querySelector("fieldset")?.disabled).toBe(false);
    expect(document.activeElement).toBe(host.querySelector('[aria-label="amount"]'));
  });
});

async function mountLatestThenGrow() {
  const api = stubApi();
  const onChange = vi.fn();
  const authored = { current: { ...authoredTwo } as unknown };
  let latest = twoPublished;
  api.backend.http.getJson.mockImplementation(async path => {
    if (path.endsWith("/versions/v1")) return twoPublished;
    if (path.includes("/versions/")) return latest;
    return { ...asset, publishedVersionId: latest.id, xml: latest.xml };
  });
  function Harness({ decision }: { decision: string }) {
    const [value, setValue] = useState(authored.current);
    return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Object" value={value} context={expressionContext(pin(decision))} onChange={next => { onChange(next); authored.current = next; setValue(next); }} />;
  }
  const { host, update } = mount(<Harness decision="approval@latest" />);
  await vi.waitFor(() => expect(host.querySelector('[aria-label="customerCategory"]')).not.toBeNull());
  const added = parseTable(regionXml).inputs.find(clause => clause.name === "region")!;
  latest = regionPublished;
  onChange.mockClear();
  window.dispatchEvent(new Event("focus"));
  await vi.waitFor(() => expect(host.querySelector(`[aria-label="${added.name}"]`)).not.toBeNull());
  expect(onChange.mock.calls.some(call => added.name in (call[0] as Record<string, unknown>))).toBe(false);
  expect(asDecisionValueMap(authored.current).region).toBeUndefined();
  return { host, update, onChange, added, authored, Harness };
}

describe("decision version picker freshness", () => {
  it("lists a newly published version after the tab is shown again", async () => {
    const api = stubApi();
    let items = [{ id: "v1", assetId: asset.id, number: 1, name: asset.name, xml: asset.xml, sourceHash: "hash1", publishedAt: asset.createdAt }];
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.endsWith("/versions")) return { items };
      if (path.endsWith("/decisions")) return { items: [{ ...asset, publishedVersionId: items.at(-1)?.id }] };
      return { ...asset, publishedVersionId: items.at(-1)?.id };
    });
    const { host } = mount(<DecisionVersionEditor
      api={api}
      value="approval@latest"
      disabled={false}
      descriptor={{ name: "Decision", referenceKey: "Decision", typeName: "System.String", uiHint: "decision-version" }}
      context={{
        activity: { nodeId: "eval-1", activityVersionId: "eval-1-v", inputs: [], outputs: [] },
        activityId: "eval-1",
        expressionDescriptors: [],
        readOnly: false
      }}
      onChange={vi.fn()}
    />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="Business decision version"]')?.textContent).toContain("Version 1"));
    expect(host.querySelector('[aria-label="Business decision version"]')?.textContent).not.toContain("Version 2");
    items = [...items, { id: "v2", assetId: asset.id, number: 2, name: asset.name, xml: amountTextXml, sourceHash: "hash2", publishedAt: asset.createdAt }];
    window.dispatchEvent(new Event("focus"));
    await vi.waitFor(() => expect(host.querySelector('[aria-label="Business decision version"]')?.textContent).toContain("Version 2"));
  });

  it("ignores a stale focused versions response after the selected decision changes", async () => {
    const api = stubApi(), onChange = vi.fn();
    const scoringAsset = { ...asset, id: "scoring", name: "Scoring", publishedVersionId: "v-s1" };
    const approvalVersion = { id: "v-a1", assetId: asset.id, number: 3, name: asset.name, xml: asset.xml, sourceHash: "hash-a", publishedAt: asset.createdAt };
    const scoringVersion = { id: "v-s1", assetId: "scoring", number: 7, name: "Scoring", xml: scoreXml, sourceHash: "hash-s", publishedAt: asset.createdAt };
    let resolveApproval: ((items: typeof approvalVersion[]) => void) | undefined;
    let hangApproval = false;
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.endsWith("/decisions")) return { items: [asset, scoringAsset] };
      if (path.endsWith("/approval/versions")) {
        if (hangApproval) return await new Promise<typeof approvalVersion[]>(resolve => { resolveApproval = resolve; }).then(items => ({ items }));
        return { items: [approvalVersion] };
      }
      if (path.endsWith("/scoring/versions")) return { items: [scoringVersion] };
      return path.includes("scoring") ? scoringAsset : { ...asset, publishedVersionId: "v-a1" };
    });
    function Harness() {
      const [value, setValue] = useState("approval@latest");
      return <DecisionVersionEditor
        api={api}
        value={value}
        disabled={false}
        descriptor={{ name: "Decision", referenceKey: "Decision", typeName: "System.String", uiHint: "decision-version" }}
        context={{ activity: { nodeId: "eval-1", activityVersionId: "eval-1-v", inputs: [], outputs: [] }, activityId: "eval-1", expressionDescriptors: [], readOnly: false }}
        onChange={next => { onChange(next); setValue(String(next)); }}
      />;
    }
    const { host } = mount(<Harness />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="Business decision version"]')?.textContent).toContain("Version 3"));
    hangApproval = true;
    host.querySelector<HTMLSelectElement>('[aria-label="Business decision version"]')!.focus();
    await vi.waitFor(() => expect(resolveApproval).toEqual(expect.any(Function)));
    select(host.querySelector<HTMLSelectElement>('[aria-label="Business decision"]')!, "scoring");
    await vi.waitFor(() => expect(host.querySelector('[aria-label="Business decision version"]')?.textContent).toContain("Version 7"));
    resolveApproval!([approvalVersion]);
    await Promise.resolve();
    await Promise.resolve();
    const versionSelect = host.querySelector<HTMLSelectElement>('[aria-label="Business decision version"]')!;
    expect([...versionSelect.options].map(option => option.value)).toEqual(["latest", "v-s1"]);
    expect(versionSelect.textContent).not.toContain("Version 3");
    select(versionSelect, "v-s1");
    expect(onChange).toHaveBeenCalledWith("scoring@v-s1");
    expect(onChange).not.toHaveBeenCalledWith("scoring@v-a1");
    expect(onChange).not.toHaveBeenCalledWith("approval@v-a1");
  });

  it("ignores a stale focused versions response after unmount", async () => {
    const api = stubApi();
    const approvalVersion = { id: "v-a1", assetId: asset.id, number: 3, name: asset.name, xml: asset.xml, sourceHash: "hash-a", publishedAt: asset.createdAt };
    let resolveApproval: ((items: typeof approvalVersion[]) => void) | undefined;
    let hangApproval = false;
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.endsWith("/versions")) {
        if (hangApproval) return await new Promise<typeof approvalVersion[]>(resolve => { resolveApproval = resolve; }).then(items => ({ items }));
        return { items: [approvalVersion] };
      }
      if (path.endsWith("/decisions")) return { items: [asset] };
      return { ...asset, publishedVersionId: "v-a1" };
    });
    const errors: unknown[] = [];
    const consoleError = vi.spyOn(console, "error").mockImplementation(message => { errors.push(message); });
    const { host, dispose } = mount(<DecisionVersionEditor
      api={api}
      value="approval@latest"
      disabled={false}
      descriptor={{ name: "Decision", referenceKey: "Decision", typeName: "System.String", uiHint: "decision-version" }}
      context={{ activity: { nodeId: "eval-1", activityVersionId: "eval-1-v", inputs: [], outputs: [] }, activityId: "eval-1", expressionDescriptors: [], readOnly: false }}
      onChange={vi.fn()}
    />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="Business decision version"]')?.textContent).toContain("Version 3"));
    hangApproval = true;
    host.querySelector<HTMLSelectElement>('[aria-label="Business decision version"]')!.focus();
    await vi.waitFor(() => expect(resolveApproval).toEqual(expect.any(Function)));
    disposals.splice(disposals.lastIndexOf(dispose), 1);
    dispose();
    resolveApproval!([approvalVersion]);
    await Promise.resolve();
    await Promise.resolve();
    expect(errors.join(" ")).not.toMatch(/unmounted|not mounted/i);
    consoleError.mockRestore();
  });

  it("keeps the unpublished warning visible while a focused versions refresh is in flight", async () => {
    const api = stubApi();
    let resolveVersions: ((items: unknown[]) => void) | undefined;
    let hang = false;
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.endsWith("/versions")) {
        if (hang) return await new Promise<unknown[]>(resolve => { resolveVersions = resolve; }).then(items => ({ items }));
        return { items: [] };
      }
      if (path.endsWith("/decisions")) return { items: [{ ...asset, publishedVersionId: null }] };
      return { ...asset, publishedVersionId: null };
    });
    const { host } = mount(<DecisionVersionEditor
      api={api}
      value="approval@latest"
      disabled={false}
      descriptor={{ name: "Decision", typeName: "String" }}
      context={{} as never}
      onChange={vi.fn()}
    />);
    await vi.waitFor(() => expect(host.textContent).toContain("no published version"));
    hang = true;
    host.querySelector<HTMLSelectElement>('[aria-label="Business decision version"]')!.focus();
    await vi.waitFor(() => expect(resolveVersions).toEqual(expect.any(Function)));
    expect(host.textContent).toContain("no published version");
    resolveVersions!([]);
    await vi.waitFor(() => expect(host.textContent).toContain("no published version"));
  });
});
