import React from "react";
import type { Diagnostic } from "./decisionsApi";
import type { Table } from "./decisionXml";

interface DecisionDiagnosticsProps {
  diagnostics: Diagnostic[];
  table: Table | null;
  label: string;
  onReview?(diagnostic: Diagnostic): void;
}

function diagnosticLocation(item: Diagnostic, table: Table | null) {
  const ruleIndex = item.ruleId && table ? table.rules.findIndex(rule => rule.id === item.ruleId) : -1;
  const clause = item.clauseId && table ? [...table.inputs, ...table.outputs].find(entry => entry.id === item.clauseId) : undefined;
  const parts = [item.ruleId ? ruleIndex >= 0 ? `Rule ${ruleIndex + 1}` : "Rule" : "", item.clauseId ? `Column ${clause?.name || ""}`.trim() : ""].filter(Boolean);
  return parts.length ? parts.join(" · ") : "";
}

export function DecisionDiagnostics({ diagnostics, table, label, onReview }: DecisionDiagnosticsProps) {
  const groups = new Map<string, { code: string; message: string; items: Diagnostic[] }>();
  for (const item of diagnostics) {
    const key = `${item.code}\u0000${item.message}`;
    let group = groups.get(key);
    if (!group) { group = { code: item.code, message: item.message, items: [] }; groups.set(key, group); }
    const locationKey = `${item.ruleId ?? ""}\u0000${item.clauseId ?? ""}`;
    if (!group.items.some(existing => `${existing.ruleId ?? ""}\u0000${existing.clauseId ?? ""}` === locationKey)) group.items.push(item);
  }
  if (!groups.size) return null;
  const needsSyntaxGuidance = table !== null && [...groups.values()].some(group => group.code === "UnsupportedExpression");

  return <section className="decision-diagnostic-groups" aria-label={label}>
    <h2 className="decision-visually-hidden">{label}</h2>
    {[...groups.values()].map((group, index) => <div className="decision-diagnostic-group" key={`${group.code}-${index}`}>
      <p className="decision-diagnostic-message"><strong>{group.code}</strong><span>{group.message}</span></p>
      {group.items.some(item => item.ruleId || item.clauseId) ? <div className="decision-diagnostic-locations" aria-label="Affected cells">
        {group.items.filter(item => item.ruleId || item.clauseId).map((item, itemIndex) => {
          const location = diagnosticLocation(item, table);
          return onReview ? <button className="decision-link" key={`${item.ruleId}-${item.clauseId}-${itemIndex}`} type="button" aria-label={`Review ${location || "affected cell"}`} onClick={() => onReview(item)}>{location || "Affected cell"}</button>
            : <span key={`${item.ruleId}-${item.clauseId}-${itemIndex}`}>{location || "Affected cell"}</span>;
        })}
      </div> : null}
    </div>)}
    {needsSyntaxGuidance ? <aside className="decision-expression-guidance" role="note" aria-label="Supported expression examples">
      <p>These cells use a limited decision-expression syntax, not general code. Number conditions can look like <code>&lt; 21</code> or <code>&gt;= 22</code>; match text with a quoted literal such as <code>"male"</code>.</p>
    </aside> : null}
  </section>;
}