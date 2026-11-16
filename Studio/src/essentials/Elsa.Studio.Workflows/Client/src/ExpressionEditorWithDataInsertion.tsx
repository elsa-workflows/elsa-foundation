import { useCallback, useEffect, useRef, useState, type ComponentType } from "react";
import { authSessionEndedEvent, type StudioExpressionEditorContribution, type StudioExpressionEditorController, type StudioExpressionEditorProps, type StudioExpressionEditorSnapshot } from "@elsa-workflows/studio-sdk";
import { ActivityDataBrowser, focusBeside, useActivityDataSelectionHost } from "./ActivityDataChooser";
import type { ActivityDataContextResult, ActivityDataSourceFacts } from "./api/activityDataSelection";
import { expressionEditingState, expressionValueFromHistory, planActivityExpressionInsertion } from "./activityExpressionInsertion";

/** One surface of the current owner. Chooser state, cursor tokens and controllers are never authored data. */
export function ExpressionEditorWithDataInsertion({ editor: Editor, contribution, ownerValue, ...props }: StudioExpressionEditorProps & {
  editor: ComponentType<StudioExpressionEditorProps>; contribution: StudioExpressionEditorContribution | null; ownerValue: unknown;
}) {
  const host = useActivityDataSelectionHost();
  const trigger = useRef<HTMLButtonElement>(null);
  const controller = useRef<StudioExpressionEditorController | null>(null);
  const snapshot = useRef<StudioExpressionEditorSnapshot | null>(null);
  const pending = useRef(false);
  const openedIdentity = useRef<string | null>(null);
  const openedOwner = useRef<object | undefined>(undefined);
  const [activating, setActivating] = useState(false);
  const [open, setOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const document = props.context.document;
  const target = { nodeId: document?.activityId ?? "", inputKey: document?.propertyKey ?? props.descriptor.referenceKey ?? props.descriptor.name };
  const identity = `${props.context.editorSessionScope}\u001f${document?.id}\u001f${props.syntax}\u001f${contribution?.id}`;
  const textMode = props.context.expressionDescriptors.some(item => item.type === props.syntax && item.editingMode === "text");
  const available = host?.expressionInsertion === true && textMode && !!document;
  const editable = available && !props.disabled && !props.context.readOnly;
  const state = expressionEditingState(ownerValue, props.syntax);
  const capable = editable && !!contribution?.sourceInsertion && state.supported;
  const current = useRef({ props, ownerValue, host, target, identity, capable, contribution });
  current.current = { props, ownerValue, host, target, identity, capable, contribution };

  const register = useCallback((next: StudioExpressionEditorController | null) => {
    controller.current = next;
    if (!next || !pending.current || !current.current.capable) return;
    snapshot.current = next.capture();
    if (snapshot.current) { pending.current = false; setActivating(false); setOpen(true); }
  }, []);
  const close = useCallback((focus: "opener" | "next" | "previous" | "none") => {
    pending.current = false; openedIdentity.current = null; snapshot.current = null; setActivating(false); setOpen(false);
    if (focus !== "none") requestAnimationFrame(() => focus === "opener" ? trigger.current?.focus() : focusBeside(trigger.current, focus));
  }, []);
  useEffect(() => { close("none"); setNotice(null); }, [close, identity, capable, host?.ownerContext]);
  useEffect(() => {
    const revoked = () => close("none");
    window.addEventListener(authSessionEndedEvent, revoked);
    return () => window.removeEventListener(authSessionEndedEvent, revoked);
  }, [close]);

  const begin = () => {
    if (!current.current.capable) return;
    setNotice(null);
    openedIdentity.current = current.current.identity;
    openedOwner.current = current.current.host?.ownerContext;
    snapshot.current = controller.current?.capture() ?? null;
    if (snapshot.current) setOpen(true);
    else { pending.current = true; setActivating(true); }
  };
  const choose = (result: ActivityDataContextResult, source: ActivityDataSourceFacts) => {
    const now = current.current;
    const captured = snapshot.current;
    if (!now.capable || !captured || !now.host || !now.contribution?.sourceInsertion || now.identity !== openedIdentity.current || now.host.ownerContext !== openedOwner.current ||
        result.documentRevision !== now.host.documentRevision || result.baseRevision !== now.host.baseRevision ||
        result.target?.target?.nodeId !== now.target.nodeId || result.target?.target?.inputKey !== now.target.inputKey) return;
    const planned = planActivityExpressionInsertion({ value: now.ownerValue, syntax: now.props.syntax, snapshot: captured,
      result, source, adapter: now.contribution.sourceInsertion });
    if (!planned.supported) { setNotice(planned.reason); return; }
    if (!controller.current?.apply({ expected: captured, insert: planned.insert, historyValue: planned.historyValue, selection: planned.selection }))
      setNotice("The expression or editing permission changed. Choose the insertion position again.");
  };
  const action = available ? <>
    <button ref={trigger} type="button" className="wf-choose-data-trigger wf-expression-insert-data" aria-haspopup="dialog"
      aria-expanded={open} disabled={!capable} title={!contribution?.sourceInsertion ? "This editor does not support data insertion." : !state.supported ? state.reason : "Insert an earlier value at the current selection"}
      onPointerDown={event => { if (controller.current?.capture()) event.preventDefault(); }} onClick={() => open ? close("opener") : begin()}>
      Insert data
    </button>
    {activating ? <span role="status">Opening expression editor…</span> : null}
    {notice ? <span role="alert">{notice}</span> : null}
    {open && host && host.ownerContext === openedOwner.current ? <ActivityDataBrowser key={identity} host={host} target={target} inputLabel={props.descriptor.displayName || props.descriptor.name}
      anchorRef={trigger} open current={null} onSelect={() => {}} onClose={close} expression={{ type: props.syntax, onSelect: choose }} /> : null}
  </> : undefined;
  return <><Editor {...props} actions={action} documentEditing={capable && state.supported ? {
    historyValue: state.historyValue, interactionActive: open || activating, onController: register
  } : undefined} onChange={(value, edit) => {
    if (current.current.props.disabled || current.current.props.context.readOnly) return;
    if (!edit || !current.current.capable) { props.onChange(value); return; }
    const restored = typeof value === "string" ? expressionValueFromHistory(value, edit.historyValue, props.syntax) : null;
    if (restored) props.onChange(restored.value, edit);
  }} />{available && !props.disabled && !props.context.readOnly && (!contribution?.sourceInsertion || !state.supported)
    ? <p className="wf-property-hint" role="status">{!state.supported ? state.reason : "This installed editor does not support data insertion. The expression is preserved."}</p> : null}</>;
}
