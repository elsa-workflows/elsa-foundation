import React, { useEffect, useMemo, useRef, useState } from "react";
import { DndContext, KeyboardSensor, PointerSensor, closestCenter, useSensor, useSensors, type DragEndEvent } from "@dnd-kit/core";
import { SortableContext, arrayMove, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { StudioButton, StudioField } from "@elsa-workflows/studio-ui";
import { Copy, GripVertical, MoveDown, MoveUp, Plus, Trash2 } from "lucide-react";
import { BasicTaskForm } from "./TaskForm";
import { createFieldRowId, createTaskField, duplicateTaskField, taskFieldKinds, validateTaskFields, type TaskFieldKind } from "./formDesignerModel";
import type { TaskField } from "./taskApi";
import "./formDesigner.css";

interface FieldRow {
  id: string;
  field: TaskField;
}

interface FormDesignerProps {
  fields: TaskField[];
  onFieldsChange: (fields: TaskField[]) => void;
  disabled?: boolean;
}

function SortableFieldCard({ row, index, count, selected, disabled, onSelect, onMove, onDuplicate, onRemove }: {
  row: FieldRow;
  index: number;
  count: number;
  selected: boolean;
  disabled: boolean;
  onSelect: () => void;
  onMove: (offset: number) => void;
  onDuplicate: () => void;
  onRemove: () => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: row.id, disabled });
  const label = row.field.label.trim() || `Field ${index + 1}`;
  return <article ref={setNodeRef} className={`user-task-designer__field${selected ? " is-selected" : ""}${isDragging ? " is-dragging" : ""}`} style={{ transform: CSS.Transform.toString(transform), transition }}>
    <div className="user-task-designer__field-heading">
      <button type="button" className="user-task-designer__drag-handle" aria-label={`Reorder ${label}`} disabled={disabled} {...attributes} {...listeners}><GripVertical aria-hidden="true" /></button>
      <button type="button" className="user-task-designer__select-field" aria-label={`Select ${label}`} aria-pressed={selected} onClick={onSelect} disabled={disabled}>
        <span>{label}</span><small>{row.field.kind}</small>
      </button>
      <div className="user-task-designer__field-actions" aria-label={`${label} actions`}>
        <StudioButton variant="ghost" title="Move up" aria-label={`Move ${label} up`} disabled={disabled || index === 0} onClick={() => onMove(-1)}><MoveUp aria-hidden="true" /></StudioButton>
        <StudioButton variant="ghost" title="Move down" aria-label={`Move ${label} down`} disabled={disabled || index === count - 1} onClick={() => onMove(1)}><MoveDown aria-hidden="true" /></StudioButton>
        <StudioButton variant="ghost" title="Duplicate field" aria-label={`Duplicate ${label}`} disabled={disabled || count >= 100} onClick={onDuplicate}><Copy aria-hidden="true" /></StudioButton>
        <StudioButton variant="ghost" title="Remove field" aria-label={`Remove ${label}`} disabled={disabled} onClick={onRemove}><Trash2 aria-hidden="true" /></StudioButton>
      </div>
    </div>
    <div className="user-task-designer__field-preview" aria-label={`${label} recipient preview`}>
      <BasicTaskForm form={{ fields: [row.field] }} value={{}} onChange={() => {}} readOnly errors={{}} />
    </div>
  </article>;
}

export function FormDesigner({ fields, onFieldsChange, disabled = false }: FormDesignerProps) {
  const [rows, setRows] = useState<FieldRow[]>(() => fields.map(field => ({ id: createFieldRowId(), field: { ...field, choices: field.choices ? [...field.choices] : undefined } })));
  const [selectedId, setSelectedId] = useState<string | null>(() => rows[0]?.id ?? null);
  const [previewMode, setPreviewMode] = useState(false);
  const [previewValues, setPreviewValues] = useState<Record<string, unknown>>({});
  const lastDragOverId = useRef<string | null>(null);
  const keyboardDrag = useRef(false);
  const keyboardOffset = useRef(0);
  const rowFields = useMemo(() => rows.map(row => row.field), [rows]);
  const fieldErrors = useMemo(() => validateTaskFields(rowFields), [rowFields]);
  const selectedIndex = rows.findIndex(row => row.id === selectedId);
  const selectedRow = selectedIndex < 0 ? undefined : rows[selectedIndex];
  const selectedKind = selectedRow?.field.kind;
  const hasAdvancedOptions = selectedKind === "choice" || selectedKind === "number" || selectedKind === "text" || selectedKind === "multiline";
  const previewSchema = JSON.stringify(rowFields);
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates })
  );

  useEffect(() => setPreviewValues({}), [previewSchema]);

  const publishFields = (next: FieldRow[]) => {
    setRows(next);
    onFieldsChange(next.map(row => ({ ...row.field, choices: row.field.choices ? [...row.field.choices] : undefined })));
  };
  const updateField = (id: string, update: Partial<TaskField>) => publishFields(rows.map(row => row.id === id ? { ...row, field: { ...row.field, ...update } } : row));
  const addField = (kind: TaskFieldKind) => {
    if (rows.length >= 100) return;
    const row = { id: createFieldRowId(), field: createTaskField(kind, rowFields) };
    publishFields([...rows, row]);
    setSelectedId(row.id);
  };
  const moveField = (id: string, offset: number) => {
    const index = rows.findIndex(row => row.id === id);
    const destination = index + offset;
    if (index < 0 || destination < 0 || destination >= rows.length) return;
    publishFields(arrayMove(rows, index, destination));
  };
  const duplicateField = (id: string) => {
    if (rows.length >= 100) return;
    const index = rows.findIndex(row => row.id === id);
    if (index < 0) return;
    const row = { id: createFieldRowId(), field: duplicateTaskField(rows[index].field, rowFields) };
    publishFields([...rows.slice(0, index + 1), row, ...rows.slice(index + 1)]);
    setSelectedId(row.id);
  };
  const removeField = (id: string) => {
    const index = rows.findIndex(row => row.id === id);
    if (index < 0) return;
    const next = rows.filter(row => row.id !== id);
    publishFields(next);
    if (selectedId === id) setSelectedId(next[Math.min(index, next.length - 1)]?.id ?? null);
  };
  const onDragEnd = ({ active, over }: DragEndEvent) => {
    // Keep the last neighboring row for keyboard drops when the final collision reports the active row.
    const overId = over?.id === active.id && keyboardDrag.current ? lastDragOverId.current : over?.id;
    const from = rows.findIndex(row => row.id === active.id);
    let to = overId == null ? -1 : rows.findIndex(row => row.id === overId);
    if ((to < 0 || to === from) && keyboardDrag.current && keyboardOffset.current) {
      to = Math.max(0, Math.min(rows.length - 1, from + keyboardOffset.current));
    }
    lastDragOverId.current = null;
    keyboardDrag.current = false;
    keyboardOffset.current = 0;
    if (from >= 0 && to >= 0 && to < rows.length && from !== to) publishFields(arrayMove(rows, from, to));
  };
  const resetDrag = () => {
    lastDragOverId.current = null;
    keyboardDrag.current = false;
    keyboardOffset.current = 0;
  };

  return <section className="user-task-designer" aria-label="Form designer">
    <header className="user-task-designer__header">
      <div><h2>Design the form</h2><p>Add fields, arrange them, then review the recipient experience.</p></div>
      <StudioButton variant={previewMode ? "primary" : "ghost"} aria-pressed={previewMode} onClick={() => setPreviewMode(value => !value)}>
        {previewMode ? "Edit fields" : "Preview form"}
      </StudioButton>
    </header>
    {previewMode ? <section className="user-task-designer__recipient-preview" aria-label="Recipient preview">
      {rows.length ? <BasicTaskForm form={{ fields: rowFields }} value={previewValues} onChange={setPreviewValues} readOnly={false} errors={{}} /> : <p>Add a field to preview the form recipients will see.</p>}
    </section> : <div className="user-task-designer__layout">
      <aside className="user-task-designer__palette" aria-label="Field palette">
        <h3>Field palette</h3><p>Choose a field to add it to the form.</p>
        <div className="user-task-designer__palette-items">{taskFieldKinds.map(item => <StudioButton key={item.kind} variant="ghost" disabled={disabled || rows.length >= 100} onClick={() => addField(item.kind)}>
          <Plus aria-hidden="true" />{item.label}
        </StudioButton>)}</div>
        <small>{rows.length} {rows.length === 1 ? "field" : "fields"} in form · {rows.length >= 100 ? "limit reached" : "maximum 100"}</small>
      </aside>
      <section className="user-task-designer__canvas" aria-label="Form canvas">
        <div className="user-task-designer__section-heading"><h3>Form canvas</h3><span>{rows.length} {rows.length === 1 ? "field" : "fields"}</span></div>
        {rows.length ? <DndContext sensors={sensors} collisionDetection={closestCenter}
          onDragStart={({ activatorEvent }) => { lastDragOverId.current = null; keyboardDrag.current = activatorEvent.type === "keydown"; keyboardOffset.current = 0; }}
          onDragOver={({ active, over }) => { lastDragOverId.current = over && over.id !== active.id ? String(over.id) : null; }}
          onDragEnd={onDragEnd} onDragCancel={resetDrag}>
          <SortableContext items={rows.map(row => row.id)} strategy={verticalListSortingStrategy}>
            <div className="user-task-designer__field-list" aria-label="Form fields" onKeyDownCapture={event => {
              if (!keyboardDrag.current) return;
              if (event.key === "ArrowUp") keyboardOffset.current--;
              if (event.key === "ArrowDown") keyboardOffset.current++;
            }}>
              {rows.map((row, index) => <SortableFieldCard key={row.id} row={row} index={index} count={rows.length} selected={row.id === selectedId} disabled={disabled}
                onSelect={() => setSelectedId(row.id)} onMove={offset => moveField(row.id, offset)} onDuplicate={() => duplicateField(row.id)} onRemove={() => removeField(row.id)} />)}
            </div>
          </SortableContext>
        </DndContext> : <div className="user-task-designer__empty"><p>Your form is empty.</p><p>Add a field from the palette to start building.</p></div>}
        {fieldErrors.some(errors => errors.length) ? <ul className="user-task-designer__validation-summary" aria-label="Field validation errors">{fieldErrors.flatMap((errors, index) => errors.map((error, errorIndex) => <li key={`${index}-${errorIndex}`}>Field {index + 1}: {error}</li>))}</ul> : null}
      </section>
      <aside className="user-task-designer__properties" aria-label="Field properties">
        <h3>Field properties</h3>
        {selectedRow ? <>
          <StudioField label="Label"><input value={selectedRow.field.label} aria-invalid={fieldErrors[selectedIndex].some(error => error.includes("label"))} onChange={event => updateField(selectedRow.id, { label: event.target.value })} /></StudioField>
          <StudioField label="Key" description="Used as the response property name."><input value={selectedRow.field.key} aria-invalid={fieldErrors[selectedIndex].some(error => error.includes("key"))} onChange={event => updateField(selectedRow.id, { key: event.target.value })} /></StudioField>
          <StudioField label="Type"><select value={selectedRow.field.kind} onChange={event => {
            const kind = event.target.value as TaskFieldKind;
            updateField(selectedRow.id, { kind, minimum: undefined, maximum: undefined, maxLength: undefined, choices: kind === "choice" ? ["Option 1"] : undefined });
          }}>
            {taskFieldKinds.map(item => <option key={item.kind} value={item.kind}>{item.label}</option>)}
          </select></StudioField>
          <label className="user-task-designer__checkbox"><input type="checkbox" checked={selectedRow.field.required} onChange={event => updateField(selectedRow.id, { required: event.target.checked })} />Required</label>
          <label className="user-task-designer__checkbox"><input type="checkbox" checked={selectedRow.field.readOnly} onChange={event => updateField(selectedRow.id, { readOnly: event.target.checked })} />Read only</label>
          <StudioField label="Help text"><textarea rows={2} value={selectedRow.field.help ?? ""} onChange={event => updateField(selectedRow.id, { help: event.target.value || undefined })} /></StudioField>
          {hasAdvancedOptions ? <details className="user-task-designer__advanced">
            <summary>{selectedKind === "choice" ? "Choices" : "Validation"}</summary>
            {selectedKind === "choice" ? <StudioField label="Choices, one per line"><textarea rows={4} value={selectedRow.field.choices?.join("\n") ?? ""} aria-invalid={fieldErrors[selectedIndex].some(error => error.includes("choice"))} onChange={event => updateField(selectedRow.id, { choices: event.target.value.split("\n") })} /></StudioField> : null}
            {selectedKind === "number" ? <>
              <StudioField label="Minimum"><input type="number" value={selectedRow.field.minimum ?? ""} onChange={event => updateField(selectedRow.id, { minimum: event.target.value === "" ? undefined : Number(event.target.value) })} /></StudioField>
              <StudioField label="Maximum"><input type="number" value={selectedRow.field.maximum ?? ""} onChange={event => updateField(selectedRow.id, { maximum: event.target.value === "" ? undefined : Number(event.target.value) })} /></StudioField>
            </> : null}
            {selectedKind === "text" || selectedKind === "multiline" ? <StudioField label="Maximum characters"><input type="number" min="1" step="1" value={selectedRow.field.maxLength ?? ""} onChange={event => updateField(selectedRow.id, { maxLength: event.target.value === "" ? undefined : Number(event.target.value) })} /></StudioField> : null}
          </details> : null}
          {fieldErrors[selectedIndex].length ? <ul className="user-task-designer__property-errors" aria-label="Selected field errors">{fieldErrors[selectedIndex].map(error => <li key={error}>{error}</li>)}</ul> : null}
        </> : <p>Select a field in the canvas to edit its properties.</p>}
      </aside>
    </div>}
  </section>;
}