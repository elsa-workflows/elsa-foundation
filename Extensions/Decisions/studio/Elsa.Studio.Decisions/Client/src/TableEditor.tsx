import React from "react";
import { StudioButton, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { editTable, parseTable, type Clause, type TableEdit } from "./decisionXml";
export function TableEditor({ xml, readOnly, onChange, onError }: { xml: string; readOnly: boolean; onChange(xml: string): void; onError(message: string): void }) {
  let table;
  try { table = parseTable(xml); } catch (error) { return <p role="alert">{error instanceof Error ? error.message : "This document cannot be edited as a table."}</p>; }
  const change = (edit: TableEdit) => { try { onChange(editTable(xml, edit)); } catch (error) { onError(error instanceof Error ? error.message : "The table change could not be applied."); } };
  const clause = (item: Clause, output: boolean, index: number) => <th scope="col" key={item.id} id={`clause-${item.id}`}>
    <label>{output ? "Output" : "Input"} {index + 1}<input aria-label={`${output ? "Output" : "Input"} ${index + 1} name`} value={item.name} disabled={readOnly} onChange={event => change({ kind: "clause", id: item.id, output, name: event.target.value, type: item.type })} /></label>
    <label className="decision-type">Type<select aria-label={`${output ? "Output" : "Input"} ${index + 1} type`} value={item.type} disabled={readOnly} onChange={event => change({ kind: "clause", id: item.id, output, name: item.name, type: event.target.value })}>
      {!["number", "string", "boolean"].includes(item.type) ? <option value={item.type}>{item.type || "Unspecified"} (unsupported)</option> : null}
      <option value="number">Number</option><option value="string">Text</option><option value="boolean">Boolean</option>
    </select></label>
    {!readOnly ? <StudioButton size="sm" variant="ghost" disabled={(output ? table.outputs : table.inputs).length <= 1} onClick={() => change({ kind: "delete-clause", id: item.id, output })}>Remove {output ? "output" : "input"} {index + 1}</StudioButton> : null}
  </th>;
  return <section aria-labelledby="decision-table-title" className="decision-table-section">
    <div className="decision-section-heading"><h2 id="decision-table-title">Decision table</h2><label>Hit policy<select value={table.hitPolicy} disabled={readOnly} onChange={event => change({ kind: "policy", value: event.target.value })} aria-describedby="decision-policy-help">
      {!["UNIQUE", "ANY", "FIRST", "RULE ORDER", "COLLECT"].includes(table.hitPolicy) ? <option value={table.hitPolicy}>{table.hitPolicy} (unsupported)</option> : null}
      <option value="UNIQUE">Unique — one match</option><option value="ANY">Any — equal results</option><option value="FIRST">First — first match</option><option value="RULE ORDER">Rule order — all matches</option><option value="COLLECT">Collect — all matches</option>
    </select></label></div>
    <p id="decision-policy-help" className="decision-muted">Conditions use literals, comparisons, ranges or alternatives. Use - for any non-null value. Text results need double quotes. Validation on the server checks this profile.</p>
    <StudioTableContainer className="decision-table-scroll" tabIndex={0} role="region" aria-label="Decision rules, scroll horizontally for more columns"><table className="decision-table"><caption className="decision-visually-hidden">{table.name || "Decision"} rules</caption><thead><tr><th scope="col">Rule</th>{table.inputs.map((item, index) => clause(item, false, index))}{table.outputs.map((item, index) => clause(item, true, index))}<th scope="col">Annotation</th>{!readOnly ? <th scope="col">Actions</th> : null}</tr></thead><tbody>
      {table.rules.map((rule, index) => <tr key={rule.id}><th scope="row" id={`rule-${rule.id}`}>{index + 1}</th>
        {rule.inputs.map((text, column) => <td key={table.inputs[column].id}><input id={`cell-${rule.id}-${table.inputs[column].id}`} aria-label={`Rule ${index + 1}, input ${table.inputs[column].name || column + 1}`} value={text} disabled={readOnly} onChange={event => change({ kind: "cell", ruleId: rule.id, output: false, column, text: event.target.value })} /></td>)}
        {rule.outputs.map((text, column) => <td key={table.outputs[column].id}><input id={`cell-${rule.id}-${table.outputs[column].id}`} aria-label={`Rule ${index + 1}, output ${table.outputs[column].name || column + 1}`} value={text} disabled={readOnly} onChange={event => change({ kind: "cell", ruleId: rule.id, output: true, column, text: event.target.value })} /></td>)}
        <td><input aria-label={`Rule ${index + 1} annotation`} value={rule.annotation} disabled={readOnly} onChange={event => change({ kind: "annotation", ruleId: rule.id, text: event.target.value })} /></td>
        {!readOnly ? <td><div className="decision-rule-actions"><StudioButton size="sm" disabled={index === 0} onClick={() => change({ kind: "move-rule", ruleId: rule.id, direction: -1 })} aria-label={`Move rule ${index + 1} up`}>↑</StudioButton><StudioButton size="sm" disabled={index === table.rules.length - 1} onClick={() => change({ kind: "move-rule", ruleId: rule.id, direction: 1 })} aria-label={`Move rule ${index + 1} down`}>↓</StudioButton><StudioButton size="sm" disabled={table.rules.length <= 1} onClick={() => change({ kind: "delete-rule", ruleId: rule.id })}>Remove rule {index + 1}</StudioButton></div></td> : null}
      </tr>)}
    </tbody></table></StudioTableContainer>
    {!readOnly ? <div className="decision-actions"><StudioButton onClick={() => change({ kind: "add-rule" })} disabled={table.rules.length >= 1000}>Add rule</StudioButton><StudioButton onClick={() => change({ kind: "add-clause", output: false })} disabled={table.inputs.length >= 32}>Add input</StudioButton><StudioButton onClick={() => change({ kind: "add-clause", output: true })} disabled={table.outputs.length >= 16}>Add output</StudioButton></div> : null}
  </section>;
}
