type Bounds = { left: number; right: number; top: number; bottom: number };
type SavedStyle = { element: HTMLElement; property: string; value: string; priority: string };
type FocusObserver = ReturnType<typeof createFocusObserver>;

const observers = new WeakMap<Document, FocusObserver>();
const gap = 8;
const toastLayoutEvent = "studio:toast-layout";

function scrollInstantly(element: HTMLElement, top: number) {
  if (typeof element.scrollTo === "function") element.scrollTo({ top, behavior: "instant" });
  else element.scrollTop = top;
}

function saveStyle(element: HTMLElement, property: string): SavedStyle {
  return { element, property, value: element.style.getPropertyValue(property), priority: element.style.getPropertyPriority(property) };
}

function restoreStyle(saved: SavedStyle | null) {
  if (!saved) return;
  if (saved.value) saved.element.style.setProperty(saved.property, saved.value, saved.priority);
  else saved.element.style.removeProperty(saved.property);
}

function overlapsHorizontally(a: Bounds, b: Bounds) {
  return a.left < b.right && a.right > b.left;
}

/** One owner per document: SettingsShell and its Theme Builder can register nested roots. */
function createFocusObserver(document: Document) {
  const view = document.defaultView!;
  const roots = new Map<HTMLElement, number>();
  let frame = 0;
  let padding: SavedStyle | null = null;
  let clearance = 0;
  let toastLimit: SavedStyle | null = null;

  const release = () => {
    restoreStyle(padding); padding = null; clearance = 0;
    restoreStyle(toastLimit); toastLimit = null;
  };

  const geometry = (field: HTMLElement) => {
    const visible: Bounds = { left: 0, right: view.innerWidth, top: 0, bottom: view.innerHeight };
    const ports: HTMLElement[] = [];
    // Internal value wrappers clip text intentionally; measure the whole field's ancestors.
    for (let parent = field.parentElement; parent; parent = parent.parentElement) {
      const style = view.getComputedStyle(parent);
      const bounds = parent.getBoundingClientRect();
      if (/(auto|scroll|hidden|clip)/.test(style.overflowY) && parent.clientHeight > 0) {
        const top = bounds.top + parent.clientTop;
        visible.top = Math.max(visible.top, top);
        visible.bottom = Math.min(visible.bottom, top + parent.clientHeight);
        if (/(auto|scroll)/.test(style.overflowY)) ports.push(parent);
      }
      if (/(auto|scroll|hidden|clip)/.test(style.overflowX) && parent.clientWidth > 0) {
        const left = bounds.left + parent.clientLeft;
        visible.left = Math.max(visible.left, left);
        visible.right = Math.min(visible.right, left + parent.clientWidth);
      }
    }
    // Prefer a real inner scroller. If content currently fits, reserve in the outer scrollport.
    const port = ports.find(element => element.scrollHeight > element.clientHeight + 1)
      ?? ports.at(-1) ?? document.scrollingElement as HTMLElement | null;
    return { port, visible };
  };

  const fieldBounds = (field: HTMLElement): Bounds => {
    const bounds = field.getBoundingClientRect();
    const label = field.querySelector(".studio-form-legend, label")?.getBoundingClientRect();
    return label ? {
      left: Math.min(bounds.left, label.left), right: Math.max(bounds.right, label.right),
      top: Math.min(bounds.top, label.top), bottom: Math.max(bounds.bottom, label.bottom)
    } : bounds;
  };

  const reveal = () => {
    const focused = document.activeElement;
    if (!(focused instanceof view.HTMLElement) || !focused.getClientRects().length
      || ![...roots.keys()].some(root => root.contains(focused) && root.getClientRects().length)) {
      release();
      return;
    }

    focused.scrollIntoView?.({ block: "nearest", inline: "nearest", behavior: "instant" });
    const field = focused.closest<HTMLElement>(".studio-form-control, .studio-choice-field, label") ?? focused;
    let { port, visible } = geometry(field);
    if (padding && padding.element !== port) {
      restoreStyle(padding); padding = null; clearance = 0;
      ({ port, visible } = geometry(field));
    }
    if (!port || visible.bottom <= visible.top) { release(); return; }

    let target = fieldBounds(field);
    const availableHeight = Math.max(0, visible.bottom - visible.top - gap * 2);
    // Unfittable editors retain native caret/scroll behavior instead of an impossible reveal loop.
    if (target.bottom - target.top > availableHeight) { release(); return; }

    const host = document.querySelector<HTMLElement>(".studio-toast-host");
    restoreStyle(toastLimit); toastLimit = null;
    if (host && host.getClientRects().length && overlapsHorizontally(target, host.getBoundingClientRect())) {
      const bounds = host.getBoundingClientRect();
      const limit = Math.max(0, bounds.bottom - visible.top - (target.bottom - target.top) - gap * 2);
      if (bounds.height > limit) {
        toastLimit = saveStyle(host, "max-height");
        host.style.setProperty("max-height", `${limit}px`, "important");
      }
    }

    let bottom = visible.bottom - gap;
    let obstructedColumn = false;
    if (host) {
      const hostBounds = host.getBoundingClientRect();
      for (const toast of host.querySelectorAll<HTMLElement>(".studio-toast")) {
        const bounds = toast.getBoundingClientRect();
        const top = Math.max(bounds.top, hostBounds.top, visible.top);
        const end = Math.min(bounds.bottom, hostBounds.bottom, visible.bottom);
        if (end > top && overlapsHorizontally(target, bounds) && view.getComputedStyle(toast).visibility !== "hidden") {
          obstructedColumn = true;
          bottom = Math.min(bottom, top - gap);
        }
      }
    }

    const reserve = obstructedColumn && (target.bottom > bottom || clearance > 0)
      ? Math.max(0, visible.bottom - bottom) : 0;
    if (reserve > 0) {
      padding ??= saveStyle(port, "padding-block-end");
      const scrollTop = port.scrollTop;
      restoreStyle(padding);
      const base = parseFloat(view.getComputedStyle(port).paddingBlockEnd) || 0;
      port.style.setProperty("padding-block-end", `${base + reserve}px`, "important");
      scrollInstantly(port, scrollTop);
      clearance = reserve;
    } else if (padding) {
      restoreStyle(padding); padding = null; clearance = 0;
    }

    target = fieldBounds(field);
    const top = visible.top + gap;
    const oversized = target.bottom - target.top > bottom - top;
    let delta = 0;
    if (oversized || target.top < top) delta = target.top - top;
    else if (target.bottom > bottom) delta = target.bottom - bottom;
    if (delta) scrollInstantly(port, port.scrollTop + delta);
  };

  const schedule = () => {
    view.cancelAnimationFrame(frame);
    frame = view.requestAnimationFrame(() => { frame = 0; reveal(); });
  };
  const resize = typeof view.ResizeObserver === "function" ? new view.ResizeObserver(schedule) : null;
  const mutations = new view.MutationObserver(schedule);
  const observeRoots = () => {
    resize?.disconnect(); mutations.disconnect();
    const measured = new Set<HTMLElement>();
    for (const root of roots.keys()) {
      mutations.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ["hidden"] });
      for (let parent: HTMLElement | null = root; parent; parent = parent.parentElement) {
        if (!measured.has(parent)) { resize?.observe(parent); measured.add(parent); }
      }
    }
  };
  document.addEventListener("focusin", schedule);
  document.addEventListener(toastLayoutEvent, schedule);
  view.addEventListener("resize", schedule);

  return {
    schedule,
    reveal,
    add(root: HTMLElement) {
      roots.set(root, (roots.get(root) ?? 0) + 1); observeRoots(); schedule();
      return () => {
        const count = roots.get(root) ?? 0;
        if (count > 1) roots.set(root, count - 1);
        else roots.delete(root);
        observeRoots();
        if (roots.size) schedule();
        else {
          view.cancelAnimationFrame(frame); resize?.disconnect(); mutations.disconnect();
          document.removeEventListener("focusin", schedule); document.removeEventListener(toastLayoutEvent, schedule);
          view.removeEventListener("resize", schedule);
          release(); observers.delete(document);
        }
      };
    }
  };
}

export function observeFocusedContent(root: HTMLElement) {
  const document = root.ownerDocument;
  let observer = observers.get(document);
  if (!observer) { observer = createFocusObserver(document); observers.set(document, observer); }
  return observer.add(root);
}

export function revealFocusedContent(root: HTMLElement) {
  observers.get(root.ownerDocument)?.reveal();
}

/** Private layout signal; notification lifetime, focus and history stay owned by ToastHost. */
export function notifyToastLayoutChange(document: Document) {
  const view = document.defaultView;
  if (view) document.dispatchEvent(new view.Event(toastLayoutEvent));
}
