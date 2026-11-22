import React from "react";
import type { Diagnostic } from "./decisionsApi";
import type { Table } from "./decisionXml";
import { decisionDiagnosticMessage } from "./decisionDiagnosticMessage";

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
    const message = decisionDiagnosticMessage(item, table);
    const key = `${item.code}\u0000${message}`;
    let group = groups.get(key);
    if (!group) { group = { code: item.code, message, items: [] }; groups.set(key, group); }
    const locationKey = `${item.ruleId ?? ""}\u0000${item.clauseId ?? ""}`;
    if (!group.items.some(existing => `${existing.ruleId ?? ""}\u0000${existing.clauseId ?? ""}` === locationKey)) group.items.push(item);
  }
  if (!groups.size) return null;

  return <section className="decision-diagnostic-groups" aria-label={label}>
    <h2 className="decision-visually-hidden">{label}</h2>
    {[...groups.values()].map((group, index) => {
      const locations = group.items.filter(item => item.ruleId || item.clauseId);
      return <div className="decision-diagnostic-group" key={`${group.code}-${index}`}>
        <p className="decision-diagnostic-message"><strong>{group.code}</strong><span>{group.message}</span></p>
        {locations.length ? <div className="decision-diagnostic-locations" aria-label="Affected cells">
          {locations.map((item, itemIndex) => {
          const location = diagnosticLocation(item, table);
          return onReview ? <button className="decision-link" key={`${item.ruleId}-${item.clauseId}-${itemIndex}`} type="button" aria-label={`Review ${location || "affected cell"}`} onClick={() => onReview(item)}>{location || "Affected cell"}</button>
            : <span key={`${item.ruleId}-${item.clauseId}-${itemIndex}`}>{location || "Affected cell"}</span>;
          })}
        </div> : null}
        {locations.length ? <p className="decision-diagnostic-summary">{locations.length} affected {locations.length === 1 ? "cell needs" : "cells need"} review.</p> : null}
      </div>;
    })}
  </section>;
}