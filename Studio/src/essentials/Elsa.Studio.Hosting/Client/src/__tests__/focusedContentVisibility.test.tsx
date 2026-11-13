import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { createNotificationController, type StudioNotificationController } from "../sdk";
import { useCompactNavigation } from "../app/navigation/useCompactNavigation";
import { StudioField } from "../app/ui/forms/Field";
import { ToastHost } from "../app/ui/feedback/ToastHost";

let container: HTMLDivElement, root: Root;
async function update(action: () => void) {
  flushSync(action);
  for (let i = 0; i < 5; i++) await new Promise(resolve => setTimeout(resolve, 0));
}
function Content({ children }: { children: React.ReactNode }) {
  const { contentRef } = useCompactNavigation();
  return <div ref={contentRef}>{children}</div>;
}
function Fixture({ controller }: { controller: StudioNotificationController }) {
  return <>
    <main id="focus-scrollport" style={{ overflowY: "auto", paddingBlockEnd: "20px" }}>
      <Content><Content><StudioField label="Primary"><input defaultValue="Retained token" /></StudioField></Content></Content>
    </main>
    <button className="studio-notification-bell">Notifications</button>
    <ToastHost controller={controller} navigate={() => {}} />
  </>;
}

beforeEach(() => {
  vi.stubGlobal("matchMedia", () => ({ matches: false, addEventListener() {}, removeEventListener() {} }));
  vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => window.setTimeout(() => callback(0), 0));
  vi.stubGlobal("cancelAnimationFrame", (id: number) => window.clearTimeout(id));
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
});
afterEach(async () => {
  await update(() => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.unstubAllGlobals();
});

/** Model browser clipping/clamping, including a last field with no initial scroll extent. */
async function mount() {
  const resizeObservers: Array<{ targets: Set<Element>; callback: ResizeObserverCallback }> = [];
  vi.stubGlobal("ResizeObserver", class {
    targets = new Set<Element>();
    constructor(public callback: ResizeObserverCallback) { resizeObservers.push(this); }
    observe(element: Element) { this.targets.add(element); }
    unobserve(element: Element) { this.targets.delete(element); }
    disconnect() { this.targets.clear(); }
  });
  let scroll = 0, toastHeight = 56, fieldHeight = 60;
  const port = () => container.querySelector<HTMLElement>("#focus-scrollport");
  vi.spyOn(Element.prototype, "getBoundingClientRect").mockImplementation(function (this: Element) {
    const offset = port()?.scrollTop ?? 0;
    if (this.matches(".studio-toast-host")) {
      const count = this.querySelectorAll(".studio-toast").length;
      const limit = parseFloat((this as HTMLElement).style.maxHeight) || 300;
      const height = Math.min(limit, count * (toastHeight + 8) - 8);
      return new DOMRect(16, 544 - height, 358, height);
    }
    if (this.matches(".studio-toast")) {
      const host = this.parentElement!, bounds = host.getBoundingClientRect();
      const index = [...host.children].indexOf(this);
      return new DOMRect(16, bounds.top + index * (toastHeight + 8) - host.scrollTop, 358, toastHeight);
    }
    if (this.matches(".studio-form-control")) return new DOMRect(20, 500 - offset, 350, fieldHeight);
    if (this.matches(".studio-form-legend, .studio-field-label")) return new DOMRect(35, 500 - offset, 100, 16);
    if (this.matches("input")) return new DOMRect(30, 520 - offset, 330, 32);
    return new DOMRect(0, 180, 390, 380);
  });
  vi.spyOn(Element.prototype, "getClientRects").mockImplementation(function (this: Element) {
    return (this.isConnected ? [this.getBoundingClientRect()] : []) as unknown as DOMRectList;
  });
  const controller = createNotificationController();
  await update(() => root.render(<Fixture controller={controller} />));
  const scrollport = port()!;
  const extra = () => Math.max(0, (parseFloat(scrollport.style.paddingBlockEnd) || 20) - 20);
  Object.defineProperties(scrollport, {
    clientHeight: { configurable: true, get: () => 380 },
    clientWidth: { configurable: true, get: () => 390 },
    scrollHeight: { configurable: true, get: () => 380 + extra() },
    scrollTop: { configurable: true, get: () => (scroll = Math.min(scroll, extra())), set: (value: number) => { scroll = Math.max(0, Math.min(extra(), value)); } }
  });
  scrollport.scrollTo = (options?: ScrollToOptions | number, y?: number) => {
    scrollport.scrollTop = typeof options === "number" ? y ?? 0 : options?.top ?? 0;
  };
  const field = container.querySelector<HTMLInputElement>("input")!;
  const frame = field.closest<HTMLElement>(".studio-form-control")!;
  const assertUncovered = () => {
    const bounds = frame.getBoundingClientRect(), host = container.querySelector(".studio-toast-host")!.getBoundingClientRect();
    expect(bounds.top).toBeGreaterThanOrEqual(180);
    expect(bounds.bottom).toBeLessThanOrEqual(host.top - 7);
    expect(document.activeElement).toBe(field);
    expect(field.value).toBe("Retained token");
    expect([field.selectionStart, field.selectionEnd]).toEqual([2, 7]);
  };
  await update(() => { field.focus(); field.setSelectionRange(2, 7); });
  return { controller, field, frame, scrollport, assertUncovered,
    setToastHeight: (height: number) => { toastHeight = height; },
    setFieldHeight: (height: number) => { fieldHeight = height; },
    resizeToasts: () => {
      const host = container.querySelector(".studio-toast-host");
      for (const observer of resizeObservers) if (host && observer.targets.has(host)) observer.callback([], observer as unknown as ResizeObserver);
    }
  };
}

it("reveals the complete last field when a notice appears after focus, without double reservation from nested owners", async () => {
  const f = await mount();
  await update(() => { f.controller.api.notify({ level: "success", title: "Theme duplicated" }); });
  f.assertUncovered();
  expect(f.scrollport.style.paddingBlockEnd).toBe("100px");
  expect(f.scrollport.scrollTop).toBe(80);
});

it("responds to wrapped persistent notices and restores scroll styles without taking focus from toast actions", async () => {
  const f = await mount();
  await update(() => { f.controller.api.notify({ level: "error", title: "A persistent notice" }); });
  f.setToastHeight(120);
  await update(f.resizeToasts);
  f.assertUncovered();
  const dismiss = container.querySelector<HTMLButtonElement>('[aria-label="Dismiss notification"]')!;
  await update(() => dismiss.focus());
  expect(document.activeElement).toBe(dismiss);
  expect(f.scrollport.style.paddingBlockEnd).toBe("20px");
  await update(() => dismiss.click());
  expect(container.querySelector(".studio-toast-host")).toBeNull();
  expect(f.controller.getEntries()).toHaveLength(1);
  expect(f.controller.getEntries()[0].toasting).toBe(false);
  expect(f.field.value).toBe("Retained token");
});

it("keeps a field reachable below the header with a tall stack and restores the stack limit when focus leaves", async () => {
  const f = await mount(); f.setToastHeight(100);
  await update(() => { for (const title of ["One", "Two", "Three"]) f.controller.api.notify({ level: "error", title }); });
  f.assertUncovered();
  const host = container.querySelector<HTMLElement>(".studio-toast-host")!;
  expect(parseFloat(host.style.maxHeight)).toBeLessThan(300);
  await update(() => container.querySelector<HTMLButtonElement>(".studio-notification-bell")!.focus());
  expect(host.style.maxHeight).toBe("");
  expect(f.scrollport.style.paddingBlockEnd).toBe("20px");
});

it("retains native behavior for an editor that cannot fit and releases reservations on unmount", async () => {
  const f = await mount();
  await update(() => { f.controller.api.notify({ level: "error", title: "Notice" }); });
  f.assertUncovered();
  f.setFieldHeight(700);
  await update(() => window.dispatchEvent(new Event("resize")));
  expect(f.scrollport.style.paddingBlockEnd).toBe("20px");
  expect(document.activeElement).toBe(f.field);
  await update(() => root.render(<p>Another route</p>));
  expect(f.scrollport.style.paddingBlockEnd).toBe("20px");
});
