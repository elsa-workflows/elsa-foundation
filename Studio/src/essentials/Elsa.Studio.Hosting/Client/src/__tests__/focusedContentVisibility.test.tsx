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
function Fixture({ controller, nested, supporting }: { controller: StudioNotificationController; nested?: boolean; supporting?: "hint" | "error" | "child" }) {
  const fields = <Content><Content><StudioField label="Primary" description={supporting === "hint" ? "Keep this instruction readable." : undefined}
    error={supporting === "error" ? "Choose a different value." : undefined}>
    <input defaultValue="Retained token" />{supporting === "child" ? <small>Keep the contributed instruction readable.</small> : null}
  </StudioField></Content></Content>;
  return <>
    <main id="focus-scrollport" style={{ overflowY: "auto", paddingBlockEnd: "20px" }}>
      {nested ? <div id="inner-scrollport" style={{ overflowY: "auto" }}>{fields}</div> : fields}
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
async function mount(nested?: "start" | "end", supporting?: "hint" | "error" | "child") {
  const resizeObservers: Array<{ targets: Set<Element>; callback: ResizeObserverCallback }> = [];
  vi.stubGlobal("ResizeObserver", class {
    targets = new Set<Element>();
    constructor(public callback: ResizeObserverCallback) { resizeObservers.push(this); }
    observe(element: Element) { this.targets.add(element); }
    unobserve(element: Element) { this.targets.delete(element); }
    disconnect() { this.targets.clear(); }
  });
  let scroll = 0, innerScroll = nested === "end" ? 300 : 0, toastHeight = 56, fieldHeight = 60, controlHeight = 32;
  const baseTop = nested === "start" ? 200 : nested === "end" ? 860 : 500;
  const port = () => container.querySelector<HTMLElement>("#focus-scrollport");
  vi.spyOn(Element.prototype, "getBoundingClientRect").mockImplementation(function (this: Element) {
    const offset = port()?.scrollTop ?? 0;
    if (this.matches(".studio-toast-host")) {
      const count = this.querySelectorAll(".studio-toast").length;
      const parsed = parseFloat((this as HTMLElement).style.maxHeight);
      const limit = Number.isFinite(parsed) ? parsed : 300;
      const height = Math.min(limit, count * (toastHeight + 8) - 8);
      return new DOMRect(16, 544 - height, 358, height);
    }
    if (this.matches(".studio-toast")) {
      const host = this.parentElement!, bounds = host.getBoundingClientRect();
      const index = [...host.children].indexOf(this);
      return new DOMRect(16, bounds.top + index * (toastHeight + 8) - host.scrollTop, 358, toastHeight);
    }
    if (this.matches(".studio-toast-dismiss")) return new DOMRect(340, 0, 28, 28);
    if (this.matches("#inner-scrollport")) return new DOMRect(0, (nested === "start" ? 180 : 320) - offset, 390, 300);
    const fieldTop = baseTop - offset - (nested ? innerScroll : 0);
    if (this.matches(".studio-form-field")) return new DOMRect(20, fieldTop, 350, fieldHeight + (supporting ? 36 : 0));
    if (this.matches(".studio-form-control")) return new DOMRect(20, fieldTop, 350, fieldHeight);
    if (this.matches("small")) return new DOMRect(20, fieldTop + fieldHeight + 4, 350, 32);
    if (this.matches(".studio-form-legend, .studio-field-label")) return new DOMRect(35, fieldTop, 100, 16);
    if (this.matches("input")) return new DOMRect(30, fieldTop + 20, 330, controlHeight);
    return new DOMRect(0, 180, 390, 380);
  });
  vi.spyOn(Element.prototype, "getClientRects").mockImplementation(function (this: Element) {
    return (this.isConnected ? [this.getBoundingClientRect()] : []) as unknown as DOMRectList;
  });
  const controller = createNotificationController();
  await update(() => root.render(<Fixture controller={controller} nested={Boolean(nested)} supporting={supporting} />));
  const scrollport = port()!;
  const extra = () => (nested ? 420 : 0) + (supporting ? 36 : 0)
    + Math.max(0, (parseFloat(scrollport.style.paddingBlockEnd) || 20) - 20);
  Object.defineProperties(scrollport, {
    clientHeight: { configurable: true, get: () => 380 },
    clientWidth: { configurable: true, get: () => 390 },
    scrollHeight: { configurable: true, get: () => 380 + extra() },
    scrollTop: { configurable: true, get: () => (scroll = Math.min(scroll, extra())), set: (value: number) => { scroll = Math.max(0, Math.min(extra(), value)); } }
  });
  scrollport.scrollTo = (options?: ScrollToOptions | number, y?: number) => {
    scrollport.scrollTop = typeof options === "number" ? y ?? 0 : options?.top ?? 0;
  };
  const inner = container.querySelector<HTMLElement>("#inner-scrollport");
  if (inner) Object.defineProperties(inner, {
    clientHeight: { configurable: true, value: 300 }, clientWidth: { configurable: true, value: 390 },
    scrollHeight: { configurable: true, value: 600 },
    scrollTop: { configurable: true, get: () => innerScroll, set: (value: number) => { innerScroll = Math.max(0, Math.min(300, value)); } }
  });
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
  return { controller, field, frame, scrollport, inner, assertUncovered,
    setSupporting: (next: "hint" | "error") => root.render(<Fixture controller={controller} nested={Boolean(nested)} supporting={next} />),
    setToastHeight: (height: number) => { toastHeight = height; },
    setFieldHeight: (height: number) => { fieldHeight = height; },
    setControlHeight: (height: number) => { controlHeight = height; },
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

for (const supporting of ["hint", "error", "child"] as const) it(`keeps a last field's ${supporting} message above persistent notices`, async () => {
  const f = await mount(undefined, supporting);
  await update(() => { f.controller.api.notify({ level: "error", title: "Persistent notice" }); });
  f.assertUncovered();
  const message = container.querySelector("small")!, host = container.querySelector(".studio-toast-host")!;
  expect(message.getBoundingClientRect().bottom).toBeLessThanOrEqual(host.getBoundingClientRect().top - 8);
  expect(f.field.getAttribute("aria-describedby")?.split(/\s+/)).toContain(message.id);
});

it("reveals a still-focused field when validation replaces its supporting text", async () => {
  const f = await mount(undefined, "hint");
  await update(() => { f.controller.api.notify({ level: "error", title: "Persistent notice" }); });
  f.assertUncovered();
  // A pointer can scroll to a validation trigger without changing the retained editor focus.
  f.scrollport.scrollTop = 0;
  await update(() => f.setSupporting("error"));
  expect(container.querySelector("small")?.textContent).toBe("Choose a different value.");
  f.assertUncovered();
  expect(container.querySelector("small")!.getBoundingClientRect().bottom)
    .toBeLessThanOrEqual(container.querySelector(".studio-toast-host")!.getBoundingClientRect().top - 8);
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
  f.setFieldHeight(700); f.setControlHeight(680);
  await update(() => window.dispatchEvent(new Event("resize")));
  expect(f.scrollport.style.paddingBlockEnd).toBe("20px");
  expect(document.activeElement).toBe(f.field);
  await update(() => root.render(<p>Another route</p>));
  expect(f.scrollport.style.paddingBlockEnd).toBe("20px");
});

it("retains usable notification space when only a composite field's active control and label fit", async () => {
  const f = await mount(); f.setFieldHeight(350);
  await update(() => { f.controller.api.notify({ level: "error", title: "Persistent notice" }); });
  const host = container.querySelector<HTMLElement>(".studio-toast-host")!;
  const control = f.field.getBoundingClientRect(), label = container.querySelector(".studio-field-label")!.getBoundingClientRect();
  expect(host.getBoundingClientRect().height).toBeGreaterThanOrEqual(28);
  expect(Math.min(control.top, label.top)).toBeGreaterThanOrEqual(188);
  expect(control.bottom).toBeLessThanOrEqual(host.getBoundingClientRect().top - 8);
  expect(document.activeElement).toBe(f.field);
  expect([f.field.value, f.field.selectionStart, f.field.selectionEnd]).toEqual(["Retained token", 2, 7]);
});

it("does not hide a persistent notice when the native editor and notice cannot both fit", async () => {
  const f = await mount(); f.setFieldHeight(350); f.setControlHeight(330);
  await update(() => { f.controller.api.notify({ level: "error", title: "Persistent notice" }); });
  const host = container.querySelector<HTMLElement>(".studio-toast-host")!;
  expect(host.style.maxHeight).toBe("");
  expect(host.getBoundingClientRect().height).toBe(56);
  expect(document.activeElement).toBe(f.field);
  expect([f.field.value, f.field.selectionStart, f.field.selectionEnd]).toEqual(["Retained token", 2, 7]);
});

it("restores both temporary styles on blur without another focus target", async () => {
  const f = await mount(); f.setToastHeight(100);
  await update(() => { for (const title of ["One", "Two", "Three"]) f.controller.api.notify({ level: "error", title }); });
  const host = container.querySelector<HTMLElement>(".studio-toast-host")!;
  expect(host.style.maxHeight).not.toBe("");
  expect(f.scrollport.style.paddingBlockEnd).not.toBe("20px");
  await update(() => f.field.blur());
  expect(host.style.maxHeight).toBe("");
  expect(f.scrollport.style.paddingBlockEnd).toBe("20px");
  expect(f.controller.getEntries()).toHaveLength(3);
  expect(f.controller.getEntries().every(entry => entry.toasting)).toBe(true);
  expect(f.field.value).toBe("Retained token");
});

for (const bound of ["start", "end"] as const) it(`uses the outer scrollport when the inner port is at its ${bound} bound`, async () => {
  const f = await mount(bound);
  f.scrollport.scrollTop = bound === "start" ? 40 : 52;
  const before = f.frame.getBoundingClientRect(), control = f.field.getBoundingClientRect();
  expect(control.top).toBeGreaterThanOrEqual(180);
  expect(control.bottom).toBeLessThanOrEqual(560);
  expect(before.top < 180 || before.bottom > 560).toBe(true);
  await update(() => window.dispatchEvent(new Event("resize")));
  const after = f.frame.getBoundingClientRect();
  expect(after.top).toBeGreaterThanOrEqual(188);
  expect(after.bottom).toBeLessThanOrEqual(552);
  expect(f.inner!.scrollTop).toBe(bound === "start" ? 0 : 300);
  expect(document.activeElement).toBe(f.field);
  expect([f.field.value, f.field.selectionStart, f.field.selectionEnd]).toEqual(["Retained token", 2, 7]);
});

it("reveals the associated label through the inner clip when the composite frame cannot fit", async () => {
  const f = await mount("start"); f.setFieldHeight(350);
  const label = container.querySelector<HTMLElement>(".studio-field-label")!;
  // Model the browser's nearest native scroll through the actual inner port, including clamping.
  const nativeNearest = (element: HTMLElement) => {
    const bounds = element.getBoundingClientRect(), clip = f.inner!.getBoundingClientRect();
    if (bounds.top < clip.top) f.inner!.scrollTop += bounds.top - clip.top;
    else if (bounds.bottom > clip.bottom) f.inner!.scrollTop += bounds.bottom - clip.bottom;
  };
  label.scrollIntoView = () => nativeNearest(label);
  f.field.scrollIntoView = () => nativeNearest(f.field);
  f.inner!.scrollTop = 30;
  expect(f.field.getBoundingClientRect().top).toBeGreaterThanOrEqual(f.inner!.getBoundingClientRect().top);
  expect(label.getBoundingClientRect().top).toBeLessThan(f.inner!.getBoundingClientRect().top);
  await update(() => window.dispatchEvent(new Event("resize")));
  expect(label.getBoundingClientRect().top).toBeGreaterThanOrEqual(f.inner!.getBoundingClientRect().top);
  expect(f.field.getBoundingClientRect().bottom).toBeLessThanOrEqual(f.inner!.getBoundingClientRect().bottom);
  expect(document.activeElement).toBe(f.field);
  expect([f.field.value, f.field.selectionStart, f.field.selectionEnd]).toEqual(["Retained token", 2, 7]);
});


it("bounds clearance by the natural content height across repeated reveals and a dock shrink", async () => {
  let naturalHeight = 150, scroll = 0;
  const controller = createNotificationController();
  await update(() => root.render(<>
    <main id="short-port" style={{ overflowY: "auto", paddingBlockStart: "16px", paddingBlockEnd: "16px" }}>
      <Content><a href="#retained">Inline Settings link</a></Content>
    </main>
    <button className="studio-notification-bell">Notifications</button>
    <ToastHost controller={controller} navigate={() => {}} />
  </>));
  const port = container.querySelector<HTMLElement>("#short-port")!;
  const link = container.querySelector<HTMLAnchorElement>("a")!;
  const height = () => Math.max(naturalHeight, 16 + parseFloat(port.style.paddingBlockEnd));
  Object.defineProperties(port, {
    clientHeight: { configurable: true, get: height },
    clientWidth: { configurable: true, value: 390 },
    scrollHeight: { configurable: true, get: () => 600 + parseFloat(port.style.paddingBlockEnd) },
    scrollTop: { configurable: true, get: () => scroll, set: (value: number) => { scroll = Math.max(0, Math.min(port.scrollHeight - height(), value)); } }
  });
  port.scrollTo = (options?: ScrollToOptions | number, y?: number) => {
    port.scrollTop = typeof options === "number" ? y ?? 0 : options?.top ?? 0;
  };
  vi.spyOn(Element.prototype, "getBoundingClientRect").mockImplementation(function (this: Element) {
    if (this === link) return new DOMRect(20, 220 - scroll, 180, 17);
    if (this.matches(".studio-toast-host")) {
      const limit = parseFloat((this as HTMLElement).style.maxHeight) || 300;
      return new DOMRect(16, 356 - limit, 358, limit);
    }
    if (this.matches(".studio-toast")) {
      const host = this.parentElement!.getBoundingClientRect();
      return new DOMRect(16, host.top + [...this.parentElement!.children].indexOf(this) * 108, 358, 100);
    }
    if (this.matches(".studio-toast-dismiss")) return new DOMRect(340, 0, 28, 28);
    return new DOMRect(0, 182, 390, this === port ? height() : naturalHeight);
  });
  vi.spyOn(Element.prototype, "getClientRects").mockImplementation(function (this: Element) {
    return (this.isConnected ? [this.getBoundingClientRect()] : []) as unknown as DOMRectList;
  });
  await update(() => {
    link.focus();
    for (const title of ["One", "Two", "Three"]) controller.api.notify({ level: "error", title });
  });
  const assertStable = async (expectedPadding: string) => {
    for (let i = 0; i < 6; i++) {
      await update(() => window.dispatchEvent(new Event("resize")));
      expect(port.clientHeight).toBe(naturalHeight);
      expect(port.style.paddingBlockEnd).toBe(expectedPadding);
      expect(document.activeElement).toBe(link);
      const target = link.getBoundingClientRect(), toast = container.querySelector(".studio-toast-host")!.getBoundingClientRect();
      expect(target.top).toBeGreaterThanOrEqual(190);
      expect(target.bottom).toBeLessThanOrEqual(toast.top - 8);
      expect(controller.getEntries().every(entry => entry.toasting)).toBe(true);
    }
  };
  await assertStable("134px");
  naturalHeight = 120;
  await assertStable("104px");
  await update(() => container.querySelector<HTMLButtonElement>(".studio-notification-bell")!.focus());
  expect(port.style.paddingBlockEnd).toBe("16px");
  expect(port.clientHeight).toBe(120);
});
