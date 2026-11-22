import { qualifyStudioBackendPath } from "@elsa-workflows/studio-sdk";
import React, { useEffect, useId, useRef, useState } from "react";
import type { ElsaStudioModuleApi, StudioActivityPropertyEditorGroupProps } from "@elsa-workflows/studio-sdk";
import { StudioButton, StudioField } from "@elsa-workflows/studio-ui";
import { forms, message, type FormVersion } from "./taskApi";

type FormInputKey = "FormId" | "FormVersion";

export function FormPicker({ api, inputs, context, disabled, renderExpressionEditor, onValidationChange }: StudioActivityPropertyEditorGroupProps & { api: ElsaStudioModuleApi }) {
  const formIdInput = inputs.find(input => formInputKey(input) === "FormId");
  const versionInput = inputs.find(input => formInputKey(input) === "FormVersion");
  const formId = formIdInput?.syntax === "Literal" && typeof formIdInput.value === "string" ? formIdInput.value : "";
  const version = versionInput?.syntax === "Literal" && typeof versionInput.value === "string" ? versionInput.value : "";
  const hasExpression = inputs.some(input => input.syntax !== "Literal");
  const [mode, setMode] = useState<"fixed" | "expression" | null>(null);
  const expressionMode = mode === "expression" || (mode !== "fixed" && hasExpression);
  const activeExpression = inputs.find(input => input.syntax !== "Literal");
  const [expressionTarget, setExpressionTarget] = useState<FormInputKey | null>(null);
  const expressionKey = expressionTarget ?? (activeExpression ? formInputKey(activeExpression) : "FormId");
  const [items, setItems] = useState<FormVersion[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [reload, setReload] = useState(0);
  const [open, setOpen] = useState(false);
  const [historyFormId, setHistoryFormId] = useState<string | null>(null);
  const detailsRef = useRef<HTMLDetailsElement>(null);
  const summaryRef = useRef<HTMLElement>(null);
  const expressionSelectRef = useRef<HTMLSelectElement>(null);
  const wasExpressionMode = useRef(expressionMode);
  const historyListId = useId();

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    void (async () => {
      const loaded: FormVersion[] = [];
      let cursor: string | null = null;
      const seen = new Set<string>();
      do {
        const page = await forms(api, cursor);
        if (!active) return;
        loaded.push(...page.items);
        cursor = page.next ?? null;
        if (cursor && seen.has(cursor)) throw new Error("Form paging did not advance. Reload forms to try again.");
        if (cursor) seen.add(cursor);
      } while (cursor);
      if (active) setItems([...new Map(loaded.map(item => [`${item.form.id}\u001f${item.form.version}`, item])).values()]);
    })().catch(failure => {
      if (active) setError(message(failure));
    }).finally(() => {
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, [api, reload]);

  const versionsByForm = new Map<string, FormVersion[]>();
  for (const item of items) {
    const versions = versionsByForm.get(item.form.id) ?? [];
    versions.push(item);
    versionsByForm.set(item.form.id, versions);
  }
  for (const versions of versionsByForm.values()) versions.sort(compareFormVersionsDescending);
  const latestForms = [...versionsByForm.values()].map(versions => versions[0]!).sort((left, right) =>
    left.form.name.localeCompare(right.form.name) || left.form.id.localeCompare(right.form.id));
  const selectedVersion = items.find(item => item.form.id === formId && item.form.version === version);
  const selectedName = selectedVersion?.form.name ?? latestForms.find(item => item.form.id === formId)?.form.name ?? formId;
  const incompletePin = !hasExpression && Boolean(formId) !== Boolean(version);
  const unavailablePin = !hasExpression && Boolean(formId && version) && !selectedVersion;
  const invalid = !loading && !error && (incompletePin || unavailablePin);

  useEffect(() => {
    onValidationChange?.(invalid);
    return () => onValidationChange?.(false);
  }, [invalid, onValidationChange]);

  useEffect(() => {
    if (expressionMode && !wasExpressionMode.current) expressionSelectRef.current?.focus();
    else if (!expressionMode && wasExpressionMode.current) summaryRef.current?.focus();
    wasExpressionMode.current = expressionMode;
  }, [expressionMode]);

  const closePicker = () => {
    setOpen(false);
    summaryRef.current?.focus();
  };

  const selectVersion = (item: FormVersion) => {
    if (disabled || !context.setLiteralInputs) return;
    context.setLiteralInputs({ FormId: item.form.id, FormVersion: item.form.version });
    setMode(null);
    setExpressionTarget(null);
    setHistoryFormId(null);
    closePicker();
  };

  const clearSelection = () => {
    if (disabled || !context.setLiteralInputs) return;
    context.setLiteralInputs({ FormId: null, FormVersion: null });
    setMode(null);
    setExpressionTarget(null);
    setHistoryFormId(null);
    closePicker();
  };

  const summary = hasExpression && mode !== "fixed"
    ? "Expression-based form"
    : hasExpression
      ? "Choose a published form (expression unchanged)"
    : formId || version
      ? `${selectedName || "Unavailable form"}${version ? ` · Version ${version}` : " · Version not set"}${unavailablePin && !loading && !error ? " (unavailable)" : ""}`
      : "Choose a published form";

  const renderActions = () => <div className="user-task-form-picker__actions">
    {expressionMode
      ? <StudioButton aria-label="Use a fixed form" disabled={disabled} onClick={() => setMode("fixed")}>Use a fixed form</StudioButton>
      : <StudioButton disabled={disabled || inputs.some(input => input.descriptor.isWrapped === false)} onClick={() => {
        setExpressionTarget(activeExpression ? formInputKey(activeExpression) : "FormId");
        setMode("expression");
        setOpen(false);
      }}>Use an expression</StudioButton>}
    <StudioButton aria-label="Clear selection" disabled={disabled || (!formId && !version && !hasExpression) || !context.setLiteralInputs} onClick={clearSelection}>Clear selection</StudioButton>
    <a className="studio-button" data-variant="secondary" data-size="sm" href={qualifyStudioBackendPath("/user-tasks/forms")}>Manage forms</a>
  </div>;

  return <div className="user-task-property-editor user-task-form-picker">
    {expressionMode ? <div className="user-task-form-picker__expression">
      <StudioField label="Expression returns">
        <select ref={expressionSelectRef} className="studio-input" aria-label="Expression returns" disabled={disabled} value={expressionKey} onChange={event => setExpressionTarget(event.target.value as FormInputKey)}>
          <option value="FormId">Form ID</option>
          <option value="FormVersion">Form version</option>
        </select>
      </StudioField>
      <div className="user-task-form-picker__expression-editor">{renderExpressionEditor(expressionKey)}</div>
      <p>The existing form pin stays unchanged until you choose a published form.</p>
      {renderActions()}
    </div> : <>
      <details
        ref={detailsRef}
        className="user-task-form-picker__disclosure"
        open={open}
        onToggle={event => setOpen(event.currentTarget.open)}
        onKeyDown={event => {
          if (event.key === "Escape" && open) {
            event.preventDefault();
            closePicker();
          }
        }}
      >
        <summary ref={summaryRef} aria-label={`Task form: ${summary}`}>
          <span>{summary}</span>
        </summary>
        <div className="user-task-form-picker__panel">
          {loading && items.length === 0 ? <p role="status">Loading published forms…</p> : null}
          {error ? <div role="alert"><p>{error}</p><StudioButton aria-label="Retry loading forms" disabled={disabled || loading} onClick={() => setReload(value => value + 1)}>Retry loading forms</StudioButton></div> : null}
          {!loading && !error && latestForms.length === 0 ? <p role="status">No published forms are available.</p> : null}
          {latestForms.length > 0 ? <div className="user-task-form-picker__forms" aria-label="Published task forms">
            {latestForms.map(latest => {
              const versions = versionsByForm.get(latest.form.id) ?? [];
              const olderVersions = versions.slice(1);
              const selected = formId === latest.form.id && version === latest.form.version;
              const historyOpen = historyFormId === latest.form.id;
              return <div className="user-task-form-picker__form" key={latest.form.id}>
                <div className="user-task-form-picker__form-row">
                  <StudioButton
                    variant="ghost"
                    aria-label={`Select ${latest.form.name} version ${latest.form.version}`}
                    aria-pressed={selected}
                    disabled={disabled || !context.setLiteralInputs}
                    onClick={() => selectVersion(latest)}
                  >
                    <span>{latest.form.name}</span>
                    <small>{latest.form.id} · Version {latest.form.version} · Latest</small>
                  </StudioButton>
                  {olderVersions.length > 0 ? <StudioButton
                    variant="ghost"
                    aria-expanded={historyOpen}
                    aria-controls={historyListId}
                    disabled={disabled}
                    onClick={() => setHistoryFormId(historyOpen ? null : latest.form.id)}
                  >Other versions</StudioButton> : null}
                </div>
                {historyOpen ? <div id={historyListId} className="user-task-form-picker__versions" role="group" aria-label={`Other versions of ${latest.form.name}`}>
                  {olderVersions.map(item => <StudioButton
                    key={item.form.version}
                    variant="ghost"
                    aria-label={`Select ${latest.form.name} version ${item.form.version}`}
                    aria-pressed={formId === item.form.id && version === item.form.version}
                    disabled={disabled || !context.setLiteralInputs}
                    onClick={() => selectVersion(item)}
                  >{latest.form.name} · Version {item.form.version}</StudioButton>)}
                </div> : null}
              </div>;
            })}
          </div> : null}
          {unavailablePin && !loading && !error ? <p role="status">The saved form pin is unavailable. Choose a published version to replace it.</p> : null}
          {incompletePin ? <p role="alert">Choose both a form and a published version.</p> : null}
          {hasExpression ? <p role="status">The expression remains unchanged until you choose a published form.</p> : null}
          {!context.setLiteralInputs && !disabled ? <p role="status">This Studio host cannot update the form pin atomically.</p> : null}
          {renderActions()}
        </div>
      </details>
    </>}
  </div>;
}

function formInputKey(input: StudioActivityPropertyEditorGroupProps["inputs"][number]): FormInputKey {
  const key = input.descriptor.referenceKey?.trim() || input.descriptor.name;
  return key === "FormVersion" ? "FormVersion" : "FormId";
}

function compareFormVersionsDescending(left: FormVersion, right: FormVersion) {
  return right.form.version.localeCompare(left.form.version, undefined, { numeric: true }) ||
    Date.parse(right.createdAt) - Date.parse(left.createdAt);
}