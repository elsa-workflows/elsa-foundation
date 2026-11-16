import type { Locator, Page } from "@playwright/test";

type HeldToast = Element & {
  __studioToastPlacementHold?: { assertCurrent(): void; release(): void };
};

/** Test-driver control of one real toast's existing hover pause; no clocks or notification inputs are changed. */
export function installToastPlacementHold(element: Element): void {
  const node = element as HeldToast;
  const view = element.ownerDocument.defaultView;
  if (!view || !element.isConnected || !element.matches('.studio-toast[data-level="success"]') || node.__studioToastPlacementHold) {
    throw new Error("Placement control requires one attached, unheld real success toast.");
  }
  const title = element.querySelector(".studio-toast-title")?.textContent;
  if (!title?.trim()) throw new Error("The real confirmation has no title.");
  let active = true;
  const assertCurrent = () => {
    if (!active || !element.isConnected || !element.matches('.studio-toast[data-level="success"]')
      || element.querySelector(".studio-toast-title")?.textContent !== title) {
      throw new Error("The captured placement confirmation disappeared or changed; do not reacquire it.");
    }
  };
  const enter = () => element.dispatchEvent(new view.MouseEvent("mouseover", { bubbles: true, relatedTarget: null }));
  // React has already delivered the normal leave AND destination enter before window's bubble listener runs.
  // Re-enter this one held element in the same task, before its resumed timeout can fire. Never swallow events.
  const maintainHold = (event: MouseEvent) => {
    if (!active || !(event.target instanceof view.Node) || !element.contains(event.target)
      || (event.relatedTarget instanceof view.Node && element.contains(event.relatedTarget))) return;
    assertCurrent();
    enter();
  };
  view.addEventListener("mouseout", maintainHold);
  node.__studioToastPlacementHold = {
    assertCurrent,
    release: () => {
      if (!active) return;
      active = false;
      view.removeEventListener("mouseout", maintainHold);
      delete node.__studioToastPlacementHold;
      if (element.isConnected) element.dispatchEvent(new view.MouseEvent("mouseout", { bubbles: true, relatedTarget: null }));
    }
  };
  enter();
}

export function assertToastPlacementHold(element: Element): void {
  const hold = (element as HeldToast).__studioToastPlacementHold;
  if (!hold) throw new Error("The placement control is no longer installed.");
  hold.assertCurrent();
}

export function releaseToastPlacementHold(element: Element): void {
  (element as HeldToast).__studioToastPlacementHold?.release();
}

export interface ToastPlacementHold { assertCurrent(): Promise<void>; release(): Promise<void> }
const heldByPage = new WeakMap<Page, Set<ToastPlacementHold>>();

export async function holdToastForPlacement(page: Page, locator: Locator): Promise<ToastPlacementHold> {
  const element = await locator.elementHandle();
  if (!element) throw new Error("The real confirmation is missing.");
  // Yield through paint before exercising the mounted timer handlers; real application time continues normally.
  await element.evaluate(node => new Promise<void>(resolve => {
    const view = node.ownerDocument.defaultView!;
    view.requestAnimationFrame(() => view.requestAnimationFrame(() => resolve()));
  }));
  await element.evaluate(installToastPlacementHold);
  const holds = heldByPage.get(page) ?? new Set<ToastPlacementHold>();
  heldByPage.set(page, holds);
  let released = false;
  const hold: ToastPlacementHold = {
    assertCurrent: async () => { if (!released) await element.evaluate(assertToastPlacementHold); },
    release: async () => {
      if (released) return;
      released = true;
      holds.delete(hold);
      if (!holds.size) heldByPage.delete(page);
      try { await element.evaluate(releaseToastPlacementHold); }
      finally { await element.dispose(); }
    }
  };
  holds.add(hold);
  return hold;
}

export async function assertToastPlacementHolds(page: Page): Promise<void> {
  for (const hold of heldByPage.get(page) ?? []) await hold.assertCurrent();
}


/** Playwright afterEach runs before the page fixture is disposed, including failed assertions. */
export async function releaseToastPlacementHolds(page: Page): Promise<void> {
  for (const hold of [...(heldByPage.get(page) ?? [])].reverse()) await hold.release();
}
