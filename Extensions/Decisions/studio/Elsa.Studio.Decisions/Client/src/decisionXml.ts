export const dmnNamespace = "https://www.omg.org/spec/DMN/20191111/MODEL/";
export type ClauseType = "number" | "string" | "boolean";
export interface Clause { id: string; name: string; type: string }
export interface Rule { id: string; inputs: string[]; outputs: string[]; annotation: string }
export interface Table { id: string; name: string; hitPolicy: string; inputs: Clause[]; outputs: Clause[]; rules: Rule[] }
export function decodeDecisionXml(bytes: ArrayBuffer): string {
  if (bytes.byteLength > 1024 * 1024) throw new Error("Decision documents must be at most 1 MiB.");
  try { return new TextDecoder("utf-8", { fatal: true, ignoreBOM: true }).decode(bytes); }
  catch { throw new Error("Import requires valid UTF-8 XML; the file was not changed or saved."); }
}
const children = (element: Element, name: string) => Array.from(element.children).filter(child => child.namespaceURI === dmnNamespace && child.localName === name);
const required = (element: Element, name: string) => {
  const entries = children(element, name);
  if (entries.length !== 1) throw new Error(`The table requires one ${name} element. Keep the original document and inspect the compatibility report.`);
  return entries[0];
};
function read(xml: string) {
  if (new TextEncoder().encode(xml).length > 1024 * 1024) throw new Error("Decision documents must be at most 1 MiB.");
  if (/<!DOCTYPE|<!ENTITY/i.test(xml)) throw new Error("Document types and XML entities are unsupported.");
  const doc = new DOMParser().parseFromString(xml, "application/xml");
  if (doc.getElementsByTagName("parsererror").length) throw new Error("The decision XML is malformed. The original document has been retained.");
  const root = doc.documentElement;
  if (root.namespaceURI !== dmnNamespace || root.localName !== "definitions") throw new Error("This editor supports the DMN 1.3 decision-table profile.");
  const decision = required(root, "decision"), table = required(decision, "decisionTable");
  const ids = new Set<string>();
  for (const element of Array.from(doc.getElementsByTagName("*"))) {
    if (!element.hasAttribute("id")) continue;
    const id = element.getAttribute("id")!;
    if (!id.trim() || ids.has(id)) throw new Error("The document contains missing or duplicate identities. Inspect its compatibility report before editing.");
    ids.add(id);
  }
  if ([decision, table, ...children(table, "input"), ...children(table, "output"), ...children(table, "rule")].some(element => !element.getAttribute("id"))) throw new Error("Table elements require stable identities before editing.");
  if (table.getAttribute("preferredOrientation") && table.getAttribute("preferredOrientation") !== "Rule-as-Row") throw new Error("This editor requires rules as rows.");
  return { doc, decision, table };
}
export function parseTable(xml: string): Table {
  const { decision, table } = read(xml);
  const inputs = children(table, "input").map(input => {
    const expression = required(input, "inputExpression");
    return { id: input.id, name: required(expression, "text").textContent ?? "", type: expression.getAttribute("typeRef") ?? "" };
  });
  const outputElements = children(table, "output");
  const outputs = outputElements.map(output => ({ id: output.id, name: outputElements.length === 1 ? output.getAttribute("label") ?? output.getAttribute("name") ?? "result" : output.getAttribute("name") ?? "", type: output.getAttribute("typeRef") ?? "" }));
  const rules = children(table, "rule").map(rule => ({ id: rule.id, inputs: children(rule, "inputEntry").map(entry => required(entry, "text").textContent ?? ""), outputs: children(rule, "outputEntry").map(entry => required(entry, "text").textContent ?? ""), annotation: children(rule, "description")[0]?.textContent ?? "" }));
  if (!inputs.length || !outputs.length || rules.some(rule => rule.inputs.length !== inputs.length || rule.outputs.length !== outputs.length)) throw new Error("The table has missing or mismatched columns. Inspect the compatibility report before editing.");
  return { id: decision.id, name: decision.getAttribute("name") ?? "", hitPolicy: table.getAttribute("hitPolicy") ?? "UNIQUE", inputs, outputs, rules };
}
export type TableEdit =
  | { kind: "cell"; ruleId: string; output: boolean; column: number; text: string }
  | { kind: "clause"; id: string; output: boolean; name: string; type: string }
  | { kind: "policy"; value: string }
  | { kind: "annotation"; ruleId: string; text: string }
  | { kind: "add-rule" }
  | { kind: "delete-rule"; ruleId: string }
  | { kind: "move-rule"; ruleId: string; direction: -1 | 1 }
  | { kind: "add-clause"; output: boolean }
  | { kind: "delete-clause"; id: string; output: boolean };
function textElement(doc: Document, name: string, value: string) { const element = doc.createElementNS(dmnNamespace, name); element.textContent = value; return element; }
function setPlainText(element: Element, value: string) {
  if (element.children.length) throw new Error("This text contains XML content that cannot be safely replaced by plain text.");
  element.textContent = value;
}
function entry(doc: Document, output: boolean, value: string) { const element = doc.createElementNS(dmnNamespace, output ? "outputEntry" : "inputEntry"); element.setAttribute("id", `cell_${crypto.randomUUID().replaceAll("-", "")}`); element.append(textElement(doc, "text", value)); return element; }
const defaultOutput = (type: string | null) => type === "number" ? "0" : type === "boolean" ? "false" : '""';
export function editTable(xml: string, edit: TableEdit): string {
  const { doc, decision, table } = read(xml);
  const rules = children(table, "rule");
  if (edit.kind === "cell") {
    const rule = rules.find(item => item.id === edit.ruleId);
    if (!rule) throw new Error("The selected rule no longer exists.");
    const cell = children(rule, edit.output ? "outputEntry" : "inputEntry")[edit.column];
    if (!cell) throw new Error("The selected cell no longer exists.");
    const text = required(cell, "text");
    setPlainText(text, edit.text);
  } else if (edit.kind === "policy") table.setAttribute("hitPolicy", edit.value);
  else if (edit.kind === "clause") {
    const clause = children(table, edit.output ? "output" : "input").find(item => item.id === edit.id);
    if (!clause) throw new Error("The selected column no longer exists.");
    if (edit.output) { clause.setAttribute(children(table, "output").length === 1 ? "label" : "name", edit.name); clause.setAttribute("typeRef", edit.type); }
    else {
      const expression = required(clause, "inputExpression"), text = required(expression, "text"), oldName = text.textContent?.trim();
      for (const data of children(doc.documentElement, "inputData")) {
        const variable = required(data, "variable");
        if ((variable.getAttribute("name") ?? data.getAttribute("name")) !== oldName) continue;
        variable.setAttribute("name", edit.name);
        if (variable.hasAttribute("typeRef")) variable.setAttribute("typeRef", edit.type);
      }
      setPlainText(text, edit.name); expression.setAttribute("typeRef", edit.type);
    }
  } else if (edit.kind === "annotation") {
    const rule = rules.find(item => item.id === edit.ruleId);
    if (!rule) throw new Error("The selected rule no longer exists.");
    const description = children(rule, "description")[0];
    if (description) setPlainText(description, edit.text); else rule.prepend(textElement(doc, "description", edit.text));
  } else if (edit.kind === "add-rule") {
    const rule = doc.createElementNS(dmnNamespace, "rule"); rule.setAttribute("id", `rule_${crypto.randomUUID().replaceAll("-", "")}`);
    children(table, "input").forEach(() => rule.append(entry(doc, false, "-")));
    children(table, "output").forEach(output => rule.append(entry(doc, true, defaultOutput(output.getAttribute("typeRef")))));
    table.append(rule);
  } else if (edit.kind === "delete-rule") {
    if (rules.length <= 1) throw new Error("Keep at least one rule in the table.");
    rules.find(item => item.id === edit.ruleId)?.remove();
  } else if (edit.kind === "move-rule") {
    const index = rules.findIndex(item => item.id === edit.ruleId), target = rules[index + edit.direction];
    if (index < 0 || !target) return xml;
    table.insertBefore(rules[index], edit.direction < 0 ? target : target.nextSibling);
  } else if (edit.kind === "add-clause") {
    const columns = children(table, edit.output ? "output" : "input");
    if (columns.length >= (edit.output ? 16 : 32)) throw new Error("The table has reached its column limit.");
    const clause = doc.createElementNS(dmnNamespace, edit.output ? "output" : "input"); const id = `clause_${crypto.randomUUID().replaceAll("-", "")}`; clause.setAttribute("id", id);
    if (edit.output) {
      if (columns.length === 1 && !columns[0].getAttribute("name")) columns[0].setAttribute("name", columns[0].getAttribute("label")?.trim() || "result1");
      const existingNames = new Set(columns.map(column => column.getAttribute("name")));
      let suffix = columns.length + 1; while (existingNames.has(`result${suffix}`)) suffix++;
      clause.setAttribute("name", `result${suffix}`); clause.setAttribute("typeRef", "string");
    }
    else {
      const names = new Set(columns.map(column => required(required(column, "inputExpression"), "text").textContent?.trim()));
      let suffix = columns.length + 1; while (names.has(`input${suffix}`)) suffix++;
      const expression = doc.createElementNS(dmnNamespace, "inputExpression"); expression.setAttribute("id", `${id}_expression`); expression.setAttribute("typeRef", "string"); expression.append(textElement(doc, "text", `input${suffix}`)); clause.append(expression);
    }
    const anchor = edit.output ? rules[0] : children(table, "output")[0] ?? rules[0]; table.insertBefore(clause, anchor ?? null);
    rules.forEach(rule => { const cell = entry(doc, edit.output, edit.output ? '""' : "-"); rule.insertBefore(cell, edit.output ? null : children(rule, "outputEntry")[0] ?? null); });
  } else {
    const columns = children(table, edit.output ? "output" : "input");
    if (columns.length <= 1) throw new Error("Keep at least one input and output column.");
    const index = columns.findIndex(item => item.id === edit.id);
    if (index < 0) throw new Error("The selected column no longer exists.");
    if (!edit.output) {
      const name = required(required(columns[index], "inputExpression"), "text").textContent?.trim();
      if (children(doc.documentElement, "inputData").some(data => (required(data, "variable").getAttribute("name") ?? data.getAttribute("name")) === name))
        throw new Error("This input has a retained DMN input-data declaration. Delete it in a DMN modeler so its references and diagrams remain consistent.");
    }
    columns[index].remove();
    if (edit.output && columns.length === 2) {
      const remaining = columns[index === 0 ? 1 : 0];
      if (!remaining.hasAttribute("label")) remaining.setAttribute("label", remaining.getAttribute("name") ?? "result");
      remaining.removeAttribute("name");
    }
    rules.forEach(rule => children(rule, edit.output ? "outputEntry" : "inputEntry")[index]?.remove());
  }
  if (edit.kind === "clause" && edit.output || edit.kind === "add-clause" && edit.output || edit.kind === "delete-clause" && edit.output) {
    const variable = children(decision, "variable")[0], outputs = children(table, "output");
    if (variable?.hasAttribute("typeRef")) {
      if (outputs.length === 1) variable.setAttribute("typeRef", outputs[0].getAttribute("typeRef") ?? "");
      else variable.removeAttribute("typeRef");
    }
  }
  return new XMLSerializer().serializeToString(doc);
}
export function newDecisionXml() {
  const id = `decision_${crypto.randomUUID().replaceAll("-", "")}`;
  return `<?xml version="1.0" encoding="utf-8"?><definitions xmlns="${dmnNamespace}" id="${id}_definitions" name="Decision" namespace="urn:elsa:decisions"><decision id="${id}" name="Decision"><decisionTable id="${id}_table" hitPolicy="UNIQUE"><input id="${id}_input"><inputExpression id="${id}_expression" typeRef="number"><text>amount</text></inputExpression></input><output id="${id}_output" label="result" typeRef="string"/><rule id="${id}_rule"><inputEntry id="${id}_condition"><text>-</text></inputEntry><outputEntry id="${id}_result"><text>"Approved"</text></outputEntry></rule></decisionTable></decision></definitions>`;
}
