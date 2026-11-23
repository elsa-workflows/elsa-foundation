import { useStudioTimeZone } from "@elsa-workflows/studio-sdk";
import { formatStudioDate } from "@elsa-workflows/studio-sdk";
import React, { useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { SkeletonRows, TechnicalDetails, StudioTimestamp, StudioAlert, StudioButton, StudioTableContainer, StudioField, OverflowMenu, StudioResultState, StudioDialogFooter } from "@elsa-workflows/studio-ui";
import { useDialogFocus, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { can, decisionFailure, deleteCase, deleteRun, failureMessage, getRun, history, listCases, runCases, saveCase, type DecisionAsset, type DecisionCase, type DecisionValue, type DecisionVersion, type Diagnostic, type Scalar, type TestRun } from "./decisionsApi";
import { parseTable, type Clause } from "./decisionXml";
import { labelsForXml, RuleExplanation, type ExplanationLabels } from "./RuleExplanation";
import { DecisionDiagnostics } from "./DecisionDiagnostics";
export const initial = (type: string): Scalar => ({ kind: type === "number" ? "number" : type === "boolean" ? "boolean" : "string", value: type === "number" ? "0" : type === "boolean" ? "false" : "" });
const scalar = (value: unknown): value is Scalar => !!value && typeof value === "object" && "kind" in value && "value" in value;
export function displayValue(value: unknown): string {
  if (scalar(value)) return value.kind === "null" ? "null" : value.value ?? "";
  if (Array.isArray(value)) return `[${value.map(displayValue).join(", ")}]`;
  if (value && typeof value === "object") return Object.entries(value).map(([key, item]) => `${key}: ${displayValue(item)}`).join(", ");
  return value === null || value === undefined ? "Not retained" : String(value);
}
export function ValueInput({ clause, value, onChange, label, displayLabel = label, unset = false, compact = false }: { clause: Clause; value: Scalar; onChange(value: Scalar): void; label: string; displayLabel?: string; unset?: boolean; compact?: boolean }) {
  const missingId = useId();
  const kind = unset ? "unset" : value.kind === "null" ? "null" : clause.type;
  const confirm = (next: string) => {
    if (next === "unset") return;
    onChange(next === "null" ? { kind: "null", value: null } : initial(clause.type));
  };
  const kindControl = <select className={compact ? "decision-value-kind" : undefined} aria-label={`${label} value kind`} value={kind} onChange={event => confirm(event.target.value)}>
      {unset ? <option value="unset">Not set</option> : null}
      <option value={clause.type}>{clause.type === "number" ? "Number" : clause.type === "boolean" ? "Boolean" : "Text"}</option>
      <option value="null">Null</option>
    </select>;
  const valueControl = unset
      ? clause.type === "boolean"
        ? <select className={compact ? "decision-value-input" : undefined} aria-label={label} value="" aria-describedby={missingId} onChange={event => onChange({ kind: "boolean", value: event.target.value })}>
            <option value="" disabled>Not set</option>
            <option value="true">True</option>
            <option value="false">False</option>
          </select>
        : <input className={compact ? "decision-value-input" : undefined} aria-label={label} aria-describedby={missingId} placeholder="Not set" value="" inputMode={clause.type === "number" ? "decimal" : "text"} onChange={event => onChange({
            kind: clause.type === "number" ? "number" : "string",
            value: event.target.value
          })} />
      : value.kind === "boolean"
        ? <select className={compact ? "decision-value-input" : undefined} aria-label={label} value={value.value ?? "false"} onChange={event => onChange({ kind: "boolean", value: event.target.value })}><option value="true">True</option><option value="false">False</option></select>
        : <input className={compact ? "decision-value-input" : undefined} aria-label={label} disabled={value.kind === "null"} inputMode={clause.type === "number" ? "decimal" : "text"} value={value.value ?? ""} onChange={event => onChange({
            kind: clause.type === "number" ? "number" : "string",
            value: event.target.value
          })} />;
  return <div role="group" aria-label={`${label} controls`} className={`decision-value-field${unset ? " is-unset" : ""}${compact ? " is-compact" : ""}`}>
    {compact ? <span className="decision-value-name">{displayLabel}</span> : null}
    <div className="decision-value-controls">{compact ? <>{kindControl}{valueControl}</> : <><StudioField label={`${label} value kind`}>{kindControl}</StudioField><StudioField label={label}>{valueControl}</StudioField></>}</div>
    {unset ? <span id={missingId} className="decision-input-missing" role="status">{label} is not set. Runtime will refuse MissingInput until you enter or confirm a value.</span> : null}
  </div>;
}
function CaseEditorDialog({ title, busy, onClose, onRestoreFocus, children }: { title: string; busy: boolean; onClose(): void; onRestoreFocus(): void; children: React.ReactNode }) {
  useStudioTimeZone();
  const dialogRef = useRef<HTMLElement>(null), titleId = useId();
  useDialogFocus(dialogRef, busy ? null : onClose, true, onRestoreFocus);
  return <div className="decision-case-dialog-backdrop" role="presentation"><section ref={dialogRef} className="decision-case-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
    <header className="decision-case-dialog-heading"><h2 id={titleId}>{title}</h2></header>
    {children}
  </section></div>;
}
export function CasePanel({ api, asset, candidate, xml, dirty, onDraftStateChange, onReviewRules, outputSupplement }: { api: ElsaStudioModuleApi; asset: DecisionAsset; candidate?: DecisionVersion; xml: string; dirty: boolean; onDraftStateChange?(dirty: boolean, busy: boolean): void; onReviewRules?(diagnostics: Diagnostic[]): void; outputSupplement?: React.ReactNode }) {
  const [cases, setCases] = useState<DecisionCase[]>([]), [runs, setRuns] = useState<TestRun[]>([]), [run, setRun] = useState<TestRun | null>(null);
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [running, setRunning] = useState(false), [error, setError] = useState(""), [runFailure, setRunFailure] = useState<{ message: string; diagnostics: Diagnostic[] } | null>(null);
  const [editing, setEditing] = useState<DecisionCase | "new" | null>(null), [name, setName] = useState(""), [inputs, setInputs] = useState<Record<string, Scalar>>({});
  const [expected, setExpected] = useState<Record<string, Scalar>[]>([]), [status, setStatus] = useState("Success"), [sensitive, setSensitive] = useState(false);
  const opener = useRef<HTMLButtonElement | null>(null), caseTriggers = useRef(new Map<string, HTMLButtonElement>()), focusCaseId = useRef<string | null>(null), baseline = useRef("");
  const restoreOpener = useRef(false);
  const focusOpener = () => {
    const trigger = focusCaseId.current ? caseTriggers.current.get(focusCaseId.current) : null;
    (trigger ?? opener.current)?.focus();
  };
  useLayoutEffect(() => {
    if (editing === null && !busy && restoreOpener.current) {
      restoreOpener.current = false;
      focusOpener();
    }
  }, [editing, busy]);
  const caseChanged = editing !== null && baseline.current !== JSON.stringify({ name, inputs, expected, status, sensitive });
  useEffect(() => { onDraftStateChange?.(caseChanged, busy); }, [caseChanged, busy, onDraftStateChange]);
  useEffect(() => () => onDraftStateChange?.(false, false), [onDraftStateChange]);
  let table; try { table = parseTable(xml); } catch { table = null; }
  const alive = useRef(true); useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => { let current = true; setLoading(true); setCases([]); setRun(null); setRunFailure(null); setRuns([]); setEditing(null);
    void listCases(api, asset.id).then(items => { if (current) setCases(items); }, failure => { if (current) setError(failureMessage(failure)); }).finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [api, asset.id]);
  const work = async (action: () => Promise<void>) => { if (busy) return; setBusy(true); setError(""); try { await action(); } catch (failure) { if (alive.current) setError(failureMessage(failure)); } finally { if (alive.current) setBusy(false); } };
  const runSavedCases = async () => {
    if (busy) return;
    setBusy(true); setRunning(true); setError(""); setRunFailure(null); setRun(null);
    try { const result = await runCases(api, asset, cases, candidate); if (alive.current) setRun(result); }
    catch (failure) { if (alive.current) setRunFailure(decisionFailure(failure)); }
    finally { if (alive.current) { setRunning(false); setBusy(false); } }
  };
  const close = () => { restoreOpener.current = true; setError(""); setEditing(null); };
  const edit = (item: DecisionCase | "new", button: HTMLButtonElement) => {
    if (!table || busy || caseChanged) return; restoreOpener.current = false; opener.current = button; focusCaseId.current = item === "new" ? null : item.id; setError("");
    const nextName = item === "new" ? "" : item.name, nextStatus = item === "new" ? "Success" : item.expectedStatus, nextSensitive = item === "new" ? asset.isSensitive : item.isSensitive ?? false;
    const nextInputs = item === "new" ? Object.fromEntries(table.inputs.map(clause => [clause.name, initial(clause.type)])) : item.inputs ?? {};
    const values: DecisionValue[] = item === "new" || item.expectedResult === null ? [] : Array.isArray(item.expectedResult) ? item.expectedResult : [item.expectedResult];
    const nextExpected = values.length ? values.map(value => Object.fromEntries(table!.outputs.map(clause => [clause.name, scalar(value) ? value : (value as Record<string, Scalar>)[clause.name] ?? initial(clause.type)]))) : [Object.fromEntries(table.outputs.map(clause => [clause.name, initial(clause.type)]))];
    baseline.current = JSON.stringify({ name: nextName, inputs: nextInputs, expected: nextExpected, status: nextStatus, sensitive: nextSensitive });
    setEditing(item); setName(nextName); setStatus(nextStatus); setSensitive(nextSensitive); setInputs(nextInputs); setExpected(nextExpected);
  };
  const submit = (event: React.FormEvent) => { event.preventDefault(); if (!table || !editing) return; void work(async () => {
    const resultRows = expected.map(row => table.outputs.length === 1 ? row[table.outputs[0].name] : row);
    const result: DecisionValue | null = status === "NoMatch" ? { kind: "null", value: null } : status !== "Success" ? null : ["RULE ORDER", "COLLECT"].includes(table.hitPolicy) ? resultRows : resultRows[0] ?? null;
    await saveCase(api, asset.id, { name: name.trim(), inputs, expectedStatus: status, expectedResult: result, isSensitive: sensitive }, editing === "new" ? undefined : editing);
    const next = await listCases(api, asset.id); if (alive.current) { setCases(next); close(); }
  }); };
  return <section className="decision-test-workspace" aria-label="Decision tests">
    <div className="decision-test-cases">
      <div className="decision-section-heading"><div><h2 id="decision-cases-title">Saved cases</h2><p className="decision-muted">Expected results for this {candidate ? `published version ${candidate.number}` : "saved draft"}.</p></div><div className="decision-actions">
        <StudioButton disabled={busy || caseChanged || !table || !can(api, "decisions:manage")} onClick={event => edit("new", event.currentTarget)}>Add case</StudioButton>
        <StudioButton variant="primary" disabled={busy || loading || dirty || caseChanged || !cases.length} onClick={() => void runSavedCases()}>{running ? "Running…" : "Run saved cases"}</StudioButton>
      </div></div>
      {dirty ? <p className="decision-muted">Save the draft before running cases so the tested content is exact.</p> : null}
      {error && !editing ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
      {!table ? <p className="decision-muted">Table source is needed to edit case inputs. You can still run saved cases with your existing test permission.</p> : null}
      {loading ? <SkeletonRows rows={3} label="Loading cases" /> : !cases.length ? <p className="decision-muted">No saved cases yet. Add representative inputs and expected results before changing a policy.</p> : <StudioTableContainer role="region" aria-label="Saved decision cases table" tabIndex={0}><table className="studio-table decision-case-table"><thead><tr><th scope="col">Case</th><th scope="col">Expected outcome</th><th scope="col">Actions</th></tr></thead><tbody>{cases.map(item => <tr key={item.id}><th scope="row">{item.name}</th><td>{item.expectedStatus}</td><td><div className="decision-case-actions" ref={element => { const trigger = element?.querySelector<HTMLButtonElement>(".studio-overflow-menu-trigger"); if (trigger) caseTriggers.current.set(item.id, trigger); else caseTriggers.current.delete(item.id); }}><OverflowMenu ariaLabel={`Actions for ${item.name}`} items={[
        { id: "edit", label: "Edit", disabled: busy || caseChanged || !table || !item.inputs || !can(api, "decisions:manage"), onSelect: () => { const trigger = caseTriggers.current.get(item.id); if (trigger) edit(item, trigger); } },
        { id: "delete", label: "Delete", destructive: true, disabled: busy || !can(api, "decisions:manage"), onSelect: () => void work(async () => { if (!await api.dialogs.confirm({ title: "Delete case", message: `Delete “${item.name}”?`, confirmLabel: "Delete", tone: "danger" })) return; await deleteCase(api, asset.id, item); if (alive.current) setCases(await listCases(api, asset.id)); }) }
      ]} /></div>{!item.inputs ? <span className="decision-muted">Protected inputs are unavailable.</span> : null}</td></tr>)}</tbody></table></StudioTableContainer>}
      {editing && table ? <CaseEditorDialog title={editing === "new" ? "New case" : "Edit case"} busy={busy} onClose={close} onRestoreFocus={focusOpener}><form className="decision-case-form" onSubmit={submit} aria-busy={busy} aria-label={editing === "new" ? "New decision case" : "Edit decision case"}>
        {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
        <fieldset disabled={busy} className="decision-case-form-fields"><legend className="decision-visually-hidden">Case details and expectations</legend><StudioField label="Case name"><input data-dialog-initial-focus required value={name} onChange={event => setName(event.target.value)} /></StudioField>
        <fieldset><legend>Inputs</legend><div className="decision-case-value-grid">{table.inputs.map(clause => <ValueInput key={clause.id} clause={clause} label={`Case input ${clause.name}`} displayLabel={clause.name} compact value={inputs[clause.name] ?? initial(clause.type)} onChange={value => setInputs(previous => ({ ...previous, [clause.name]: value }))} />)}</div></fieldset>
        <StudioField label="Expected outcome"><select value={status} onChange={event => setStatus(event.target.value)}><option>Success</option><option>NoMatch</option><option>HitPolicyViolation</option><option>InvalidInput</option></select></StudioField>
        {status === "Success" ? <fieldset><legend>Expected result</legend>{expected.map((row, rowIndex) => <div className="decision-form-grid" key={rowIndex}><div className="decision-case-value-grid">{table.outputs.map(clause => <ValueInput key={clause.id} clause={clause} label={`Expected ${clause.name}${expected.length > 1 ? `, result ${rowIndex + 1}` : ""}`} displayLabel={clause.name} compact value={row[clause.name] ?? initial(clause.type)} onChange={value => setExpected(previous => previous.map((item, index) => index === rowIndex ? { ...item, [clause.name]: value } : item))} />)}</div>{["RULE ORDER", "COLLECT"].includes(table.hitPolicy) ? <StudioButton onClick={() => setExpected(previous => previous.filter((_, index) => index !== rowIndex))}>Remove expected result {rowIndex + 1}</StudioButton> : null}</div>)}{["RULE ORDER", "COLLECT"].includes(table.hitPolicy) ? <StudioButton onClick={() => setExpected(previous => [...previous, Object.fromEntries(table!.outputs.map(clause => [clause.name, initial(clause.type)]))])}>Add expected result</StudioButton> : null}</fieldset> : null}
        <label className="decision-checkbox"><input type="checkbox" checked={sensitive} onChange={event => setSensitive(event.target.checked)} /> Protect saved case values</label></fieldset>
        <StudioDialogFooter onCancel={close} cancelDisabled={busy} confirmLabel="Save case" confirmDisabled={busy || !name.trim()} />
      </form></CaseEditorDialog> : null}
    </div>
    <aside className="decision-test-output" aria-label="Test output">
      {outputSupplement}
      <section className="decision-test-latest" aria-label="Latest saved-case run" aria-live="polite" aria-busy={running}>
        {running ? <StudioResultState className="decision-output-state" title="Running saved cases" description="Comparing the saved cases with this exact candidate." />
          : runFailure ? <div className="decision-case-run-failure">
            <div className="decision-case-run-failure-summary"><h3>Saved cases could not run</h3><StudioAlert tone="danger">{runFailure.message}</StudioAlert></div>
            {runFailure.diagnostics.length ? <DecisionDiagnostics diagnostics={runFailure.diagnostics} table={table} label="Decision compilation diagnostics" onReview={item => onReviewRules?.([item])} /> : null}
            {runFailure.diagnostics.length ? <div className="decision-case-run-recovery"><p>{candidate ? "Review the affected cells in Rules. Clone this version to a draft, correct the expressions, then run the cases again." : "Review the affected cells in Rules. Correct the expressions, save the draft, then run the cases again."}</p><StudioButton onClick={() => onReviewRules?.(runFailure.diagnostics)}>Review rules</StudioButton></div> : null}
          </div>
          : run ? <TestResult run={run} changed={run.candidateHash !== (candidate?.sourceHash ?? asset.sourceHash) || dirty} labels={run.candidateHash === (candidate?.sourceHash ?? asset.sourceHash) && !dirty ? labelsForXml(xml) : undefined} />
            : <StudioResultState className="decision-output-state" title="No recent run" description="Run saved cases to compare their expected outcomes with this decision." />}
      </section>
      {can(api, "decisions:history") ? <details className="decision-history"><summary>Test history{runs.length ? ` · ${runs.length}` : ""}</summary><div className="decision-history-content"><StudioButton disabled={busy} onClick={() => void work(async () => { const items = await history(api, asset.id); if (alive.current) setRuns(items); })}>Load test history</StudioButton>{runs.map(item => <div className="decision-history-row" key={item.id}><span>{item.passed ? "Passed" : "Failed"} · <StudioTimestamp value={item.completedAt} /></span><StudioButton disabled={busy} onClick={() => void work(async () => { const receipt = await getRun(api, asset.id, item.id); if (alive.current) setRun(receipt); })}>Inspect run {item.id}</StudioButton>{can(api, "decisions:manage") ? <StudioButton disabled={busy} onClick={() => void work(async () => { if (!await api.dialogs.confirm({ title: "Delete test history", message: "Delete this completed test receipt and retained details?", confirmLabel: "Delete", tone: "danger" })) return; await deleteRun(api, asset.id, item.id); if (alive.current) { setRuns(previous => previous.filter(row => row.id !== item.id)); if (run?.id === item.id) setRun(null); } })}>Delete run {item.id}</StudioButton> : null}</div>)}</div></details> : null}
    </aside>
  </section>;
}
export function TestResult({ run, changed, labels }: { run: TestRun; changed: boolean; labels?: ExplanationLabels }) {
  return <section className="decision-test-result" aria-label="Decision test results"><h3>{run.passed ? "All cases passed" : "Cases failed"}</h3>{changed ? <p>These results belong to a different saved candidate.</p> : null}<TechnicalDetails label="Tested candidate"><dl><dt>Candidate</dt><dd><code>{run.candidateId}</code></dd><dt>Content identity</dt><dd><code>{run.candidateHash}</code></dd></dl></TechnicalDetails>
    {!run.hasDetailedPayload ? <p className="decision-muted">Detailed values were not retained or are unavailable with your permissions.</p> : null}
    {run.comparisons?.map(item => <article className="decision-comparison" key={item.caseId}><h4>{item.name} · {item.passed ? "Passed" : "Failed"}</h4><p>{item.status}{item.failurePath ? ` at ${item.failurePath}` : ""}</p>{item.expected !== undefined && item.expected !== null ? <p>Expected: <output>{displayValue(item.expected)}</output></p> : null}{item.actual !== undefined && item.actual !== null ? <p>Actual: <output>{displayValue(item.actual)}</output></p> : null}{item.trace?.length ? <RuleExplanation trace={item.trace} labels={labels} /> : item.matchedRules?.length ? <TechnicalDetails label="Rule identities"><p>Matched: {item.matchedRules.join(", ")}</p>{item.selectedRules?.length ? <p>Selected: {item.selectedRules.join(", ")}</p> : null}</TechnicalDetails> : null}</article>)}
  </section>;
}
