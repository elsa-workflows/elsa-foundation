import type { BpmnDocumentAuthoring, BpmnDocumentCanvasEditorContext, WorkflowCanvasState } from "@elsa-workflows/studio-workflows";
import { collectBpmnNativeCommandGroups, type NativeCommandEvents } from "./bpmnDocumentCommands";
import { createBpmnDocumentModelerSession, type BpmnDocumentModelerSession } from "./bpmnDocumentModelerSession";

type NativeElement = { id: string; businessObject?: { id?: string }; labelTarget?: NativeElement; di?: { id?: string } };
interface NativeCanvas {
  getRootElement(): NativeElement;
  getRootElements(): NativeElement[];
  setRootElement(element: NativeElement, ...args: unknown[]): NativeElement;
  viewbox(box?: unknown): { inner?: { x: number; y: number; width: number; height: number }; outer?: { width: number; height: number } };
  zoom(value: string): void;
  resized(): void;
}
export interface BpmnDocumentNativeModeler {
  importXML(xml: string, diagramId: string): Promise<{ warnings: readonly unknown[] }>;
  saveXML(): Promise<{ xml: string }>;
  get<T>(name: string): T;
  attachTo(container: HTMLElement): void;
  detach(): void;
  destroy(): void;
}
interface Editing { complete(): void; isActive(): boolean }
interface Dragging { context(): unknown; end(): void }
interface NativeEvents {
  on(name: string, priority: number, handler: (event: Record<string, unknown>) => unknown): void;
}
interface Commands { execute(...args: unknown[]): void; undo(): void; redo(): void; clear(): void }
const identity = (element: NativeElement | undefined) => element?.businessObject?.id ?? element?.id;

/** Retained by a resource registration. Detaching React's view never destroys native pending work. */
export function createBpmnDocumentController(options: {
  context: BpmnDocumentCanvasEditorContext;
  authoring: BpmnDocumentAuthoring;
  createModeler(): Promise<BpmnDocumentNativeModeler>;
}) {
  let context = options.context, attached: HTMLElement | null = null, modeler: BpmnDocumentNativeModeler | undefined;
  let session: BpmnDocumentModelerSession | undefined, initializing: Promise<void> | undefined, synchronizing: Promise<void> | undefined;
  let importing = false, completing = false, detachingInteraction = false, retired = false, selecting = false, navigating = false, executing = 0, version = 0;
  let removeCollector: (() => void) | undefined;
  let modeledRoot: string | undefined;
  let failure: string | undefined, state: WorkflowCanvasState = { status: "loading" };
  const listeners = new Set<() => void>();
  const interaction = () => !!modeler && (modeler.get<Editing>("directEditing").isActive() || !!modeler.get<Dragging>("dragging").context());
  const work = () => { const native = session?.readWork() ?? { pending: false, sequence: 0, acceptedSequence: 0 };
    return { ...native, pending: native.pending || interaction() }; };
  const notify = () => {
    const facts = work();
    state = { status: failure ? "failed" : importing || !session || !context.ready ? "loading" : "ready", pending: facts.pending, ...(failure ? { message: failure } : {}) };
    version++;
    // The parent observer synchronously samples cached work; view listeners are observation only.
    try { context.onStateChange(state); } catch { /* Parent admission/work retention does not depend on a view observer. */ }
    for (const listener of listeners) { try { listener(); } catch { /* Fallible view only. */ } }
  };
  const refuse = (error: unknown) => { failure = error instanceof Error ? error.message : String(error); notify(); };
  const host = context.createSessionHost("bpmn-js", { readWork: work, async checkpoint() {
    if (initializing) await initializing;
    if (!session) return { acknowledgement: { ...context.readLive(), acceptedSequence: 0 }, release() {} };
    let lease: Awaited<ReturnType<BpmnDocumentModelerSession["checkpoint"]>>;
    try { lease = await session.checkpoint(); } catch (error) { refuse(error); throw error; }
    notify();
    return { acknowledgement: lease.acknowledgement, release() { lease.release(); notify(); } };
  } });
  const canExecute = () => !retired && (detachingInteraction || !!session?.canMutate() && (completing || !!attached && !navigating && (!modeledRoot || executing > 0)
    && context.ready && !context.readOnly && (context.canMutate?.() ?? true)));
  const navigate = (scope: BpmnDocumentCanvasEditorContext["scope"]) => {
    navigating = true;
    try {
      void Promise.resolve(context.onNavigateScope(scope)).catch(refuse).finally(() => { navigating = false; });
    } catch (error) { navigating = false; refuse(error); }
  };
  const navigateModeledRoot = () => {
    if (!modeledRoot || !attached || navigating || work().pending) return;
    if (modeledRoot === context.scope.scopeElementId) { modeledRoot = undefined; return; }
    const scope = context.resolveStandardScope(modeledRoot);
    if (scope) navigate(scope);
  };
  const select = () => {
    if (!modeler || importing || !session?.canMutate()) return;
    const selected = context.selection?.elementId;
    const element = selected ? modeler.get<{ get(id: string): NativeElement | undefined }>("elementRegistry").get(selected) : undefined;
    selecting = true;
    try { modeler.get<{ select(element: NativeElement | null): void }>("selection").select(element ?? null); }
    finally { selecting = false; }
  };
  const fit = () => {
    if (!modeler || !attached) return;
    const canvas = modeler.get<NativeCanvas>("canvas");
    canvas.resized();
    const { inner, outer } = canvas.viewbox();
    if (!inner?.width || !inner.height || !outer?.width || !outer.height) { canvas.zoom("fit-viewport"); return; }
    const palette = attached.querySelector(".djs-palette")?.getBoundingClientRect(), bounds = attached.getBoundingClientRect();
    const left = Math.max(24, palette ? palette.right - bounds.left + 24 : 24), top = 24;
    const width = Math.max(1, outer.width - left - 24), height = Math.max(1, outer.height - top - 48);
    const scale = Math.min(1, width / inner.width, height / inner.height);
    canvas.viewbox({ x: inner.x - (left + (width - inner.width * scale) / 2) / scale,
      y: inner.y - (top + (height - inner.height * scale) / 2) / scale, width: outer.width / scale, height: outer.height / scale });
  };
  const synchronize = async () => {
    if (!session || !context.ready || !attached || work().pending || session.canMutate()) { select(); return; }
    if (synchronizing) return synchronizing;
    importing = true; notify();
    const task = session.open().then(() => { failure = undefined; modeledRoot = undefined; fit(); select(); }, error => { refuse(error); });
    synchronizing = task;
    try { await task; } finally { importing = false; synchronizing = undefined; notify(); }
  };
  async function initialize() {
    if (retired || session || !context.ready) return;
    if (initializing) return initializing;
    const task = (async () => {
      try {
        modeler = await options.createModeler();
        if (attached) modeler.attachTo(attached);
        const native = modeler, canvas = native.get<NativeCanvas>("canvas"), commands = native.get<Commands>("commandStack");
        const events = native.get<NativeEvents>("eventBus"), execute = commands.execute.bind(commands), setRoot = canvas.setRootElement.bind(canvas);
        session = createBpmnDocumentModelerSession({ authoring: options.authoring, scope: context.scope, readLive: () => context.readLive(),
          commit: async request => host.commit(request), lookup: async request => host.lookup(request), onWorkChanged: notify,
          modeler: { importXML: (xml, diagramId) => native.importXML(xml, diagramId), saveXML: () => native.saveXML(),
            clearHistory: () => commands.clear(),
            activateScope(scopeId) {
              const root = canvas.getRootElements().find(element => identity(element) === scopeId);
              if (!root) throw new Error("The selected standard BPMN scope was not rendered by the native importer.");
              setRoot(root);
            },
            completeEditing() {
              completing = true;
              try {
                const dragging = native.get<Dragging>("dragging");
                if (dragging.context()) dragging.end();
                native.get<Editing>("directEditing").complete();
              } finally { completing = false; }
            }
          } });
        commands.execute = (...args) => {
          if (!canExecute()) throw new Error("The native edit belongs to an unavailable or changed authoring source. Its local view is retained.");
          executing++;
          try { execute(...args); } finally { executing--; }
        };
        commands.undo = () => { if (attached && !context.readOnly) context.onUndo(); };
        commands.redo = () => { if (attached && !context.readOnly) context.onRedo(); };
        events.on("commandStack.canExecute", 30000, () => canExecute() ? undefined : false);
        events.on("directEditing.activate.allowed", 30000, () => canExecute() ? undefined : false);
        events.on("drag.init", 30000, () => canExecute() ? undefined : false);
        for (const name of ["directEditing.activate", "drag.init", "drag.ended", "drag.canceled"])
          events.on(name, -30000, () => { notify(); });
        // Upstream fires deactivate before clearing its active textbox, including unchanged text.
        events.on("directEditing.deactivate", -30000, () => { queueMicrotask(notify); });
        removeCollector = collectBpmnNativeCommandGroups(native.get<NativeCommandEvents>("eventBus"), group => {
          session!.enqueue(group);
          void session!.flush().then(navigateModeledRoot, refuse);
        }, error => { session!.refuse(error); refuse(error); });
        events.on("selection.changed", 1000, event => {
          if (!attached || importing || selecting) return;
          const selected = (event.newSelection as NativeElement[] | undefined)?.[0], target = selected?.labelTarget ?? selected;
          const id = identity(target);
          context.onSelect(id ? { elementId: id, ...(target?.di?.id ? { diId: target.di.id } : {}) } : null);
        });
        canvas.setRootElement = (root, ...args) => {
          if (importing || !attached) return setRoot(root, ...args);
          const id = identity(root), current = canvas.getRootElement();
          if (!id || id === context.scope.scopeElementId) return setRoot(root, ...args);
          // A modeling group may create/replace the process/collaboration root. Keep the native
          // command coherent, then navigate the parent only after that whole group is acknowledged.
          if (executing) { modeledRoot = id; return setRoot(root, ...args); }
          const scope = context.resolveStandardScope(id);
          if (scope && !navigating) navigate(scope);
          return current;
        };
        await synchronize();
      } catch (error) { refuse(error); }
    })();
    initializing = task;
    try { await task; } finally { initializing = undefined; notify(); }
  }
  return {
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    getVersion: () => version,
    read: () => state,
    readWork: work,
    update(next: BpmnDocumentCanvasEditorContext) { if (retired) throw new Error("The native canvas resource has retired."); context = next; void initialize().then(synchronize); select(); queueMicrotask(navigateModeledRoot); },
    attach(container: HTMLElement) {
      if (retired) throw new Error("The native canvas resource has retired.");
      attached = container; host.reattach(); modeler?.attachTo(container);
      void initialize().then(synchronize); notify();
      return () => {
        if (attached !== container) return;
        // Finish the already active local gesture before removing its DOM. Its captured command
        // still belongs to the old baseline; the parent independently refuses stale authority.
        // This also releases the pinned Dragging implementation's document event listeners.
        detachingInteraction = true;
        try {
          const dragging = modeler?.get<Dragging>("dragging");
          if (dragging?.context()) dragging.end();
          modeler?.get<Editing>("directEditing").complete();
        } catch (error) { session?.refuse(error); refuse(error); }
        finally { detachingInteraction = false; attached = null; host.detach(); modeler?.detach(); notify(); }
      };
    },
    retire() {
      if (!retired) {
        if (attached || initializing || synchronizing) throw new Error("The native canvas is still attached or importing.");
        host.retire(); // Refuses pending work and outstanding checkpoint leases before destruction.
        retired = true;
      }
      // A fallible destroy can be retried without retiring the parent registration twice.
      removeCollector?.(); removeCollector = undefined;
      modeler?.destroy(); modeler = undefined; session = undefined;
      listeners.clear();
    },
    async retry() {
      if (retired) throw new Error("The native canvas resource has retired.");
      failure = undefined;
      try { if (session?.readWork().pending) await session.retry(); else await initialize().then(synchronize); notify(); navigateModeledRoot(); }
      catch (error) { refuse(error); throw error; }
    },
    undo() { if (attached && !context.readOnly) context.onUndo(); },
    redo() { if (attached && !context.readOnly) context.onRedo(); }
  };
}
