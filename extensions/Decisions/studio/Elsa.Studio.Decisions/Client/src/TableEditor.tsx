import React, { useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { StudioAlert, AnchoredPopover, OverflowMenu, StudioButton, StudioTableContainer, StudioField } from "@elsa-workflows/studio-ui";
import { editTable, parseTable, type Clause, type Table, type TableEdit } from "./decisionXml";
import type { Diagnostic } from "./decisionsApi";
import { decisionDiagnosticMessage } from "./decisionDiagnosticMessage";

interface EditorProps { xml: string; readOnly: boolean; onChange(xml: string): void; onError(message: string): void; diagnostics?: Diagnostic[] }
interface Position { row: number; column: number }
const same = (left: Position | null, right: Position) => left?.row === right.row && left.column === right.column;
const positionKey = ({ row, column }: Position) => `${row}:${column}`;

export function TableEditor(props: EditorProps) {
  const parsed = useMemo(() => {
    try { return { table: parseTable(props.xml) }; }
    catch (error) { return { error: error instanceof Error ? error.message : "This document cannot be edited as a table." }; }
  }, [props.xml]);
  return parsed.table ? <DecisionGrid {...props} table={parsed.table} /> : <StudioAlert tone="danger">{parsed.error}</StudioAlert>;
}

function DecisionGrid({ xml, readOnly, onChange, onError, diagnostics = [], table }: EditorProps & { table: Table }) {
  const [selected, setSelected] = useState<Position | null>(null);
  const [anchor, setAnchor] = useState<Position | null>(null);
  const [editing, setEditing] = useState<Position | null>(null);
  const [draft, setDraft] = useState("");
  const [showAnnotations, setShowAnnotations] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const [helpOpen, setHelpOpen] = useState(false);
  const [undoStack, setUndoStack] = useState<string[]>([]);
  const [redoStack, setRedoStack] = useState<string[]>([]);
  const cellRefs = useRef(new Map<string, HTMLTableCellElement>());
  const editorRef = useRef<HTMLInputElement>(null);
  const selectDraftOnStart = useRef(false);
  const previousXml = useRef(xml);
  const expectedXml = useRef<string | null>(null);
  const columnCount = table.inputs.length + table.outputs.length;

  useEffect(() => {
    if (previousXml.current === xml) return;
    if (expectedXml.current === xml) expectedXml.current = null;
    else {
      setUndoStack([]); setRedoStack([]); setSelected(null); setAnchor(null); setEditing(null);
    }
    previousXml.current = xml;
  }, [xml]);
  useLayoutEffect(() => {
    if (!editing) return;
    const editor = editorRef.current;
    editor?.focus();
    if (selectDraftOnStart.current) editor?.select();
    else if (editor) editor.setSelectionRange(editor.value.length, editor.value.length);
  }, [editing]);

  const change = (edit: TableEdit) => {
    if (readOnly) return;
    try {
      const next = editTable(xml, edit);
      if (next === xml) return;
      setUndoStack(current => [...current.slice(-49), xml]); setRedoStack([]); expectedXml.current = next; onChange(next);
    } catch (error) { onError(error instanceof Error ? error.message : "The table change could not be applied."); }
  };
  const focusCell = (position: Position) => cellRefs.current.get(positionKey(position))?.focus();
  const startEditing = (position: Position, initial?: string) => {
    if (readOnly) return;
    selectDraftOnStart.current = initial === undefined;
    setSelected(position); setAnchor(position); setDraft(initial ?? cellValue(position)); setEditing(position);
  };
  const cellValue = (position: Position) => position.column < table.inputs.length
    ? table.rules[position.row]?.inputs[position.column] ?? ""
    : table.rules[position.row]?.outputs[position.column - table.inputs.length] ?? "";
  const commit = (nextPosition?: Position, restoreFocus = true) => {
    if (!editing) return;
    const position = editing;
    const output = position.column >= table.inputs.length;
    change({ kind: "cell", ruleId: table.rules[position.row].id, output, column: output ? position.column - table.inputs.length : position.column, text: draft });
    setEditing(null);
    const next = nextPosition ?? position;
    setSelected(next); setAnchor(next);
    if (restoreFocus) requestAnimationFrame(() => focusCell(next));
  };
  const cancel = () => {
    if (!editing) return;
    const position = editing;
    setEditing(null); setDraft("");
    requestAnimationFrame(() => focusCell(position));
  };
  const selectCell = (position: Position, extend = false) => {
    if (editing) commit(position);
    setSelected(position);
    if (!extend || !anchor) setAnchor(position);
    focusCell(position);
  };
  const move = (position: Position, rowDelta: number, columnDelta: number, extend: boolean) => {
    const next = { row: Math.max(0, Math.min(table.rules.length - 1, position.row + rowDelta)), column: Math.max(0, Math.min(columnCount - 1, position.column + columnDelta)) };
    selectCell(next, extend);
  };
  const adjacentCell = (position: Position, backwards: boolean): Position | undefined => {
    const index = position.row * columnCount + position.column + (backwards ? -1 : 1);
    if (index < 0 || index >= table.rules.length * columnCount) return undefined;
    return { row: Math.floor(index / columnCount), column: index % columnCount };
  };
  const handleCellKeyDown = (position: Position, event: React.KeyboardEvent<HTMLTableCellElement>) => {
    if (event.target !== event.currentTarget) return;
    if (event.key === "ArrowUp" || event.key === "ArrowDown" || event.key === "ArrowLeft" || event.key === "ArrowRight") {
      event.preventDefault();
      move(position, event.key === "ArrowUp" ? -1 : event.key === "ArrowDown" ? 1 : 0, event.key === "ArrowLeft" ? -1 : event.key === "ArrowRight" ? 1 : 0, event.shiftKey);
    } else if (event.key === "Tab") {
      const next = adjacentCell(position, event.shiftKey);
      if (next) { event.preventDefault(); selectCell(next); }
    }
    else if (event.key === "Enter" || event.key === "F2") { event.preventDefault(); startEditing(position); }
    else if (event.key === "Delete" || event.key === "Backspace") { event.preventDefault(); startEditing(position, ""); }
    else if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) { event.preventDefault(); startEditing(position, event.key); }
  };
  const undo = () => {
    if (readOnly || !undoStack.length) return;
    const next = undoStack[undoStack.length - 1];
    setUndoStack(undoStack.slice(0, -1)); setRedoStack(current => [...current.slice(-49), xml]);
    expectedXml.current = next; onChange(next);
  };
  const redo = () => {
    if (readOnly || !redoStack.length) return;
    const next = redoStack[redoStack.length - 1];
    setRedoStack(redoStack.slice(0, -1)); setUndoStack(current => [...current.slice(-49), xml]);
    expectedXml.current = next; onChange(next);
  };
  const selectionBounds = () => {
    if (!selected) return null;
    const start = anchor ?? selected;
    return { top: Math.min(start.row, selected.row), bottom: Math.max(start.row, selected.row), left: Math.min(start.column, selected.column), right: Math.max(start.column, selected.column) };
  };
  const onCopy = (event: React.ClipboardEvent<HTMLTableElement>) => {
    if (editing || !selected || !event.clipboardData) return;
    const bounds = selectionBounds()!;
    const text = table.rules.slice(bounds.top, bounds.bottom + 1).map(rule => [
      ...rule.inputs.slice(bounds.left, Math.min(bounds.right + 1, table.inputs.length)),
      ...rule.outputs.slice(Math.max(0, bounds.left - table.inputs.length), Math.max(0, bounds.right + 1 - table.inputs.length)),
    ].join("\t")).join("\n");
    event.preventDefault(); event.clipboardData.setData("text/plain", text);
  };
  const onPaste = (event: React.ClipboardEvent<HTMLTableElement>) => {
    if (readOnly || editing || !selected) return;
    const source = event.clipboardData?.getData("text/plain");
    if (source == null) return;
    event.preventDefault();
    const rows = source.replace(/\r\n?/g, "\n").replace(/\n$/, "").split("\n").map(row => row.split("\t"));
    if (rows.some(row => row.length !== rows[0].length)) { onError("The pasted range is not rectangular."); return; }
    if (selected.row + rows.length > table.rules.length || rows.some(row => selected.column + row.length > columnCount)) {
      onError("The pasted range extends outside the decision table."); return;
    }
    const edits = rows.flatMap((values, rowOffset) => values.map((text, columnOffset) => {
      const column = selected.column + columnOffset, output = column >= table.inputs.length;
      return { ruleId: table.rules[selected.row + rowOffset].id, output, column: output ? column - table.inputs.length : column, text };
    }));
    if (edits.length) change({ kind: "cells", edits });
  };
  const selectedBounds = selectionBounds();
  const addColumn = (output: boolean) => change({ kind: "add-clause", output });
  const ruleLabel = table.rules.length === 1 ? "rule" : "rules";
  const inputLabel = table.inputs.length === 1 ? "input" : "inputs";
  const outputLabel = table.outputs.length === 1 ? "output" : "outputs";

  return <section aria-labelledby="decision-table-title" className={`decision-table-section${expanded ? " is-expanded" : ""}`}>
    <div className="decision-section-heading"><h2 id="decision-table-title">Rules</h2><StudioField className="decision-policy-field" label="Hit policy"><select value={table.hitPolicy} disabled={readOnly} onChange={event => change({ kind: "policy", value: event.target.value })}>
      {!["UNIQUE", "ANY", "FIRST", "RULE ORDER", "COLLECT"].includes(table.hitPolicy) ? <option value={table.hitPolicy}>{table.hitPolicy} (unsupported)</option> : null}
      <option value="UNIQUE">Unique — one match</option><option value="ANY">Any — equal results</option><option value="FIRST">First — first match</option><option value="RULE ORDER">Rule order — all matches</option><option value="COLLECT">Collect — all matches</option>
    </select></StudioField></div>
    <div className="decision-grid-toolbar" aria-label="Decision table tools">
      {!readOnly ? <div className="decision-grid-tool-group" role="group" aria-label="History"><StudioButton size="sm" disabled={!undoStack.length} onClick={undo} aria-label="Undo table edit">Undo</StudioButton><StudioButton size="sm" disabled={!redoStack.length} onClick={redo} aria-label="Redo table edit">Redo</StudioButton></div> : null}
      <div className="decision-grid-tool-group" role="group" aria-label="Structure">
        {!readOnly ? <StudioButton size="sm" onClick={() => change({ kind: "add-rule" })} disabled={table.rules.length >= 1000}>Add rule</StudioButton> : null}
        <OverflowMenu ariaLabel="More table actions" items={[
          ...(!readOnly ? [
            { id: "add-input", label: "Add input", disabled: table.inputs.length >= 32, onSelect: () => addColumn(false) },
            { id: "add-output", label: "Add output", disabled: table.outputs.length >= 16, onSelect: () => addColumn(true) }
          ] : []),
          { id: "annotations", label: showAnnotations ? "Hide annotations" : "Show annotations", onSelect: () => setShowAnnotations(value => !value) },
          { id: "expand", label: expanded ? "Collapse table" : "Expand table", onSelect: () => setExpanded(value => !value) },
          { id: "expression-help", label: helpOpen ? "Hide expression help" : "Expression help", onSelect: () => setHelpOpen(value => !value) }
        ]} />
      </div>
    </div>
    {helpOpen ? <div className="decision-expression-help" role="note" aria-label="Expression syntax help"><p id="decision-policy-help">Conditions accept literals, comparisons, ranges or alternatives. Use <code>-</code> for any non-null value. Text results need double quotes. The server validates the supported profile.</p><StudioButton size="sm" onClick={() => setHelpOpen(false)}>Hide expression help</StudioButton></div> : null}
    <StudioTableContainer className="decision-table-scroll" tabIndex={0} role="region" aria-label="Decision rules, scroll horizontally for more columns">
      <table className="decision-table decision-grid" role="grid" aria-label={`${table.name || "Decision"} rules`} aria-readonly={readOnly} aria-rowcount={table.rules.length + 2} aria-colcount={columnCount + 1 + (showAnnotations ? 1 : 0) + (readOnly ? 0 : 1)} onCopy={onCopy} onPaste={onPaste}>
        <caption className="decision-visually-hidden">{table.name || "Decision"} rules</caption>
        <colgroup>
          <col className="decision-grid-number-column" />
          {table.inputs.map(item => <col className="decision-grid-data-column" key={item.id} />)}
          {table.outputs.map(item => <col className="decision-grid-data-column" key={item.id} />)}
          {showAnnotations ? <col className="decision-grid-annotation-column" /> : null}
          {!readOnly ? <col className="decision-grid-actions-column" /> : null}
        </colgroup>
        <thead>
          <tr><th scope="col" rowSpan={2} className="decision-grid-row-number">#</th><th scope="colgroup" colSpan={table.inputs.length}>Inputs</th><th scope="colgroup" colSpan={table.outputs.length}>Outputs</th>{showAnnotations ? <th scope="col" rowSpan={2}>Annotation</th> : null}{!readOnly ? <th scope="col" rowSpan={2} className="decision-grid-actions-heading"><span className="decision-visually-hidden">Actions</span></th> : null}</tr>
          <tr>{table.inputs.map((item, index) => <ColumnHeader key={item.id} item={item} index={index} output={false} readOnly={readOnly} count={table.inputs.length} onChange={change} />)}{table.outputs.map((item, index) => <ColumnHeader key={item.id} item={item} index={index} output readOnly={readOnly} count={table.outputs.length} onChange={change} />)}</tr>
        </thead>
        <tbody>{table.rules.map((rule, row) => <tr key={rule.id} aria-rowindex={row + 3}>
          <th scope="row" id={`rule-${rule.id}`} className="decision-grid-row-number">{row + 1}</th>
          {[...rule.inputs, ...rule.outputs].map((text, column) => {
            const output = column >= table.inputs.length, clause = output ? table.outputs[column - table.inputs.length] : table.inputs[column];
            const position = { row, column }, active = same(selected, position) || !selected && row === 0 && column === 0;
            const inRange = !!selectedBounds && row >= selectedBounds.top && row <= selectedBounds.bottom && column >= selectedBounds.left && column <= selectedBounds.right;
            const isEditing = same(editing, position);
            const diagnostic = diagnostics.find(item => item.ruleId === rule.id && item.clauseId === clause.id);
            const diagnosticId = diagnostic ? `diagnostic-${rule.id}-${clause.id}` : undefined;
            return <td key={clause.id} id={`cell-${rule.id}-${clause.id}`} role="gridcell" aria-label={`Rule ${row + 1}, ${output ? "output" : "input"} ${clause.name || (output ? column - table.inputs.length : column) + 1}`} aria-selected={inRange || active} aria-describedby={diagnosticId} tabIndex={active && !isEditing ? 0 : -1} className={`decision-grid-cell${active ? " is-active" : ""}${inRange ? " is-selected" : ""}${isEditing ? " is-editing" : ""}`} ref={element => { if (element) cellRefs.current.set(positionKey(position), element); else cellRefs.current.delete(positionKey(position)); }} onFocus={() => { if (!selected) { setSelected(position); setAnchor(position); } }} onClick={() => selectCell(position)} onDoubleClick={() => startEditing(position)} onKeyDown={event => handleCellKeyDown(position, event)}>
              {isEditing ? <input ref={editorRef} aria-label="Edit selected cell" value={draft} onChange={event => setDraft(event.target.value)} onKeyDown={event => {
                if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); cancel(); }
                else if (event.key === "Enter") { event.preventDefault(); event.stopPropagation(); commit(); }
                else if (event.key === "Tab") {
                  const next = adjacentCell(position, event.shiftKey);
                  if (next) { event.preventDefault(); event.stopPropagation(); commit(next); }
                }
              }} onBlur={() => commit(undefined, false)} /> : <span>{text}</span>}
              {diagnostic ? <span id={diagnosticId} className="decision-cell-diagnostic" role="note">{decisionDiagnosticMessage(diagnostic, table)}</span> : null}
            </td>;
          })}
          {showAnnotations ? <td><input aria-label={`Rule ${row + 1} annotation`} value={rule.annotation} disabled={readOnly} onChange={event => change({ kind: "annotation", ruleId: rule.id, text: event.target.value })} /></td> : null}
          {!readOnly ? <td className="decision-grid-actions"><OverflowMenu ariaLabel={`Actions for rule ${row + 1}`} items={[
            { id: "duplicate", label: "Duplicate below", disabled: table.rules.length >= 1000, onSelect: () => change({ kind: "duplicate-rule", ruleId: rule.id }) },
            { id: "insert-above", label: "Insert above", disabled: table.rules.length >= 1000, onSelect: () => change({ kind: "insert-rule", ruleId: rule.id, direction: -1 }) },
            { id: "insert-below", label: "Insert below", disabled: table.rules.length >= 1000, onSelect: () => change({ kind: "insert-rule", ruleId: rule.id, direction: 1 }) },
            { id: "move-up", label: "Move up", disabled: row === 0, onSelect: () => change({ kind: "move-rule", ruleId: rule.id, direction: -1 }) },
            { id: "move-down", label: "Move down", disabled: row === table.rules.length - 1, onSelect: () => change({ kind: "move-rule", ruleId: rule.id, direction: 1 }) },
            { id: "delete", label: "Delete rule", disabled: table.rules.length <= 1, onSelect: () => change({ kind: "delete-rule", ruleId: rule.id }) }
          ]} /></td> : null}
        </tr>)}</tbody>
      </table>
    </StudioTableContainer>
    <div className="decision-grid-footer"><span>{table.rules.length} {ruleLabel} · {table.inputs.length} {inputLabel} · {table.outputs.length} {outputLabel}</span></div>
  </section>;
}

function ColumnHeader({ item, index, output, readOnly, count, onChange }: { item: Clause; index: number; output: boolean; readOnly: boolean; count: number; onChange(edit: TableEdit): void }) {
  const popoverId = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const nameInputRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState(item.name);
  const [type, setType] = useState(item.type);
  const [open, setOpen] = useState(false);
  useEffect(() => { setName(item.name); setType(item.type); }, [item.name, item.type]);
  const group = output ? "Output" : "Input";
  const close = (restoreFocus: boolean) => {
    setOpen(false);
    if (restoreFocus) requestAnimationFrame(() => triggerRef.current?.focus());
  };
  return <th scope="col" id={`clause-${item.id}`} className="decision-grid-column">
    <div className="decision-column-header"><span className="decision-column-summary"><span>{item.name || `${group} ${index + 1}`}</span><small>{item.type || "Unspecified"}</small></span>
    {!readOnly ? <>
      <button ref={triggerRef} type="button" className="decision-column-settings" aria-label={`Settings for ${group.toLowerCase()} ${index + 1}`} aria-haspopup="dialog" aria-expanded={open} aria-controls={open ? popoverId : undefined} onClick={() => { setName(item.name); setType(item.type); setOpen(value => !value); }}>⋯</button>
      <AnchoredPopover id={popoverId} anchorRef={triggerRef} open={open} className="decision-column-popover" minWidth={220} onDismiss={() => close(false)} onPositioned={() => nameInputRef.current?.focus()}
        role="dialog" aria-label={`${group} ${index + 1} settings`} onKeyDown={event => {
          if (event.key === "Escape") { event.preventDefault(); close(true); }
        }}>
          <label>{group} {index + 1} name<input ref={nameInputRef} aria-label={`${group} ${index + 1} name`} value={name} onChange={event => setName(event.target.value)} /></label>
          <label>Type<select aria-label={`${group} ${index + 1} type`} value={type} onChange={event => setType(event.target.value)}>
            {!(["number", "string", "boolean"].includes(item.type)) ? <option value={item.type}>{item.type || "Unspecified"} (unsupported)</option> : null}
            <option value="number">Number</option><option value="string">Text</option><option value="boolean">Boolean</option>
          </select></label>
          <StudioButton size="sm" onClick={() => { onChange({ kind: "clause", id: item.id, output, name, type }); close(true); }}>Save column</StudioButton>
          <StudioButton size="sm" variant="ghost" disabled={count <= 1} onClick={() => { onChange({ kind: "delete-clause", id: item.id, output }); close(false); }}>Remove {group.toLowerCase()}</StudioButton>
      </AnchoredPopover>
    </> : null}
    </div>
  </th>;
}
