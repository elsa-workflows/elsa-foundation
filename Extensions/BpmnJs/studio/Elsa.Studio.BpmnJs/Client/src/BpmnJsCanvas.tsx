import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import type { ActivityNode, DesignMetadataRecord, WorkflowCanvasEditorContext } from "@elsa-workflows/studio-workflows";
import { mergeBpmnJsDefinitions, projectBpmnJsXml } from "./bpmnJsBridge";
import "bpmn-js/dist/assets/diagram-js.css";
import "bpmn-js/dist/assets/bpmn-font/css/bpmn.css";
import "./bpmnJsCanvas.css";

export interface BpmnJsCanvasState {
  status: "loading" | "ready" | "refused" | "failed";
  message?: string;
}

export interface BpmnJsCanvasProps {
  owner: ActivityNode;
  layout: DesignMetadataRecord[];
  selectedElementId: string | null;
  readOnly: boolean;
  onChange(next: {
    owner: ActivityNode;
    layout: DesignMetadataRecord[];
    baseOwner: ActivityNode;
    baseLayout: DesignMetadataRecord[];
  }): boolean;
  onSelect(elementId: string | null): void;
  onUndo(): void;
  onRedo(): void;
  onStateChange?(state: BpmnJsCanvasState): void;
}

// These are deliberately small runtime boundaries. The bridge accepts unknown moddle definitions
// and validates them; the host never receives a mutable bpmn-js object or its command history.
interface Modeler {
  importXML(xml: string): Promise<{ warnings: { message?: string }[] }>;
  getDefinitions(): unknown;
  get<T>(name: string): T;
  destroy(): void;
}

interface BpmnCanvas {
  zoom(scale: "fit-viewport"): void;
  viewbox(box?: unknown): unknown;
}

interface CanvasBounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

interface BpmnElementRegistry {
  get(id: string): unknown;
}

interface BpmnSelection {
  select(element: unknown | null): void;
}

interface BpmnCommandStack {
  execute(...args: unknown[]): void;
  undo(): void;
  redo(): void;
}

interface BpmnEventBus {
  on(name: string, priority: number, callback: (event: BpmnEvent) => unknown): void;
}

interface BpmnEvent {
  newSelection?: {
    id: string;
    labelTarget?: { id: string };
    businessObject?: { id?: string };
  }[];
}

interface Projection {
  id: number;
  xml: string;
  ownerNodeId: string;
}

function describeFailure(failure: unknown) {
  return failure instanceof Error ? failure.message : String(failure);
}

function fitInitialViewport(canvas: BpmnCanvas, container: HTMLDivElement | null) {
  const viewport = canvas.viewbox() as { inner?: CanvasBounds; outer?: { width: number; height: number } };
  const { inner, outer } = viewport;
  if (!inner?.width || !inner.height || !outer?.width || !outer.height) {
    canvas.zoom("fit-viewport");
    return;
  }
  // Native fit-viewport aligns large diagrams to their top-left bounds and includes the space
  // occupied by the palette. Fit into the unobstructed area with room for labels and attribution.
  const containerBounds = container?.getBoundingClientRect();
  const paletteBounds = container?.querySelector(".djs-palette")?.getBoundingClientRect();
  const left = Math.max(24, paletteBounds && containerBounds ? paletteBounds.right - containerBounds.left + 24 : 24);
  const top = 24;
  const availableWidth = Math.max(1, outer.width - left - 24);
  const availableHeight = Math.max(1, outer.height - top - 48);
  const scale = Math.min(1, availableWidth / inner.width, availableHeight / inner.height);
  canvas.viewbox({
    x: inner.x - (left + (availableWidth - inner.width * scale) / 2) / scale,
    y: inner.y - (top + (availableHeight - inner.height * scale) / 2) / scale,
    width: outer.width / scale,
    height: outer.height / scale
  });
}

/** Experimental controlled canvas. Elsa's complete authoring document remains authoritative. */
export function BpmnJsCanvas(props: BpmnJsCanvasProps) {
  const hostRef = useRef<HTMLDivElement>(null);
  const modelerRef = useRef<Modeler | null>(null);
  const latestRef = useRef(props);
  latestRef.current = props;
  const baselineRef = useRef({ owner: props.owner, layout: props.layout });
  const canonicalSignatureRef = useRef("");
  const desiredRef = useRef<Projection | null>(null);
  const importedXmlRef = useRef<string | null>(null);
  const importedOwnerRef = useRef<string | null>(null);
  const readyRef = useRef(false);
  const importingRef = useRef(false);
  const emittingRef = useRef(false);
  const selectingRef = useRef(false);
  const projectionIdRef = useRef(0);
  const pumpRef = useRef<(() => void) | null>(null);
  const [state, setState] = useState<BpmnJsCanvasState>({ status: "loading" });
  const [notice, setNotice] = useState("");
  const [loadAttempt, setLoadAttempt] = useState(0);
  const reportRef = useRef((next: BpmnJsCanvasState) => {
    setState(next);
    latestRef.current.onStateChange?.(next);
  });

  const canMutate = () => readyRef.current && !importingRef.current && !emittingRef.current && !latestRef.current.readOnly
    && importedOwnerRef.current === latestRef.current.owner.nodeId;

  const requestProjectionRef = useRef((force = false) => {
    // A host may acknowledge via flushSync. Its render/effects must not reimport a half-acknowledged
    // command before onChange returns; acceptance below advances both sides together.
    if (emittingRef.current) return;
    const current = latestRef.current;
    try {
      const xml = projectBpmnJsXml(current.owner, current.layout);
      if (!force && xml === importedXmlRef.current && current.owner.nodeId === importedOwnerRef.current && readyRef.current) return;
      readyRef.current = false;
      desiredRef.current = { id: ++projectionIdRef.current, xml, ownerNodeId: current.owner.nodeId };
      reportRef.current({ status: "loading" });
      pumpRef.current?.();
    } catch (failure) {
      readyRef.current = false;
      desiredRef.current = null;
      ++projectionIdRef.current;
      reportRef.current({ status: "refused", message: describeFailure(failure) });
    }
  });

  useEffect(() => {
    let retired = false;
    let ownedModeler: Modeler | null = null;
    requestProjectionRef.current(true);
    const selectCurrent = (modeler: Modeler) => {
      const id = latestRef.current.selectedElementId;
      const element = id ? modeler.get<BpmnElementRegistry>("elementRegistry").get(id) : null;
      selectingRef.current = true;
      try { modeler.get<BpmnSelection>("selection").select(element ?? null); }
      finally { selectingRef.current = false; }
    };
    const pump = async () => {
      const modeler = ownedModeler;
      if (retired || !modeler || importingRef.current) return;
      importingRef.current = true;
      readyRef.current = false;
      try {
        while (!retired && desiredRef.current) {
          const requested = desiredRef.current;
          const sameScope = importedOwnerRef.current === requested.ownerNodeId;
          const canvas = modeler.get<BpmnCanvas>("canvas");
          const viewport = sameScope ? canvas.viewbox() : null;
          try {
            const result = await modeler.importXML(requested.xml);
            if (retired) return;
            if (desiredRef.current?.id !== requested.id) continue;
            if (result.warnings.length) throw new Error(`The process could not be represented without warnings: ${result.warnings.map(warning => warning.message ?? String(warning)).join("; ")}`);
            if (sameScope && viewport) canvas.viewbox(viewport);
            else fitInitialViewport(canvas, hostRef.current);
            importedXmlRef.current = requested.xml;
            importedOwnerRef.current = requested.ownerNodeId;
            desiredRef.current = null;
            selectCurrent(modeler);
            readyRef.current = true;
            reportRef.current({ status: "ready" });
          } catch (failure) {
            if (retired) return;
            if (desiredRef.current?.id !== requested.id) continue;
            desiredRef.current = null;
            reportRef.current({ status: "failed", message: describeFailure(failure) });
          }
        }
      } finally {
        importingRef.current = false;
      }
    };
    pumpRef.current = () => { void pump(); };
    void import("bpmn-js/lib/Modeler").then(({ default: BpmnModeler }) => {
      if (retired || !hostRef.current) return;
      const modeler = new BpmnModeler({
        container: hostRef.current,
        bpmnRenderer: {
          defaultFillColor: "var(--studio-canvas)",
          defaultStrokeColor: "var(--studio-text)",
          defaultLabelColor: "var(--studio-text)"
        },
        additionalModules: [{
          // The body of an Elsa-bound subprocess is not an empty BPMN child plane. Its existing
          // Inspector/Slots navigation is authoritative until actual nested projection is qualified.
          drilldownOverlayBehavior: ["value", {}],
          drilldownBreadcrumbs: ["value", {}]
        }]
      }) as unknown as Modeler;
      ownedModeler = modeler;
      modelerRef.current = modeler;
      const commands = modeler.get<BpmnCommandStack>("commandStack");
      const execute = commands.execute.bind(commands);
      // canExecute alone is advisory in diagram-js. Guard execution too, including keyboard/context
      // actions, so read-only and an outstanding import cannot mutate the local model underneath Elsa.
      commands.execute = (...args) => {
        if (!canMutate()) return;
        if (args[0] === "shape.toggleCollapse") {
          setNotice("Open embedded activity content from the Elsa inspector's Slots section. This canvas preserves that content in the current process.");
          return;
        }
        execute(...args);
      };
      commands.undo = () => { if (canMutate()) latestRef.current.onUndo(); };
      commands.redo = () => { if (canMutate()) latestRef.current.onRedo(); };
      const events = modeler.get<BpmnEventBus>("eventBus");
      events.on("commandStack.canExecute", 10000, () => canMutate() ? undefined : false);
      events.on("selection.changed", 1000, event => {
        if (!readyRef.current || importingRef.current || selectingRef.current || retired) return;
        const element = event.newSelection?.[0];
        // External labels have their own diagram id; Elsa configures the owning BPMN element.
        const selected = element?.labelTarget?.id ?? element?.businessObject?.id ?? element?.id ?? null;
        if (selected !== latestRef.current.selectedElementId) latestRef.current.onSelect(selected);
      });
      events.on("commandStack.changed", 1000, () => {
        if (!canMutate() || retired) return;
        try {
          const baseline = baselineRef.current;
          const next = mergeBpmnJsDefinitions(baseline.owner, modeler.getDefinitions(), baseline.layout);
          let accepted: boolean;
          emittingRef.current = true;
          try {
            accepted = latestRef.current.onChange({ ...next, baseOwner: baseline.owner, baseLayout: baseline.layout });
          } finally {
            emittingRef.current = false;
          }
          if (!accepted) throw new Error("The process changed before this canvas edit could apply. Repeat the edit on the current process.");
          // Advance only after the host acknowledges its revision check. Another modeling command
          // can run before React renders; never merge it against the previous activity inventory.
          baselineRef.current = next;
          importedXmlRef.current = projectBpmnJsXml(next.owner, next.layout);
          setNotice("");
        } catch (failure) {
          const reason = `Change refused: ${describeFailure(failure)} The current Elsa process is preserved.`;
          setNotice(reason);
          readyRef.current = false;
          reportRef.current({ status: "refused", message: reason });
          baselineRef.current = { owner: latestRef.current.owner, layout: latestRef.current.layout };
          // Restore the current canonical document, including unsaved Elsa property edits. No invalid
          // moddle state enters autosave/publication and no detached XML editing buffer is retained.
          requestProjectionRef.current(true);
        }
      });
      void pump();
    }).catch(failure => {
      if (!retired) reportRef.current({ status: "failed", message: describeFailure(failure) });
    });
    return () => {
      retired = true;
      readyRef.current = false;
      importingRef.current = false;
      if (modelerRef.current === ownedModeler) modelerRef.current = null;
      pumpRef.current = null;
      ownedModeler?.destroy();
      importedXmlRef.current = null;
      importedOwnerRef.current = null;
    };
  }, [loadAttempt]);

  useEffect(() => {
    const signature = JSON.stringify([props.owner, props.layout]);
    if (canonicalSignatureRef.current !== signature) {
      canonicalSignatureRef.current = signature;
      baselineRef.current = { owner: props.owner, layout: props.layout };
    }
    requestProjectionRef.current();
  }, [props.owner, props.layout]);

  useEffect(() => {
    const modeler = modelerRef.current;
    if (!modeler || !readyRef.current || importingRef.current) return;
    const element = props.selectedElementId ? modeler.get<BpmnElementRegistry>("elementRegistry").get(props.selectedElementId) : null;
    selectingRef.current = true;
    try { modeler.get<BpmnSelection>("selection").select(element ?? null); }
    finally { selectingRef.current = false; }
  }, [props.selectedElementId]);

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const target = event.target as HTMLElement;
    if (target.closest("input, textarea, select, [contenteditable='true']")) return;
    if (!(event.metaKey || event.ctrlKey) || event.altKey) return;
    const key = event.key.toLowerCase();
    if (key !== "z" && key !== "y") return;
    event.preventDefault();
    event.stopPropagation();
    if (!canMutate()) return;
    if (key === "y" || event.shiftKey) latestRef.current.onRedo();
    else latestRef.current.onUndo();
  };

  return <section className="wf-bpmn-js" aria-label="bpmn-js process editor" data-bpmn-js-canvas data-bpmn-js-state={state.status} data-read-only={props.readOnly || undefined} onKeyDownCapture={handleKeyDown}>
    {notice ? <p className="wf-bpmn-js-notice" role="alert">{notice}</p> : null}
    {state.status === "refused" || state.status === "failed" ? <div className="wf-bpmn-js-notice" role="alert">
      <p>{state.message}</p>
      <p>The process remains available in the integrated editor.</p>
      {state.status === "failed" ? <button type="button" onClick={() => {
        if (modelerRef.current) requestProjectionRef.current(true);
        else setLoadAttempt(attempt => attempt + 1);
      }}>Retry loading this process</button> : null}
    </div> : null}
    {props.readOnly ? <p className="wf-bpmn-js-mode" role="status">Read-only process</p> : null}
    <div className="wf-bpmn-js-modeler" ref={hostRef} tabIndex={0} aria-label="BPMN modeling canvas" aria-busy={state.status === "loading"} />
    {state.status === "loading" ? <p className="wf-bpmn-js-loading" role="status">Loading BPMN canvas…</p> : null}
  </section>;
}

/** Registry boundary consumes only the explicitly matched Workflows protocol. */
export function BpmnJsCanvasEditor({ context }: { context: unknown }) {
  const controlled = context as WorkflowCanvasEditorContext;
  if (controlled.protocol?.id !== "elsa.workflows.controlled-canvas" || controlled.protocol.version !== 1) throw new Error("Unsupported Workflows canvas protocol.");
  return <BpmnJsCanvas {...controlled} />;
}
