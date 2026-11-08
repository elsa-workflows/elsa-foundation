import React, { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioExpressionEditorContext } from "@elsa-workflows/studio-sdk";
import { DecisionInputsEditor } from "../DecisionInputsEditor";
import { DecisionVersionEditor } from "../DecisionVersionEditor";
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
  it("seeds amount from the selected published table and commits typed values without JSON", async () => {
    const api = stubApi(), onChange = vi.fn();
    api.backend.http.getJson.mockImplementation(async path => path.endsWith("/versions/v1") ? published : { ...asset, publishedVersionId: "v1" });
    function Harness() {
      const [value, setValue] = useState<unknown>({});
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Literal" value={value} context={expressionContext(pin("approval@v1"))} onChange={next => { onChange(next); setValue(next); }} />;
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

  it("reseeds after Advanced JSON is cleared and restores stored JSON on invalid blur", async () => {
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
    await vi.waitFor(() => expect(onChange).toHaveBeenCalledWith({ amount: { kind: "number", value: "0" } }));
    expect(host.querySelector<HTMLInputElement>('[aria-label="amount"]')?.value).toBe("0");
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
    expect(onChange.mock.calls.at(-1)?.[0]).toEqual({ score: { kind: "number", value: "0" } });
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).toBeNull());
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
    const api = stubApi();
    const onChange = vi.fn();
    const regionXml = editTable(asset.xml ?? "", { kind: "add-clause", output: false });
    let latest = published;
    api.backend.http.getJson.mockImplementation(async path => {
      if (path.includes("/versions/")) return latest;
      return { ...asset, publishedVersionId: latest.id, xml: latest.xml };
    });
    function Harness() {
      const [value, setValue] = useState<unknown>({ amount: { kind: "number", value: "950" } });
      return <DecisionInputsEditor api={api} descriptor={descriptor} syntax="Object" value={value} context={expressionContext(pin("approval@latest"))} onChange={next => { onChange(next); setValue(next); }} />;
    }
    const { host } = mount(<Harness />);
    await vi.waitFor(() => expect(host.querySelector('[aria-label="amount"]')).not.toBeNull());
    const added = parseTable(regionXml).inputs.find(clause => clause.name !== "amount")!;
    latest = { ...published, id: "v2", number: 2, xml: regionXml, sourceHash: "hash-region" };
    onChange.mockClear();
    window.dispatchEvent(new Event("focus"));
    await vi.waitFor(() => expect(host.querySelector(`[aria-label="${added.name}"]`)).not.toBeNull());
    expect(onChange.mock.calls.some(call => added.name in (call[0] as Record<string, unknown>))).toBe(false);
  });
});

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
});
