import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PublishActionButton, type WorkflowPublishMode } from "../workflow-editor/PublishActionButton";

const rememberedModeKey = "elsa-studio-workflow-publish-mode";
const cleanups: Array<() => void> = [];

beforeEach(() => {
  window.localStorage.removeItem(rememberedModeKey);
  vi.stubGlobal("ResizeObserver", class {
    observe() {}
    disconnect() {}
  });
});

afterEach(() => {
  cleanups.splice(0).forEach(cleanup => cleanup());
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  window.localStorage.removeItem(rememberedModeKey);
});

describe("PublishActionButton", () => {
  it("defaults to review, remembers a menu choice across remounts, and repeats it from the primary action", async () => {
    const selectedModes: WorkflowPublishMode[] = [];
    const first = mount(mode => selectedModes.push(mode));
    const firstPrimary = primaryButton(first.host);
    const primaryReviewName = firstPrimary.getAttribute("aria-label") ?? firstPrimary.textContent?.trim();

    flushSync(() => firstPrimary.click());
    expect(selectedModes).toEqual(["review"]);

    const trigger = publishOptionsButton(first.host);
    flushSync(() => trigger.click());
    await nextFrame();
    const reviewOption = menuItem("review");
    const reviewMenuName = reviewOption.getAttribute("aria-label") ?? reviewOption.textContent?.trim();
    const publishOption = menuItem("publish");
    flushSync(() => publishOption.click());

    expect(selectedModes).toEqual(["review", "publish"]);
    expect(window.localStorage.getItem(rememberedModeKey)).toBe("publish");
    expect(firstPrimary.textContent).toContain("Publish");

    first.unmount();
    const remounted = mount(mode => selectedModes.push(mode));
    const rememberedPrimary = primaryButton(remounted.host);
    expect(rememberedPrimary.textContent).toContain("Publish");
    flushSync(() => rememberedPrimary.click());
    expect(selectedModes).toEqual(["review", "publish", "publish"]);
    expect.soft(primaryReviewName).toBe("Review & publish");
    expect.soft(reviewMenuName).toBe("Review & publish");
  });

  it("falls back to review for an invalid value and keeps working when storage is unavailable", () => {
    window.localStorage.setItem(rememberedModeKey, "preview");
    const invalid = mount();
    const invalidFallbackName = primaryButton(invalid.host).getAttribute("aria-label") ?? primaryButton(invalid.host).textContent?.trim();
    invalid.unmount();

    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("storage is blocked"); });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("storage is blocked"); });
    const selectedModes: WorkflowPublishMode[] = [];
    const unavailable = mount(mode => selectedModes.push(mode));
    const unavailableFallbackName = primaryButton(unavailable.host).getAttribute("aria-label") ?? primaryButton(unavailable.host).textContent?.trim();

    flushSync(() => publishOptionsButton(unavailable.host).click());
    flushSync(() => menuItem("publish").click());
    expect(selectedModes).toEqual(["publish"]);
    expect(primaryButton(unavailable.host).textContent).toContain("Publish");
    expect.soft(invalidFallbackName).toBe("Review & publish");
    expect.soft(unavailableFallbackName).toBe("Review & publish");
  });

  it("disables both controls while busy but leaves publication available during a save", () => {
    const busy = mount(() => undefined, { busy: true });
    expect(primaryButton(busy.host).disabled).toBe(true);
    expect(publishOptionsButton(busy.host).disabled).toBe(true);
    busy.unmount();

    const saving = mount(() => undefined, { saving: true });
    expect(primaryButton(saving.host).disabled).toBe(false);
    expect(publishOptionsButton(saving.host).disabled).toBe(false);
    expect(primaryButton(saving.host).title).toContain("current save");
    flushSync(() => publishOptionsButton(saving.host).click());
    expect(menuItem("review").disabled).toBe(false);
    expect(menuItem("publish").disabled).toBe(false);
  });

  it("supports menu arrows, Home, End, Enter, Escape, Tab, and focus return", async () => {
    const selectedModes: WorkflowPublishMode[] = [];
    const mounted = mount(mode => selectedModes.push(mode));
    const trigger = publishOptionsButton(mounted.host);
    trigger.focus();
    press("ArrowDown");
    await nextFrame();

    const reviewOption = menuItem("review");
    const publishOption = menuItem("publish");
    expect(document.activeElement).toBe(reviewOption);
    expect(reviewOption.getAttribute("aria-checked")).toBe("true");
    expect(reviewOption.querySelector("svg")?.getAttribute("style")).toContain("visibility: visible");
    expect(publishOption.querySelector("svg")?.getAttribute("style")).toContain("visibility: hidden");
    press("End");
    expect(document.activeElement).toBe(publishOption);
    press("ArrowUp");
    expect(document.activeElement).toBe(reviewOption);
    press("ArrowDown");
    expect(document.activeElement).toBe(publishOption);
    press("Home");
    expect(document.activeElement).toBe(reviewOption);
    press("End");
    press("Enter");

    expect(selectedModes).toEqual(["publish"]);
    expect(document.body.querySelector("[role='menu']")).toBeNull();
    expect(document.activeElement).toBe(trigger);

    pressOn(trigger, "ArrowUp");
    await nextFrame();
    expect(document.activeElement).toBe(menuItem("publish"));
    press("Escape");
    expect(document.body.querySelector("[role='menu']")).toBeNull();
    expect(document.activeElement).toBe(trigger);

    flushSync(() => trigger.click());
    await nextFrame();
    press("Tab");
    expect(document.body.querySelector("[role='menu']")).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it("restores focus after a command completes only when focus has fallen back to the document", async () => {
    const primaryCommand = mount(() => undefined);
    const primary = primaryButton(primaryCommand.host);
    primary.focus();
    flushSync(() => primary.click());
    primaryCommand.rerender({ busy: true });
    // Browsers blur a focused button when it is disabled; jsdom keeps focus unless modeled explicitly.
    document.body.tabIndex = -1;
    document.body.focus();
    document.body.removeAttribute("tabindex");
    expect(document.activeElement).toBe(document.body);
    primaryCommand.rerender({ busy: false });
    await nextFrame();
    expect(document.activeElement).toBe(primary);

    const dialog = document.createElement("button");
    dialog.setAttribute("aria-label", "Review dialog");
    document.body.appendChild(dialog);
    cleanups.push(() => dialog.remove());

    primary.focus();
    flushSync(() => primary.click());
    primaryCommand.rerender({ busy: true });
    dialog.focus();
    primaryCommand.rerender({ busy: false });
    await nextFrame();
    expect(document.activeElement).toBe(dialog);

    const input = document.createElement("input");
    document.body.appendChild(input);
    cleanups.push(() => input.remove());

    primary.focus();
    flushSync(() => primary.click());
    primaryCommand.rerender({ busy: true });
    input.focus();
    primaryCommand.rerender({ busy: false });
    await nextFrame();
    expect(document.activeElement).toBe(input);
  });

  it("returns focus to the menu trigger when a busy transition closes its focused menu", async () => {
    const mounted = mount();
    const trigger = publishOptionsButton(mounted.host);
    trigger.focus();
    pressOn(trigger, "ArrowDown");
    await nextFrame();
    expect(document.activeElement).toBe(menuItem("review"));

    mounted.rerender({ busy: true });
    expect(document.body.querySelector("[role='menu']")).toBeNull();
    mounted.rerender({ busy: false });
    await nextFrame();
    expect(document.activeElement).toBe(trigger);
  });
});

function mount(
  onPublish: (mode: WorkflowPublishMode) => void = () => undefined,
  { busy = false, saving = false }: { busy?: boolean; saving?: boolean } = {}
) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root: Root = createRoot(host);
  flushSync(() => root.render(<PublishActionButton busy={busy} saving={saving} onPublish={onPublish} />));
  let mounted = true;
  const unmount = () => {
    if (!mounted) return;
    mounted = false;
    flushSync(() => root.unmount());
    host.remove();
  };
  cleanups.push(unmount);
  let current = { busy, saving };
  const rerender = (next: { busy?: boolean; saving?: boolean } = {}) => {
    current = { ...current, ...next };
    flushSync(() => root.render(
      <PublishActionButton busy={current.busy} saving={current.saving} onPublish={onPublish} />
    ));
  };
  return { host, rerender, unmount };
}

function primaryButton(host: HTMLElement) {
  return host.querySelector<HTMLButtonElement>(".wf-publish-primary")!;
}

function publishOptionsButton(host: HTMLElement) {
  return host.querySelector<HTMLButtonElement>("[aria-label='Publish options']")!;
}

function menuItem(mode: "review" | "publish") {
  return [...document.body.querySelectorAll<HTMLButtonElement>("[role='menuitemradio']")][mode === "review" ? 0 : 1]!;
}

async function nextFrame() {
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
}

function press(key: string) {
  pressOn(document.activeElement as HTMLElement, key);
}

function pressOn(element: HTMLElement, key: string) {
  flushSync(() => element.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true })));
}
