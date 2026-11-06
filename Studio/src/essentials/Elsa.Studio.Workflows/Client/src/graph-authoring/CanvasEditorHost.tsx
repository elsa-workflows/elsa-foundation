import { Component, Suspense, lazy, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from "react";
import type { StudioCanvasEditorPreferenceStore, StudioGraphAuthoringResourceKind, StudioWorkflowDesignerCanvasContribution } from "@elsa-workflows/studio-sdk";
import type { WorkflowCanvasEditorContext, WorkflowCanvasState } from "./canvasEditorContract";
import type { BpmnDocumentCanvasEditorContext } from "../bpmn/bpmnDocumentSurfacePort";
import type { BpmnDocumentSelection } from "../bpmn/bpmnDocument";
import "../bpmn/bpmnEditorChoice.css";

const legacyProtocol = { id: "elsa.workflows.controlled-canvas", version: 1 };
export function matchingCanvasEditors(contributions: StudioWorkflowDesignerCanvasContribution[], resourceKind: StudioGraphAuthoringResourceKind, structure?: { kind: string; schemaVersion: string } | null,
  protocol: { id: string; version: number } = legacyProtocol) {
  return structure ? contributions.filter(editor => editor.protocol.id === protocol.id && editor.protocol.version === protocol.version
    && editor.supportedResourceKinds.includes(resourceKind)
    && editor.supportedStructures.some(supported => supported.kind === structure.kind && supported.schemaVersions.includes(structure.schemaVersion))) : [];
}

export function useCanvasEditorChoice(contributions: StudioWorkflowDesignerCanvasContribution[], resourceKind: StudioGraphAuthoringResourceKind,
  structure: { kind: string; schemaVersion: string } | null | undefined, sessionKey: string, preferences?: StudioCanvasEditorPreferenceStore,
  protocol: { id: string; version: number } = legacyProtocol) {
  const [preferredId, setPreferredId] = useState("integrated");
  const [reported, setReported] = useState<{ key: string; state: WorkflowCanvasState }>({ key: "", state: { status: "loading" } });
  const choiceRevision = useRef(0);
  useEffect(() => {
    let retired = false;
    const revision = ++choiceRevision.current;
    setPreferredId("integrated");
    void preferences?.load().then(id => { if (!retired && revision === choiceRevision.current && id) setPreferredId(id); }).catch(() => undefined);
    return () => { retired = true; };
  }, [preferences]);
  const editors = matchingCanvasEditors(contributions, resourceKind, structure, protocol);
  const active = editors.find(editor => editor.id === preferredId);
  const key = `${sessionKey}:${active?.id ?? "integrated"}`;
  const pending = !!active && (reported.key !== key || reported.state.status !== "ready");
  const choose = (id: string) => {
    ++choiceRevision.current;
    setPreferredId(id);
    setReported({ key: "", state: { status: "loading" } });
    void preferences?.save(id).catch(() => undefined);
  };
  return { editors, active, preferredId, pending, choose, report: (state: WorkflowCanvasState) => setReported({ key, state }) };
}

export function CanvasEditorChoice({ choice, beforeChange, onChoose }: { choice: ReturnType<typeof useCanvasEditorChoice>; beforeChange?(): void;
  /** Resource hosts perform the choice itself while retained canvas fences are held. */
  onChoose?(id: string): Promise<void> }) {
  const [changing, setChanging] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  if (!choice.editors.length && choice.preferredId === "integrated") return null;
  const unavailable = choice.preferredId !== "integrated" && !choice.active;
  return <label className="wf-bpmn-editor-choice">BPMN editor
    <select aria-label="BPMN editor" value={choice.preferredId} disabled={changing} onChange={event => {
      const id = event.target.value;
      if (!onChoose) { beforeChange?.(); choice.choose(id); return; }
      setChanging(true); setFailure(null);
      void onChoose(id).catch(error => setFailure(error instanceof Error ? error.message : String(error))).finally(() => setChanging(false));
    }}>
      <option value="integrated">Elsa</option>
      {choice.editors.map(editor => <option key={editor.id} value={editor.id}>{editor.title}</option>)}
      {unavailable ? <option value={choice.preferredId}>Saved editor unavailable — using Elsa</option> : null}
    </select>
    {failure ? <span role="alert">{failure}</span> : null}
  </label>;
}

class CanvasLoadBoundary extends Component<{ children: ReactNode; onFailure(error: Error): void; fallback: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch(error: Error) { this.props.onFailure(error); }
  render() { return this.state.failed ? this.props.fallback : this.props.children; }
}

/** The canvas can fail locally without taking its host document or inspector down. */
type CanvasContext = WorkflowCanvasEditorContext | BpmnDocumentCanvasEditorContext;
const isDocumentContext = (context: CanvasContext): context is BpmnDocumentCanvasEditorContext => context.protocol.id === "elsa.workflows.bpmn-document-canvas";
const canvasIdentity = (context: CanvasContext, editorId: string) => JSON.stringify([context.protocol, context.resourceKind, context.documentId,
  isDocumentContext(context) ? context.scope : context.scopeId, editorId]);
export function CanvasEditorHost({ editor, context, onFallback }: { editor: StudioWorkflowDesignerCanvasContribution; context: CanvasContext; onFallback(): void }) {
  const [attempt, setAttempt] = useState(0);
  const [failure, setFailure] = useState<string | null>(null);
  const latest = useRef(context);
  const ready = useRef(false);
  latest.current = context;
  const identity = canvasIdentity(context, editor.id);
  const lifetime = useMemo(() => ({ identity, attempt, active: true, Canvas: lazy(editor.load) }), [identity, editor, attempt]);
  useLayoutEffect(() => {
    lifetime.active = true;
    ready.current = false;
    latest.current.onStateChange({ status: "loading" });
    return () => { lifetime.active = false; ready.current = false; };
  }, [lifetime]);
  const LoadedCanvas = lifetime.Canvas;
  const current = () => lifetime.active && lifetime.identity === canvasIdentity(latest.current, editor.id);
  const shared = {
    onUndo: () => { if (current() && ready.current && !latest.current.readOnly) latest.current.onUndo(); },
    onRedo: () => { if (current() && ready.current && !latest.current.readOnly) latest.current.onRedo(); },
    onStateChange: (state: WorkflowCanvasState) => { if (current()) { ready.current = state.status === "ready"; latest.current.onStateChange(state); } }
  };
  const guarded: CanvasContext = isDocumentContext(context) ? {
    ...context, ...shared,
    createSessionHost: (id, participant) => {
      const live = latest.current;
      if (!current() || !isDocumentContext(live)) throw new Error("This canvas view has retired; its retained parent session remains authoritative.");
      return live.createSessionHost(id, participant);
    },
    retainCanvasController: (id, create) => {
      const live = latest.current;
      if (!current() || !isDocumentContext(live)) throw new Error("This canvas view has retired.");
      return live.retainCanvasController(id, create);
    },
    // Retained admission/checkpoint ports belong to the parent resource, not this view lifetime.
    onSelect: (selection: BpmnDocumentSelection | null) => { const live = latest.current; if (current() && ready.current && isDocumentContext(live)) live.onSelect(selection); },
    onNavigateScope: scope => { const live = latest.current; if (current() && ready.current && isDocumentContext(live)) return live.onNavigateScope(scope); }
  } : {
    ...context, ...shared,
    onChange: change => { const live = latest.current; return current() && ready.current && !live.readOnly && !isDocumentContext(live) ? live.onChange(change) : false; },
    onSelect: (id: string | null) => { const live = latest.current; if (current() && ready.current && !isDocumentContext(live)) live.onSelect(id); }
  };
  const fallback = <div role="alert"><p>{failure ?? "The canvas editor could not load."}</p>
    <button type="button" onClick={() => { setFailure(null); setAttempt(value => value + 1); }}>Retry loading editor</button>
    <button type="button" onClick={onFallback}>Use Elsa editor</button>
  </div>;
  return <CanvasLoadBoundary key={`${identity}:${attempt}`} fallback={fallback} onFailure={error => {
    setFailure(error.message);
    if (current()) latest.current.onStateChange({ status: "failed", message: error.message });
    lifetime.active = false;
    ready.current = false;
  }}><Suspense fallback={<p role="status">Loading BPMN editor…</p>}><LoadedCanvas context={guarded} /></Suspense>
    <button type="button" className="wf-canvas-editor-fallback" onClick={onFallback}>Use Elsa editor</button>
  </CanvasLoadBoundary>;
}
