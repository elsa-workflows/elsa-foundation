import { describe, expect, it } from "vitest";
import { decodeDecisionXml, dmnNamespace, editTable, newDecisionXml, parseTable } from "../decisionXml";
describe("targeted decision XML authoring", () => {
  it("preserves a UTF-8 byte-order mark and refuses invalid byte sequences instead of replacing them", () => {
    const source = `\uFEFF${newDecisionXml()}`;
    expect(decodeDecisionXml(new TextEncoder().encode(source).buffer)).toBe(source);
    expect(() => decodeDecisionXml(new Uint8Array([0xc3, 0x28]).buffer)).toThrow("valid UTF-8");
  });
  it("preserves diagram content, opaque extensions and rule identity when editing a cell", () => {
    const original = newDecisionXml().replace('<decision ', '<decision xmlns:extra="urn:other" extra:flag="unchanged" ').replace("<decisionTable", '<extensionElements><extra:policy xmlns:extra="urn:other">opaque</extra:policy></extensionElements><decisionTable').replace("</definitions>", '<dmndi:DMNDI xmlns:dmndi="https://www.omg.org/spec/DMN/20191111/DMNDI/"><dmndi:DMNDiagram id="diagram" /></dmndi:DMNDI></definitions>');
    const table = parseTable(original), ruleId = table.rules[0].id;
    const edited = editTable(original, { kind: "cell", ruleId, output: false, column: 0, text: "<= 9007199254740993" });
    const doc = new DOMParser().parseFromString(edited, "application/xml");
    expect(parseTable(edited).rules[0]).toEqual({ ...table.rules[0], inputs: ["<= 9007199254740993"] });
    expect(doc.getElementsByTagNameNS("urn:other", "policy")[0].textContent).toBe("opaque");
    expect(doc.getElementsByTagNameNS(dmnNamespace, "decision")[0].getAttributeNS("urn:other", "flag")).toBe("unchanged");
    expect(doc.getElementsByTagNameNS("https://www.omg.org/spec/DMN/20191111/DMNDI/", "DMNDiagram")[0].id).toBe("diagram");
  });
  it("keeps exact numeric text through serialization without coercing it into JavaScript Number", () => {
    const xml = newDecisionXml(), table = parseTable(xml), number = "1234567890123456789012345678901234";
    const edited = editTable(xml, { kind: "cell", ruleId: table.rules[0].id, output: true, column: 0, text: number });
    expect(parseTable(edited).rules[0].outputs).toEqual([number]);
  });
  it("adds/removes columns and reorders rules without losing original cells", () => {
    let xml = newDecisionXml(); const first = parseTable(xml).rules[0].id;
    xml = editTable(xml, { kind: "add-rule" });
    const second = parseTable(xml).rules[1].id;
    xml = editTable(xml, { kind: "cell", ruleId: second, output: true, column: 0, text: '"Manager"' });
    xml = editTable(xml, { kind: "add-clause", output: false });
    expect(parseTable(xml).rules.map(rule => rule.inputs.length)).toEqual([2, 2]);
    xml = editTable(xml, { kind: "move-rule", ruleId: second, direction: -1 });
    expect(parseTable(xml).rules.map(rule => rule.id)).toEqual([second, first]);
    xml = editTable(xml, { kind: "delete-clause", id: parseTable(xml).inputs[1].id, output: false });
    expect(parseTable(xml).rules[0].outputs).toEqual(['"Manager"']);
    expect(parseTable(xml).rules.every(rule => rule.inputs.length === 1)).toBe(true);
  });
  it("keeps imported input declarations consistent when renaming or retyping a column", () => {
    const xml = newDecisionXml().replace("<decision ", '<inputData id="source" name="Purchase amount"><variable id="source_variable" name="amount" typeRef="number"/></inputData><decision '), table = parseTable(xml);
    const changed = editTable(xml, { kind: "clause", id: table.inputs[0].id, output: false, name: "total", type: "string" });
    const doc = new DOMParser().parseFromString(changed, "application/xml"), data = doc.getElementsByTagNameNS(dmnNamespace, "inputData")[0];
    expect(data.getAttribute("name")).toBe("Purchase amount"); expect(data.id).toBe("source");
    expect(data.firstElementChild?.getAttribute("name")).toBe("total"); expect(data.firstElementChild?.getAttribute("typeRef")).toBe("string");
    expect(data.firstElementChild?.id).toBe("source_variable");
  });
  it("refuses deletion of a declaration-backed input rather than leaving stale declarations or dropping diagrams", () => {
    const original = newDecisionXml().replace("<decision ", '<inputData id="source" name="amount"><variable id="source_variable" typeRef="number"/></inputData><decision ');
    const xml = editTable(original, { kind: "add-clause", output: false });
    expect(() => editTable(xml, { kind: "delete-clause", id: parseTable(xml).inputs[0].id, output: false })).toThrow("input-data declaration");
    expect(parseTable(xml).inputs[0].name).toBe("amount");
  });
  it("keeps an optional decision variable consistent across scalar type and result-shape changes", () => {
    const xml = newDecisionXml().replace("<decisionTable", '<variable id="decision_variable" name="approval" typeRef="string"/><decisionTable'), table = parseTable(xml);
    const scalar = editTable(xml, { kind: "clause", id: table.outputs[0].id, output: true, name: "result", type: "number" });
    const variable = (source: string) => new DOMParser().parseFromString(source, "application/xml").getElementsByTagNameNS(dmnNamespace, "variable")[0];
    expect(variable(scalar).getAttribute("typeRef")).toBe("number");
    const multiple = editTable(scalar, { kind: "add-clause", output: true });
    expect(variable(multiple).hasAttribute("typeRef")).toBe(false); expect(variable(multiple).id).toBe("decision_variable");
  });
  it("generates a distinct input symbol after existing columns have been renamed", () => {
    const xml = newDecisionXml(), table = parseTable(xml);
    const renamed = editTable(xml, { kind: "clause", id: table.inputs[0].id, output: false, name: "input2", type: "number" });
    expect(parseTable(editTable(renamed, { kind: "add-clause", output: false })).inputs.map(input => input.name)).toEqual(["input2", "input3"]);
  });
  it("refuses ambiguous tables and entities while retaining the source", () => {
    const xml = newDecisionXml();
    expect(() => parseTable(xml.replace("<definitions", '<!DOCTYPE definitions [<!ENTITY foo SYSTEM "file:///etc/passwd">]><definitions'))).toThrow("entities");
    expect(() => parseTable(xml.replace("</definitions>", '<decision id="second" /></definitions>'))).toThrow("one decision");
    expect(() => parseTable("<broken>")).toThrow("malformed");
  });
  it("refuses replacement of a text cell containing unknown XML instead of dropping it", () => {
    const xml = newDecisionXml().replace("<text>-</text>", '<text><opaque xmlns="urn:other" />-</text>');
    const table = parseTable(xml);
    expect(() => editTable(xml, { kind: "cell", ruleId: table.rules[0].id, output: false, column: 0, text: "< 10" })).toThrow("cannot be safely replaced");
  });
  it("refuses duplicate rule identities before a second-row edit could retarget the first row", () => {
    const first = newDecisionXml(), added = editTable(first, { kind: "add-rule" }), table = parseTable(added);
    const duplicate = added.replace(`id="${table.rules[1].id}"`, `id="${table.rules[0].id}"`);
    expect(() => parseTable(duplicate)).toThrow("duplicate identities");
    expect(() => editTable(duplicate, { kind: "cell", ruleId: table.rules[1].id, column: 0, output: false, text: "< 50" })).toThrow("duplicate identities");
  });
  it("refuses an input rename that would discard nested unknown XML", () => {
    const xml = newDecisionXml().replace("<text>amount</text>", '<text><opaque xmlns="urn:other" />amount</text>'), table = parseTable(xml);
    expect(() => editTable(xml, { kind: "clause", id: table.inputs[0].id, output: false, name: "newAmount", type: "number" })).toThrow("cannot be safely replaced");
  });
  it("authors an unnamed single output using its label and retains omission on unrelated edits", () => {
    const xml = newDecisionXml(), table = parseTable(xml);
    expect(table.outputs[0].name).toBe("result");
    const changed = editTable(xml, { kind: "cell", ruleId: table.rules[0].id, output: false, column: 0, text: "> 1000" });
    const output = new DOMParser().parseFromString(changed, "application/xml").getElementsByTagNameNS(dmnNamespace, "output")[0];
    expect(output.hasAttribute("name")).toBe(false); expect(output.getAttribute("label")).toBe("result");
    const labeled = editTable(changed, { kind: "clause", id: table.outputs[0].id, output: true, name: "Approval level", type: "string" });
    const node = new DOMParser().parseFromString(labeled, "application/xml").getElementsByTagNameNS(dmnNamespace, "output")[0];
    expect(node.getAttribute("label")).toBe("Approval level"); expect(node.hasAttribute("name")).toBe(false);
  });
  it("gives multiple outputs distinct names and returns to an unnamed scalar output when one is removed", () => {
    let xml = newDecisionXml(); const table = parseTable(xml);
    xml = editTable(xml, { kind: "clause", id: table.outputs[0].id, output: true, name: "result2", type: "string" });
    xml = editTable(xml, { kind: "add-clause", output: true });
    expect(parseTable(xml).outputs.map(output => output.name)).toEqual(["result2", "result3"]);
    xml = editTable(xml, { kind: "delete-clause", id: parseTable(xml).outputs[1].id, output: true });
    const output = new DOMParser().parseFromString(xml, "application/xml").getElementsByTagNameNS(dmnNamespace, "output")[0];
    expect(output.hasAttribute("name")).toBe(false); expect(parseTable(xml).outputs[0].name).toBe("result2");
  });
  it("preserves the commonly named single-output form without changing it during other edits", () => {
    const xml = newDecisionXml().replace('label="result"', 'name="result"'), table = parseTable(xml);
    const changed = editTable(xml, { kind: "cell", ruleId: table.rules[0].id, output: true, column: 0, text: '"Manager"' });
    const output = new DOMParser().parseFromString(changed, "application/xml").getElementsByTagNameNS(dmnNamespace, "output")[0];
    expect(output.getAttribute("name")).toBe("result"); expect(output.hasAttribute("label")).toBe(false);
  });
});
