import { useEffect, useId, useMemo, useRef, useState } from "react";
import { Copy, Maximize2, Plus, Trash2 } from "lucide-react";
import { StudioActionNotice, StudioButton, StudioSearchInput, useTablistKeyboard } from "@elsa-workflows/studio-ui";
import {
  StudioCodeEditor,
  type StudioCodeDiagnostic,
  type StudioCodeDocument,
  type StudioCodeLanguageAdapter
} from "@elsa-workflows/studio-code-editor";
import type {
  StudioActivityInputDescriptor,
  StudioActivityPropertyEditorContribution,
  StudioActivityPropertyEditorContext
} from "@elsa-workflows/studio-sdk";
import {
  defaultDictionaryValue,
  dictionaryEntries,
  formatDictionaryJson,
  parseDictionaryJson,
  validateDictionaryRows,
  type DictionaryKeyComparison,
  type DictionaryRow,
  type DictionaryValidationError
} from "./dictionaryEditorModel";
import {
  dictionaryValueSignature,
  readDictionaryEditorSession,
  writeDictionaryEditorSession,
  type DictionaryEditorTab
} from "./dictionaryEditorSession";
import { copyTextToClipboard } from "./workflow-editor/editorHelpers";

const jsonAdapter: StudioCodeLanguageAdapter = { language: "json", displayName: "JSON" };
let nextRowId = 0;
let nextStandaloneScopeId = 0;
const standaloneScopes = new WeakMap<object, string>();

interface EditorRow extends DictionaryRow {
  id: string;
}

interface RemovedEntry {
  row: EditorRow;
  index: number;
  noticeKey: string;
}

export interface DictionaryValueEditorProps {
  input: StudioActivityInputDescriptor;
  valueTypeName: string | null;
  value: unknown;
  editors: StudioActivityPropertyEditorContribution[];
  context: StudioActivityPropertyEditorContext;
  disabled: boolean;
  sessionScopeKey?: string;
  variant?: "inline" | "expanded";
  onOpenExpanded?(): void;
  onChange(value: unknown): void;
}

export function DictionaryValueEditor({
  input,
  valueTypeName,
  value,
  editors,
  context,
  disabled,
  sessionScopeKey,
  variant = "inline",
  onOpenExpanded,
  onChange
}: DictionaryValueEditorProps) {
  const configuration = readDictionaryConfiguration(input);
  const effectiveSessionScope = sessionScopeKey ?? readStandaloneSessionScope(context.activity);
  const sessionKey = `${effectiveSessionScope}:${readActivityIdentity(context.activity)}:${input.name}`;
  const externalValue = useMemo(() => normalizeDictionaryValue(value), [value]);
  const externalSignature = dictionaryValueSignature(externalValue);
  const initialSession = readDictionaryEditorSession<EditorRow>(sessionKey);
  const [rows, setRows] = useState<EditorRow[]>(() => initialSession?.baseline === externalSignature && initialSession.rows
    ? initialSession.rows
    : createRows(externalValue));
  const [activeTab, setActiveTab] = useState<DictionaryEditorTab>(initialSession?.activeTab ?? "table");
  const [jsonDraft, setJsonDraft] = useState(() => initialSession?.baseline === externalSignature && initialSession.jsonDraft != null
    ? initialSession.jsonDraft
    : formatDictionaryJson(externalValue));
  const [complexDrafts, setComplexDrafts] = useState<Record<string, string>>(
    () => initialSession?.baseline === externalSignature ? initialSession.complexDrafts ?? {} : {}
  );
  const [filter, setFilter] = useState("");
  const [pinnedRowIds, setPinnedRowIds] = useState<Set<string>>(() => new Set());
  const [removed, setRemoved] = useState<RemovedEntry | null>(null);
  const [notice, setNotice] = useState<{ key: string; message: string } | null>(null);
  const [pendingTab, setPendingTab] = useState<DictionaryEditorTab | null>(null);
  const effectiveTab: DictionaryEditorTab = variant === "inline" ? "table" : activeTab;
  const emittedSignature = useRef<string | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const mountedRef = useRef(false);
  const keyRefs = useRef(new Map<string, HTMLInputElement>());
  const tableValidation = useMemo(
    () => validateDictionaryRows(rows, configuration.keyComparison, valueTypeName),
    [configuration.keyComparison, rows, valueTypeName]
  );
  const jsonValidation = useMemo(
    () => parseDictionaryJson(jsonDraft, configuration.keyComparison, valueTypeName),
    [configuration.keyComparison, jsonDraft, valueTypeName]
  );
  const invalidComplexDraftIds = useMemo(
    () => new Set(Object.entries(complexDrafts).flatMap(([id, draft]) => parseComplexDraft(draft).valid ? [] : [id])),
    [complexDrafts]
  );

  useEffect(() => {
    if (variant !== "expanded") return;
    const frame = requestAnimationFrame(() => {
      rootRef.current?.querySelector<HTMLElement>("[role='tab'][aria-selected='true']")?.focus();
    });
    return () => cancelAnimationFrame(frame);
  }, [variant]);

  useEffect(() => {
    if (!mountedRef.current) {
      mountedRef.current = true;
      return;
    }
    if (emittedSignature.current === externalSignature) {
      emittedSignature.current = null;
      return;
    }
    const retained = readDictionaryEditorSession<EditorRow>(sessionKey);
    if (retained?.baseline === externalSignature) return;
    const nextRows = createRows(externalValue);
    setRows(nextRows);
    setJsonDraft(formatDictionaryJson(externalValue));
    setComplexDrafts({});
    setActiveTab("table");
    writeDictionaryEditorSession(sessionKey, { baseline: externalSignature, rows: nextRows, activeTab: "table" });
  }, [externalSignature, externalValue, sessionKey]);

  useEffect(() => {
    writeDictionaryEditorSession(sessionKey, {
      baseline: externalSignature,
      rows,
      jsonDraft: jsonValidation.valid ? undefined : jsonDraft,
      complexDrafts: Object.keys(complexDrafts).length > 0 ? complexDrafts : undefined,
      activeTab
    });
  }, [activeTab, complexDrafts, externalSignature, jsonDraft, jsonValidation.valid, rows, sessionKey]);

  const emitRows = (nextRows: EditorRow[]) => {
    const validation = validateDictionaryRows(nextRows, configuration.keyComparison, valueTypeName);
    if (!validation.valid) return;
    const signature = dictionaryValueSignature(validation.value);
    emittedSignature.current = signature;
    setJsonDraft(formatDictionaryJson(validation.value));
    writeDictionaryEditorSession(sessionKey, { baseline: signature, rows: nextRows, complexDrafts, activeTab });
    onChange(validation.value);
  };

  const setNextRows = (nextRows: EditorRow[]) => {
    setRows(nextRows);
    setRemoved(null);
    emitRows(nextRows);
  };

  const updateRow = (id: string, update: Partial<DictionaryRow>) => {
    const nextRows = rows.map(row => row.id === id ? { ...row, ...update } : row);
    setNextRows(nextRows);
  };

  const addRow = () => {
    setFilter("");
    setRemoved(null);
    const row: EditorRow = { id: createRowId(), key: "", value: defaultValueForType(valueTypeName) };
    setPinnedRowIds(current => new Set(current).add(row.id));
    setRows(current => [...current, row]);
    requestAnimationFrame(() => keyRefs.current.get(row.id)?.focus());
  };

  const removeRow = (id: string) => {
    const index = rows.findIndex(row => row.id === id);
    if (index < 0) return;
    const row = rows[index];
    const nextRows = rows.filter(candidate => candidate.id !== id);
    setPinnedRowIds(current => {
      const next = new Set(current);
      next.delete(id);
      return next;
    });
    const nextRemoved = { row, index, noticeKey: `removed-${row.id}-${Date.now()}` };
    setRows(nextRows);
    setRemoved(nextRemoved);
    emitRows(nextRows);
    requestAnimationFrame(() => {
      const next = nextRows[Math.min(index, nextRows.length - 1)];
      if (next) keyRefs.current.get(next.id)?.focus();
      else document.querySelector<HTMLButtonElement>(`[data-dictionary-add='${cssEscape(sessionKey)}']`)?.focus();
    });
  };

  const undoRemove = () => {
    if (!removed) return;
    const nextRows = [...rows];
    nextRows.splice(Math.min(removed.index, nextRows.length), 0, removed.row);
    setRows(nextRows);
    emitRows(nextRows);
    setRemoved(null);
    requestAnimationFrame(() => keyRefs.current.get(removed.row.id)?.focus());
  };

  const requestTab = (next: string) => {
    const target = next as DictionaryEditorTab;
    if (target === activeTab) return;
    const sourceInvalid = effectiveTab === "table"
      ? !tableValidation.valid || invalidComplexDraftIds.size > 0
      : !jsonValidation.valid;
    if (sourceInvalid) setPendingTab(target);
    else setActiveTab(target);
  };

  const discardAndSwitch = () => {
    if (!pendingTab) return;
    const cleanRows = createRows(externalValue);
    setRows(cleanRows);
    setJsonDraft(formatDictionaryJson(externalValue));
    setComplexDrafts({});
    setActiveTab(pendingTab);
    setPendingTab(null);
  };

  const changeJson = (document: StudioCodeDocument) => {
    setRemoved(null);
    setJsonDraft(document.value);
    const parsed = parseDictionaryJson(document.value, configuration.keyComparison, valueTypeName);
    if (!parsed.valid) return;
    const nextRows = createRows(parsed.value);
    const signature = dictionaryValueSignature(parsed.value);
    emittedSignature.current = signature;
    setRows(nextRows);
    writeDictionaryEditorSession(sessionKey, { baseline: signature, rows: nextRows, activeTab: "json" });
    onChange(parsed.value);
  };

  const formatJson = () => {
    if (!jsonValidation.valid) return;
    setJsonDraft(formatDictionaryJson(jsonValidation.value));
  };

  const copyJson = async () => {
    await copyTextToClipboard(jsonDraft);
    setNotice({ key: `copied-${Date.now()}`, message: "JSON copied" });
  };

  const visibleRows = filterRows(rows, filter, variant, tableValidation.valid ? [] : tableValidation.errors, pinnedRowIds);
  const hiddenCount = variant === "inline" ? Math.max(0, rows.length - visibleRows.length) : 0;
  const errorMap = groupErrors(rows, tableValidation.valid ? [] : tableValidation.errors);
  const changeComplexDraft = (row: EditorRow, draft: string) => {
    setRemoved(null);
    setComplexDrafts(current => ({ ...current, [row.id]: draft }));
    const parsed = parseComplexDraft(draft);
    if (parsed.valid) updateRow(row.id, { value: parsed.value });
  };
  const tabIds: DictionaryEditorTab[] = ["table", "json"];
  const tabsId = useId();
  const onTabKeyDown = useTablistKeyboard(tabIds, activeTab, requestTab);

  return (
    <div ref={rootRef} className={`wf-dictionary-editor ${variant}`} data-dictionary-session={sessionKey}>
      {variant === "expanded" ? (
        <>
          <div className="wf-dictionary-tabs" role="tablist" aria-label={`${input.displayName || input.name} editor mode`} onKeyDown={onTabKeyDown}>
            {tabIds.map((tab, index) => {
              const selected = effectiveTab === tab;
              const errors = tab === "table"
                ? (tableValidation.valid ? 0 : tableValidation.errors.length) + invalidComplexDraftIds.size
                : jsonValidation.valid ? 0 : jsonValidation.errors.length;
              return (
                <button
                  type="button"
                  role="tab"
                  id={`${tabsId}-tab-${index}`}
                  aria-controls={`${tabsId}-panel-${index}`}
                  aria-selected={selected}
                  tabIndex={selected ? 0 : -1}
                  data-tab-id={tab}
                  className={selected ? "active" : ""}
                  key={tab}
                  onClick={() => requestTab(tab)}
                >
                  {tab === "table" ? "Table" : "JSON"}{errors > 0 ? ` (${errors} errors)` : ""}
                </button>
              );
            })}
          </div>
          {pendingTab ? (
            <div className="wf-dictionary-tab-guard" role="alertdialog" aria-label="Discard invalid dictionary draft?">
              <p>Fix the current errors before switching, or discard this private draft.</p>
              <div>
                <StudioButton size="sm" onClick={discardAndSwitch}>Discard draft</StudioButton>
                <StudioButton size="sm" variant="ghost" onClick={() => setPendingTab(null)}>Keep editing</StudioButton>
              </div>
            </div>
          ) : null}
        </>
      ) : null}

      {effectiveTab === "table" ? (
        <div
          id={variant === "expanded" ? `${tabsId}-panel-0` : undefined}
          role={variant === "expanded" ? "tabpanel" : undefined}
          aria-labelledby={variant === "expanded" ? `${tabsId}-tab-0` : undefined}
          className="wf-dictionary-table-panel"
        >
          {variant === "expanded" ? (
            <StudioSearchInput
              aria-label={`Filter ${input.displayName || input.name} entries`}
              placeholder="Filter keys and values"
              value={filter}
              onChange={event => setFilter(event.target.value)}
            />
          ) : null}
          {rows.length === 0 ? <p className="wf-dictionary-empty">No entries.</p> : (
            <div className="wf-dictionary-table" role="table" aria-label={`${input.displayName || input.name} entries`}>
              <div className="wf-dictionary-head" role="row">
                <span role="columnheader">{configuration.keyLabel}</span>
                <span role="columnheader">{configuration.valueLabel}</span>
                <span role="columnheader" className="wf-visually-hidden">Actions</span>
              </div>
              <div role="rowgroup" className="wf-dictionary-rows">
                {visibleRows.map((row, visibleIndex) => (
                  <DictionaryRowEditor
                    key={row.id}
                    row={row}
                    rowNumber={rows.indexOf(row) + 1}
                    input={input}
                    valueTypeName={valueTypeName}
                    editors={editors}
                    context={context}
                    disabled={disabled}
                    expanded={variant === "expanded"}
                    configuration={configuration}
                    errors={errorMap.get(row.id) ?? []}
                    complexDraft={complexDrafts[row.id]}
                    keyRef={element => {
                      if (element) keyRefs.current.set(row.id, element);
                      else keyRefs.current.delete(row.id);
                    }}
                    onKeyChange={key => updateRow(row.id, { key })}
                    onValueChange={next => updateRow(row.id, { value: next })}
                    onComplexDraftChange={draft => changeComplexDraft(row, draft)}
                    onAdvance={() => advanceFromValue(visibleRows, visibleIndex, row, addRow, keyRefs)}
                    onRemove={() => removeRow(row.id)}
                  />
                ))}
              </div>
            </div>
          )}
          {hiddenCount > 0 ? <p className="wf-dictionary-more">{hiddenCount} more {hiddenCount === 1 ? "entry" : "entries"}</p> : null}
          <div className="wf-dictionary-actions">
            <StudioButton
              size="sm"
              disabled={disabled}
              data-dictionary-add={sessionKey}
              onClick={addRow}
            >
              <Plus size={13} /> Add entry
            </StudioButton>
            {variant === "inline" && onOpenExpanded ? (
              <StudioButton className="wf-dictionary-open-expanded" size="sm" variant="ghost" onClick={onOpenExpanded}>
                <Maximize2 size={13} /> Open expanded editor
              </StudioButton>
            ) : null}
          </div>
        </div>
      ) : (
        <div
          id={`${tabsId}-panel-1`}
          role="tabpanel"
          aria-labelledby={`${tabsId}-tab-1`}
          className="wf-dictionary-json-panel"
        >
          <div className="wf-dictionary-json-actions">
            <StudioButton size="sm" disabled={!jsonValidation.valid} onClick={formatJson}>Format JSON</StudioButton>
            <StudioButton size="sm" variant="ghost" onClick={() => void copyJson()}><Copy size={13} /> Copy JSON</StudioButton>
          </div>
          <StudioCodeEditor
            ariaLabel={`${input.displayName || input.name} dictionary JSON`}
            document={{ uri: `elsa://dictionaries/${encodeURIComponent(input.name)}.json`, language: "json", value: jsonDraft }}
            diagnostics={jsonDiagnostics(jsonValidation)}
            languageAdapter={jsonAdapter}
            minHeight="300px"
            readOnly={disabled}
            theme="studio"
            onChange={changeJson}
          />
        </div>
      )}

      {removed ? (
        <StudioActionNotice
          key={removed.noticeKey}
          className="wf-dictionary-notice"
          message="Entry removed"
          actionLabel="Undo"
          onAction={undoRemove}
          onDismiss={() => setRemoved(null)}
        />
      ) : notice ? (
        <StudioActionNotice
          key={notice.key}
          className="wf-dictionary-notice"
          message={notice.message}
          onDismiss={() => setNotice(null)}
        />
      ) : null}
    </div>
  );
}

function DictionaryRowEditor({
  row,
  rowNumber,
  input,
  valueTypeName,
  editors,
  context,
  disabled,
  expanded,
  configuration,
  errors,
  complexDraft,
  keyRef,
  onKeyChange,
  onValueChange,
  onComplexDraftChange,
  onAdvance,
  onRemove
}: {
  row: EditorRow;
  rowNumber: number;
  input: StudioActivityInputDescriptor;
  valueTypeName: string | null;
  editors: StudioActivityPropertyEditorContribution[];
  context: StudioActivityPropertyEditorContext;
  disabled: boolean;
  expanded: boolean;
  configuration: DictionaryConfiguration;
  errors: DictionaryValidationError[];
  complexDraft?: string;
  keyRef(element: HTMLInputElement | null): void;
  onKeyChange(value: string): void;
  onValueChange(value: unknown): void;
  onComplexDraftChange(value: string): void;
  onAdvance(): void;
  onRemove(): void;
}) {
  const errorId = `${useId()}-error`;
  const valueDescriptor: StudioActivityInputDescriptor = {
    ...input,
    name: `${input.name}.Value`,
    displayName: configuration.valueLabel,
    description: null,
    typeName: valueTypeName ?? "System.String",
    isWrapped: false
  };
  const valueContext: StudioActivityPropertyEditorContext = { ...context, scope: "element" };
  const ValueEditor = resolveEditor(editors, valueDescriptor, valueContext)?.component;
  const keyError = errors.find(error => error.code === "blank-key" || error.code === "duplicate-key");
  const valueError = errors.find(error => error.code === "invalid-value");
  const label = row.key || `row ${rowNumber}`;

  return (
    <div className={errors.length > 0 ? "wf-dictionary-row invalid" : "wf-dictionary-row"} role="row">
      <label role="cell" className="wf-dictionary-cell key">
        <span>{configuration.keyLabel}</span>
        <input
          ref={keyRef}
          type="text"
          aria-label={`${configuration.keyLabel} ${rowNumber}`}
          aria-invalid={keyError ? true : undefined}
          aria-describedby={errors.length > 0 ? errorId : undefined}
          placeholder={configuration.keyPlaceholder}
          value={row.key}
          disabled={disabled}
          onChange={event => onKeyChange(event.target.value)}
          onKeyDown={event => {
            if (event.key !== "Enter") return;
            event.preventDefault();
            event.currentTarget.closest("[role='row']")?.querySelector<HTMLElement>(".wf-dictionary-cell.value input, .wf-dictionary-cell.value select, .wf-dictionary-cell.value button")?.focus();
          }}
        />
      </label>
      <div role="cell" className="wf-dictionary-cell value">
        <span>{configuration.valueLabel}</span>
        {ValueEditor ? (
          <ValueEditor descriptor={valueDescriptor} value={row.value} disabled={disabled} context={valueContext} onChange={onValueChange} />
        ) : isComplexType(valueTypeName) ? (
          <ComplexValueEditor
            label={`${configuration.valueLabel} ${rowNumber}`}
            value={row.value}
            disabled={disabled}
            expanded={expanded}
            draft={complexDraft}
            onDraftChange={onComplexDraftChange}
          />
        ) : (
          <FallbackValueEditor
            typeName={valueTypeName}
            value={row.value}
            disabled={disabled}
            placeholder={configuration.valuePlaceholder}
            invalid={!!valueError}
            onChange={onValueChange}
            onAdvance={onAdvance}
          />
        )}
      </div>
      <div role="cell" className="wf-dictionary-row-actions">
        <button type="button" aria-label={`Remove ${label}`} title="Remove entry" disabled={disabled} onClick={onRemove}>
          <Trash2 size={14} />
        </button>
      </div>
      {errors.length > 0 ? <p id={errorId} className="wf-dictionary-row-error">{errors.map(error => error.message).join(" ")}</p> : null}
    </div>
  );
}

function ComplexValueEditor({
  label,
  value,
  disabled,
  expanded,
  draft: retainedDraft,
  onDraftChange
}: {
  label: string;
  value: unknown;
  disabled: boolean;
  expanded: boolean;
  draft?: string;
  onDraftChange(value: string): void;
}) {
  const [open, setOpen] = useState(false);
  const draft = retainedDraft ?? formatComplexValue(value);
  const parsedDraft = parseComplexDraft(draft);

  if (!expanded) {
    return (
      <div className="wf-dictionary-complex-summary">
        <strong>{summarizeComplexValue(value)}</strong>
        <small>Open the expanded editor to edit this value.</small>
      </div>
    );
  }

  const change = (document: StudioCodeDocument) => {
    onDraftChange(document.value);
  };

  return (
    <div className="wf-dictionary-complex-editor">
      <button type="button" className="wf-dictionary-complex-toggle" aria-expanded={open} onClick={() => setOpen(current => !current)}>
        <span>{summarizeComplexValue(value)}</span>
        <small>{open ? "Hide JSON editor" : "Edit value JSON"}</small>
      </button>
      {open ? (
        <StudioCodeEditor
          ariaLabel={`${label} JSON`}
          document={{ uri: `elsa://dictionary-values/${encodeURIComponent(label)}.json`, language: "json", value: draft }}
          diagnostics={parsedDraft.valid ? [] : [{ severity: "error", code: "INVALID_VALUE_JSON", message: parsedDraft.error }]}
          languageAdapter={jsonAdapter}
          minHeight="180px"
          readOnly={disabled}
          theme="studio"
          onChange={change}
        />
      ) : null}
    </div>
  );
}

function FallbackValueEditor({
  typeName,
  value,
  disabled,
  placeholder,
  invalid,
  onChange,
  onAdvance
}: {
  typeName: string | null;
  value: unknown;
  disabled: boolean;
  placeholder?: string;
  invalid: boolean;
  onChange(value: unknown): void;
  onAdvance(): void;
}) {
  const normalized = (typeName ?? "").toLowerCase();
  if (["system.boolean", "boolean", "bool"].includes(normalized)) {
    return <input type="checkbox" checked={value === true} disabled={disabled} onChange={event => onChange(event.target.checked)} />;
  }
  const numeric = /(?:byte|sbyte|int16|uint16|int32|uint32|int64|uint64|single|double|decimal|number)$/.test(normalized);
  return (
    <input
      type={numeric ? "number" : "text"}
      value={value == null ? "" : String(value)}
      disabled={disabled}
      placeholder={placeholder}
      aria-invalid={invalid ? true : undefined}
      onChange={event => onChange(numeric && event.target.value !== "" ? Number(event.target.value) : event.target.value)}
      onKeyDown={event => {
        if (event.key !== "Enter") return;
        event.preventDefault();
        onAdvance();
      }}
    />
  );
}

interface DictionaryConfiguration {
  keyLabel: string;
  valueLabel: string;
  keyPlaceholder?: string;
  valuePlaceholder?: string;
  keyComparison: DictionaryKeyComparison;
}

function readDictionaryConfiguration(input: StudioActivityInputDescriptor): DictionaryConfiguration {
  const raw = input.uiSpecifications?.dictionary;
  const dictionary = raw && typeof raw === "object" ? raw as Record<string, unknown> : {};
  return {
    keyLabel: nonEmptyString(dictionary.keyLabel) ?? "Key",
    valueLabel: nonEmptyString(dictionary.valueLabel) ?? "Value",
    keyPlaceholder: nonEmptyString(dictionary.keyPlaceholder) ?? undefined,
    valuePlaceholder: nonEmptyString(dictionary.valuePlaceholder) ?? undefined,
    keyComparison: dictionary.keyComparison === "ordinalIgnoreCase" ? "ordinalIgnoreCase" : "ordinal"
  };
}

function nonEmptyString(value: unknown) {
  return typeof value === "string" && value.trim() ? value : null;
}

function normalizeDictionaryValue(value: unknown): Record<string, unknown> {
  return value != null && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : defaultDictionaryValue();
}

function createRows(value: unknown): EditorRow[] {
  return dictionaryEntries(value).map(row => ({ ...row, id: createRowId() }));
}

function createRowId() {
  nextRowId += 1;
  return `dictionary-row-${nextRowId}`;
}

function defaultValueForType(typeName: string | null) {
  const normalized = (typeName ?? "").toLowerCase();
  if (["system.boolean", "boolean", "bool"].includes(normalized)) return false;
  if (/(?:byte|sbyte|int16|uint16|int32|uint32|int64|uint64|single|double|decimal|number)$/.test(normalized)) return 0;
  if (isComplexType(typeName)) return {};
  return "";
}

function resolveEditor(
  editors: StudioActivityPropertyEditorContribution[],
  descriptor: StudioActivityInputDescriptor,
  context: StudioActivityPropertyEditorContext
) {
  return [...editors]
    .sort((left, right) => (left.order ?? 500) - (right.order ?? 500))
    .find(editor => editor.supports(descriptor, context));
}

function filterRows(
  rows: EditorRow[],
  filter: string,
  variant: "inline" | "expanded",
  errors: DictionaryValidationError[],
  pinnedRowIds: Set<string>
) {
  if (variant === "inline") {
    const first = rows.slice(0, 5);
    const draftIds = new Set(errors.map(error => error.index == null ? null : rows[error.index]?.id).filter(Boolean));
    return [...first, ...rows.slice(5).filter(row => draftIds.has(row.id) || pinnedRowIds.has(row.id))];
  }
  const term = filter.trim().toLowerCase();
  if (!term) return rows;
  const invalid = new Set(errors.map(error => error.index == null ? null : rows[error.index]?.id).filter(Boolean));
  return rows.filter(row => invalid.has(row.id) || row.key.toLowerCase().includes(term) || simpleValueText(row.value).includes(term));
}

function simpleValueText(value: unknown) {
  return ["string", "number", "boolean"].includes(typeof value) ? String(value).toLowerCase() : "";
}

function groupErrors(rows: EditorRow[], errors: DictionaryValidationError[]) {
  const result = new Map<string, DictionaryValidationError[]>();
  for (const error of errors) {
    const row = error.index == null ? null : rows[error.index];
    if (!row) continue;
    const existing = result.get(row.id);
    if (existing) existing.push(error);
    else result.set(row.id, [error]);
  }
  return result;
}

function jsonDiagnostics(result: ReturnType<typeof parseDictionaryJson>): StudioCodeDiagnostic[] {
  if (result.valid) return [];
  return result.errors.map((error, index) => ({
    severity: "error",
    code: error.code.toUpperCase().replaceAll("-", "_"),
    message: error.key ? `${error.key}: ${error.message}` : error.message,
    startLineNumber: index + 1
  }));
}

function isComplexType(typeName: string | null) {
  if (!typeName) return false;
  const normalized = typeName.toLowerCase();
  return !normalized.includes("string") && !normalized.includes("bool") &&
    !/(?:byte|sbyte|int16|uint16|int32|uint32|int64|uint64|single|double|decimal|number)$/.test(normalized);
}

function summarizeComplexValue(value: unknown) {
  if (Array.isArray(value)) return `${value.length} ${value.length === 1 ? "item" : "items"}`;
  if (value && typeof value === "object") {
    const count = Object.keys(value).length;
    return `${count} ${count === 1 ? "property" : "properties"}`;
  }
  return value == null ? "Null" : "JSON value";
}

function formatComplexValue(value: unknown) {
  try {
    return JSON.stringify(value ?? {}, null, 2) ?? "{}";
  } catch {
    return "{}";
  }
}

function parseComplexDraft(draft: string): { valid: true; value: object } | { valid: false; error: string } {
  try {
    const value = JSON.parse(draft) as unknown;
    if (value == null || typeof value !== "object") {
      return { valid: false, error: "Value must be a JSON object or array." };
    }
    return { valid: true, value };
  } catch (error) {
    return { valid: false, error: error instanceof Error ? error.message : "Invalid JSON value." };
  }
}

function advanceFromValue(
  visibleRows: EditorRow[],
  visibleIndex: number,
  row: EditorRow,
  addRow: () => void,
  keyRefs: { current: Map<string, HTMLInputElement> }
) {
  const next = visibleRows[visibleIndex + 1];
  if (next) keyRefs.current.get(next.id)?.focus();
  else if (row.key.trim()) addRow();
}

function readActivityIdentity(activity: unknown) {
  if (!activity || typeof activity !== "object") return "activity";
  const record = activity as Record<string, unknown>;
  return String(record.nodeId ?? record.id ?? "activity");
}

function readStandaloneSessionScope(activity: unknown) {
  if (!activity || typeof activity !== "object") return "standalone-dictionary-editor";
  const existing = standaloneScopes.get(activity);
  if (existing) return existing;
  nextStandaloneScopeId += 1;
  const scope = `standalone-dictionary-editor-${nextStandaloneScopeId}`;
  standaloneScopes.set(activity, scope);
  return scope;
}

function cssEscape(value: string) {
  return value.replace(/["\\]/g, "\\$&");
}
