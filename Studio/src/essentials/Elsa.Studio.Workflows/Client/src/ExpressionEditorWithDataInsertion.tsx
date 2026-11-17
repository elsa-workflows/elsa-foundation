import { useCallback, useEffect, useId, useMemo, useRef, useState, type ComponentType } from "react";
import { authSessionEndedEvent, type StudioExpressionEditorContribution, type StudioExpressionEditorController, type StudioExpressionEditorProps, type StudioExpressionEditorSnapshot, type StudioExpressionReferenceFix } from "@elsa-workflows/studio-sdk";
import { AnchoredPopover, StudioButton } from "@elsa-workflows/studio-ui";
import { readPortableExpressionDefinition } from "./activityProperties";
import { formatArgumentType, useWorkflowReferenceAuthoring } from "./workflowReferenceAuthoring";
import { ActivityDataBrowser, focusBeside, useActivityDataSelectionHost } from "./ActivityDataChooser";
import type { ActivityDataContextResult, ActivityDataSourceFacts } from "./api/activityDataSelection";
import { useExpressionResultDiagnostic } from "./useExpressionResultDiagnostic";
import { expressionEditingState, expressionValueFromHistory, planActivityExpressionInsertion } from "./activityExpressionInsertion";

type SourceFix = Pick<StudioExpressionReferenceFix, "from" | "to" | "replacement">;

type Props = StudioExpressionEditorProps & {
  editor: ComponentType<StudioExpressionEditorProps>;
  contribution: StudioExpressionEditorContribution | null;
  ownerValue: unknown;
  reusableOutput?: "referenceKey" | "name";
};

/** One surface of the current owner. Chooser state, cursor tokens and controllers are never authored data. */
export function ExpressionEditorWithDataInsertion(props: Props) {
  return props.contribution?.sourceInsertion?.findArgumentFixes
    ? <ScopedExpressionEditor {...props} />
    : <ExpressionEditor {...props} />;
}

function ScopedExpressionEditor(props: Props) {
  return <ExpressionEditor {...props} references={useWorkflowReferenceAuthoring()} />;
}

function ExpressionEditor({ editor: Editor, contribution, ownerValue, reusableOutput, references, ...props }: Props & {
  references?: ReturnType<typeof useWorkflowReferenceAuthoring>;
}) {
  const host = useActivityDataSelectionHost();
  const chooserId = useId();
  const chooserPanel = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const controller = useRef<StudioExpressionEditorController | null>(null);
  const snapshot = useRef<StudioExpressionEditorSnapshot | null>(null);
  const pending = useRef<"picker" | { fix: SourceFix; source: string } | null>(null);
  const openedIdentity = useRef<string | null>(null);
  const openedOwner = useRef<object | undefined>(undefined);
  const [activating, setActivating] = useState(false);
  const [open, setOpen] = useState(false);
  const [scopeOpen, setScopeOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const document = props.context.document;
  const target = { nodeId: document?.activityId ?? "", inputKey: document?.propertyKey ?? props.descriptor.referenceKey ?? props.descriptor.name };
  const identity = `${props.context.editorSessionScope}\u001f${document?.id}\u001f${props.syntax}\u001f${contribution?.id}`;
  const textMode = props.context.expressionDescriptors.some(item => item.type === props.syntax && item.editingMode === "text");
  const adapter = contribution?.sourceInsertion;
  const scoped = !!adapter?.findArgumentFixes && references?.status === "ready";
  const inputs = useMemo(() => scoped && reusableOutput ? references.workflowInputs : [], [references?.workflowInputs, reusableOutput, scoped]);
  const variables = useMemo(() => scoped ? references.visibleVariables.filter((variable, index, all) =>
    all.findIndex(other => other.name === variable.name) === index) : [], [references?.visibleVariables, scoped]);
  const hasEarlierOutputs = !reusableOutput && host?.expressionInsertion === true && !!document;
  const hasScope = !hasEarlierOutputs && (inputs.length > 0 || variables.length > 0);
  const available = textMode && (hasEarlierOutputs || hasScope);
  const editable = textMode && !props.disabled && !props.context.readOnly;
  const emptyOutput = reusableOutput && scoped && ownerValue == null;
  const state = expressionEditingState(emptyOutput ? "" : ownerValue, props.syntax);
  const canInsert = available && editable && !!adapter && state.supported;
  const sourceText = typeof props.value === "string" ? props.value : "";
  const diagnostic = useExpressionResultDiagnostic(
    editable && state.supported && !reusableOutput && !props.descriptor.isSensitive && !props.descriptor.isCredential
      ? contribution?.resultCheck : undefined,
    sourceText, props.syntax, props.descriptor, ownerValue, target, host
  );
  const resultFix = diagnostic?.fix;
  const capable = canInsert || !!resultFix;
  const fixes = useMemo(() => {
    if (!scoped || !canInsert || !adapter?.findArgumentFixes || !sourceText.includes("args")) return [];
    const parameters = readPortableExpressionDefinition(ownerValue, props.syntax)?.parameters ?? {};
    return adapter.findArgumentFixes(sourceText, name => {
      if (reusableOutput) {
        if (reusableOutput !== "referenceKey") return;
        if (inputs.some(input => input.referenceKey === name)) return;
        const named = inputs.find(input => input.name === name);
        if (named) return { namespace: "args", name: named.referenceKey };
        const matches = inputs.filter(input => input.referenceKey.toLowerCase() === name.toLowerCase() || input.name.toLowerCase() === name.toLowerCase());
        if (matches.length === 1) return { namespace: "args", name: matches[0].referenceKey };
      } else if (!Object.prototype.hasOwnProperty.call(parameters, name) && variables.some(variable => variable.name === name)) {
        return { namespace: "variables", name };
      }
    });
  }, [adapter, canInsert, inputs, ownerValue, props.syntax, reusableOutput, scoped, sourceText, variables]);
  const current = useRef({ props, ownerValue, host, target, identity, capable, contribution, hasScope, state, sourceText, fixes, diagnostic });
  current.current = { props, ownerValue, host, target, identity, capable, contribution, hasScope, state, sourceText, fixes, diagnostic };

  const applyFix = useCallback((fix: SourceFix, source: string, editor: StudioExpressionEditorController) => {
    const now = current.current;
    const captured = editor.capture();
    const stillOffered = [now.diagnostic?.fix, ...now.fixes].some(offered =>
      offered?.from === fix.from && offered.to === fix.to && offered.replacement === fix.replacement);
    if (!now.capable || !now.state.supported || !stillOffered || !captured || captured.source !== source || now.sourceText !== source ||
        !editor.apply({ expected: captured, range: fix, insert: fix.replacement, historyValue: now.state.historyValue }))
      setNotice("The expression changed. Review the available fix again.");
  }, []);
  const register = useCallback((next: StudioExpressionEditorController | null) => {
    controller.current = next;
    const action = pending.current;
    if (!next || !action || !current.current.capable) return;
    pending.current = null;
    setActivating(false);
    if (action !== "picker") {
      applyFix(action.fix, action.source, next);
      return;
    }
    snapshot.current = next.capture();
    if (!snapshot.current) return;
    if (current.current.hasScope) setScopeOpen(true);
    else setOpen(true);
  }, [applyFix]);
  const close = useCallback((focus: "opener" | "next" | "previous" | "none") => {
    pending.current = null; openedIdentity.current = null; snapshot.current = null; setActivating(false); setOpen(false); setScopeOpen(false);
    if (focus !== "none") requestAnimationFrame(() => focus === "opener" ? trigger.current?.focus() : focusBeside(trigger.current, focus));
  }, []);
  useEffect(() => { close("none"); setNotice(null); }, [close, identity, capable, host?.ownerContext]);
  useEffect(() => {
    const revoked = () => close("none");
    window.addEventListener(authSessionEndedEvent, revoked);
    return () => window.removeEventListener(authSessionEndedEvent, revoked);
  }, [close]);

  const begin = () => {
    if (!canInsert) return;
    setNotice(null);
    openedIdentity.current = current.current.identity;
    openedOwner.current = current.current.host?.ownerContext;
    snapshot.current = controller.current?.capture() ?? null;
    if (snapshot.current) {
      if (hasScope) setScopeOpen(true);
      else setOpen(true);
    }
    else { pending.current = "picker"; setActivating(true); }
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
  const applyScoped = (namespace: "args" | "variables", name: string) => {
    const captured = snapshot.current;
    if (!captured || !capable || !state.supported || !adapter ||
        identity !== openedIdentity.current || captured.source !== sourceText) {
      close("opener");
      setNotice("The expression changed. Choose the insertion position again.");
      return;
    }
    const result = adapter.render({ namespace, alias: name, memberPath: [], source: captured.source, selection: captured.selection });
    if (!result.supported) {
      close("opener");
      setNotice(result.reason);
      return;
    }
    if (!controller.current?.apply({ expected: captured, insert: result.insert, historyValue: state.historyValue })) {
      close("opener");
      setNotice("The expression changed. Choose the insertion position again.");
      return;
    }
    close("none");
  };
  const startFix = (fix: SourceFix) => {
    setNotice(null);
    if (controller.current) applyFix(fix, sourceText, controller.current);
    else { pending.current = { fix, source: sourceText }; setActivating(true); }
  };
  const action = available || diagnostic ? <>
    {available ? <button ref={trigger} type="button" className="wf-choose-data-trigger wf-expression-insert-data" aria-haspopup="dialog"
      aria-expanded={open || scopeOpen} disabled={!canInsert} title={!contribution?.sourceInsertion ? "This editor does not support data insertion." : !state.supported ? state.reason : "Insert a reference at the current selection"}
      onPointerDown={event => { if (controller.current?.capture()) event.preventDefault(); }} onClick={() => open || scopeOpen ? close("opener") : begin()}>
      Insert data
    </button> : null}
    {scopeOpen && hasScope ? <AnchoredPopover anchorRef={trigger}
      portalContainer={trigger.current?.closest(".wf-property-editor-dialog")} open className="wf-data-chooser"
      minWidth={220} onDismiss={() => close("none")} onPositioned={() => chooserPanel.current?.querySelector("button")?.focus()}>
      <div ref={chooserPanel} role="dialog" aria-labelledby={chooserId} className="wf-data-chooser-panel"
        onKeyDown={event => {
          if (event.key === "Escape") {
            event.stopPropagation();
            close("opener");
          }
          if (event.key === "Tab") {
            const buttons = event.currentTarget.querySelectorAll("button");
            if (event.target !== buttons[event.shiftKey ? 0 : buttons.length - 1]) return;
            event.preventDefault();
            close(event.shiftKey ? "previous" : "next");
          }
        }}>
        <div id={chooserId} className="wf-data-chooser-heading">Insert data</div>
        {inputs.length > 0 ? <div role="group" aria-label="Activity inputs" className="wf-data-chooser-group">
          <span className="wf-data-chooser-group-label">Activity inputs</span>
          {inputs.map(input => <StudioButton key={input.referenceKey} variant="ghost" size="sm"
            onClick={() => applyScoped("args", reusableOutput === "referenceKey" ? input.referenceKey : input.name)}>{input.displayName || input.name} · {formatArgumentType(input.type)}</StudioButton>)}
        </div> : null}
        {variables.length > 0 ? <div role="group" aria-label="Variables" className="wf-data-chooser-group">
          <span className="wf-data-chooser-group-label">{reusableOutput ? "Activity variables" : "Variables"}</span>
          {variables.map(variable => <StudioButton key={`${variable.scopeId}:${variable.referenceKey}`} variant="ghost" size="sm"
            onClick={() => applyScoped("variables", variable.name)}>{variable.name}{variable.type ? ` · ${formatArgumentType(variable.type)}` : null}</StudioButton>)}
        </div> : null}
      </div>
    </AnchoredPopover> : null}
    {fixes.map(fix => <div className="wf-expression-reference-fix" key={fix.from}>
      {reusableOutput
        ? <span><code>{fix.current}</code> is unavailable in a stable-key expression.</span>
        : <span><code>{fix.current}</code> is unavailable here.</span>}
      <StudioButton variant="ghost" size="sm" onClick={() => startFix(fix)}>Use <code>{fix.replacement}</code></StudioButton>
    </div>)}
    {diagnostic ? <div className="wf-expression-reference-fix">
      <span role="status">{diagnostic.message}</span>
      {resultFix ? <StudioButton variant="ghost" size="sm" onClick={() => startFix(resultFix)}>{resultFix.label}</StudioButton> : null}
    </div> : null}
    {activating ? <span role="status">Opening expression editor…</span> : null}
    {notice ? <span role="alert">{notice}</span> : null}
    {open && host && host.ownerContext === openedOwner.current ? <ActivityDataBrowser key={identity} host={host} target={target} inputLabel={props.descriptor.displayName || props.descriptor.name}
      anchorRef={trigger} open current={null} onSelect={() => {}} onClose={close} expression={{ type: props.syntax, onSelect: choose }} /> : null}
  </> : undefined;
  return <><Editor {...props} actions={action} documentEditing={(capable || editable && !!contribution?.resultCheck) && state.supported ? {
    historyValue: state.historyValue, interactionActive: open || scopeOpen || activating, onController: register
  } : undefined} onChange={(value, edit) => {
    if (current.current.props.disabled || current.current.props.context.readOnly) return;
    if (!edit) { props.onChange(value); return; }
    const restored = typeof value === "string" ? expressionValueFromHistory(value, edit.historyValue, props.syntax) : null;
    if (restored) props.onChange(restored.value, edit);
  }} />{available && !props.disabled && !props.context.readOnly && (!contribution?.sourceInsertion || !state.supported)
    ? <p className="wf-property-hint" role="status">{!state.supported ? state.reason : "This installed editor does not support data insertion. The expression is preserved."}</p> : null}</>;
}
