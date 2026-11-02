import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createNotificationController, type StudioNotificationController } from "../../../sdk";
import { NotificationCenter } from "../feedback/NotificationCenter";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
  vi.restoreAllMocks();
});

function mount(controller: StudioNotificationController, props: { navigate?: (path: string) => void; now?: () => number } = {}): HTMLElement {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  flushSync(() => root.render(<NotificationCenter controller={controller} navigate={props.navigate ?? (() => {})} now={props.now} />));
  cleanup = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
  return host;
}

const bell = () => document.querySelector<HTMLButtonElement>(".studio-notification-bell")!;
const badge = () => document.querySelector<HTMLElement>(".studio-notification-badge");
const sheet = () => document.querySelector<HTMLElement>(".studio-notification-center");
const entries = () => Array.from(document.querySelectorAll<HTMLElement>(".studio-notification-entry"));
const buttonNamed = (name: string) => Array.from(document.querySelectorAll<HTMLButtonElement>("button")).find(button => button.textContent === name)!;

function openCenter() {
  flushSync(() => bell().click());
}

function notifyMany(controller: StudioNotificationController, count: number, level: "info" | "error" = "info") {
  flushSync(() => {
    for (let index = 0; index < count; index++) controller.api.notify({ level, title: `Item ${index}` });
  });
}

describe("NotificationCenter bell", () => {
  it("shows the unread count and caps at 9+", () => {
    const controller = createNotificationController();
    mount(controller);
    expect(badge()).toBeNull();
    expect(bell().getAttribute("aria-label")).toBe("Notifications, 0 unread");

    notifyMany(controller, 3);
    expect(bell().getAttribute("aria-label")).toBe("Notifications, 3 unread");
    expect(badge()!.textContent).toBe("3");

    notifyMany(controller, 9);
    expect(bell().getAttribute("aria-label")).toBe("Notifications, 12 unread");
    expect(badge()!.textContent).toBe("9+");
  });

  it("uses the danger tone when an unread error exists", () => {
    const controller = createNotificationController();
    mount(controller);
    notifyMany(controller, 1, "info");
    expect(badge()!.dataset.tone).toBeUndefined();

    notifyMany(controller, 1, "error");
    expect(badge()!.dataset.tone).toBe("danger");

    flushSync(() => controller.markAllRead());
    expect(badge()).toBeNull();
  });
});

describe("NotificationCenter sheet", () => {
  it("opens without marking entries read", () => {
    const controller = createNotificationController();
    mount(controller);
    notifyMany(controller, 2);
    openCenter();

    expect(sheet()).not.toBeNull();
    expect(sheet()!.textContent).toContain("Notifications");
    expect(entries()).toHaveLength(2);
    expect(controller.getEntries().every(entry => !entry.read)).toBe(true);
    expect(entries().every(entry => entry.hasAttribute("data-unread"))).toBe(true);
    expect(bell().getAttribute("aria-label")).toBe("Notifications, 2 unread");
  });

  it("does not render a blocking overlay", () => {
    const controller = createNotificationController();
    mount(controller);
    openCenter();
    expect(document.querySelector("[data-radix-dialog-overlay], .studio-dialog-overlay")).toBeNull();
    expect(document.body.style.pointerEvents).not.toBe("none");
  });

  it("toggles from the bell", () => {
    const controller = createNotificationController();
    mount(controller);
    openCenter();
    expect(sheet()).not.toBeNull();
    flushSync(() => bell().click());
    expect(sheet()).toBeNull();
  });

  it("clicking an entry marks it read", () => {
    const controller = createNotificationController();
    mount(controller);
    notifyMany(controller, 2);
    openCenter();
    const [newest, oldest] = entries();
    flushSync(() => newest.querySelector<HTMLButtonElement>(".studio-notification-entry-main")!.click());

    const [first, second] = controller.getEntries();
    expect(first.read).toBe(true);
    expect(second.read).toBe(false);
    expect(newest.hasAttribute("data-unread")).toBe(false);
    expect(oldest.hasAttribute("data-unread")).toBe(true);
  });

  it("Mark all read marks every entry", () => {
    const controller = createNotificationController();
    mount(controller);
    notifyMany(controller, 3);
    openCenter();
    flushSync(() => buttonNamed("Mark all read").click());
    expect(controller.getEntries().every(entry => entry.read)).toBe(true);
    expect(badge()).toBeNull();
  });

  it("Dismiss all empties the list and shows the empty state", () => {
    const controller = createNotificationController();
    mount(controller);
    notifyMany(controller, 3);
    openCenter();
    expect(sheet()!.textContent).not.toContain("No notifications");
    flushSync(() => buttonNamed("Dismiss all").click());
    expect(controller.getEntries()).toHaveLength(0);
    expect(entries()).toHaveLength(0);
    expect(sheet()!.textContent).toContain("No notifications");
  });

  it("dismiss removes a single entry", () => {
    const controller = createNotificationController();
    mount(controller);
    notifyMany(controller, 2);
    openCenter();
    flushSync(() => entries()[0].querySelector<HTMLButtonElement>("button[aria-label='Dismiss notification']")!.click());
    expect(controller.getEntries().map(entry => entry.title)).toEqual(["Item 0"]);
    expect(entries()).toHaveLength(1);
  });

  it("link navigates, marks read and closes the center", () => {
    const controller = createNotificationController();
    const navigate = vi.fn();
    mount(controller, { navigate });
    flushSync(() => { controller.api.notify({ level: "success", title: "Restored", link: { label: "Open workflow", path: "/workflows/abc" } }); });
    openCenter();
    flushSync(() => buttonNamed("Open workflow").click());

    expect(navigate).toHaveBeenCalledWith("/workflows/abc");
    expect(controller.getEntries()[0].read).toBe(true);
    expect(sheet()).toBeNull();
  });

  it("Escape closes the center and returns focus to the bell", async () => {
    const controller = createNotificationController();
    mount(controller);
    bell().focus();
    openCenter();
    expect(sheet()).not.toBeNull();
    expect(sheet()!.contains(document.activeElement)).toBe(true);

    flushSync(() => {
      (document.activeElement ?? document.body).dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    });
    expect(sheet()).toBeNull();
    // Radix restores focus to the trigger on the next task.
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(document.activeElement).toBe(bell());
  });

  it("shows no action button for an entry restored without its action", () => {
    const controller = createNotificationController();
    mount(controller);
    flushSync(() => controller.replaceEntries([{
      id: "persisted",
      level: "warning",
      title: "Deleted",
      link: { label: "Open folder", path: "/workflows" },
      source: "studio",
      createdAt: 1,
      updatedAt: 1,
      read: false,
      toasting: false
    }]));
    openCenter();
    expect(buttonNamed("Open folder")).toBeDefined();
    expect(document.querySelector(".studio-notification-action")).toBeNull();
  });

  it("runs an action and reports a failure as an error notification", async () => {
    const controller = createNotificationController();
    mount(controller);
    const run = vi.fn(() => Promise.reject(new Error("nope")));
    flushSync(() => { controller.api.notify({ level: "warning", title: "Deleted", action: { label: "Restore", run } }); });
    openCenter();
    flushSync(() => buttonNamed("Restore").click());
    await Promise.resolve();
    await Promise.resolve();
    flushSync(() => {});

    expect(run).toHaveBeenCalledTimes(1);
    const failure = controller.getEntries().find(entry => entry.title === "Restore failed");
    expect(failure?.level).toBe("error");
    expect(failure?.message).toBe("nope");
    expect(controller.getEntries().find(entry => entry.title === "Deleted")!.read).toBe(true);
  });

  it("shows relative time with the absolute time on hover", () => {
    const controller = createNotificationController({ now: () => 1_000_000 });
    mount(controller, { now: () => 1_000_000 + 5 * 60_000 });
    flushSync(() => { controller.api.notify({ level: "info", title: "Hello" }); });
    openCenter();
    const time = document.querySelector<HTMLElement>(".studio-notification-time")!;
    expect(time.textContent).toBe("5 minutes ago");
    expect(time.title).toBe(new Date(1_000_000).toLocaleString());
  });
});
