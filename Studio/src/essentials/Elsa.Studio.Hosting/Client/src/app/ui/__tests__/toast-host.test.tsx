import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createNotificationController, type StudioNotificationController } from "../../../sdk";
import { ToastHost } from "../feedback/ToastHost";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
  vi.useRealTimers();
  vi.restoreAllMocks();
});

function mount(controller: StudioNotificationController, navigate: (path: string) => void = () => {}): HTMLElement {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  flushSync(() => root.render(<ToastHost controller={controller} navigate={navigate} />));
  cleanup = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
  return host;
}

/** Runs controller work and timers, then flushes the resulting React updates. */
function run(work: () => void) {
  flushSync(work);
}

const toasts = (host: HTMLElement) => Array.from(host.querySelectorAll<HTMLElement>(".studio-toast"));

describe("ToastHost", () => {
  it("shows at most three toasts newest first", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const host = mount(controller);
    run(() => {
      for (const title of ["one", "two", "three", "four"]) controller.api.notify({ level: "error", title });
    });
    expect(toasts(host)).toHaveLength(3);
    expect(toasts(host)[0].textContent).toContain("four");

    const newest = controller.getEntries()[0];
    run(() => controller.hideToast(newest.id));
    expect(toasts(host)).toHaveLength(3);
    expect(toasts(host)[0].textContent).toContain("three");
    expect(toasts(host)[2].textContent).toContain("one");
  });

  it("success toast hides after 8 seconds but stays in the center", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const host = mount(controller);
    run(() => { controller.api.notify({ level: "success", title: "Saved" }); });
    expect(toasts(host)).toHaveLength(1);

    run(() => vi.advanceTimersByTime(7999));
    expect(toasts(host)).toHaveLength(1);
    run(() => vi.advanceTimersByTime(1));
    expect(toasts(host)).toHaveLength(0);
    expect(controller.getEntries()).toHaveLength(1);
  });

  it("error toast is sticky", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const host = mount(controller);
    run(() => { controller.api.notify({ level: "error", title: "Broke" }); });
    run(() => vi.advanceTimersByTime(60_000));
    expect(toasts(host)).toHaveLength(1);
  });

  it("dismiss button hides the toast but keeps the entry", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const host = mount(controller);
    run(() => { controller.api.notify({ level: "error", title: "Broke" }); });
    run(() => host.querySelector<HTMLButtonElement>("button[aria-label='Dismiss notification']")!.click());
    expect(toasts(host)).toHaveLength(0);
    expect(controller.getEntries()).toHaveLength(1);
  });

  it("progress toast updates in place", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const host = mount(controller);
    let handle!: ReturnType<typeof controller.api.notify>;
    run(() => { handle = controller.api.notify({ level: "progress", title: "Working" }); });
    expect(toasts(host)).toHaveLength(1);
    expect(toasts(host)[0].dataset.level).toBe("progress");

    run(() => vi.advanceTimersByTime(30_000));
    expect(toasts(host)).toHaveLength(1);

    run(() => handle.update({ level: "success", title: "Done" }));
    expect(toasts(host)).toHaveLength(1);
    expect(toasts(host)[0].dataset.level).toBe("success");
    expect(toasts(host)[0].textContent).toContain("Done");

    run(() => vi.advanceTimersByTime(7999));
    expect(toasts(host)).toHaveLength(1);
    run(() => vi.advanceTimersByTime(1));
    expect(toasts(host)).toHaveLength(0);
  });

  it("link navigates and marks read", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const navigate = vi.fn();
    const host = mount(controller, navigate);
    run(() => { controller.api.notify({ level: "success", title: "Restored", link: { label: "Open workflow", path: "/workflows/abc" } }); });
    const link = Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(button => button.textContent === "Open workflow")!;
    run(() => link.click());
    expect(navigate).toHaveBeenCalledWith("/workflows/abc");
    expect(controller.getEntries()[0].read).toBe(true);
    expect(toasts(host)).toHaveLength(0);
  });

  it("action runs and closes the toast", async () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const run_ = vi.fn();
    const host = mount(controller);
    run(() => { controller.api.notify({ level: "warning", title: "Deleted", action: { label: "Undo", run: run_ } }); });
    const undo = Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(button => button.textContent === "Undo")!;
    run(() => undo.click());
    await vi.advanceTimersByTimeAsync(0);
    expect(run_).toHaveBeenCalledTimes(1);
    expect(toasts(host)).toHaveLength(0);
  });

  it("rejected action closes the toast and reports an error", async () => {
    vi.useFakeTimers();
    const unhandled = vi.fn();
    // vitest runs in Node; the DOM-only tsconfig has no Node typings.
    const nodeProcess = (globalThis as unknown as { process: { on(event: string, listener: () => void): void; off(event: string, listener: () => void): void } }).process;
    nodeProcess.on("unhandledRejection", unhandled);
    try {
      const controller = createNotificationController();
      const host = mount(controller);
      run(() => {
        controller.api.notify({
          level: "warning",
          title: "Deleted",
          action: { label: "Restore", run: () => Promise.reject(new Error("nope")) }
        });
      });
      const restore = Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(button => button.textContent === "Restore")!;
      run(() => restore.click());
      await vi.advanceTimersByTimeAsync(0);
      flushSync(() => {});

      const entries = controller.getEntries();
      const failure = entries.find(entry => entry.title === "Restore failed");
      expect(failure?.message).toBe("nope");
      expect(failure?.level).toBe("error");
      expect(toasts(host).some(toast => toast.textContent?.includes("Deleted"))).toBe(false);
      await vi.advanceTimersByTimeAsync(10);
      expect(unhandled).not.toHaveBeenCalled();
    } finally {
      nodeProcess.off("unhandledRejection", unhandled);
    }
  });

  it("a synchronously throwing action also reports an error", async () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const host = mount(controller);
    run(() => {
      controller.api.notify({
        level: "warning",
        title: "Deleted",
        action: { label: "Restore", run: () => { throw new Error("sync nope"); } }
      });
    });
    const restore = Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(button => button.textContent === "Restore")!;
    run(() => restore.click());
    await vi.advanceTimersByTimeAsync(0);
    expect(controller.getEntries().find(entry => entry.title === "Restore failed")?.message).toBe("sync nope");
    expect(toasts(host).some(toast => toast.textContent?.includes("Deleted"))).toBe(false);
  });

  it("errors are announced assertively and others politely", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const host = mount(controller);
    run(() => { controller.api.notify({ level: "error", title: "Broke", message: "Details" }); });
    run(() => { controller.api.notify({ level: "success", title: "Saved" }); });
    expect(host.querySelector("[aria-live='assertive']")!.textContent).toContain("Broke");
    expect(host.querySelector("[aria-live='polite']")!.textContent).toContain("Saved");
    expect(host.querySelector("[aria-live='polite']")!.textContent).not.toContain("Broke");
  });

  it("prefixes warning and error announcements with the level", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const host = mount(controller);
    run(() => { controller.api.notify({ level: "error", title: "Broke", message: "Details" }); });
    expect(host.querySelector("[aria-live='assertive']")!.textContent).toBe("Error: Broke. Details");

    run(() => { controller.api.notify({ level: "warning", title: "Slow" }); });
    expect(host.querySelector("[aria-live='polite']")!.textContent).toBe("Warning: Slow");

    run(() => { controller.api.notify({ level: "success", title: "Saved" }); });
    expect(host.querySelector("[aria-live='polite']")!.textContent).toBe("Saved");
  });

  it("announces an identical notification again", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const host = mount(controller);
    const polite = () => host.querySelector("[aria-live='polite']")!.textContent;
    run(() => { controller.api.notify({ level: "success", title: "Saved" }); });
    const first = polite();

    run(() => { controller.api.notify({ level: "success", title: "Saved" }); });
    const second = polite();
    run(() => { controller.api.notify({ level: "success", title: "Saved" }); });

    expect(first).toBe("Saved");
    expect(second).not.toBe(first);
    expect(second!.replace("\u200B", "")).toBe("Saved");
    expect(polite()).toBe("Saved");
  });

  it("labels the stack as a region and each toast by its title", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const host = mount(controller);
    expect(host.querySelector(".studio-toast-host")).toBeNull();

    run(() => { controller.api.notify({ level: "info", title: "Hello" }); });
    const region = host.querySelector<HTMLElement>(".studio-toast-host")!;
    expect(region.getAttribute("role")).toBe("region");
    expect(region.getAttribute("aria-label")).toBe("Notifications");
    const [toast] = toasts(host);
    expect(toast.getAttribute("role")).toBe("group");
    expect(document.getElementById(toast.getAttribute("aria-labelledby")!)!.textContent).toBe("Hello");
  });

  it("moves focus to the next toast, then the bell, when a focused toast closes", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const bell = document.createElement("button");
    bell.className = "studio-notification-bell";
    document.body.appendChild(bell);
    try {
      const host = mount(controller);
      run(() => {
        controller.api.notify({ level: "error", title: "older" });
        controller.api.notify({ level: "error", title: "newer" });
      });
      const dismiss = (toast: HTMLElement) => toast.querySelector<HTMLButtonElement>("button[aria-label='Dismiss notification']")!;

      dismiss(toasts(host)[0]).focus();
      run(() => dismiss(toasts(host)[0]).click());
      expect(toasts(host)).toHaveLength(1);
      expect(document.activeElement).toBe(toasts(host)[0]);

      dismiss(toasts(host)[0]).focus();
      run(() => dismiss(toasts(host)[0]).click());
      expect(toasts(host)).toHaveLength(0);
      expect(document.activeElement).toBe(bell);
    } finally {
      bell.remove();
    }
  });

  it("marks a link read and closes the toast before navigating", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    let stateAtNavigate: { read: boolean; toasting: boolean } | undefined;
    const navigate = vi.fn(() => {
      const [entry] = controller.getEntries();
      stateAtNavigate = { read: entry.read, toasting: entry.toasting };
    });
    const host = mount(controller, navigate);
    run(() => { controller.api.notify({ level: "success", title: "Moved", link: { label: "Open folder", path: "/workflows" } }); });

    run(() => Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(button => button.textContent === "Open folder")!.click());

    expect(stateAtNavigate).toEqual({ read: true, toasting: false });
  });

  it("an action runs once and is gone from the entry afterwards", async () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const restore = vi.fn();
    const host = mount(controller);
    run(() => { controller.api.notify({ level: "success", title: "Deleted", action: { label: "Restore", run: restore } }); });

    run(() => Array.from(host.querySelectorAll<HTMLButtonElement>("button")).find(button => button.textContent === "Restore")!.click());
    await vi.advanceTimersByTimeAsync(0);

    expect(restore).toHaveBeenCalledTimes(1);
    expect(controller.getEntries()[0].action).toBeUndefined();
  });

  it("announces in-place updates", () => {
    vi.useFakeTimers();
    const controller = createNotificationController();
    const host = mount(controller);
    let handle!: ReturnType<typeof controller.api.notify>;
    run(() => { handle = controller.api.notify({ level: "progress", title: "Working" }); });
    run(() => handle.update({ level: "error", title: "Failed" }));
    expect(host.querySelector("[aria-live='assertive']")!.textContent).toContain("Failed");
  });
});
