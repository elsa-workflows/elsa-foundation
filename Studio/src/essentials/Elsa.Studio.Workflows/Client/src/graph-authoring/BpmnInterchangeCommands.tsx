import { useEffect, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioField } from "@elsa-workflows/studio-ui";
import { resolveScope } from "../workflowAdapter";
import { exportBpmnDocument, importBpmnDocument, withDiagramFromLayout, type BpmnImportResult } from "../api/bpmnInterchange";
import { NativeBlockDialog, type NativeBlockCommandProps } from "./NativeBlockCommands";
import { applyNativeBlockPlan, type NativeBlockContext, type NativeBlockPlan } from "./nativeBlocks";
import { nativeBlockEditorDocument, nativeBlockPlannerContext } from "./nativeBlockEditorContext";
import { planBpmnBodyImport } from "./bpmnImportPlan";

type ReadyPlan = Extract<NativeBlockPlan, { status: "ready" }>;
type Props = NativeBlockCommandProps & { endpoint: StudioEndpointContext };
const snapshot = (context: NativeBlockContext) => JSON.stringify([context.sourceKey, context.document, context.scope, context.catalog, context.canEdit, context.referenceContext]);
function download(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a"); anchor.href = url; anchor.download = name;
  anchor.click(); URL.revokeObjectURL(url);
}

/** The same review and exact confirmation flow serves both graph resource hosts and every native BPMN scope. */
export default function BpmnInterchangeCommands(props: Props) {
  const current = useRef(props); current.current = props;
  const input = useRef<HTMLInputElement>(null);
  const request = useRef(0);
  const abort = useRef<AbortController | null>(null);
  const committing = useRef(false);
  const [review, setReview] = useState<{ file: File; xml: string; original: Props; result: BpmnImportResult } | null>(null);
  const [processId, setProcessId] = useState("");
  const [confirmed, setConfirmed] = useState<string[]>([]);
  const [plan, setPlan] = useState<ReadyPlan | null>(null);
  const [messages, setMessages] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  useEffect(() => () => { request.current++; abort.current?.abort(); }, []);
  const close = () => { if (committing.current) return; request.current++; abort.current?.abort(); setReview(null); setPlan(null); setMessages([]); setBusy(false); };
  const beginRequest = () => { abort.current?.abort(); abort.current = new AbortController(); return { id: ++request.current, signal: abort.current.signal }; };
  const live = (id: number, original: Props) => id === request.current && current.current.endpoint === original.endpoint
    && current.current.context.canEdit && snapshot(current.current.context) === snapshot(original.context);
  const load = async (file: File) => {
    if (!current.current.context.canEdit) return;
    const original = current.current, { id, signal } = beginRequest();
    setBusy(true); setPlan(null); setMessages([]); setConfirmed([]); setProcessId("");
    // Keep the original bytes available even when analysis refuses the document.
    setReview({ file, xml: "", original, result: { processNode: original.context.document.rootActivity, analysis: { issues: [], processIds: [], elementCounts: {} } } });
    try {
      const xml = await file.text();
      const result = await importBpmnDocument(original.endpoint, xml, undefined, signal);
      if (!live(id, original)) throw new Error("The resource changed while reading this file. Cancel and choose it again.");
      setReview({ file, xml, original, result });
    } catch (error) { if (id === request.current) setMessages([error instanceof Error ? error.message : "The BPMN file could not be analyzed."]); }
    finally { if (id === request.current) setBusy(false); }
  };
  const loadRequest = useRef(load); loadRequest.current = load;
  useEffect(() => {
    if (props.bpmnImportFile) {
      void loadRequest.current(props.bpmnImportFile);
      current.current.onBpmnImportFileConsumed?.();
    }
  }, [props.bpmnImportFile]);
  const analyze = async (selectedProcess: string) => {
    if (!review?.xml) return;
    const original = review.original, { id, signal } = beginRequest();
    setProcessId(selectedProcess); setPlan(null); setConfirmed([]); setBusy(true); setMessages([]);
    try {
      const result = await importBpmnDocument(original.endpoint, review.xml, selectedProcess || undefined, signal);
      if (!live(id, original)) throw new Error("The resource changed. Cancel and choose the file again.");
      setReview({ ...review, result });
    } catch (error) { if (id === request.current) setMessages([error instanceof Error ? error.message : "Analysis is unavailable."]); }
    finally { if (id === request.current) setBusy(false); }
  };
  const preview = async () => {
    if (!review?.xml || busy) return;
    const original = review.original, { id, signal } = beginRequest(); setBusy(true); setPlan(null); setMessages([]);
    try {
      const result = await importBpmnDocument(original.endpoint, review.xml, processId || undefined, signal, confirmed);
      if (!live(id, original)) throw new Error("The resource changed. Cancel and choose the file again.");
      const proposed = planBpmnBodyImport(nativeBlockPlannerContext(original.context, original.createPosition), result);
      if (proposed.status === "refused") { setMessages(proposed.diagnostics.map(item => item.message)); return; }
      const validation = await original.validate(nativeBlockEditorDocument(proposed.document, original.context.catalog));
      if (!live(id, original)) throw new Error("The resource changed during validation. Cancel and choose the file again.");
      setMessages(validation.messages);
      if (validation.state === "validated") setPlan(proposed);
      else if (!validation.messages.length) setMessages(["The complete resource could not be validated. The current body is unchanged."]);
    } catch (error) { if (id === request.current) setMessages([error instanceof Error ? error.message : "The proposed body could not be validated."]); }
    finally { if (id === request.current) setBusy(false); }
  };
  const apply = async () => {
    if (!plan || !review || busy) return;
    const proposed = plan, original = review.original, { id } = beginRequest(); setBusy(true); setMessages([]);
    const isCurrent = () => live(id, original) && applyNativeBlockPlan(nativeBlockPlannerContext(current.current.context, current.current.createPosition), proposed).status === "ready";
    try {
      if (!isCurrent()) throw new Error("The preview is stale. Cancel and choose the file again.");
      const document = nativeBlockEditorDocument(proposed.document, current.current.context.catalog);
      const validation = await current.current.validate(document);
      if (!isCurrent() || validation.state !== "validated") { setPlan(null); setMessages(validation.messages.length ? validation.messages : ["The resource or its authority changed. Nothing was applied."]); return; }
      committing.current = true;
      await current.current.apply(document, [], isCurrent);
      committing.current = false;
      if (id === request.current) close();
    } catch (error) { if (id === request.current) setMessages([error instanceof Error ? error.message : "The body was not applied."]); }
    finally { committing.current = false; if (id === request.current) setBusy(false); }
  };
  const exportBody = async () => {
    const original = current.current;
    const scope = resolveScope(original.context.document.rootActivity, original.context.scope, original.context.catalog);
    if (!scope || busy) return;
    setBusy(true); setMessages([]);
    try {
      const xml = await exportBpmnDocument(original.endpoint, withDiagramFromLayout(scope.owner, original.context.document.layout, original.context.document.activityPresentation));
      if (snapshot(current.current.context) !== snapshot(original.context) || current.current.endpoint !== original.endpoint)
        throw new Error("The process changed while exporting. Export the current body again.");
      download(new Blob([xml], { type: "application/xml" }), "process.bpmn");
    } catch (error) { setMessages([error instanceof Error ? error.message : "BPMN export is unavailable."]); }
    finally { setBusy(false); }
  };
  const stale = review && snapshot(props.context) !== snapshot(review.original.context);
  return <>
    <button type="button" disabled={busy || !props.context.canEdit} onClick={() => input.current?.click()}>Import BPMN body…</button>
    <button type="button" disabled={busy} onClick={() => void exportBody()}>Export BPMN body</button>
    <input ref={input} type="file" accept=".bpmn,.xml" hidden aria-label="Import native BPMN body file" onChange={event => { const file = event.target.files?.[0]; event.target.value = ""; if (file) void load(file); }} />
    {!review && messages.length ? <span role="alert">{messages.join(" ")}</span> : null}
    {review ? <NativeBlockDialog title="Review BPMN body import" onClose={close}>
      <h2>Review BPMN body import</h2>
      <p>Replace the current body with {review.file.name}. The owning activity keeps its version, inputs, outputs and runtime settings. Apply records one edit that can be undone.</p>
      <button type="button" onClick={() => download(review.file, review.file.name)}>Download original file</button>
      {review.result.analysis.processIds.length > 1 ? <StudioField label="Process"><select value={processId} disabled={busy} onChange={event => void analyze(event.target.value)}><option value="">Default process</option>{review.result.analysis.processIds.map(id => <option key={id} value={id}>{id}</option>)}</select></StudioField> : null}
      {review.result.analysis.issues.length ? <ul>{review.result.analysis.issues.map((issue, index) => <li key={index}>{issue.elementId ? `${issue.elementId}: ` : ""}{issue.message}</li>)}</ul> : null}
      <h3>Bindings retained in this file</h3>
      <p>Confirm only versions and literal input keys you intend to use. The host checks their availability before applying.</p>
      {review.result.analysis.capabilities?.map((item, index) => <div key={`${item.elementId}-${index}`}>
        {item.retainedBinding ? <label className="wf-block-binding"><input type="checkbox" disabled={busy || !!stale} checked={confirmed.includes(item.elementId)} onChange={event => { const checked = event.currentTarget.checked; setPlan(null); setConfirmed(current => checked ? [...current, item.elementId] : current.filter(id => id !== item.elementId)); }} /><span>{item.elementId} · {item.retainedBinding.activityVersionId} · inputs: {item.retainedBinding.inputKeys.join(", ") || "none"}</span></label> : <span>{item.elementId} · {item.capability}</span>}
        <p>{item.reason}</p>
      </div>)}
      {stale ? <p role="alert">The resource changed. Cancel and choose the file again.</p> : null}
      {messages.length ? <ul role="alert">{messages.map((message, index) => <li key={index}>{message}</li>)}</ul> : null}
      {plan ? <><p>The complete proposed resource passed validation. Review its body, bindings and layout before applying.</p><details><summary>Current resource</summary><pre>{JSON.stringify(review.original.context.document, null, 2)}</pre></details><details open><summary>Proposed resource</summary><pre>{JSON.stringify(plan.document, null, 2)}</pre></details></> : null}
      <div className="wf-block-dialog-actions"><button type="button" disabled={committing.current} onClick={close}>Cancel</button><button type="button" disabled={busy || !!stale || !review.xml || !props.context.canEdit} onClick={() => void preview()}>{busy ? "Validating…" : "Preview imported body"}</button><button type="button" disabled={busy || !!stale || !plan || !props.context.canEdit} onClick={() => void apply()}>Apply imported body</button></div>
    </NativeBlockDialog> : null}
  </>;
}
