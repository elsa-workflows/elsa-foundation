import React, { useEffect, useId, useMemo, useRef, useState } from "react";
import type { ElsaStudioModuleApi, StudioExpressionEditorContribution, StudioExpressionEditorProps } from "@elsa-workflows/studio-sdk";
import { StudioAlert, StudioButton } from "@elsa-workflows/studio-ui";
import { ValueInput } from "./CasePanel";
import { decisionsCapability, failureMessage, type Scalar } from "./decisionsApi";
import {
  asDecisionValueMap,
  decisionInputsEditorId,
  displayedDecisionInput,
  extraDecisionInputs,
  formatDecisionInputsJson,
  invalidDecisionInputs,
  isDecisionInputsContext,
  isScalar,
  matchDecisionInputTypes,
  mismatchedDecisionInputs,
  parseDecisionInputsJson,
  parseDecisionPin,
  readDecisionPin,
  removeDecisionInputs,
  writtenDecisionInput
} from "./decisionInputs";
import { loadDecisionTable, missingDecisionInputs } from "./decisionDraftValidation";
import { useForegroundGeneration } from "./decisionFreshness";
import type { Table } from "./decisionXml";

export function createDecisionInputsEditorContribution(api: ElsaStudioModuleApi): StudioExpressionEditorContribution {
  return {
    id: decisionInputsEditorId,
    requiresBackend: decisionsCapability,
    order: 40,
    supports: isDecisionInputsContext,
    readsActivityContext: true,
    surfaces: {
      inline: props => <DecisionInputsEditor {...props} api={api} />,
      expanded: props => <DecisionInputsEditor {...props} api={api} />
    },
    createDefaultValue: () => ({})
  };
}

export function DecisionInputsEditor({
  api,
  value,
  disabled = false,
  context,
  onChange,
  onValidationChange
}: StudioExpressionEditorProps & { api: ElsaStudioModuleApi }) {
  const pin = readDecisionPin(context.activity);
  const parsed = parseDecisionPin(pin);
  const freshness = useForegroundGeneration();
  const jsonErrorId = useId();
  const [table, setTable] = useState<Table | null>(null);
  const [tablePin, setTablePin] = useState("");
  const [loading, setLoading] = useState(!!parsed);
  const [error, setError] = useState("");
  const [unpublished, setUnpublished] = useState(false);
  const values = asDecisionValueMap(value);
  const extras = table && !loading ? extraDecisionInputs(values, table.inputs) : [];
  const mismatches = table && !loading ? mismatchedDecisionInputs(values, table.inputs) : [];
  const invalids = table && !loading ? invalidDecisionInputs(values, table.inputs) : [];
  const missing = table && !loading ? missingDecisionInputs(values, table.inputs) : [];
  const jsonExternal = useMemo(() => formatDecisionInputsJson(values), [values]);
  const [jsonDraft, setJsonDraft] = useState(jsonExternal);
  const [jsonError, setJsonError] = useState("");
  const jsonFocused = useRef(false);
  const shownTable = useRef({ pin: tablePin, table });
  shownTable.current = { pin: tablePin, table };
  const blocking = extras.length > 0 || mismatches.length > 0 || invalids.length > 0 || missing.length > 0 || !!jsonError || !!error;

  useEffect(() => {
    onValidationChange?.(blocking);
    return () => onValidationChange?.(false);
  }, [blocking, onValidationChange]);

  useEffect(() => {
    if (jsonFocused.current) return;
    setJsonDraft(jsonExternal);
    setJsonError("");
  }, [jsonExternal]);

  useEffect(() => {
    const selection = parseDecisionPin(pin);
    if (!selection) {
      setTable(null);
      setTablePin("");
      setLoading(false);
      setError("");
      setUnpublished(false);
      return;
    }
    const nextPin = `${selection.assetId}@${selection.selection}`;
    const background = shownTable.current.pin === nextPin && shownTable.current.table !== null;
    let active = true;
    if (!background) {
      setLoading(true);
      setError("");
      setUnpublished(false);
    }
    void loadDecisionTable(api, selection.assetId, selection.selection).then(result => {
      if (!active) return;
      setTable(result.table);
      setTablePin(nextPin);
      setUnpublished(result.unpublished);
      setError(result.error);
    }, failure => {
      if (!active) return;
      if (!background) {
        setTable(null);
        setTablePin("");
        setUnpublished(false);
      }
      setError(failureMessage(failure));
    }).finally(() => {
      if (active && !background) setLoading(false);
    });
    return () => { active = false; };
  }, [api, pin, freshness]);

  const commit = (next: Record<string, Scalar>) => onChange(next);
  const updateClause = (name: string, clause: Table["inputs"][number], next: Scalar) =>
    commit({ ...values, [name]: writtenDecisionInput(clause, next) });
  const updateExtra = (name: string, next: Scalar) => commit({ ...values, [name]: next });

  return <div className="decision-inputs-editor">
    {!parsed ? <p className="decision-muted">Select a decision to load its input fields from the table. JSON stays available below.</p> : null}
    {loading ? <p role="status">Loading input fields…</p> : null}
    {unpublished ? <StudioAlert tone="warning">This decision has no published version. Publish it before Review &amp; publish; these fields follow the current draft table.</StudioAlert> : null}
    {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
    {missing.length ? <StudioAlert tone="danger">
      {missing.join(", ")} {missing.length === 1 ? "is" : "are"} missing from this table.
      {" "}Runtime will refuse MissingInput. Enter or confirm a value before Review &amp; publish.
    </StudioAlert> : null}
    {invalids.length ? <StudioAlert tone="danger">
      {invalids.map(item => `${item.name} is empty or not a valid ${item.tableType}.`).join(" ")}
      {" "}Runtime will refuse InvalidValue. Enter a valid value before Review &amp; publish.
    </StudioAlert> : null}
    {mismatches.length ? <StudioAlert tone="danger">
      {mismatches.map(item => `${item.name} is stored as ${item.storedKind}; this table expects ${item.tableType}.`).join(" ")}
      {" "}Runtime will refuse InputTypeMismatch. Edit a field or store as table types so the saved kind matches what you see.
    </StudioAlert> : null}
    {extras.length ? <StudioAlert tone="danger">
      {extras.map(item => item.name).join(", ")} {extras.length === 1 ? "is" : "are"} not in this table.
      {" "}Runtime will refuse extra inputs (InputMembership). Remove them to keep only this table&apos;s fields; authored values are kept until you remove them.
    </StudioAlert> : null}
    {table ? <fieldset disabled={disabled || loading} className="decision-evaluate-fields"><legend className="decision-visually-hidden">Decision table inputs</legend><div className="decision-form-grid">{table.inputs.map(clause => {
      const stored = values[clause.name];
      const unset = !isScalar(stored);
      return <ValueInput
        key={clause.id}
        clause={clause}
        label={clause.name}
        unset={unset}
        value={unset ? { kind: clause.type === "number" ? "number" : clause.type === "boolean" ? "boolean" : "string", value: "" } : displayedDecisionInput(clause, stored)}
        onChange={next => updateClause(clause.name, clause, next)}
      />;
    })}</div>
      {mismatches.length ? <StudioButton disabled={disabled || loading} onClick={() => { const next = matchDecisionInputTypes(values, table.inputs); if (next) commit(next); }}>Store as table types</StudioButton> : null}
    </fieldset> : null}
    {extras.length ? <fieldset disabled={disabled || loading} className="decision-evaluate-fields"><legend className="decision-visually-hidden">Inputs not in this table</legend><div className="decision-form-grid">{extras.map(item =>
      <div key={item.name} className="decision-extra-input">
        <ValueInput clause={{ id: item.name, name: item.name, type: item.value.kind === "number" ? "number" : item.value.kind === "boolean" ? "boolean" : "string" }} label={`${item.name} (not in this table)`} value={item.value} onChange={next => updateExtra(item.name, next)} />
        <StudioButton disabled={disabled} onClick={() => commit(removeDecisionInputs(values, [item.name]))}>Remove {item.name}</StudioButton>
      </div>
    )}</div>
      <StudioButton disabled={disabled} onClick={() => commit(removeDecisionInputs(values, extras.map(item => item.name)))}>Remove inputs not in this table</StudioButton>
    </fieldset> : null}
    <details className="decision-inputs-json">
      <summary>Advanced JSON</summary>
      <p className="decision-muted">Tagged values update the draft immediately. Kind and value stay exact text.</p>
      <textarea
        aria-label="Decision inputs JSON"
        aria-invalid={jsonError ? true : undefined}
        aria-describedby={jsonError ? jsonErrorId : undefined}
        disabled={disabled}
        spellCheck={false}
        value={jsonDraft}
        onFocus={() => { jsonFocused.current = true; }}
        onBlur={() => {
          jsonFocused.current = false;
          setJsonDraft(formatDecisionInputsJson(asDecisionValueMap(value)));
          setJsonError("");
        }}
        onChange={event => {
          const text = event.target.value;
          setJsonDraft(text);
          const parsedJson = parseDecisionInputsJson(text);
          if (!parsedJson.valid) { setJsonError(parsedJson.message); return; }
          setJsonError("");
          onChange(parsedJson.value);
        }}
      />
      {jsonError ? <p id={jsonErrorId} role="alert">{jsonError}</p> : null}
    </details>
  </div>;
}
