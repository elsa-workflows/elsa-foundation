import { describeWorkflowError } from "../workflow-editor/editorHelpers";
import { describeNativeBlockChange } from "./nativeBlockPreview";
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { StudioField } from "@elsa-workflows/studio-ui";
import { getChildSlots, resolveScope, sequenceStructureKind, flowchartStructureKind } from "../workflowAdapter";
import { useDialogFocus } from "../workflow-editor/useDialogFocus";
import { applyNativeBlockPlan, planCreateNativeBlock, planExtractNativeBlock, planUngroupNativeBlock, type NativeBlockContext, type NativeBlockDocument, type NativeBlockKind, type NativeBlockPlan } from "./nativeBlocks";
import { nativeBlockEditorDocument, nativeBlockPlannerContext } from "./nativeBlockEditorContext";
import "./nativeBlockCommands.css";

export interface NativeBlockValidation {
  state: string;
  messages: string[];
}
type Action = "sequence" | "flowchart" | "extract" | "ungroup";
type ReadyPlan = Extract<NativeBlockPlan, { status: "ready" }>;

/** Shared commands; the resource host retains authorization, validation, persistence and history. */
export function NativeBlockCommands(props: {
  context: NativeBlockContext;
  selectedNodeIds: string[];
  createPosition: { x: number; y: number };
  validate(document: NativeBlockDocument): Promise<NativeBlockValidation>;
  apply(document: NativeBlockDocument, selection: string[], isCurrent: () => boolean): void | Promise<void>;
  open(nodeId: string): void;
}) {
  const current = useRef(props);
  current.current = props;
  const [action, setAction] = useState<Action | null>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [version, setVersion] = useState("");
  const [plan, setPlan] = useState<ReadyPlan | null>(null);
  const [messages, setMessages] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [retryValidation, setRetryValidation] = useState(false);
  const previewButton = useRef<HTMLButtonElement>(null);
  const validating = useRef(false);
  const [committing, setCommitting] = useState(false);
  const committingRef = useRef(false);
  const request = useRef(0);
  const close = () => { if (committingRef.current) return; request.current++; setAction(null); setPlan(null); setMessages([]); setBusy(false); setRetryValidation(false); validating.current = false; };
  useEffect(() => () => { request.current++; }, []);
  useLayoutEffect(() => { if (retryValidation && !busy) previewButton.current?.focus(); }, [retryValidation, busy]);
  const invalidatePreview = () => { setPlan(null); setMessages([]); setRetryValidation(false); };
  const reportFailure = (error: unknown, applying = false) => {
    const failure = describeWorkflowError(error, "The validation request could not be completed.");
    setPlan(null); setRetryValidation(true);
    setMessages([applying ? "The apply request failed. Check the resource before previewing again." : "Could not validate this change. Your document has not changed.",
      failure.message, failure.detail, failure.status !== undefined ? `HTTP ${failure.status}` : undefined,
      failure.code ? `Code: ${failure.code}` : undefined, failure.traceId ? `Trace: ${failure.traceId}` : undefined,
      failure.status === 401 || failure.status === 403 ? "Check your sign-in and access, then retry validation." :
        failure.status === 409 ? "Refresh the resource to load its current revision, then preview again." : "Retry validation when the service is available."]
      .filter((message): message is string => Boolean(message)));
  };
  const scope = resolveScope(props.context.document.rootActivity, props.context.scope, props.context.catalog);
  const selected = scope?.slot.activities.find(node => props.selectedNodeIds.length === 1 && node.nodeId === props.selectedNodeIds[0]);
  const selectedKind = selected?.structure?.kind;
  const isBlock = selectedKind === sequenceStructureKind || selectedKind === flowchartStructureKind;
  const supported = scope?.slot.cardinality === "many" && scope.slot.property === "activities" &&
    (scope.owner.structure?.kind === sequenceStructureKind || scope.owner.structure?.kind === flowchartStructureKind);
  const unavailable = !props.context.canEdit ? "Block editing is unavailable in this view. Open an editable draft."
    : !supported ? "Open a Sequence or Flowchart body to create or extract native blocks." : undefined;
  const kind: NativeBlockKind = action === "flowchart" ? "flowchart" : action === "extract" && scope?.owner.structure?.kind === flowchartStructureKind ? "flowchart" : "sequence";
  const choices = props.context.catalog.filter(item => item.available !== false && item.activityTypeKey ===
    `Elsa.Activities.${kind === "sequence" ? "Sequence" : "Flowchart"}.Activities.${kind === "sequence" ? "Sequence" : "Flowchart"}`);
  const begin = (next: Action) => {
    setAction(next); setPlan(null); setMessages([]); setName(""); setDescription(""); setVersion(""); setRetryValidation(false);
  };
  const refuseStale = () => setMessages(["The document, selection, scope, permissions or installed modules changed. Preview this change again."]);
  const preview = async () => {
    if (!action || busy || validating.current) return;
    setPlan(null); setRetryValidation(false);
    const original = current.current;
    const plannerContext = nativeBlockPlannerContext(original.context, original.createPosition);
    const identity = { nodeId: crypto.randomUUID(), activityVersionId: version || (choices.length === 1 ? choices[0].activityVersionId : ""), displayName: name, description };
    const proposed = action === "ungroup" ? planUngroupNativeBlock(plannerContext, original.selectedNodeIds[0] ?? "") :
      action === "extract" ? planExtractNativeBlock(plannerContext, { ...identity, selectedNodeIds: original.selectedNodeIds }) :
        planCreateNativeBlock(plannerContext, { ...identity, kind: action, position: original.createPosition, beforeNodeId: original.selectedNodeIds[0] });
    if (proposed.status === "refused") { setMessages(proposed.diagnostics.map(item => item.message)); return; }
    const id = ++request.current;
    validating.current = true;
    setBusy(true); setPlan(null); setMessages([]);
    try {
      const result = await original.validate(nativeBlockEditorDocument(proposed.document, original.context.catalog));
      if (id !== request.current) return;
      if (applyNativeBlockPlan(nativeBlockPlannerContext(current.current.context, current.current.createPosition), proposed).status !== "ready") { refuseStale(); return; }
      setMessages(result.messages);
      setRetryValidation(result.state === "unavailable");
      if (result.state === "validated") setPlan(proposed);
      else setMessages(result.messages.length ? result.messages : ["The owning resource could not validate this proposal. Refresh the resource and try again."]);
    } catch (error) { if (id === request.current) reportFailure(error); }
    finally { if (id === request.current) { validating.current = false; setBusy(false); } }
  };
  const apply = async () => {
    if (!plan || busy || validating.current || committingRef.current) return;
    const checked = applyNativeBlockPlan(nativeBlockPlannerContext(current.current.context, current.current.createPosition), plan);
    if (checked.status !== "ready") { setPlan(null); refuseStale(); return; }
    const id = ++request.current;
    validating.current = true;
    setBusy(true); setMessages([]);
    try {
      // Recheck normal resource authority at confirmation, including server-side revision and access changes.
      const result = await current.current.validate(nativeBlockEditorDocument(checked.document, current.current.context.catalog));
      if (id !== request.current) return;
      const final = applyNativeBlockPlan(nativeBlockPlannerContext(current.current.context, current.current.createPosition), checked);
      if (final.status !== "ready") { setPlan(null); refuseStale(); return; }
      if (result.state !== "validated") { setPlan(null); setMessages(result.messages.length ? result.messages : ["The resource or its authority changed. Preview again."]); return; }
      committingRef.current = true; setCommitting(true);
      await current.current.apply(nativeBlockEditorDocument(final.document, current.current.context.catalog), final.selection, () => request.current === id && applyNativeBlockPlan(nativeBlockPlannerContext(current.current.context, current.current.createPosition), checked).status === "ready");
      committingRef.current = false; setCommitting(false);
      if (id === request.current) close();
    } catch (error) { if (id === request.current) reportFailure(error, committingRef.current); }
    finally { committingRef.current = false; if (id === request.current) { validating.current = false; setCommitting(false); setBusy(false); } }
  };
  const stale = plan && applyNativeBlockPlan(nativeBlockPlannerContext(props.context, props.createPosition), plan).status !== "ready";
  const changes = plan ? describeNativeBlockChange(nativeBlockPlannerContext(props.context, props.createPosition).document, plan.document, props.context.catalog) : null;
  const title = action === "extract" ? "Extract named block" : action === "ungroup" ? "Ungroup block" : `Create ${action === "sequence" ? "Sequence" : "Flowchart"} block`;
  const label = selected && props.context.document.activityPresentation.find(item => item.nodeId === selected.nodeId)?.displayName;
  const childCount = selected ? getChildSlots(selected, props.context.catalog).reduce((sum, slot) => sum + slot.activities.length, 0) : 0;
  return <>
    <div className="wf-block-commands" role="group" aria-label="Named block commands" title={unavailable}>
      <button type="button" disabled={!props.context.canEdit || !supported} onClick={() => begin("sequence")}>Create Sequence block</button>
      <button type="button" disabled={!props.context.canEdit || !supported} onClick={() => begin("flowchart")}>Create Flowchart block</button>
      <button type="button" disabled={!props.context.canEdit || !supported || !props.selectedNodeIds.length} onClick={() => begin("extract")}>Extract block…</button>
      <button type="button" disabled={!props.context.canEdit || !supported || !isBlock} onClick={() => begin("ungroup")}>Ungroup…</button>
      {isBlock && selected ? <button type="button" onClick={() => props.open(selected.nodeId)}>Open {label || "block"} ({childCount} steps)</button> : null}
    </div>
    {unavailable ? <p className="wf-muted wf-block-command-notice">{unavailable}</p> : null}
    {action ? <NativeBlockDialog title={title} onClose={close}>
      <h2>{title}</h2>
      {action !== "ungroup" ? <>
        <StudioField label="Name"><input value={name} maxLength={200} disabled={busy} onChange={event => { setName(event.target.value); invalidatePreview(); }} /></StudioField>
        <StudioField label="Summary"><textarea value={description} maxLength={2000} disabled={busy} onChange={event => { setDescription(event.target.value); invalidatePreview(); }} /></StudioField>
        {choices.length !== 1 ? <StudioField label={`${kind === "sequence" ? "Sequence" : "Flowchart"} version`}><select value={version} disabled={busy} onChange={event => { setVersion(event.target.value); invalidatePreview(); }}><option value="">Choose an available version</option>{choices.map(item => <option key={item.activityVersionId} value={item.activityVersionId}>{item.version} · {item.activityVersionId}</option>)}</select></StudioField> : null}
      </> : <p>Remove this container and keep its steps in the current scope. Generated result-transfer steps and variables are retained when still needed.</p>}
      <p>{action === "extract" ? `${props.selectedNodeIds.length} selected ${props.selectedNodeIds.length === 1 ? "step" : "steps"} will move into a ${kind === "sequence" ? "Sequence" : "Flowchart"} body.` : action === "ungroup" ? `${childCount} direct steps in ${label || "this block"}.` : `The block uses a native ${kind === "sequence" ? "Sequence" : "Flowchart"} body.`}</p>
      <p>Complete ends the nearest reusable graph boundary and cancels its remaining work. It does not continue through an outgoing connection.</p>
      {messages.length ? <ul role="alert">{messages.map((message, index) => <li key={index}>{message}</li>)}</ul> : null}
      {stale ? <p role="alert">This preview is stale. Preview again before applying.</p> : null}
      {plan ? <div className="wf-block-preview"><h3>Proposed change</h3><p>{plan.selection.length} resulting {plan.selection.length === 1 ? "step" : "steps"} selected. The complete resource passed its normal validation.</p>
        {changes ? <>
          <ul>{changes.containers.map((item, index) => <li key={index}>{item.change === "add" ? "Create" : "Remove"} <strong>{item.name}</strong> · {item.kind} · {item.children} direct steps</li>)}</ul>
          {changes.movedSteps.length ? <details open><summary>Move {changes.movedSteps.length} steps</summary><ul>{changes.movedSteps.map((label, index) => <li key={index}>{label}</li>)}</ul></details> : null}
          {changes.addedSteps.length ? <details open><summary>Add {changes.addedSteps.length} result-transfer steps</summary><ul>{changes.addedSteps.map((label, index) => <li key={index}>{label}</li>)}</ul></details> : null}
          {changes.addedVariables.length ? <details open><summary>Add {changes.addedVariables.length} result variables</summary><ul>{changes.addedVariables.map((label, index) => <li key={index}>{label}</li>)}</ul></details> : null}
          {changes.removedConnections.length || changes.addedConnections.length ? <details open><summary>Rewire connections</summary><ul>{changes.removedConnections.map((label, index) => <li key={`old-${index}`}>Remove: {label}</li>)}{changes.addedConnections.map((label, index) => <li key={`new-${index}`}>Add: {label}</li>)}</ul></details> : null}
        </> : null}<details><summary>Current graph, layout and names</summary><pre>{JSON.stringify(nativeBlockPlannerContext(props.context, props.createPosition).document, null, 2)}</pre></details><details><summary>Complete proposed graph, connections, bindings, layout and names</summary><pre>{JSON.stringify(plan.document, null, 2)}</pre></details></div> : null}
      <div className="wf-block-dialog-actions"><button type="button" disabled={committing} onClick={close}>Cancel</button><button ref={previewButton} type="button" disabled={busy || !props.context.canEdit} onClick={() => void preview()}>{busy ? "Validating…" : retryValidation ? "Retry validation" : "Preview change"}</button><button type="button" disabled={busy || !plan || !!stale || !props.context.canEdit} onClick={() => void apply()}>Apply change</button></div>
    </NativeBlockDialog> : null}
  </>;
}

function NativeBlockDialog({ title, onClose, children }: { title: string; onClose(): void; children: ReactNode }) {
  const dialog = useRef<HTMLElement>(null);
  useDialogFocus(dialog, onClose);
  return <div className="wf-block-backdrop"><section ref={dialog} className="wf-block-dialog" tabIndex={-1} role="dialog" aria-modal="true" aria-label={title}>{children}</section></div>;
}
