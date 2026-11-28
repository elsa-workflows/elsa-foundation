import React, { useState } from "react";
import { StudioTableContainer, StudioToggleButton } from "@elsa-workflows/studio-ui";
import type { RuleTrace } from "./decisionsApi";
import { parseTable } from "./decisionXml";
export interface ExplanationLabels { rules: Record<string, string>; inputs: Record<string, string> }
export function labelsForXml(xml: string): ExplanationLabels | undefined {
  try { const table = parseTable(xml); return { rules: Object.fromEntries(table.rules.map((rule, index) => [rule.id, `Rule ${index + 1}`])), inputs: Object.fromEntries(table.inputs.map(clause => [clause.id, clause.name])) }; } catch { return undefined; }
}
export function RuleExplanation({ trace, labels }: { trace: RuleTrace[]; labels?: ExplanationLabels }) {
  const [matchedOnly, setMatchedOnly] = useState(false);
  const [showRuleIds, setShowRuleIds] = useState(false);
  const matchedCount = trace.filter(item => item.matched).length;
  const selectedCount = trace.filter(item => item.selected).length;
  const canFilterMatched = matchedCount > 0 && matchedCount < trace.length;
  const rows = matchedOnly && canFilterMatched ? trace.filter(item => item.matched) : trace;
  const hasFriendlyLabels = !!labels && trace.some(item => !!labels.rules[item.ruleId]);
  return <div className="decision-rule-explanation">
    <div className="decision-rule-explanation-header">
      <div className="decision-rule-explanation-title">
        <span>Actual rule evaluation</span>
        <span className="decision-rule-explanation-summary">{matchedCount} matched · {selectedCount} selected</span>
      </div>
      <div className="decision-rule-explanation-controls">
        {canFilterMatched ? <div className="decision-filter-group" role="group" aria-label="Filter evaluated rules">
          <StudioToggleButton size="sm" pressed={!matchedOnly} onClick={() => setMatchedOnly(false)}>All ({trace.length})</StudioToggleButton>
          <StudioToggleButton size="sm" pressed={matchedOnly} onClick={() => setMatchedOnly(true)}>Matched ({matchedCount})</StudioToggleButton>
        </div> : null}
        {hasFriendlyLabels ? <StudioToggleButton size="sm" pressed={showRuleIds} onClick={() => setShowRuleIds(previous => !previous)}>Rule IDs</StudioToggleButton> : null}
      </div>
    </div>
    <StudioTableContainer role="region" aria-label="Actual rule evaluation, scroll horizontally for input tests" tabIndex={0}>
      <table className="studio-table decision-case-table decision-rule-table">
        <caption className="decision-visually-hidden">Actual rule evaluation</caption>
        <thead><tr><th scope="col">Rule</th><th scope="col">Matched</th><th scope="col">Selected</th><th scope="col">Input tests</th></tr></thead>
        <tbody>{rows.map(item => <tr key={item.ruleId} data-matched={item.matched || undefined} data-selected={item.selected || undefined}>
          <th scope="row" title={item.ruleId}>
            <span className="decision-rule-label">{labels?.rules[item.ruleId] ?? item.ruleId}</span>
            {showRuleIds && labels?.rules[item.ruleId] ? <code className="decision-rule-id">{item.ruleId}</code> : null}
          </th>
          <td className="decision-rule-flag" data-yes={item.matched || undefined}>{item.matched ? "Yes" : "No"}</td>
          <td className="decision-rule-flag" data-yes={item.selected || undefined}>{item.selected ? "Yes" : "No"}</td>
          <td className="decision-rule-cells">{item.cells.map(cell => `${labels?.inputs[cell.inputId] ?? cell.inputId}: ${cell.truth}`).join(", ")}</td>
        </tr>)}</tbody>
      </table>
    </StudioTableContainer>
  </div>;
}
