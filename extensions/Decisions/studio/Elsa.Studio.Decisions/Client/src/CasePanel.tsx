import React, { useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { CheckCircle2, Circle, Copy, Pencil, Play, RotateCcw, Trash2, XCircle } from "lucide-react";
import { SkeletonRows, TechnicalDetails, StudioTimestamp, StudioAlert, StudioButton, StudioTableContainer, StudioField, OverflowMenu, StudioResultState, StudioDialogFooter, StatusChip, StatusPill, StudioSearchInput, StudioToggleButton, PeerViewSwitch } from "@elsa-workflows/studio-ui";
import { useDialogFocus, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { can, decisionFailure, deleteCase, deleteRun, failureMessage, getRun, history, listCases, runCases, saveCase, type Comparison, type DecisionAsset, type DecisionCase, type DecisionValue, type DecisionVersion, type Diagnostic, type Scalar, type TestRun } from "./decisionsApi";
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
  const dialogRef = useRef<HTMLElement>(null), titleId = useId();
  useDialogFocus(dialogRef, busy ? null : onClose, true, onRestoreFocus);
  return <div className="decision-case-dialog-backdrop" role="presentation"><section ref={dialogRef} className="decision-case-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
    <header className="decision-case-dialog-heading"><h2 id={titleId}>{title}</h2></header>
    {children}
  </section></div>;
}
interface CaseEntry { id: string; name: string; saved?: DecisionCase; comparison?: Comparison }
type CaseFilter = "all" | "failed" | "passed";
function verdictSummary(comparison: Comparison, expectedStatus?: string): { title: string; detail: string | null } {
  if (comparison.passed) return { title: `Outcome matched (${comparison.status})`, detail: null };
  if (comparison.failurePath === "$.status") {
    const title = expectedStatus ? `Outcome mismatch: expected ${expectedStatus}, got ${comparison.status}` : `Outcome mismatch: got ${comparison.status} at $.status`;
    const detail = comparison.status === "HitPolicyViolation"
      ? "Multiple rules matched under a hit policy that allows at most one."
      : comparison.status === "NoMatch"
        ? "No rule matched the supplied inputs."
        : comparison.status === "InvalidInput"
          ? "One or more supplied inputs did not match the table's expected types."
          : null;
    return { title, detail };
  }
  if (comparison.failurePath) {
    return {
      title: `Output mismatch at ${comparison.failurePath}`,
      detail: `Evaluation returned ${comparison.status}, but the output value did not match the expected result.`
    };
  }
  return { title: `Failed (${comparison.status})`, detail: null };
}
function caseRowHint(comparison?: Comparison, saved?: DecisionCase): string | null {
  if (comparison) {
    if (comparison.passed) {
      if (comparison.actual !== undefined && comparison.actual !== null) return displayValue(comparison.actual);
      return comparison.status;
    }
    if (comparison.failurePath === "$.status") {
      return saved ? `Expected ${saved.expectedStatus}, got ${comparison.status}` : comparison.status;
    }
    if (comparison.expected !== undefined && comparison.expected !== null && comparison.actual !== undefined && comparison.actual !== null) {
      return `Expected ${displayValue(comparison.expected)}, got ${displayValue(comparison.actual)}`;
    }
    return `${comparison.status}${comparison.failurePath ? ` at ${comparison.failurePath}` : ""}`;
  }
  if (saved && !saved.inputs) return "Protected inputs are unavailable.";
  if (saved && saved.expectedStatus === "Success" && saved.expectedResult !== null) return `→ ${displayValue(saved.expectedResult)}`;
  return null;
}
function CaseInputsSummary({ inputs }: { inputs: Record<string, Scalar> | null | undefined }) {
  const entries = inputs ? Object.entries(inputs) : [];
  if (!entries.length) return null;
  return <div className="decision-inspector-section">
    <h5 className="decision-inspector-label">Inputs</h5>
    <dl className="decision-inspector-inputs">
      {entries.map(([key, value]) => <div className="decision-inspector-input-item" key={key}>
        <dt>{key}</dt>
        <dd><code>{displayValue(value)}</code></dd>
      </div>)}
    </dl>
  </div>;
}
export function CasePanel({ api, asset, candidate, xml, dirty, onDraftStateChange, onReviewRules, outputSupplement }: { api: ElsaStudioModuleApi; asset: DecisionAsset; candidate?: DecisionVersion; xml: string; dirty: boolean; onDraftStateChange?(dirty: boolean, busy: boolean): void; onReviewRules?(diagnostics: Diagnostic[]): void; outputSupplement?: React.ReactNode }) {
  const [cases, setCases] = useState<DecisionCase[]>([]), [runs, setRuns] = useState<TestRun[]>([]), [run, setRun] = useState<TestRun | null>(null);
  const [historicalRun, setHistoricalRun] = useState(false);
  const [selectedCaseId, setSelectedCaseId] = useState<string | null>(null);
  const [search, setSearch] = useState(""), [statusFilter, setStatusFilter] = useState<CaseFilter>("all");
  const [rightView, setRightView] = useState<"evaluate" | "inspector">(() => outputSupplement ? "evaluate" : "inspector");
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [running, setRunning] = useState(false), [error, setError] = useState(""), [runFailure, setRunFailure] = useState<{ message: string; diagnostics: Diagnostic[] } | null>(null);
  const [editing, setEditing] = useState<DecisionCase | "new" | null>(null), [name, setName] = useState(""), [inputs, setInputs] = useState<Record<string, Scalar>>({});
  const [expected, setExpected] = useState<Record<string, Scalar>[]>([]), [status, setStatus] = useState("Success"), [sensitive, setSensitive] = useState(false);
  const opener = useRef<HTMLButtonElement | null>(null), caseTriggers = useRef(new Map<string, HTMLButtonElement>()), caseSelectButtons = useRef(new Map<string, HTMLButtonElement>()), focusCaseId = useRef<string | null>(null), baseline = useRef("");
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
  useEffect(() => {
    if (!outputSupplement) setRightView("inspector");
  }, [outputSupplement]);
  let table; try { table = parseTable(xml); } catch { table = null; }
  const alive = useRef(true); useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => { let current = true; setLoading(true); setCases([]); setRun(null); setHistoricalRun(false); setRunFailure(null); setRuns([]); setEditing(null); setSelectedCaseId(null); setSearch(""); setStatusFilter("all");
    void listCases(api, asset.id).then(items => { if (current) { setCases(items); setSelectedCaseId(items[0]?.id ?? null); } }, failure => { if (current) setError(failureMessage(failure)); }).finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [api, asset.id]);
  const work = async (action: () => Promise<void>) => { if (busy) return; setBusy(true); setError(""); try { await action(); } catch (failure) { if (alive.current) setError(failureMessage(failure)); } finally { if (alive.current) setBusy(false); } };
  const comparisonByCaseId = useMemo(() => new Map((run?.comparisons ?? []).map(item => [item.caseId, item])), [run]);
  const entries = useMemo<CaseEntry[]>(() => {
    const list: CaseEntry[] = cases.map(item => ({ id: item.id, name: item.name, saved: item, comparison: comparisonByCaseId.get(item.id) }));
    const seen = new Set(cases.map(item => item.id));
    for (const comparison of run?.comparisons ?? []) {
      if (!seen.has(comparison.caseId)) list.push({ id: comparison.caseId, name: comparison.name, comparison });
    }
    return list;
  }, [cases, comparisonByCaseId, run]);
  const runComparisons = useMemo(() => run?.comparisons ?? [], [run]);
  const hasComparisons = runComparisons.length > 0;
  const passedCount = useMemo(() => runComparisons.filter(item => item.passed).length, [runComparisons]);
  const failedCount = useMemo(() => runComparisons.filter(item => !item.passed).length, [runComparisons]);
  const failedCases = useMemo(() => cases.filter(item => {
    const comparison = comparisonByCaseId.get(item.id);
    return comparison !== undefined && !comparison.passed;
  }), [cases, comparisonByCaseId]);
  const filteredEntries = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    const matching = entries.filter(entry => {
      if (query && !entry.name.toLocaleLowerCase().includes(query)) return false;
      if (statusFilter === "failed") return entry.comparison !== undefined && !entry.comparison.passed;
      if (statusFilter === "passed") return entry.comparison !== undefined && entry.comparison.passed;
      return true;
    });
    if (statusFilter === "all" && failedCount > 0) {
      return matching.map((entry, index) => ({
        entry,
        index,
        rank: entry.comparison ? (entry.comparison.passed ? 1 : 0) : 2
      })).sort((a, b) => a.rank - b.rank || a.index - b.index).map(item => item.entry);
    }
    return matching;
  }, [entries, search, statusFilter, failedCount]);
  useEffect(() => {
    if (!entries.length) {
      if (selectedCaseId !== null) setSelectedCaseId(null);
      return;
    }
    if (!selectedCaseId || !entries.some(entry => entry.id === selectedCaseId)) {
      setSelectedCaseId(filteredEntries[0]?.id ?? entries[0].id);
    }
  }, [entries, filteredEntries, selectedCaseId]);
  const activeEntry = useMemo(() => entries.find(entry => entry.id === selectedCaseId) ?? filteredEntries[0] ?? entries[0] ?? null, [entries, filteredEntries, selectedCaseId]);
  const executeCases = async (targetCases: DecisionCase[], mode: "all" | "single" | "failed") => {
    if (busy || !targetCases.length) return;
    setBusy(true); setRunning(true); setError(""); setRunFailure(null);
    if (mode === "all") { setRun(null); setHistoricalRun(false); }
    if (outputSupplement) setRightView("inspector");
    try {
      const result = await runCases(api, asset, targetCases, candidate);
      if (!alive.current) return;
      let nextRun: TestRun = result;
      if (mode !== "all" && run && !historicalRun && run.candidateHash === result.candidateHash && run.hasDetailedPayload === result.hasDetailedPayload && (run.comparisons?.length ?? 0) > 0) {
        const incoming = new Map((result.comparisons ?? []).map(item => [item.caseId, item]));
        const merged = (run.comparisons ?? []).map(item => incoming.get(item.caseId) ?? item);
        for (const item of result.comparisons ?? []) {
          if (!merged.some(existing => existing.caseId === item.caseId)) merged.push(item);
        }
        nextRun = {
          ...result,
          comparisons: merged,
          passed: merged.length ? merged.every(item => item.passed) : result.passed
        };
      }
      setRun(nextRun);
      setHistoricalRun(false);
      const nextComparisons = nextRun.comparisons ?? [];
      if (mode === "single") {
        setSelectedCaseId(targetCases[0].id);
      } else {
        const firstFailed = nextComparisons.find(item => !item.passed);
        if (firstFailed) setSelectedCaseId(firstFailed.caseId);
        else if (!selectedCaseId || !cases.some(item => item.id === selectedCaseId)) setSelectedCaseId(nextComparisons[0]?.caseId ?? cases[0]?.id ?? null);
      }
      if (statusFilter === "failed" && !nextComparisons.some(item => !item.passed)) setStatusFilter("all");
    }
    catch (failure) { if (alive.current) { setRun(null); setHistoricalRun(false); setRunFailure(decisionFailure(failure)); } }
    finally { if (alive.current) { setRunning(false); setBusy(false); } }
  };
  const close = () => { restoreOpener.current = true; setError(""); setEditing(null); };
  const edit = (item: DecisionCase | "new", button: HTMLButtonElement, options?: { restoreToCaseTrigger?: boolean; duplicateFrom?: DecisionCase }) => {
    if (!table || busy || caseChanged) return;
    restoreOpener.current = false; opener.current = button;
    const source = options?.duplicateFrom ?? (item === "new" ? null : item);
    focusCaseId.current = options?.restoreToCaseTrigger === false ? null : source ? source.id : null;
    setError("");
    const nextName = item === "new" ? (source ? `${source.name} copy` : "") : item.name;
    const nextStatus = source ? source.expectedStatus : "Success";
    const nextSensitive = source ? source.isSensitive ?? false : asset.isSensitive;
    const nextInputs = source ? source.inputs ?? {} : Object.fromEntries(table.inputs.map(clause => [clause.name, initial(clause.type)]));
    const values: DecisionValue[] = !source || source.expectedResult === null ? [] : Array.isArray(source.expectedResult) ? source.expectedResult : [source.expectedResult];
    const nextExpected = values.length ? values.map(value => Object.fromEntries(table!.outputs.map(clause => [clause.name, scalar(value) ? value : (value as Record<string, Scalar>)[clause.name] ?? initial(clause.type)]))) : [Object.fromEntries(table.outputs.map(clause => [clause.name, initial(clause.type)]))];
    baseline.current = JSON.stringify({ name: nextName, inputs: nextInputs, expected: nextExpected, status: nextStatus, sensitive: nextSensitive });
    setEditing(item); setName(nextName); setStatus(nextStatus); setSensitive(nextSensitive); setInputs(nextInputs); setExpected(nextExpected);
  };
  const submit = (event: React.FormEvent) => { event.preventDefault(); if (!table || !editing) return; void work(async () => {
    const resultRows = expected.map(row => table.outputs.length === 1 ? row[table.outputs[0].name] : row);
    const result: DecisionValue | null = status === "NoMatch" ? { kind: "null", value: null } : status !== "Success" ? null : ["RULE ORDER", "COLLECT"].includes(table.hitPolicy) ? resultRows : resultRows[0] ?? null;
    const saved = await saveCase(api, asset.id, { name: name.trim(), inputs, expectedStatus: status, expectedResult: result, isSensitive: sensitive }, editing === "new" ? undefined : editing);
    const next = await listCases(api, asset.id);
    if (alive.current) {
      setCases(next);
      if (saved?.id) setSelectedCaseId(saved.id);
      close();
    }
  }); };
  const selectEntry = (id: string) => {
    setSelectedCaseId(id);
    if (outputSupplement) setRightView("inspector");
  };
  const onCaseListKeyDown = (event: React.KeyboardEvent<HTMLButtonElement>, currentIndex: number) => {
    if (!filteredEntries.length) return;
    let targetIndex: number | null = null;
    if (event.key === "ArrowDown") targetIndex = Math.min(filteredEntries.length - 1, currentIndex + 1);
    else if (event.key === "ArrowUp") targetIndex = Math.max(0, currentIndex - 1);
    else if (event.key === "Home") targetIndex = 0;
    else if (event.key === "End") targetIndex = filteredEntries.length - 1;
    if (targetIndex === null || targetIndex === currentIndex) return;
    event.preventDefault();
    const nextEntry = filteredEntries[targetIndex];
    selectEntry(nextEntry.id);
    caseSelectButtons.current.get(nextEntry.id)?.focus();
  };
  const expectedHash = candidate?.sourceHash ?? asset.sourceHash;
  const runChanged = !!run && (run.candidateHash !== expectedHash || dirty);
  const runLabels = run && run.candidateHash === expectedHash && !dirty ? labelsForXml(xml) : undefined;
  const canRunCases = !busy && !loading && !dirty && !caseChanged;
  const canManage = can(api, "decisions:manage");
  return <section className="decision-test-workspace" aria-label="Decision tests">
    <div className="decision-test-cases">
      <div className="decision-section-heading"><div><h2 id="decision-cases-title">Saved cases</h2><p className="decision-muted">Expected results for this {candidate ? `published version ${candidate.number}` : "saved draft"}.</p></div><div className="decision-actions">
        <StudioButton disabled={busy || caseChanged || !table || !canManage} onClick={event => edit("new", event.currentTarget)}>Add case</StudioButton>
        <StudioButton variant="primary" disabled={!canRunCases || !cases.length} onClick={() => void executeCases(cases, "all")}>{running ? "Running…" : "Run saved cases"}</StudioButton>
      </div></div>
      {dirty ? <p className="decision-muted">Save the draft before running cases so the tested content is exact.</p> : null}
      {error && !editing ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
      {!table ? <p className="decision-muted">Table source is needed to edit case inputs. You can still run saved cases with your existing test permission.</p> : null}
      {!loading && entries.length > 0 ? <div className="decision-case-toolbar">
        <StudioSearchInput aria-label="Filter saved cases" placeholder="Filter cases…" value={search} onChange={event => setSearch(event.target.value)} />
        {hasComparisons ? <div className="decision-case-filters" role="group" aria-label="Filter cases by result">
          <StudioToggleButton size="sm" pressed={statusFilter === "all"} onClick={() => setStatusFilter("all")}>All ({entries.length})</StudioToggleButton>
          <StudioToggleButton size="sm" pressed={statusFilter === "failed"} onClick={() => setStatusFilter("failed")}>Failed ({failedCount})</StudioToggleButton>
          <StudioToggleButton size="sm" pressed={statusFilter === "passed"} onClick={() => setStatusFilter("passed")}>Passed ({passedCount})</StudioToggleButton>
        </div> : null}
      </div> : null}
      {loading ? <SkeletonRows rows={3} label="Loading cases" /> : !entries.length ? <p className="decision-muted">No saved cases yet. Add representative inputs and expected results before changing a policy.</p>
        : !filteredEntries.length ? <div className="decision-case-filter-empty"><p className="decision-muted">No cases match the current filter.</p><StudioButton size="sm" onClick={() => { setSearch(""); setStatusFilter("all"); }}>Clear filters</StudioButton></div>
        : <StudioTableContainer role="region" aria-label="Saved decision cases table" tabIndex={0}><table className="studio-table decision-case-table decision-case-list"><thead><tr><th scope="col">Case</th><th scope="col">Status</th><th scope="col">Actions</th></tr></thead><tbody>{filteredEntries.map((entry, index) => {
          const item = entry.saved;
          const comparison = entry.comparison;
          const isSelected = activeEntry?.id === entry.id;
          const hint = caseRowHint(comparison, item);
          const statusTone = comparison ? (comparison.passed ? "success" : "danger") : "neutral";
          const statusLabel = comparison ? (comparison.passed ? "Passed" : "Failed") : (item?.expectedStatus ?? "Historical");
          return <tr key={entry.id} className="decision-case-row" data-selected={isSelected || undefined} data-result={comparison ? (comparison.passed ? "passed" : "failed") : undefined} data-stale={comparison && runChanged ? "true" : undefined} onClick={event => {
            const target = event.target as HTMLElement;
            if (target.closest(".decision-case-actions") || target.closest('[role="menu"]')) return;
            selectEntry(entry.id);
          }}>
            <th scope="row">
              <button
                type="button"
                ref={element => { if (element) caseSelectButtons.current.set(entry.id, element); else caseSelectButtons.current.delete(entry.id); }}
                className="decision-case-select"
                aria-current={isSelected ? "true" : undefined}
                onClick={() => selectEntry(entry.id)}
                onKeyDown={event => onCaseListKeyDown(event, index)}
              >
                <span className="decision-case-status-icon" data-tone={statusTone} aria-hidden="true">
                  {comparison ? (comparison.passed ? <CheckCircle2 size={14} /> : <XCircle size={14} />) : <Circle size={12} />}
                </span>
                <span className="decision-case-row-text">
                  <span className="decision-case-row-name">{entry.name}</span>
                  {hint ? <span className="decision-case-row-hint">{hint}</span> : null}
                </span>
              </button>
            </th>
            <td className="decision-case-status-cell">
              {comparison ? <StatusChip tone={statusTone}>{statusLabel}</StatusChip> : <StatusPill tone="neutral">{statusLabel}</StatusPill>}
            </td>
            <td className="decision-case-actions-cell">{item ? <div className="decision-case-actions" ref={element => { const trigger = element?.querySelector<HTMLButtonElement>(".studio-overflow-menu-trigger"); if (trigger) caseTriggers.current.set(item.id, trigger); else caseTriggers.current.delete(item.id); }}><OverflowMenu ariaLabel={`Actions for ${item.name}`} items={[
              { id: "run", label: "Run case", icon: <Play size={14} aria-hidden="true" />, disabled: !canRunCases, onSelect: () => void executeCases([item], "single") },
              { id: "edit", label: "Edit", icon: <Pencil size={14} aria-hidden="true" />, disabled: busy || caseChanged || !table || !item.inputs || !canManage, onSelect: () => { const trigger = caseTriggers.current.get(item.id); if (trigger) edit(item, trigger); } },
              { id: "duplicate", label: "Duplicate", icon: <Copy size={14} aria-hidden="true" />, disabled: busy || caseChanged || !table || !item.inputs || !canManage, onSelect: () => { const trigger = caseTriggers.current.get(item.id); if (trigger) edit("new", trigger, { duplicateFrom: item }); } },
              { id: "delete", label: "Delete", icon: <Trash2 size={14} aria-hidden="true" />, destructive: true, disabled: busy || !canManage, onSelect: () => void work(async () => { if (!await api.dialogs.confirm({ title: "Delete case", message: `Delete “${item.name}”?`, confirmLabel: "Delete", tone: "danger" })) return; await deleteCase(api, asset.id, item); if (alive.current) setCases(await listCases(api, asset.id)); }) }
            ]} /></div> : <span className="decision-muted">—</span>}</td>
          </tr>;
        })}</tbody></table></StudioTableContainer>}
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
      {outputSupplement ? <div className="decision-output-view-switch">
        <PeerViewSwitch ariaLabel="Test output view" value={rightView} views={[{ id: "evaluate", label: "Evaluate" }, { id: "inspector", label: "Case results" }]} onChange={view => setRightView(view as "evaluate" | "inspector")} />
      </div> : null}
      {outputSupplement && rightView === "evaluate" ? outputSupplement : <>
        <section className="decision-test-latest" aria-label="Latest saved-case run" aria-live="polite" aria-busy={running}>
          {running ? <StudioResultState className="decision-output-state" title="Running saved cases" description="Comparing the saved cases with this exact candidate." />
            : runFailure ? <div className="decision-case-run-failure">
              <div className="decision-case-run-failure-summary"><h3>Saved cases could not run</h3><StudioAlert tone="danger">{runFailure.message}</StudioAlert></div>
              {runFailure.diagnostics.length ? <DecisionDiagnostics diagnostics={runFailure.diagnostics} table={table} label="Decision compilation diagnostics" onReview={item => onReviewRules?.([item])} /> : null}
              {runFailure.diagnostics.length ? <div className="decision-case-run-recovery"><p>{candidate ? "Review the affected cells in Rules. Clone this version to a draft, correct the expressions, then run the cases again." : "Review the affected cells in Rules. Correct the expressions, save the draft, then run the cases again."}</p><StudioButton onClick={() => onReviewRules?.(runFailure.diagnostics)}>Review rules</StudioButton></div> : null}
            </div>
            : run ? <TestResult
                run={run}
                changed={runChanged}
                labels={runLabels}
                selectedCaseId={activeEntry?.id ?? null}
                selectedCase={activeEntry?.saved}
                onSelectCase={selectEntry}
                onRunCase={activeEntry?.saved && canRunCases ? () => void executeCases([activeEntry.saved!], "single") : undefined}
                onEditCase={activeEntry?.saved && !busy && !caseChanged && !!table && !!activeEntry.saved.inputs && canManage ? button => edit(activeEntry.saved!, button, { restoreToCaseTrigger: false }) : undefined}
                onRerunFailed={failedCases.length > 0 && canRunCases ? () => void executeCases(failedCases, "failed") : undefined}
                failedCaseCount={failedCases.length}
              />
              : activeEntry?.saved ? <div className="decision-case-preview">
                  <div className="decision-case-detail">
                    <header className="decision-case-detail-header">
                      <div className="decision-case-detail-title">
                        <h4>{activeEntry.saved.name}</h4>
                        <StatusPill tone="neutral">Not run</StatusPill>
                      </div>
                      <div className="decision-actions">
                        <StudioButton size="sm" disabled={!canRunCases} onClick={() => void executeCases([activeEntry.saved!], "single")}><Play size={14} aria-hidden="true" /> Run</StudioButton>
                        <StudioButton size="sm" disabled={busy || caseChanged || !table || !activeEntry.saved.inputs || !canManage} onClick={event => edit(activeEntry.saved!, event.currentTarget, { restoreToCaseTrigger: false })}><Pencil size={14} aria-hidden="true" /> Edit case</StudioButton>
                      </div>
                    </header>
                    <p className="decision-muted">Expected outcome: <strong>{activeEntry.saved.expectedStatus}</strong>{activeEntry.saved.expectedStatus === "Success" && activeEntry.saved.expectedResult !== null ? <> · Result: <output>{displayValue(activeEntry.saved.expectedResult)}</output></> : null}</p>
                    <CaseInputsSummary inputs={activeEntry.saved.inputs} />
                  </div>
                  <StudioResultState className="decision-output-state" title="No recent run" description="Run saved cases to compare their expected outcomes with this decision." />
                </div>
                : <StudioResultState className="decision-output-state" title="No recent run" description="Run saved cases to compare their expected outcomes with this decision." />}
        </section>
        {can(api, "decisions:history") ? <details className="decision-history"><summary>Test history{runs.length ? ` · ${runs.length}` : ""}</summary><div className="decision-history-content"><StudioButton size="sm" disabled={busy} onClick={() => void work(async () => { const items = await history(api, asset.id); if (alive.current) setRuns(items); })}>Load test history</StudioButton>{runs.map(item => {
          const total = item.comparisons?.length ?? 0;
          const passed = item.comparisons?.filter(c => c.passed).length ?? 0;
          return <div className="decision-history-row" key={item.id}>
            <div className="decision-history-meta">
              <StatusChip tone={item.passed ? "success" : "danger"}>{item.passed ? "Passed" : "Failed"}</StatusChip>
              {total > 0 ? <span className="decision-muted">{passed}/{total}</span> : null}
              <StudioTimestamp value={item.completedAt} />
            </div>
            <div className="decision-history-actions">
              <StudioButton size="sm" disabled={busy} onClick={() => void work(async () => {
                const receipt = await getRun(api, asset.id, item.id);
                if (!alive.current) return;
                setRun(receipt); setHistoricalRun(true); setRunFailure(null);
                if (outputSupplement) setRightView("inspector");
                const firstFailed = receipt.comparisons?.find(c => !c.passed);
                if (firstFailed) setSelectedCaseId(firstFailed.caseId);
                else if (receipt.comparisons?.[0]) setSelectedCaseId(receipt.comparisons[0].caseId);
              })}>Inspect<span className="decision-visually-hidden"> run {item.id}</span></StudioButton>
              {canManage ? <StudioButton size="sm" variant="ghost" disabled={busy} onClick={() => void work(async () => { if (!await api.dialogs.confirm({ title: "Delete test history", message: "Delete this completed test receipt and retained details?", confirmLabel: "Delete", tone: "danger" })) return; await deleteRun(api, asset.id, item.id); if (alive.current) { setRuns(previous => previous.filter(row => row.id !== item.id)); if (run?.id === item.id) { setRun(null); setHistoricalRun(false); } } })}>Delete<span className="decision-visually-hidden"> run {item.id}</span></StudioButton> : null}
            </div>
          </div>;
        })}</div></details> : null}
      </>}
    </aside>
  </section>;
}
export function TestResult({ run, changed, labels, selectedCaseId, selectedCase, onSelectCase, onRunCase, onEditCase, onRerunFailed, failedCaseCount }: {
  run: TestRun;
  changed: boolean;
  labels?: ExplanationLabels;
  selectedCaseId?: string | null;
  selectedCase?: DecisionCase;
  onSelectCase?(caseId: string): void;
  onRunCase?(): void;
  onEditCase?(button: HTMLButtonElement): void;
  onRerunFailed?(): void;
  failedCaseCount?: number;
}) {
  const comparisons = useMemo(() => run.comparisons ?? [], [run.comparisons]);
  const [internalSelectedId, setInternalSelectedId] = useState<string | null>(() => comparisons.find(item => !item.passed)?.caseId ?? comparisons[0]?.caseId ?? null);
  useEffect(() => {
    if (selectedCaseId !== undefined) return;
    setInternalSelectedId(comparisons.find(item => !item.passed)?.caseId ?? comparisons[0]?.caseId ?? null);
  }, [run.id, comparisons, selectedCaseId]);
  const activeId = selectedCaseId !== undefined ? selectedCaseId : internalSelectedId;
  const comparison = comparisons.find(item => item.caseId === activeId) ?? (selectedCaseId === undefined ? comparisons[0] : undefined);
  const passedCount = comparisons.filter(item => item.passed).length;
  const failedCount = comparisons.length - passedCount;
  const rerunCount = failedCaseCount ?? failedCount;
  const verdict = comparison ? verdictSummary(comparison, selectedCase?.expectedStatus) : null;
  const showExpected = comparison !== undefined && comparison.expected !== undefined && comparison.expected !== null;
  const showActual = comparison !== undefined && comparison.actual !== undefined && comparison.actual !== null;
  return <section className="decision-test-result" aria-label="Decision test results">
    <header className="decision-run-summary">
      <div className="decision-run-summary-main">
        <h3>{run.passed ? "All cases passed" : "Cases failed"}</h3>
        {comparisons.length > 0 ? <div className="decision-run-counts">
          <StatusPill tone="success">{passedCount} passed</StatusPill>
          {failedCount > 0 ? <StatusPill tone="danger">{failedCount} failed</StatusPill> : null}
        </div> : null}
      </div>
      {onRerunFailed && rerunCount > 0 ? <StudioButton size="sm" onClick={onRerunFailed}><RotateCcw size={14} aria-hidden="true" /> Re-run failed ({rerunCount})</StudioButton> : null}
    </header>
    {changed ? <p className="decision-stale-notice">These results belong to a different saved candidate.</p> : null}
    {!run.hasDetailedPayload ? <p className="decision-muted">Detailed values were not retained or are unavailable with your permissions.</p> : null}
    {selectedCaseId === undefined && comparisons.length > 1 ? <div className="decision-standalone-picker" role="group" aria-label="Select case result">
      {comparisons.map(item => <StudioToggleButton key={item.caseId} size="sm" pressed={comparison?.caseId === item.caseId} onClick={() => { setInternalSelectedId(item.caseId); onSelectCase?.(item.caseId); }}>{item.name} · {item.passed ? "Passed" : "Failed"}</StudioToggleButton>)}
    </div> : null}
    {comparison ? <article className="decision-case-detail" key={comparison.caseId}>
      <header className="decision-case-detail-header">
        <div className="decision-case-detail-title">
          <h4>{comparison.name}</h4>
          <StatusChip tone={comparison.passed ? "success" : "danger"}>{comparison.passed ? "Passed" : "Failed"}</StatusChip>
        </div>
        {onRunCase || onEditCase ? <div className="decision-actions">
          {onRunCase ? <StudioButton size="sm" onClick={onRunCase}><Play size={14} aria-hidden="true" /> Run</StudioButton> : null}
          {onEditCase ? <StudioButton size="sm" onClick={event => onEditCase(event.currentTarget)}><Pencil size={14} aria-hidden="true" /> Edit case</StudioButton> : null}
        </div> : null}
      </header>
      {verdict ? <div className="decision-verdict" data-tone={comparison.passed ? "success" : "danger"}>
        <p className="decision-verdict-title">{verdict.title}</p>
        {verdict.detail ? <p className="decision-verdict-detail">{verdict.detail}</p> : null}
      </div> : null}
      {showExpected || showActual ? <div className="decision-diff-grid">
        {showExpected ? <div className="decision-diff-card">
          <span className="decision-diff-label">Expected: </span>
          <output className="decision-diff-value">{displayValue(comparison.expected)}</output>
        </div> : null}
        {showActual ? <div className="decision-diff-card" data-mismatch={!comparison.passed || undefined}>
          <span className="decision-diff-label">Actual: </span>
          <output className="decision-diff-value">{displayValue(comparison.actual)}</output>
        </div> : null}
      </div> : null}
      <CaseInputsSummary inputs={selectedCase?.inputs} />
      {comparison.trace?.length ? <RuleExplanation trace={comparison.trace} labels={labels} /> : comparison.matchedRules?.length ? <TechnicalDetails label="Rule identities"><p>Matched: {comparison.matchedRules.join(", ")}</p>{comparison.selectedRules?.length ? <p>Selected: {comparison.selectedRules.join(", ")}</p> : null}</TechnicalDetails> : null}
    </article> : selectedCase && comparisons.length > 0 ? <article className="decision-case-detail">
      <header className="decision-case-detail-header">
        <div className="decision-case-detail-title">
          <h4>{selectedCase.name}</h4>
          <StatusPill tone="neutral">Not in this run</StatusPill>
        </div>
        {onRunCase || onEditCase ? <div className="decision-actions">
          {onRunCase ? <StudioButton size="sm" onClick={onRunCase}><Play size={14} aria-hidden="true" /> Run</StudioButton> : null}
          {onEditCase ? <StudioButton size="sm" onClick={event => onEditCase(event.currentTarget)}><Pencil size={14} aria-hidden="true" /> Edit case</StudioButton> : null}
        </div> : null}
      </header>
      <p className="decision-muted">This case was not part of the latest run. Run it to inspect its outcome and rule evaluation.</p>
      <CaseInputsSummary inputs={selectedCase.inputs} />
    </article> : null}
    <div className="decision-run-footer">
      <TechnicalDetails label="Tested candidate"><dl><dt>Candidate</dt><dd><code>{run.candidateId}</code></dd><dt>Content identity</dt><dd><code>{run.candidateHash}</code></dd></dl></TechnicalDetails>
    </div>
  </section>;
}
