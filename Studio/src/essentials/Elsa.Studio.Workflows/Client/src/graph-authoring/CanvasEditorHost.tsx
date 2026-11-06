import { Component, Suspense, lazy, useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from "react";
import type { StudioCanvasEditorPreferenceStore, StudioGraphAuthoringResourceKind, StudioWorkflowDesignerCanvasContribution } from "@elsa-workflows/studio-sdk";
import type { WorkflowCanvasEditorContext, WorkflowCanvasState } from "./canvasEditorContract";
import "../bpmn/bpmnEditorChoice.css";

export function matchingCanvasEditors(contributions: StudioWorkflowDesignerCanvasContribution[], resourceKind: StudioGraphAuthoringResourceKind, structure?: { kind: string; schemaVersion: string } | null) {
  return structure ? contributions.filter(editor => editor.protocol.id === "elsa.workflows.controlled-canvas" && editor.protocol.version === 1
    && editor.supportedResourceKinds.includes(resourceKind)
    && editor.supportedStructures.some(supported => supported.kind === structure.kind && supported.schemaVersions.includes(structure.schemaVersion))) : [];
}

export function useCanvasEditorChoice(contributions: StudioWorkflowDesignerCanvasContribution[], resourceKind: StudioGraphAuthoringResourceKind,
  structure: { kind: string; schemaVersion: string } | null | undefined, sessionKey: string, preferences?: StudioCanvasEditorPreferenceStore) {
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
  const editors = matchingCanvasEditors(contributions, resourceKind, structure);
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

export function CanvasEditorChoice({ choice, beforeChange }: { choice: ReturnType<typeof useCanvasEditorChoice>; beforeChange?(): void }) {
  if (!choice.editors.length && choice.preferredId === "integrated") return null;
  const unavailable = choice.preferredId !== "integrated" && !choice.active;
  return <label className="wf-bpmn-editor-choice">BPMN editor
    <select aria-label="BPMN editor" value={choice.preferredId} onChange={event => { beforeChange?.(); choice.choose(event.target.value); }}>
      <option value="integrated">Elsa</option>
      {choice.editors.map(editor => <option key={editor.id} value={editor.id}>{editor.title}</option>)}
      {unavailable ? <option value={choice.preferredId}>Saved editor unavailable — using Elsa</option> : null}
    </select>
  </label>;
}

class CanvasLoadBoundary extends Component<{ children: ReactNode; onFailure(error: Error): void; fallback: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch(error: Error) { this.props.onFailure(error); }
  render() { return this.state.failed ? this.props.fallback : this.props.children; }
}

/** The canvas can fail locally without taking its host document or inspector down. */
export function CanvasEditorHost({ editor, context, onFallback }: { editor: StudioWorkflowDesignerCanvasContribution; context: WorkflowCanvasEditorContext; onFallback(): void }) {
  const [attempt, setAttempt] = useState(0);
  const [failure, setFailure] = useState<string | null>(null);
  const latest = useRef(context);
  const ready = useRef(false);
  latest.current = context;
  const identity = `${context.resourceKind}:${context.documentId}:${context.scopeId}:${editor.id}`;
  const lifetime = useMemo(() => ({ identity, attempt, active: true, Canvas: lazy(editor.load) }), [identity, editor, attempt]);
  useLayoutEffect(() => {
    lifetime.active = true;
    ready.current = false;
    latest.current.onStateChange({ status: "loading" });
    return () => { lifetime.active = false; ready.current = false; };
  }, [lifetime]);
  const LoadedCanvas = lifetime.Canvas;
  const current = () => lifetime.active && lifetime.identity === `${latest.current.resourceKind}:${latest.current.documentId}:${latest.current.scopeId}:${editor.id}`;
  const guarded: WorkflowCanvasEditorContext = {
    ...context,
    onChange: change => current() && ready.current && !latest.current.readOnly ? latest.current.onChange(change) : false,
    onSelect: id => { if (current() && ready.current) latest.current.onSelect(id); },
    onUndo: () => { if (current() && ready.current && !latest.current.readOnly) latest.current.onUndo(); },
    onRedo: () => { if (current() && ready.current && !latest.current.readOnly) latest.current.onRedo(); },
    onStateChange: state => { if (current()) { ready.current = state.status === "ready"; latest.current.onStateChange(state); } }
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
