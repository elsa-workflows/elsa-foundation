import React, { useRef, useState } from "react";
import { StudioAlert, StudioButton } from "@elsa-workflows/studio-ui";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { evaluateDecision, failureMessage, type Evaluation, type Scalar } from "./decisionsApi";
import { parseTable } from "./decisionXml";
import { displayValue, initial, ValueInput } from "./CasePanel";
import { RuleExplanation } from "./RuleExplanation";
export function EvaluationPanel({ api, id, versionId, xml }: { api: ElsaStudioModuleApi; id: string; versionId: string; xml: string }) {
  let table; try { table = parseTable(xml); } catch { table = null; }
  const [inputs, setInputs] = useState<Record<string, Scalar>>(() => Object.fromEntries((table?.inputs ?? []).map(clause => [clause.name, initial(clause.type)]))), [result, setResult] = useState<Evaluation | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState("");
  const active = useRef(true); React.useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  if (!table) return null;
  return <section className="decision-cases" aria-labelledby="decision-evaluate-title"><h2 id="decision-evaluate-title">Evaluate this version</h2><p className="decision-muted">Supply example inputs and inspect the actual matched and selected rules.</p><form onSubmit={event => { event.preventDefault(); if (busy) return; setBusy(true); setError(""); void evaluateDecision(api, id, versionId, inputs).then(value => { if (active.current) setResult(value); }, failure => { if (active.current) setError(failureMessage(failure)); }).finally(() => { if (active.current) setBusy(false); }); }}>
    <fieldset disabled={busy} className="decision-evaluate-fields"><legend className="decision-visually-hidden">Evaluation inputs</legend><div className="decision-form-grid">{table.inputs.map(clause => <ValueInput key={clause.id} clause={clause} label={`Evaluate input ${clause.name}`} value={inputs[clause.name] ?? initial(clause.type)} onChange={value => setInputs(previous => ({ ...previous, [clause.name]: value }))} />)}</div><StudioButton type="submit" variant="primary" disabled={busy}>{busy ? "Evaluating…" : "Evaluate decision"}</StudioButton></fieldset>
  </form>{error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}{result ? <section aria-label="Decision evaluation explanation" className="decision-test-result"><h3>{result.status}</h3><p>Result: <output>{result.status === "NoMatch" ? "No matching rule" : displayValue(result.result)}</output></p><p className="decision-muted">Exact decision {result.decisionId} · Content {result.contentHash}</p>{result.diagnostics.map((item, index) => <p role="status" key={`${item.code}-${index}`}>{item.code} · {item.message}</p>)}<RuleExplanation trace={result.trace} /></section> : null}</section>;
}
