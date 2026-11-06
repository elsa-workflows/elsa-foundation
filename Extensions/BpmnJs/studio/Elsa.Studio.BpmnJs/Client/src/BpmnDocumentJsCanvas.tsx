import { useLayoutEffect, useRef, useState, useSyncExternalStore, type KeyboardEvent } from "react";
import { loadBpmnDocumentAuthoring, type BpmnDocumentCanvasEditorContext } from "@elsa-workflows/studio-workflows";
import { createBpmnDocumentController, type BpmnDocumentNativeModeler } from "./bpmnDocumentController";
import "bpmn-js/dist/assets/diagram-js.css";
import "bpmn-js/dist/assets/bpmn-font/css/bpmn.css";
import "./bpmnJsCanvas.css";

/** The resource retains controller/Modeler state; this component owns its visible attachment only. */
export function BpmnDocumentJsCanvas({ context }: { context: BpmnDocumentCanvasEditorContext }) {
  const container = useRef<HTMLDivElement>(null);
  const controllerRef = useRef<ReturnType<typeof createBpmnDocumentController> | null>(null);
  const [controller, setController] = useState<ReturnType<typeof createBpmnDocumentController> | null>(null);
  const latest = useRef(context);
  useLayoutEffect(() => { latest.current = context; });
  const version = useSyncExternalStore(controller?.subscribe ?? (() => () => undefined), controller?.getVersion ?? (() => 0));
  useLayoutEffect(() => {
    let active = true, detach: (() => void) | undefined;
    void loadBpmnDocumentAuthoring().then(authoring => {
      if (!active || !container.current) return;
      const current = latest.current;
      const owned = current.retainCanvasController("bpmn-js", () => createBpmnDocumentController({ context: current, authoring, async createModeler() {
        const { default: Modeler } = await import("bpmn-js/lib/Modeler");
        return new Modeler({ bpmnRenderer: { defaultFillColor: "var(--studio-surface)", defaultStrokeColor: "var(--studio-text)", defaultLabelColor: "var(--studio-text)" },
          textRenderer: { defaultStyle: { fontFamily: "var(--studio-font-ui)" }, externalStyle: { fontFamily: "var(--studio-font-ui)" } } }) as unknown as BpmnDocumentNativeModeler;
      } }));
      controllerRef.current = owned;
      setController(owned);
      owned.update(current); detach = owned.attach(container.current);
      // Subscribe registration happens on the next render after the controller's initial state report.
      current.onStateChange(owned.read());
    }).catch(error => { if (active) latest.current.onStateChange({ status: "failed", message: error instanceof Error ? error.message : String(error) }); });
    return () => { active = false; detach?.(); };
  }, []);
  useLayoutEffect(() => { controllerRef.current?.update(context); }, [context]);
  void version;
  const state = controller?.read() ?? { status: "loading" as const };
  const history = (event: KeyboardEvent<HTMLElement>) => {
    if ((event.target as HTMLElement).closest("input,textarea,select,[contenteditable=true]") || !(event.ctrlKey || event.metaKey) || event.altKey) return;
    const key = event.key.toLowerCase();
    if (key !== "z" && key !== "y") return;
    event.preventDefault(); event.stopPropagation();
    if (key === "y" || event.shiftKey) controllerRef.current?.redo(); else controllerRef.current?.undo();
  };
  return <section className="wf-bpmn-js" aria-label="bpmn-js process editor" data-bpmn-js-canvas data-bpmn-js-state={state.status} data-read-only={context.readOnly || undefined} onKeyDownCapture={history}>
    {state.status === "failed" || state.status === "refused" ? <div role="alert" className="wf-bpmn-js-notice"><p>{state.message}</p><p>Pending native work remains with this authoring resource.</p><button type="button" onClick={() => { void controllerRef.current?.retry().catch(error => context.onStateChange({ status: "failed", pending: true, message: String(error) })); }}>Retry retained canvas</button></div> : null}
    <div className="wf-bpmn-js-modeler" ref={container} tabIndex={0} aria-label="BPMN modeling canvas" aria-busy={state.status === "loading"} />
    {state.status === "loading" ? <p role="status" className="wf-bpmn-js-loading">Loading BPMN canvas…</p> : null}
  </section>;
}

export function BpmnDocumentJsCanvasEditor({ context }: { context: unknown }) {
  const controlled = context as BpmnDocumentCanvasEditorContext;
  if (controlled.protocol?.id !== "elsa.workflows.bpmn-document-canvas" || controlled.protocol.version !== 1) throw new Error("Unsupported Workflows BPMN document canvas protocol.");
  return <BpmnDocumentJsCanvas context={controlled} />;
}
