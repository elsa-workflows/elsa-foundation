import React from "react";
import { StudioTableContainer } from "@elsa-workflows/studio-ui";
import type { RuleTrace } from "./decisionsApi";
import { parseTable } from "./decisionXml";
export interface ExplanationLabels { rules: Record<string, string>; inputs: Record<string, string> }
export function labelsForXml(xml: string): ExplanationLabels | undefined {
  try { const table = parseTable(xml); return { rules: Object.fromEntries(table.rules.map((rule, index) => [rule.id, `Rule ${index + 1}`])), inputs: Object.fromEntries(table.inputs.map(clause => [clause.id, clause.name])) }; } catch { return undefined; }
}
export function RuleExplanation({ trace, labels }: { trace: RuleTrace[]; labels?: ExplanationLabels }) {
  return <StudioTableContainer role="region" aria-label="Actual rule evaluation, scroll horizontally for input tests" tabIndex={0}><table className="decision-case-table decision-rule-table"><caption>Actual rule evaluation</caption><thead><tr><th scope="col">Rule</th><th scope="col">Matched</th><th scope="col">Selected</th><th scope="col">Input tests</th></tr></thead><tbody>{trace.map(item => <tr key={item.ruleId}><th scope="row">{labels?.rules[item.ruleId] ?? item.ruleId}{labels?.rules[item.ruleId] ? <details className="decision-rule-provenance"><summary>Rule identity</summary><code>{item.ruleId}</code></details> : null}</th><td>{item.matched ? "Yes" : "No"}</td><td>{item.selected ? "Yes" : "No"}</td><td>{item.cells.map(cell => `${labels?.inputs[cell.inputId] ?? cell.inputId}: ${cell.truth}`).join(", ")}</td></tr>)}</tbody></table></StudioTableContainer>;
}
