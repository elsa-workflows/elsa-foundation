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
    const ports: HTMLElement[] = [];
    for (let parent = field.parentElement; parent; parent = parent.parentElement) {
      const style = view.getComputedStyle(parent);
      if (/(auto|scroll)/.test(style.overflowY) && parent.clientHeight > 0) ports.push(parent);
    }
    // Native whole-field reveal handles inner clipping; fixed overlays need the outer scrollport.
    const port = ports.at(-1) ?? document.scrollingElement as HTMLElement | null;
    const visible: Bounds = { left: 0, right: view.innerWidth, top: 0, bottom: view.innerHeight };
    for (let parent = port; parent; parent = parent.parentElement) {
      const style = view.getComputedStyle(parent);
      const bounds = parent.getBoundingClientRect();
      if (/(auto|scroll|hidden|clip)/.test(style.overflowY) && parent.clientHeight > 0) {
        const top = bounds.top + parent.clientTop;
        visible.top = Math.max(visible.top, top);
        visible.bottom = Math.min(visible.bottom, top + parent.clientHeight);
      }
      if (/(auto|scroll|hidden|clip)/.test(style.overflowX) && parent.clientWidth > 0) {
        const left = bounds.left + parent.clientLeft;
        visible.left = Math.max(visible.left, left);
        visible.right = Math.min(visible.right, left + parent.clientWidth);
      }
    }
    const innerHeight = Math.min(...ports.filter(element => element !== port).map(element => element.clientHeight));
    return { port, visible, innerHeight };
  };

  const controlLabels = (focused: HTMLElement): HTMLElement[] => {
    const labels = Array.from((focused as HTMLInputElement).labels ?? []);
    const named = (focused.getAttribute("aria-labelledby") ?? "").split(/\s+/).map(id => document.getElementById(id)).filter((element): element is HTMLElement => element !== null);
    return [...new Set<HTMLElement>([...labels, ...named])];
  };

  const controlBounds = (focused: HTMLElement): Bounds => {
    const control = focused.getBoundingClientRect();
    const bounds = controlLabels(focused).map(label => label.getBoundingClientRect());
    return {
      left: Math.min(control.left, ...bounds.map(label => label.left)), right: Math.max(control.right, ...bounds.map(label => label.right)),
      top: Math.min(control.top, ...bounds.map(label => label.top)), bottom: Math.max(control.bottom, ...bounds.map(label => label.bottom))
    };
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

    // Measure the natural scrollport, never the padding footprint installed by a prior reveal.
    // Removing clearance may clamp scrollTop; restore its requested position after the final padding.
    const previousPadding = padding;
    const previousScrollTop = previousPadding?.element.scrollTop;
    restoreStyle(padding); padding = null;
    focused.scrollIntoView?.({ block: "nearest", inline: "nearest", behavior: "instant" });
    // Supporting help and validation belong to the reveal target as well as its border.
    const field = focused.closest<HTMLElement>(".studio-field")
      ?? focused.closest<HTMLElement>(".studio-form-control, label") ?? focused;
    let { port, visible, innerHeight } = geometry(field);
    if (previousPadding && previousPadding.element !== port) clearance = 0;
    if (!port || visible.bottom <= visible.top) { release(); return; }

    let boundedControl = false;
    let target = fieldBounds(field);
    let availableHeight = Math.max(0, Math.min(visible.bottom - visible.top - gap * 2, innerHeight));
    if (target.bottom - target.top <= availableHeight) {
      field.scrollIntoView?.({ block: "nearest", inline: "nearest", behavior: "instant" });
      ({ port, visible, innerHeight } = geometry(field));
      if (!port) { release(); return; }
      target = fieldBounds(field);
      availableHeight = Math.max(0, Math.min(visible.bottom - visible.top - gap * 2, innerHeight));
    }
    if (target.bottom - target.top > availableHeight) {
      boundedControl = true;
      // A tall composite cannot be revealed as a whole. Its label still needs native inner scrolling.
      for (const label of controlLabels(focused)) label.scrollIntoView?.({ block: "nearest", inline: "nearest", behavior: "instant" });
      focused.scrollIntoView?.({ block: "nearest", inline: "nearest", behavior: "instant" });
      ({ port, visible, innerHeight } = geometry(field));
      if (!port) { release(); return; }
      target = controlBounds(focused);
      availableHeight = Math.max(0, Math.min(visible.bottom - visible.top - gap * 2, innerHeight));
    }
    // An unfittable native editor retains its normal caret/scroll behavior; never hide a notice.
    if (target.bottom - target.top > availableHeight) { release(); return; }
    const ownerBounds = port.getBoundingClientRect();
    let top = Math.max(visible.top, ownerBounds.top + port.clientTop + gap);

    const host = document.querySelector<HTMLElement>(".studio-toast-host");
    restoreStyle(toastLimit); toastLimit = null;
    if (host && host.getClientRects().length && overlapsHorizontally(target, host.getBoundingClientRect())) {
      const bounds = host.getBoundingClientRect();
      const controls = [...host.querySelectorAll<HTMLElement>(".studio-toast-dismiss")].map(button => {
        const toast = button.closest<HTMLElement>(".studio-toast")!;
        const style = view.getComputedStyle(toast);
        return button.getBoundingClientRect().height + [style.paddingTop, style.paddingBottom, style.borderTopWidth, style.borderBottomWidth].reduce((sum, value) => sum + (parseFloat(value) || 0), 0);
      }).filter(height => height > 0);
      const minimum = controls.length ? Math.max(...controls) : bounds.height;
      const budget = bounds.bottom - top - minimum - gap;
      if (target.bottom - target.top > budget) { boundedControl = true; target = controlBounds(focused); }
      if (target.bottom - target.top > budget || target.bottom - target.top > availableHeight) {
        release(); focused.scrollIntoView?.({ block: "nearest", inline: "nearest", behavior: "instant" }); return;
      }
      const limit = Math.max(minimum, bounds.bottom - top - (target.bottom - target.top) - gap);
      if (bounds.height > limit) {
        toastLimit = saveStyle(host, "max-height");
        host.style.setProperty("max-height", `${limit}px`, "important");
      }
    }

    const safeBottom = (bounds: Bounds) => {
      const owner = port!.getBoundingClientRect();
      let bottom = Math.min(visible.bottom, owner.top + port!.clientTop + port!.clientHeight - gap);
      let obstructedColumn = false;
      if (host) {
        const hostBounds = host.getBoundingClientRect();
        for (const toast of host.querySelectorAll<HTMLElement>(".studio-toast")) {
          const toastBounds = toast.getBoundingClientRect();
          const start = Math.max(toastBounds.top, hostBounds.top, visible.top);
          const end = Math.min(toastBounds.bottom, hostBounds.bottom, visible.bottom);
          if (end > start && overlapsHorizontally(bounds, toastBounds) && view.getComputedStyle(toast).visibility !== "hidden") {
            obstructedColumn = true;
            bottom = Math.min(bottom, start - gap);
          }
        }
      }
      return { bottom, obstructedColumn };
    };
    const obstruction = safeBottom(target);
    let bottom = obstruction.bottom;
    const style = view.getComputedStyle(port);
    const base = parseFloat(style.paddingBlockEnd) || 0;
    const contentHeight = Math.max(0, port.clientHeight - (parseFloat(style.paddingBlockStart) || 0) - base);
    const reserve = obstruction.obstructedColumn && (target.bottom > bottom || clearance > 0)
      ? Math.min(contentHeight, Math.max(0, ownerBounds.top + port.clientTop + port.clientHeight - bottom)) : 0;
    if (reserve > 0) {
      padding = previousPadding?.element === port ? previousPadding : saveStyle(port, "padding-block-end");
      const scrollTop = previousPadding?.element === port ? previousScrollTop! : port.scrollTop;
      port.style.setProperty("padding-block-end", `${base + reserve}px`, "important");
      scrollInstantly(port, scrollTop);
    }
    clearance = reserve;

    // Native reveals and restoring the natural padding can both move the focused content.
    ({ visible } = geometry(field));
    top = Math.max(visible.top, port.getBoundingClientRect().top + port.clientTop + gap);
    target = boundedControl ? controlBounds(focused) : fieldBounds(field);
    ({ bottom } = safeBottom(target));
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
      mutations.observe(root, { childList: true, characterData: true, subtree: true, attributes: true, attributeFilter: ["hidden"] });
      for (let parent: HTMLElement | null = root; parent; parent = parent.parentElement) {
        if (!measured.has(parent)) { resize?.observe(parent); measured.add(parent); }
      }
    }
  };
  document.addEventListener("focusin", schedule);
  document.addEventListener("focusout", schedule);
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
          document.removeEventListener("focusout", schedule);
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
