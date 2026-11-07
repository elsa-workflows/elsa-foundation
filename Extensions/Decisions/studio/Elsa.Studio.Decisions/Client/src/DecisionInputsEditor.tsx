import React, { useEffect, useMemo, useRef, useState } from "react";
import type { ElsaStudioModuleApi, StudioExpressionEditorContribution, StudioExpressionEditorProps } from "@elsa-workflows/studio-sdk";
import { StudioAlert } from "@elsa-workflows/studio-ui";
import { initial, ValueInput } from "./CasePanel";
import { decisionsCapability, failureMessage, getDecision, version, type Scalar } from "./decisionsApi";
import {
  asDecisionValueMap,
  decisionInputsEditorId,
  extraDecisionInputs,
  formatDecisionInputsJson,
  isDecisionInputsContext,
  parseDecisionInputsJson,
  parseDecisionPin,
  readDecisionPin,
  seedDecisionInputs
} from "./decisionInputs";
import { parseTable, type Table } from "./decisionXml";

export function createDecisionInputsEditorContribution(api: ElsaStudioModuleApi): StudioExpressionEditorContribution {
  return {
    id: decisionInputsEditorId,
    requiresBackend: decisionsCapability,
    order: 40,
    supports: isDecisionInputsContext,
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
  onChange
}: StudioExpressionEditorProps & { api: ElsaStudioModuleApi }) {
  const pin = readDecisionPin(context.activity);
  const parsed = parseDecisionPin(pin);
  const [table, setTable] = useState<Table | null>(null);
  const [loading, setLoading] = useState(!!parsed);
  const [error, setError] = useState("");
  const [unpublished, setUnpublished] = useState(false);
  const values = asDecisionValueMap(value);
  const extras = extraDecisionInputs(values, table?.inputs ?? []);
  const jsonExternal = useMemo(() => formatDecisionInputsJson(values), [values]);
  const [jsonDraft, setJsonDraft] = useState(jsonExternal);
  const [jsonError, setJsonError] = useState("");
  const jsonFocused = useRef(false);
  const lastSeed = useRef("");

  useEffect(() => {
    if (jsonFocused.current) return;
    setJsonDraft(jsonExternal);
    setJsonError("");
  }, [jsonExternal]);

  useEffect(() => {
    const selection = parseDecisionPin(pin);
    if (!selection) {
      setTable(null);
      setLoading(false);
      setError("");
      setUnpublished(false);
      return;
    }
    let active = true;
    setLoading(true);
    setError("");
    void loadTable(api, selection.assetId, selection.selection).then(result => {
      if (!active) return;
      setTable(result.table);
      setUnpublished(result.unpublished);
      setError(result.error);
    }, failure => {
      if (!active) return;
      setTable(null);
      setUnpublished(false);
      setError(failureMessage(failure));
    }).finally(() => {
      if (active) setLoading(false);
    });
    return () => { active = false; };
  }, [api, pin]);

  useEffect(() => {
    if (!table || disabled) return;
    const seeded = seedDecisionInputs(value, table.inputs);
    if (!seeded) return;
    const signature = JSON.stringify(seeded);
    if (lastSeed.current === signature) return;
    lastSeed.current = signature;
    onChange(seeded);
  }, [disabled, onChange, table, value]);

  const update = (name: string, next: Scalar) => onChange({ ...values, [name]: next });

  return <div className="decision-inputs-editor">
    {!parsed ? <p className="decision-muted">Select a decision to load its input fields from the table. JSON stays available below.</p> : null}
    {loading ? <p role="status">Loading input fields…</p> : null}
    {unpublished ? <StudioAlert tone="warning">This decision has no published version. Publish it before Review &amp; publish; these fields follow the current draft table.</StudioAlert> : null}
    {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
    {table ? <fieldset disabled={disabled} className="decision-evaluate-fields"><legend className="decision-visually-hidden">Decision table inputs</legend><div className="decision-form-grid">{table.inputs.map(clause =>
      <ValueInput key={clause.id} clause={clause} label={clause.name} value={values[clause.name] ?? initial(clause.type)} onChange={next => update(clause.name, next)} />
    )}</div></fieldset> : null}
    {extras.length ? <fieldset disabled={disabled} className="decision-evaluate-fields"><legend className="decision-visually-hidden">Additional decision inputs</legend><div className="decision-form-grid">{extras.map(item =>
      <ValueInput key={item.name} clause={{ id: item.name, name: item.name, type: item.value.kind === "number" ? "number" : item.value.kind === "boolean" ? "boolean" : "string" }} label={`${item.name} (not in this table)`} value={item.value} onChange={next => update(item.name, next)} />
    )}</div></fieldset> : null}
    <details className="decision-inputs-json">
      <summary>Advanced JSON</summary>
      <p className="decision-muted">Tagged values update the draft immediately. Kind and value stay exact text.</p>
      <textarea
        aria-label="Decision inputs JSON"
        disabled={disabled}
        spellCheck={false}
        value={jsonDraft}
        onFocus={() => { jsonFocused.current = true; }}
        onBlur={() => { jsonFocused.current = false; setJsonDraft(formatDecisionInputsJson(asDecisionValueMap(value))); }}
        onChange={event => {
          const text = event.target.value;
          setJsonDraft(text);
          const parsedJson = parseDecisionInputsJson(text);
          if (!parsedJson.valid) { setJsonError(parsedJson.message); return; }
          setJsonError("");
          onChange(parsedJson.value);
        }}
      />
      {jsonError ? <p role="status">{jsonError}</p> : null}
    </details>
  </div>;
}

async function loadTable(api: ElsaStudioModuleApi, assetId: string, selection: string) {
  if (selection === "latest") {
    const asset = await getDecision(api, assetId);
    if (asset.publishedVersionId) {
      const published = await version(api, assetId, asset.publishedVersionId);
      return readLoadedTable(published.xml, false, "The published table source is unavailable.");
    }
    return readLoadedTable(asset.xml, true, "The draft table source is unavailable.");
  }
  const published = await version(api, assetId, selection);
  return readLoadedTable(published.xml, false, "The published table source is unavailable.");
}

function readLoadedTable(xml: string | null | undefined, unpublished: boolean, missing: string) {
  if (!xml) return { table: null, unpublished, error: missing };
  const table = parseXml(xml);
  return { table, unpublished, error: table ? "" : "The selected decision's table could not be read. JSON editing remains available." };
}

function parseXml(xml: string | null | undefined): Table | null {
  if (!xml) return null;
  try { return parseTable(xml); }
  catch { return null; }
}
