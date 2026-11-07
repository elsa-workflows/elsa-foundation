import { useState } from "react";
import { BpmnNotation } from "./bpmnNotation";
import { bpmnDocumentToolboxConnections, bpmnDocumentToolboxShapes, bpmnToolboxNotation, type BpmnToolboxShape } from "./bpmnDocumentToolbox";
import type { BpmnDocumentConnectionKind } from "./bpmnDocumentPolicy";
import "./bpmnDocumentControls.css";

export type BpmnDocumentTool = { kind: "shape"; entry: BpmnToolboxShape } | { kind: "connection"; connectionKind: BpmnDocumentConnectionKind };
export interface BpmnDocumentPaletteProps {
  readOnly: boolean;
  pending?: boolean;
  activeTool?: string | null;
  /** The owning canvas resolves placement/attachment and dispatches one source transaction. */
  onChoose(tool: BpmnDocumentTool): void;
}

/** Standard syntax inventory; its availability does not grant execution admission or native binding versions. */
export function BpmnDocumentPalette({ readOnly, pending = false, activeTool, onChoose }: BpmnDocumentPaletteProps) {
  const [query, setQuery] = useState("");
  const matching = bpmnDocumentToolboxShapes.filter(entry => `${entry.label} ${entry.type} ${entry.group}`.toLowerCase().includes(query.trim().toLowerCase()));
  return <aside className="wf-bpmn-document-palette" aria-label="BPMN toolbox">
    <label>Find BPMN construct<input type="search" value={query} onChange={event => setQuery(event.target.value)} /></label>
    {[...new Set(matching.map(entry => entry.group))].map(group => <details key={group} open>
      <summary>{group}</summary><div className="wf-bpmn-document-tools">{matching.filter(entry => entry.group === group).map(entry =>
        <button key={entry.id} type="button" disabled={readOnly || pending} aria-pressed={activeTool === entry.id} onClick={() => onChoose({ kind: "shape", entry })}>
          <span className="wf-bpmn-document-tool-icon" aria-hidden="true"><BpmnNotation descriptor={bpmnToolboxNotation(entry)} /></span><span>{entry.label}</span>
        </button>)}</div>
    </details>)}
    {!matching.length && <p>No matching BPMN constructs.</p>}
    <details open><summary>Connections</summary><div className="wf-bpmn-document-tools">{bpmnDocumentToolboxConnections.filter(entry => entry.label.toLowerCase().includes(query.trim().toLowerCase())).map(entry =>
      <button key={entry.id} type="button" disabled={readOnly || pending} aria-pressed={activeTool === entry.id} onClick={() => onChoose({ kind: "connection", connectionKind: entry.id })}>{entry.label}</button>)}</div></details>
  </aside>;
}
