import React, { useEffect, useId, useMemo, useRef, useState } from "react";
import { DndContext, KeyboardSensor, PointerSensor, closestCenter, useSensor, useSensors, type DragEndEvent } from "@dnd-kit/core";
import { SortableContext, arrayMove, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { AdvancedSection, StudioButton, StudioField, StudioSidePanelActions, StudioSidePanelResizeHandle, StudioTabPanel, StudioTabs, useStudioSidePanelLayout } from "@elsa-workflows/studio-ui";
import { Copy, GripVertical, MoveDown, MoveUp, Plus, Trash2 } from "lucide-react";
import { ContributedTaskForm } from "./TaskForm";
import { createFieldRowId, duplicateTaskField, validateTaskFields } from "./formDesignerModel";
import { builtInTaskFieldControls, listAvailableTaskFieldControls, resolveTaskFieldControl, type TaskFieldControlSource } from "./fieldControls";
import { basicProvider, type TaskField, type TaskForm } from "./taskApi";
import { FormSourceEditor } from "./FormSourceEditor";
import "./formDesigner.css";

const formPanelStorageKeys = {
  paletteWidth: "elsa-studio-task-form-palette-width",
  inspectorWidth: "elsa-studio-task-form-inspector-width",
  paletteCollapsed: "elsa-studio-task-form-palette-collapsed",
  inspectorCollapsed: "elsa-studio-task-form-inspector-collapsed",
  maximized: "elsa-studio-task-form-side-panel-maximized"
};

interface FieldRow {
  id: string;
  field: TaskField;
}

interface FormDesignerProps {
  fields: TaskField[];
  onFieldsChange: (fields: TaskField[]) => void;
  form?: Pick<TaskForm, "id" | "version" | "name"> & Partial<Pick<TaskForm, "provider">>;
  onFormChange?: (update: Partial<Pick<TaskForm, "id" | "name">>) => void;
  description?: React.ReactNode;
  versionLabel?: string;
  formIdReadOnly?: boolean;
  toolbarActions?: React.ReactNode;
  compactHeader?: boolean;
  versionsPanel?: React.ReactNode;
  onVersionsSelect?: () => boolean | void;
  disabled?: boolean;
  onUnappliedSourceChange?: (unapplied: boolean) => void;
  fieldControls?: TaskFieldControlSource;
}

function SortableFieldCard({ row, index, count, selected, disabled, fieldControls, onSelect, onMove, onDuplicate, onRemove }: {
  row: FieldRow;
  index: number;
  count: number;
  selected: boolean;
  disabled: boolean;
  fieldControls: TaskFieldControlSource;
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
        <span>{label}</span><small>{resolveTaskFieldControl(row.field, fieldControls).control?.label ?? row.field.controlId ?? row.field.kind}</small>
      </button>
      <div className="user-task-designer__field-actions" aria-label={`${label} actions`}>
        <StudioButton variant="ghost" title="Move up" aria-label={`Move ${label} up`} disabled={disabled || index === 0} onClick={() => onMove(-1)}><MoveUp aria-hidden="true" /></StudioButton>
        <StudioButton variant="ghost" title="Move down" aria-label={`Move ${label} down`} disabled={disabled || index === count - 1} onClick={() => onMove(1)}><MoveDown aria-hidden="true" /></StudioButton>
        <StudioButton variant="ghost" title="Duplicate field" aria-label={`Duplicate ${label}`} disabled={disabled || count >= 100} onClick={onDuplicate}><Copy aria-hidden="true" /></StudioButton>
        <StudioButton variant="ghost" title="Remove field" aria-label={`Remove ${label}`} disabled={disabled} onClick={onRemove}><Trash2 aria-hidden="true" /></StudioButton>
      </div>
    </div>
    <div className="user-task-designer__field-preview" aria-label={`${label} recipient preview`}>
      <ContributedTaskForm form={{ fields: [row.field] }} value={{}} onChange={() => {}} readOnly errors={{}} controls={fieldControls} />
    </div>
  </article>;
}

export function FormDesigner({ fields, onFieldsChange, form, onFormChange, description, versionLabel, formIdReadOnly = false, toolbarActions, compactHeader = false, versionsPanel, onVersionsSelect, disabled = false, onUnappliedSourceChange, fieldControls = builtInTaskFieldControls }: FormDesignerProps) {
  const [rows, setRows] = useState<FieldRow[]>(() => fields.map(field => ({ id: createFieldRowId(), field: structuredClone(field) })));
  const [selectedId, setSelectedId] = useState<string | null>(() => rows[0]?.id ?? null);
  const [showFormSettings, setShowFormSettings] = useState(Boolean(form));
  const [activeView, setActiveView] = useState("canvas");
  const [visitedViews, setVisitedViews] = useState<Record<string, boolean>>({ canvas: true });
  const [unappliedSource, setUnappliedSource] = useState(false);
  const [previewValues, setPreviewValues] = useState<Record<string, unknown>>({});
  const controlItems = useMemo(() => listAvailableTaskFieldControls(fieldControls), [fieldControls]);
  const lastDragOverId = useRef<string | null>(null);
  const keyboardDrag = useRef(false);
  const keyboardOffset = useRef(0);
  const rowFields = useMemo(() => rows.map(row => row.field), [rows]);
  const fieldErrors = useMemo(() => validateTaskFields(rowFields, fieldControls), [rowFields, fieldControls]);
  const selectedIndex = rows.findIndex(row => row.id === selectedId);
  const selectedRow = selectedIndex < 0 ? undefined : rows[selectedIndex];
  const selectedControl = selectedRow ? resolveTaskFieldControl(selectedRow.field, fieldControls) : undefined;
  const tabBaseId = useId();
  const sourceForm: TaskForm = {
    id: form?.id ?? "",
    version: form?.version ?? "1",
    name: form?.name ?? "",
    provider: form?.provider ?? basicProvider,
    fields: rowFields
  };
  const layout = useStudioSidePanelLayout(formPanelStorageKeys);
  const previewSchema = JSON.stringify(rowFields);
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates })
  );

  useEffect(() => setPreviewValues({}), [previewSchema]);

  const publishFields = (next: FieldRow[]) => {
    setRows(next);
    onFieldsChange(next.map(row => structuredClone(row.field)));
  };
  const applySourceForm = (nextForm: TaskForm) => {
    const rowsByKey = new Map(rows.map(row => [row.field.key, row]));
    const nextRows = nextForm.fields.map((field, index) => {
      const existing = rowsByKey.get(field.key) ?? rows[index];
      return { id: existing?.id ?? createFieldRowId(), field: structuredClone(field) };
    });
    publishFields(nextRows);
    setSelectedId(current => nextRows.some(row => row.id === current) ? current : nextRows[0]?.id ?? null);
    if (form && onFormChange && nextForm.name !== form.name) onFormChange({ name: nextForm.name });
  };
  const updateUnappliedSource = (unapplied: boolean) => {
    setUnappliedSource(unapplied);
    onUnappliedSourceChange?.(unapplied);
  };
  const selectView = (view: string) => {
    if (view === "versions" && onVersionsSelect?.() === false) {
      requestAnimationFrame(() => document.getElementById(`${tabBaseId}-tab-${activeView === "canvas" ? 0 : activeView === "source" ? 1 : activeView === "preview" ? 2 : 3}`)?.focus());
      return;
    }
    setActiveView(view);
    setVisitedViews(current => ({ ...current, [view]: true }));
  };
  const updateField = (id: string, update: Partial<TaskField>) => publishFields(rows.map(row => row.id === id ? { ...row, field: { ...row.field, ...update } } : row));
  const replaceField = (id: string, field: TaskField) => publishFields(rows.map(row => row.id === id ? { ...row, field } : row));
  const updateSelectedControl = (value: unknown) => {
    if (typeof value !== "object" || value === null || !selectedRow) return;
    const candidate = value as Partial<TaskField>;
    const update: Partial<TaskField> = {};
    for (const key of ["minimum", "maximum", "maxLength", "choices", "presentation", "mustBeTrue", "configuration"] as const) {
      if (Object.hasOwn(candidate, key)) Object.assign(update, { [key]: candidate[key] });
    }
    updateField(selectedRow.id, update);
  };
  const addField = (controlId: string) => {
    if (rows.length >= 100) return;
    const control = controlItems.find(item => item.id === controlId);
    if (!control) return;
    const created = control.createField(rowFields) as TaskField;
    const field = control.legacyKind
      ? { ...created, kind: control.legacyKind, controlId: undefined }
      : { ...created, kind: control.valueKind, controlId: control.id };
    const row = { id: createFieldRowId(), field };
    publishFields([...rows, row]);
    setSelectedId(row.id);
    setShowFormSettings(false);
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
    setShowFormSettings(false);
  };
  const removeField = (id: string) => {
    const index = rows.findIndex(row => row.id === id);
    if (index < 0) return;
    const next = rows.filter(row => row.id !== id);
    publishFields(next);
    if (selectedId === id) {
      const nextSelectedId = next[Math.min(index, next.length - 1)]?.id ?? null;
      setSelectedId(nextSelectedId);
      if (!nextSelectedId && form) setShowFormSettings(true);
    }
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

  const editorBodyClassName = [
    "user-task-designer__layout",
    layout.paletteCollapsed ? "palette-collapsed" : "",
    layout.inspectorCollapsed ? "inspector-collapsed" : "",
    layout.maximizedSidePanel === "palette" ? "palette-maximized" : "",
    layout.maximizedSidePanel === "inspector" ? "inspector-maximized" : ""
  ].filter(Boolean).join(" ");
  const editorBodyStyle = {
    "--studio-palette-width": `${layout.paletteWidth}px`,
    "--studio-inspector-width": `${layout.inspectorWidth}px`
  } as React.CSSProperties;
  const canvasDisabled = disabled || unappliedSource;

  return <section className="user-task-designer" aria-label="Form designer" data-field-order={rowFields.map(field => field.key).join(",")}>
    <header className="user-task-designer__header">
      <div className="user-task-designer__heading">
        {compactHeader && form ? <small>{versionLabel ?? `Version ${form.version}`}</small> : form ? <><span>Forms / {form.name || "Untitled form"}</span><h2>{form.name || "Untitled form"}</h2><small>{versionLabel ?? `Version ${form.version}`}</small><p>{description ?? "Changes apply only to new tasks after this version is published."}</p></> : <><h2>Design the form</h2><p>Add fields, arrange them, then review the recipient experience.</p></>}
      </div>
      <div className="user-task-designer__toolbar-actions">
        {toolbarActions}
      </div>
    </header>
    <div className="user-task-designer__views">
      <StudioTabs tabs={[{ id: "canvas", label: "Designer" }, { id: "source", label: "Source" }, { id: "preview", label: "Preview" }, ...(versionsPanel !== undefined ? [{ id: "versions", label: "Versions" }] : [])]} activeTab={activeView} onSelect={selectView} ariaLabel="Form workspace" baseId={tabBaseId} className="user-task-designer__view-tabs" />
      <div ref={layout.containerRef} className="user-task-designer__view-content">
        <StudioTabPanel baseId={tabBaseId} index={0} hidden={activeView !== "canvas"} className="user-task-designer__view-panel user-task-designer__canvas-panel">
          <div className={editorBodyClassName} style={editorBodyStyle}>
            <aside className="user-task-designer__palette" aria-label="Field palette" data-studio-side-panel="palette">
              <div className="user-task-designer__panel-heading"><h3>Controls</h3><StudioSidePanelActions side="palette" panelLabel="controls" collapsed={layout.paletteCollapsed} maximized={layout.maximizedSidePanel === "palette"}
                onToggleCollapsed={() => layout.toggleSidePanelCollapsed("palette")} onToggleMaximized={() => layout.toggleSidePanelMaximized("palette")} /></div>
              <div className="user-task-designer__panel-content">
                <p>Choose a control to add it to the form.</p>
                <div className="user-task-designer__palette-items">{controlItems.map(item => <StudioButton key={item.id} variant="ghost" disabled={canvasDisabled || rows.length >= 100} onClick={() => addField(item.id)}>
                  {item.icon ?? <Plus aria-hidden="true" />}{item.label}
                </StudioButton>)}</div>
                <small>{rows.length} {rows.length === 1 ? "field" : "fields"} in form · {rows.length >= 100 ? "limit reached" : "maximum 100"}</small>
              </div>
            </aside>
            <StudioSidePanelResizeHandle side="palette" visible={layout.paletteExpanded && !layout.maximizedSidePanel} value={layout.paletteWidth} min={layout.paletteMinimumWidth} max={460} panelLabel="controls" className="user-task-designer__resize-handle"
              onPointerDown={event => layout.startSidePanelResize("palette", event)} onKeyDown={event => layout.handleSidePanelResizeKeyDown("palette", event)} />
            <section className="user-task-designer__canvas" aria-label="Form canvas">
              <div className="user-task-designer__section-heading"><h3>Form canvas</h3><span>{rows.length} {rows.length === 1 ? "field" : "fields"}</span></div>
              <div className="user-task-designer__canvas-scroll">
                {rows.length ? <DndContext sensors={sensors} collisionDetection={closestCenter}
                  onDragStart={({ activatorEvent }) => { lastDragOverId.current = null; keyboardDrag.current = activatorEvent.type === "keydown"; keyboardOffset.current = 0; }}
                  onDragOver={({ active, over }) => { lastDragOverId.current = over && over.id !== active.id ? String(over.id) : null; }}
                  onDragEnd={onDragEnd} onDragCancel={resetDrag}>
                  <SortableContext items={rows.map(row => row.id)} strategy={verticalListSortingStrategy}>
                    <div className="user-task-designer__form-surface">
                      <div className="user-task-designer__field-list" aria-label="Form fields" onKeyDownCapture={event => {
                        if (!keyboardDrag.current) return;
                        if (event.key === "ArrowUp") keyboardOffset.current--;
                        if (event.key === "ArrowDown") keyboardOffset.current++;
                      }}>
                        {rows.map((row, index) => <SortableFieldCard key={row.id} row={row} index={index} count={rows.length} selected={row.id === selectedId} disabled={canvasDisabled} fieldControls={fieldControls}
                          onSelect={() => { setSelectedId(row.id); setShowFormSettings(false); }} onMove={offset => moveField(row.id, offset)} onDuplicate={() => duplicateField(row.id)} onRemove={() => removeField(row.id)} />)}
                      </div>
                    </div>
                  </SortableContext>
                </DndContext> : <div className="user-task-designer__form-surface user-task-designer__empty"><p>Your form is empty.</p><p>Add a control from the palette to start building.</p></div>}
                {fieldErrors.some(errors => errors.length) ? <ul className="user-task-designer__validation-summary" aria-label="Field validation errors">{fieldErrors.flatMap((errors, index) => errors.map((error, errorIndex) => <li key={`${index}-${errorIndex}`}>Field {index + 1}: {error}</li>))}</ul> : null}
              </div>
            </section>
            <StudioSidePanelResizeHandle side="inspector" visible={layout.inspectorExpanded && !layout.maximizedSidePanel} value={layout.inspectorWidth} min={layout.inspectorMinimumWidth} max={560} panelLabel="properties" className="user-task-designer__resize-handle"
              onPointerDown={event => layout.startSidePanelResize("inspector", event)} onKeyDown={event => layout.handleSidePanelResizeKeyDown("inspector", event)} />
            <aside className="user-task-designer__properties" aria-label="Field properties" data-studio-side-panel="inspector">
              <div className="user-task-designer__panel-heading"><h3>Properties</h3><StudioSidePanelActions side="inspector" panelLabel="properties" collapsed={layout.inspectorCollapsed} maximized={layout.maximizedSidePanel === "inspector"}
                onToggleCollapsed={() => layout.toggleSidePanelCollapsed("inspector")} onToggleMaximized={() => layout.toggleSidePanelMaximized("inspector")} /></div>
              <div className="user-task-designer__panel-content">
                {form && onFormChange ? <button type="button" className="user-task-designer__form-settings-toggle" aria-pressed={showFormSettings} disabled={canvasDisabled} onClick={() => setShowFormSettings(true)}>Form settings</button> : null}
                {showFormSettings && form && onFormChange ? <div className="user-task-designer__form-settings">
                  <h4>Form settings</h4>
                  <StudioField label="Form ID"><input value={form.id} aria-invalid={!form.id.trim()} disabled={canvasDisabled || formIdReadOnly} onChange={event => onFormChange({ id: event.target.value })} /></StudioField>
                  <StudioField label="Version" description="Assigned automatically when the form is published."><output className="user-task-version-value">{versionLabel ?? form.version}</output></StudioField>
                  <StudioField label="Name"><input value={form.name} aria-invalid={!form.name.trim()} disabled={canvasDisabled} onChange={event => onFormChange({ name: event.target.value })} /></StudioField>
                </div> : selectedRow ? <>
                  <h4>Field properties</h4>
                  <StudioField label="Label"><input value={selectedRow.field.label} disabled={canvasDisabled} aria-invalid={fieldErrors[selectedIndex].some(error => error.includes("label"))} onChange={event => updateField(selectedRow.id, { label: event.target.value })} /></StudioField>
                  <StudioField label="Key" description="Used as the response property name."><input value={selectedRow.field.key} disabled={canvasDisabled} aria-invalid={fieldErrors[selectedIndex].some(error => error.includes("key"))} onChange={event => updateField(selectedRow.id, { key: event.target.value })} /></StudioField>
                  <StudioField label="Type"><select value={selectedControl?.control?.legacyKind ?? selectedControl?.control?.id ?? selectedRow.field.controlId ?? ""} disabled={canvasDisabled} onChange={event => {
                    const control = controlItems.find(item => item.legacyKind === event.target.value || item.id === event.target.value);
                    if (!control) return;
                    const created = control.createField(rowFields) as TaskField;
                    replaceField(selectedRow.id, { ...created, key: selectedRow.field.key, label: selectedRow.field.label, required: selectedRow.field.required,
                      readOnly: selectedRow.field.readOnly, help: selectedRow.field.help, kind: control.legacyKind ?? control.valueKind,
                      controlId: control.legacyKind ? undefined : control.id });
                  }}>
                    {selectedControl?.status !== "available" && selectedRow.field.controlId ? <option value={selectedRow.field.controlId} disabled>Unavailable: {selectedRow.field.controlId}</option> : null}
                    {controlItems.map(item => <option key={item.id} value={item.legacyKind ?? item.id}>{item.label}</option>)}
                  </select></StudioField>
                  <label className="user-task-designer__checkbox"><input type="checkbox" checked={selectedRow.field.required} disabled={canvasDisabled} onChange={event => updateField(selectedRow.id, { required: event.target.checked })} />Required</label>
                  <label className="user-task-designer__checkbox"><input type="checkbox" checked={selectedRow.field.readOnly} disabled={canvasDisabled} onChange={event => updateField(selectedRow.id, { readOnly: event.target.checked })} />Read only</label>
                  <StudioField label="Help text"><textarea rows={2} value={selectedRow.field.help ?? ""} disabled={canvasDisabled} onChange={event => updateField(selectedRow.id, { help: event.target.value || undefined })} /></StudioField>
                  {selectedControl?.status === "available" && selectedControl.control ? selectedControl.control.propertiesEditor ? <div className="user-task-designer__control-properties" key={selectedControl.control.id}>
                    {selectedControl.control.propertiesLabel ? <AdvancedSection label={selectedControl.control.propertiesLabel}>
                      <div className="user-task-designer__advanced-content">
                        <selectedControl.control.propertiesEditor field={selectedRow.field} disabled={canvasDisabled} onChange={updateSelectedControl} />
                      </div>
                    </AdvancedSection> : <selectedControl.control.propertiesEditor field={selectedRow.field} disabled={canvasDisabled} onChange={updateSelectedControl} />}
                  </div> : null : <p role="alert">{selectedControl?.reason ?? "This field control is not available."} The original field configuration has been preserved.</p>}
                  {fieldErrors[selectedIndex].length ? <ul className="user-task-designer__property-errors" aria-label="Selected field errors">{fieldErrors[selectedIndex].map(error => <li key={error}>{error}</li>)}</ul> : null}
                </> : <p>Select a field in the canvas to edit its properties, or open Form settings.</p>}
              </div>
            </aside>
          </div>
        </StudioTabPanel>
        <StudioTabPanel baseId={tabBaseId} index={1} hidden={activeView !== "source"} className="user-task-designer__view-panel">
          {visitedViews.source ? <FormSourceEditor form={sourceForm} disabled={disabled} fieldControls={fieldControls} onApply={applySourceForm} onUnappliedChange={updateUnappliedSource} /> : null}
        </StudioTabPanel>
        <StudioTabPanel baseId={tabBaseId} index={2} hidden={activeView !== "preview"} className="user-task-designer__view-panel user-task-designer__preview-panel">
          {visitedViews.preview ? <>
            <div className="user-task-designer__section-heading"><h3>Recipient preview</h3><span>{rows.length} {rows.length === 1 ? "field" : "fields"}</span></div>
            <div className="user-task-designer__canvas-scroll">
              {rows.length ? <div className="user-task-designer__recipient-preview" role="region" aria-label="Recipient preview"><ContributedTaskForm form={{ fields: rowFields }} value={previewValues} onChange={setPreviewValues} readOnly={false} errors={{}} controls={fieldControls} /></div>
                : <div className="user-task-designer__empty"><p>Add a field to preview the form recipients will see.</p></div>}
            </div>
          </> : null}
        </StudioTabPanel>
        {versionsPanel !== undefined ? <StudioTabPanel baseId={tabBaseId} index={3} hidden={activeView !== "versions"} className="user-task-designer__view-panel user-task-designer__versions-panel">
          {visitedViews.versions ? versionsPanel : null}
        </StudioTabPanel> : null}
      </div>
    </div>
  </section>;
}