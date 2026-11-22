import { describeWorkflowError } from "../workflow-editor/editorHelpers";
import { describeNativeBlockChange } from "./nativeBlockPreview";
import { lazy, Suspense, useEffect, useId, useLayoutEffect, useRef, useState, type ReactNode, type RefObject } from "react";
import { createPortal } from "react-dom";
import { Boxes } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioButton, OverflowMenu, StudioDialog, StudioDialogFooter, TechnicalDetails, StudioField } from "@elsa-workflows/studio-ui";
import { getChildSlots, resolveScope, sequenceStructureKind, flowchartStructureKind } from "../workflowAdapter";
import { bpmnStructureKind, type BpmnElement } from "../bpmn/bpmnTypes";
import { applyNativeBlockPlan, planCreateNativeBlock, planExtractNativeBlock, planUngroupNativeBlock, type NativeBlockContext, type NativeBlockDocument, type NativeBlockKind, type NativeBlockPlan } from "./nativeBlocks";
import { nativeBlockEditorDocument, nativeBlockPlannerContext } from "./nativeBlockEditorContext";
import { useNativeBlockAuthority } from "./nativeBlockAuthority";
import type { BlockPromotionHost } from "./BlockPromotionDialog";
import type { ActivityNode } from "../workflowTypes";
import "./nativeBlockCommands.css";

export interface NativeBlockValidation {
  state: string;
  messages: string[];
}
type Action = "sequence" | "flowchart" | "bpmn" | "extract" | "ungroup";
type ReadyPlan = Extract<NativeBlockPlan, { status: "ready" }>;
const BpmnInterchangeCommands = lazy(() => import("./BpmnInterchangeCommands"));
const BlockPromotionDialog = lazy(() => import("./BlockPromotionDialog").then(module => ({ default: module.BlockPromotionDialog })));

/** Shared commands; the resource host retains authorization, validation, persistence and history. */
export interface NativeBlockCommandProps {
  endpoint?: StudioEndpointContext;
  bpmnImportFile?: File | null;
  onBpmnImportFileConsumed?(): void;
  context: NativeBlockContext;
  selectedNodeIds: string[];
  createPosition: { x: number; y: number };
  validate(document: NativeBlockDocument): Promise<NativeBlockValidation>;
  apply(document: NativeBlockDocument, selection: string[], isCurrent: () => boolean): void | Promise<void>;
  open(nodeId: string): void;
  /** Present when the owning resource can author reusable activities; it owns promotion's validation and history too. */
  promotion?: BlockPromotionHost;
  toolbarHost?: HTMLElement | null;
}
export function NativeBlockCommands(props: NativeBlockCommandProps) {
  const authority = useNativeBlockAuthority();
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
  const [promoting, setPromoting] = useState<string | null>(null);
  const [promotionStatus, setPromotionStatus] = useState<{ message: string; nodeId: string; versionId: string } | null>(null);
  const promotionReasonId = useId();
  const nameInput = useRef<HTMLInputElement>(null);
  const committingRef = useRef(false);
  const request = useRef(0);
  const close = () => { if (committingRef.current) return; request.current++; setAction(null); setPlan(null); setMessages([]); setBusy(false); setRetryValidation(false); validating.current = false; };
  useEffect(() => () => { request.current++; }, []);
  // The replacement notice holds only while the call it describes is in the document; Undo restores the block and retires it.
  const promotedCallPresent = promotionStatus ? findNode(props.context.document.rootActivity, promotionStatus.nodeId, props.context.catalog)?.activityVersionId === promotionStatus.versionId : false;
  // The host may render the committed document after the notice is set, so it retires only once the call was seen.
  const promotedCallSeen = useRef(false);
  useEffect(() => {
    if (!promotionStatus) { promotedCallSeen.current = false; return; }
    if (promotedCallPresent) promotedCallSeen.current = true;
    else if (promotedCallSeen.current) setPromotionStatus(null);
  }, [promotionStatus, promotedCallPresent]);
  useLayoutEffect(() => { if (retryValidation && !busy) previewButton.current?.focus(); }, [retryValidation, busy]);
  const invalidatePreview = () => { setPlan(null); setMessages([]); setRetryValidation(false); };
  const reportFailure = (error: unknown, applying = false) => {
    const failure = describeWorkflowError(error, "The validation request could not be completed.");
    setPlan(null); setRetryValidation(true);
    setMessages(Array.from(new Set([applying ? "The apply request failed. Check the resource before previewing again." : "Could not validate this change. Your document has not changed.",
      failure.message, failure.detail, failure.status !== undefined ? `HTTP ${failure.status}` : undefined,
      failure.code ? `Code: ${failure.code}` : undefined, failure.traceId ? `Trace: ${failure.traceId}` : undefined,
      failure.status === 401 || failure.status === 403 ? "Check your sign-in and access, then retry validation." :
        failure.status === 409 ? "Refresh the resource to load its current revision, then preview again." : "Retry validation when the service is available."]
      .filter((message): message is string => Boolean(message)))));
  };
  const scope = resolveScope(props.context.document.rootActivity, props.context.scope, props.context.catalog);
  const bpmnScope = scope?.owner.structure?.kind === bpmnStructureKind;
  const selectedElement = bpmnScope && props.selectedNodeIds.length === 1 && Array.isArray(scope?.owner.structure?.payload.elements)
    ? (scope.owner.structure!.payload.elements as BpmnElement[]).find(element => element.elementId === props.selectedNodeIds[0]) : undefined;
  const selected = scope?.slot.activities.find(node => props.selectedNodeIds.length === 1 && node.nodeId === (bpmnScope ? selectedElement?.childNodeId : props.selectedNodeIds[0]));
  const selectedKind = selected?.structure?.kind;
  const isBlock = selectedKind === sequenceStructureKind || selectedKind === flowchartStructureKind || selectedKind === bpmnStructureKind;
  const supported = scope?.slot.cardinality === "many" && scope.slot.property === "activities" &&
    (scope.owner.structure?.kind === sequenceStructureKind || scope.owner.structure?.kind === flowchartStructureKind || bpmnScope);
  const canStartEditing = !props.context.canEdit && Boolean(authority?.startEditing);
  const startEditingGuidance = authority?.startingEditing ? "Preparing your draft…" : "Start editing to use block commands. Other edits create a draft automatically.";
  const unavailable = !props.context.canEdit ? canStartEditing ? startEditingGuidance : "Block editing is unavailable in this view. Open an editable draft."
    : !supported ? "Open a Sequence, Flowchart or BPMN body to create or extract native blocks." : undefined;
  // Promotion acts on one named block, so its command appears beside "Open block" only then; an always-present
  // command wraps the toolbar and takes a row from the canvas.
  const promotion = isBlock && selected ? props.promotion : undefined;
  const promotionUnavailable = !promotion ? undefined : !props.context.canEdit ? canStartEditing ? startEditingGuidance : "Open an editable draft to promote a block."
    : promotion.capabilitiesState === "loading" ? "Loading reusable activity authoring…"
    : promotion.capabilitiesState === "failed" ? "Reusable activity authoring is unavailable on this server." : undefined;
  const kind: NativeBlockKind | "bpmn" = bpmnScope ? "bpmn" : action === "flowchart" ? "flowchart" : action === "extract" && scope?.owner.structure?.kind === flowchartStructureKind ? "flowchart" : "sequence";
  const kindLabel = kind === "bpmn" ? "BPMN subprocess" : kind === "sequence" ? "Sequence" : "Flowchart";
  const typeKey = kind === "bpmn" ? "Elsa.Activities.Bpmn.Activities.BpmnProcess" : `Elsa.Activities.${kindLabel}.Activities.${kindLabel}`;
  const choices = props.context.catalog.filter(item => item.available !== false && item.activityTypeKey === typeKey);
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
    const id = ++request.current;
    validating.current = true;
    setBusy(true); setPlan(null); setMessages([]);
    try {
      const bpm = bpmnScope ? await import("./bpmnBlocks") : null;
      if (id !== request.current) return;
      const proposed = bpm ? action === "ungroup" ? bpm.planUngroupBpmnBlock(plannerContext, original.selectedNodeIds[0] ?? "")
        : action === "extract" ? bpm.planExtractBpmnBlock(plannerContext, { ...identity, selectedNodeIds: original.selectedNodeIds })
        : bpm.planCreateBpmnBlock(plannerContext, { ...identity, position: original.createPosition, beforeElementId: original.selectedNodeIds[0] })
        : action === "ungroup" ? planUngroupNativeBlock(plannerContext, original.selectedNodeIds[0] ?? "")
        : action === "extract" ? planExtractNativeBlock(plannerContext, { ...identity, selectedNodeIds: original.selectedNodeIds })
        : planCreateNativeBlock(plannerContext, { ...identity, kind: action as NativeBlockKind, position: original.createPosition, beforeNodeId: original.selectedNodeIds[0] });
      if (proposed.status === "refused") { setMessages(proposed.diagnostics.map(item => item.message)); return; }
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
  const title = action === "extract" ? "Extract named block" : action === "ungroup" ? "Ungroup block" : action === "bpmn" ? "Create BPMN subprocess" : `Create ${kindLabel} block`;
  const label = selected && props.context.document.activityPresentation.find(item => item.nodeId === selected.nodeId)?.displayName;
  const childCount = selected ? getChildSlots(selected, props.context.catalog).reduce((sum, slot) => sum + slot.activities.length, 0) : 0;
  const commandGroup = <div className="wf-block-commands" role="group" aria-label="Named block commands">
    {canStartEditing ? <StudioButton type="button" disabled={authority?.startingEditing} onClick={() => authority?.startEditing?.()}>{authority?.startingEditing ? "Starting…" : "Start editing"}</StudioButton> : null}
    <OverflowMenu ariaLabel="Blocks" menuLabel="Block commands" triggerTitle="Named block commands"
      triggerIcon={<Boxes size={15} aria-hidden="true" />} triggerText="Blocks" triggerClassName="wf-block-menu-trigger"
      minWidth={240} maxHeight={480}
      items={[
        ...(bpmnScope ? [{ id: "bpmn", label: "Create BPMN subprocess", onSelect: () => begin("bpmn") }]
          : [{ id: "sequence", label: "Create Sequence block", onSelect: () => begin("sequence") },
            { id: "flowchart", label: "Create Flowchart block", onSelect: () => begin("flowchart") }]
        ).map(item => ({ ...item, group: "Create and organize blocks", disabled: !props.context.canEdit || !supported })),
        { id: "extract", group: "Create and organize blocks", label: "Extract block…", disabled: !props.context.canEdit || !supported || !props.selectedNodeIds.length, onSelect: () => begin("extract") },
        { id: "ungroup", group: "Create and organize blocks", label: "Ungroup…", disabled: !props.context.canEdit || !supported || !isBlock, onSelect: () => begin("ungroup") },
        ...(isBlock && selected ? [{ id: "open", group: "Selected block", label: `Open ${label || "block"} (${childCount} steps)`, onSelect: () => props.open(selected.nodeId) },
          ...(promotion ? [{ id: "promote", group: "Selected block", label: "Promote to reusable activity…",
            disabled: !props.context.canEdit || promotion.capabilitiesState !== "ready", describedBy: promotionUnavailable ? promotionReasonId : undefined,
            onSelect: () => { setPromotionStatus(null); setPromoting(selected.nodeId); } }] : [])] : [])
      ]}
      notes={(unavailable && !authority?.startingEditing) || promotionUnavailable ? <div className="wf-block-menu-notes">
        {unavailable && !authority?.startingEditing ? <p>{unavailable}</p> : null}
        {promotionUnavailable ? <p id={promotionReasonId}>{promotionUnavailable}</p> : null}
      </div> : null} />
    {bpmnScope && props.endpoint ? <Suspense fallback={<span role="status">Loading BPMN interchange…</span>}><BpmnInterchangeCommands {...props} endpoint={props.endpoint} /></Suspense> : null}
  </div>;
  return <>
    {props.toolbarHost === undefined ? commandGroup : props.toolbarHost ? createPortal(commandGroup, props.toolbarHost) : null}
    {authority?.startingEditing ? <p className="wf-block-command-notice" role="status">{startEditingGuidance}</p> : null}
    {promotionStatus ? <p className="wf-block-command-notice" role="status">{promotionStatus.message}</p> : null}
    {promoting && props.promotion && props.endpoint ? <Suspense fallback={<span role="status">Loading reusable activity promotion…</span>}>
      <BlockPromotionDialog endpoint={props.endpoint} context={props.context} createPosition={props.createPosition} host={props.promotion} blockNodeId={promoting}
        onClose={() => setPromoting(null)} onReplaced={(message, versionId) => { setPromotionStatus({ message, nodeId: promoting, versionId }); setPromoting(null); }} />
    </Suspense> : null}
    {action ? <NativeBlockDialog title={title} onClose={close} dismissDisabled={committing} initialFocusRef={action === "ungroup" ? previewButton : nameInput}
      footer={<StudioDialogFooter leading={<StudioButton ref={previewButton} disabled={busy || !props.context.canEdit} onClick={() => void preview()}>
        {busy ? "Validating…" : retryValidation ? "Retry validation" : "Preview change"}</StudioButton>}
        onCancel={close} cancelDisabled={committing} confirmLabel="Apply change" confirmType="button"
        confirmDisabled={busy || !plan || !!stale || !props.context.canEdit} onConfirm={() => void apply()} />}>
      {action !== "ungroup" ? <>
        <StudioField label="Name"><input ref={nameInput} value={name} maxLength={200} disabled={busy} onChange={event => { setName(event.target.value); invalidatePreview(); }} /></StudioField>
        <StudioField label="Summary"><textarea value={description} maxLength={2000} disabled={busy} onChange={event => { setDescription(event.target.value); invalidatePreview(); }} /></StudioField>
        {choices.length !== 1 ? <StudioField label={`${kindLabel} version`}><select value={version} disabled={busy} onChange={event => { setVersion(event.target.value); invalidatePreview(); }}><option value="">Choose an available version</option>{choices.map(item => <option key={item.activityVersionId} value={item.activityVersionId}>{item.version} · {item.activityVersionId}</option>)}</select></StudioField> : null}
      </> : <p>Remove this container and keep its steps in the current scope. Generated result-transfer steps and variables are retained when still needed.</p>}
      <p>{action === "extract" ? `${props.selectedNodeIds.length} selected ${props.selectedNodeIds.length === 1 ? "step" : "steps"} will move into a ${kindLabel} body.` : action === "ungroup" ? `${childCount} direct steps in ${label || "this block"}.` : `The block uses a native ${kindLabel} body.`}</p>
      <p>Complete ends the nearest reusable graph boundary and cancels its remaining work. It does not continue through an outgoing connection.</p>
      {messages.length ? <ul role="alert">{messages.map((message, index) => <li key={index}>{message}</li>)}</ul> : null}
      {stale ? <p role="alert">This preview is stale. Preview again before applying.</p> : null}
      {plan ? <div className="wf-block-preview"><h3>Proposed change</h3><p>{plan.selection.length} resulting {plan.selection.length === 1 ? "step" : "steps"} selected. The complete resource passed its normal validation.</p>
        {changes ? <>
          <ul>{changes.containers.map((item, index) => <li key={index}>{item.change === "add" ? "Create" : "Remove"} <strong>{item.name}</strong> · {item.kind} · {item.children} direct steps</li>)}</ul>
          {changes.movedSteps.length ? <TechnicalDetails defaultOpen label={<>Move {changes.movedSteps.length} steps</>}><ul>{changes.movedSteps.map((label, index) => <li key={index}>{label}</li>)}</ul></TechnicalDetails> : null}
          {changes.addedSteps.length ? <TechnicalDetails defaultOpen label={<>Add {changes.addedSteps.length} result-transfer steps</>}><ul>{changes.addedSteps.map((label, index) => <li key={index}>{label}</li>)}</ul></TechnicalDetails> : null}
          {changes.addedVariables.length ? <TechnicalDetails defaultOpen label={<>Add {changes.addedVariables.length} result variables</>}><ul>{changes.addedVariables.map((label, index) => <li key={index}>{label}</li>)}</ul></TechnicalDetails> : null}
          {changes.removedElements.length || changes.addedElements.length ? <TechnicalDetails defaultOpen label={<>Change BPMN elements</>}><ul>{changes.removedElements.map((label, index) => <li key={`old-${index}`}>Remove: {label}</li>)}{changes.addedElements.map((label, index) => <li key={`new-${index}`}>Add: {label}</li>)}</ul></TechnicalDetails> : null}
          {changes.removedConnections.length || changes.addedConnections.length ? <TechnicalDetails defaultOpen label={<>Rewire connections</>}><ul>{changes.removedConnections.map((label, index) => <li key={`old-${index}`}>Remove: {label}</li>)}{changes.addedConnections.map((label, index) => <li key={`new-${index}`}>Add: {label}</li>)}</ul></TechnicalDetails> : null}
        </> : null}<TechnicalDetails label={<>Current graph, layout and names</>}><pre>{JSON.stringify(nativeBlockPlannerContext(props.context, props.createPosition).document, null, 2)}</pre></TechnicalDetails><TechnicalDetails label={<>Complete proposed graph, connections, bindings, layout and names</>}><pre>{JSON.stringify(plan.document, null, 2)}</pre></TechnicalDetails></div> : null}
    </NativeBlockDialog> : null}
  </>;
}

function findNode(node: ActivityNode, nodeId: string, catalog: NativeBlockContext["catalog"]): ActivityNode | undefined {
  if (node.nodeId === nodeId) return node;
  for (const slot of getChildSlots(node, catalog)) for (const child of slot.activities) {
    const found = findNode(child, nodeId, catalog);
    if (found) return found;
  }
  return undefined;
}

export function NativeBlockDialog({ title, onClose, dismissDisabled, initialFocusRef, footer, children }: {
  title: string; onClose(): void; dismissDisabled?: boolean; initialFocusRef?: RefObject<HTMLElement | null>;
  footer: ReactNode; children: ReactNode;
}) {
  return <StudioDialog open title={title} closeLabel={`Close ${title}`} className="wf-block-dialog"
    dismissOnOutsideClick={false} dismissDisabled={dismissDisabled} initialFocusRef={initialFocusRef} onClose={onClose} footer={footer}>
    <div className="wf-block-dialog-body">{children}</div>
  </StudioDialog>;
}
