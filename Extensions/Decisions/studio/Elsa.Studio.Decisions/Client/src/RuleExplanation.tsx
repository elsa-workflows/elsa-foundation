import React from "react";
import { StudioTableContainer } from "@elsa-workflows/studio-ui";
import type { RuleTrace } from "./decisionsApi";
export function RuleExplanation({ trace }: { trace: RuleTrace[] }) {
  return <StudioTableContainer><table className="decision-case-table"><caption>Actual rule evaluation</caption><thead><tr><th scope="col">Rule</th><th scope="col">Matched</th><th scope="col">Selected</th><th scope="col">Input tests</th></tr></thead><tbody>{trace.map(item => <tr key={item.ruleId}><th scope="row">{item.ruleId}</th><td>{item.matched ? "Yes" : "No"}</td><td>{item.selected ? "Yes" : "No"}</td><td>{item.cells.map(cell => `${cell.inputId}: ${cell.truth}`).join(", ")}</td></tr>)}</tbody></table></StudioTableContainer>;
}
