import { describe, expect, it, vi } from "vitest";
import {
  createNotificationController,
  notificationToastDurationMs,
  type StudioNotificationEntry,
  type StudioNotificationLevel,
  type StudioNotificationStorage
} from "../sdk";

function createFakeStorage(stored: Record<string, StudioNotificationEntry[]> = {}) {
  return {
    load: vi.fn((scope: string) => stored[scope] ?? []),
    save: vi.fn<(scope: string, entries: StudioNotificationEntry[]) => void>()
  } satisfies StudioNotificationStorage;
}

function entry(overrides: Partial<StudioNotificationEntry> = {}): StudioNotificationEntry {
  return {
    id: "stored-1",
    level: "info",
    title: "Stored",
    source: "studio",
    createdAt: 1,
    updatedAt: 1,
    read: true,
    toasting: false,
    ...overrides
  };
}

function createController(options: Parameters<typeof createNotificationController>[0] = {}) {
  let clock = 1000;
  let nextId = 1;
  return createNotificationController({ now: () => clock++, createId: () => `n${nextId++}`, ...options });
}

describe("notification controller", () => {
  it("notify adds an unread toasting entry newest first", () => {
    const controller = createController();

    controller.api.notify({ level: "info", title: "first" });
    controller.api.notify({ level: "info", title: "second" });

    const entries = controller.getEntries();
    expect(entries.map(e => e.title)).toEqual(["second", "first"]);
    expect(entries.every(e => e.read === false && e.toasting === true && e.source === "studio")).toBe(true);
  });

  it("toast false records in the center only", () => {
    const controller = createController();

    controller.api.notify({ level: "info", title: "quiet", toast: false });

    expect(controller.getEntries()[0].toasting).toBe(false);
  });

  it("lifetime policy per level", () => {
    const levels: StudioNotificationLevel[] = ["info", "success", "warning", "error", "progress"];

    expect(levels.map(notificationToastDurationMs)).toEqual([8000, 8000, 12000, null, null]);
  });

  it("update from progress to success re-raises", () => {
    const controller = createController();
    const handle = controller.api.notify({ level: "progress", title: "Working" });
    controller.hideToast(handle.id);
    controller.markRead(handle.id);

    handle.update({ level: "success", title: "Done" });

    const [updated] = controller.getEntries();
    expect(controller.getEntries()).toHaveLength(1);
    expect(updated).toMatchObject({ id: handle.id, level: "success", title: "Done", read: false, toasting: true });
  });

  it("update without a progress transition keeps read and toast state", () => {
    const controller = createController();
    const handle = controller.api.notify({ level: "info", title: "Hello" });
    controller.hideToast(handle.id);
    controller.markRead(handle.id);

    handle.update({ message: "More detail" });

    expect(controller.getEntries()[0]).toMatchObject({ message: "More detail", read: true, toasting: false });
  });

  it("update into an outcome level from any other level re-raises", () => {
    const controller = createController();
    const handle = controller.api.notify({ level: "info", title: "Queued" });
    controller.hideToast(handle.id);
    controller.markRead(handle.id);

    handle.update({ level: "warning", title: "Slow" });
    expect(controller.getEntries()[0]).toMatchObject({ level: "warning", read: false, toasting: true });

    controller.hideToast(handle.id);
    controller.markRead(handle.id);
    handle.update({ level: "warning", title: "Still slow" });
    expect(controller.getEntries()[0]).toMatchObject({ title: "Still slow", read: true, toasting: false });
  });

  it("a terminal update re-raises an entry another tab turned into a warning", () => {
    const controller = createController();
    const live = controller.api.notify({ level: "progress", title: "Importing" });
    controller.replaceEntries(controller.getEntries().map(e => ({ ...e, level: "warning" as const, read: true })));
    controller.hideToast(live.id);

    live.update({ level: "success", title: "Imported" });

    expect(controller.getEntries()[0]).toMatchObject({ level: "success", read: false, toasting: true });
  });

  it("update clears an optional field passed as undefined and keeps an omitted one", () => {
    const controller = createController();
    const handle = controller.api.notify({ level: "progress", title: "Importing", message: "3 of 10", link: { label: "Open", path: "/a" } });

    handle.update({ title: "Importing more" });
    expect(controller.getEntries()[0]).toMatchObject({ message: "3 of 10", link: { label: "Open", path: "/a" } });

    handle.update({ level: "success", title: "Imported", message: undefined });
    const [updated] = controller.getEntries();
    expect(updated).toMatchObject({ level: "success", title: "Imported", link: { label: "Open", path: "/a" } });
    expect("message" in updated).toBe(false);
  });

  it("level and title cannot be cleared", () => {
    const controller = createController();
    const handle = controller.api.notify({ level: "info", title: "Kept" });

    handle.update({ level: undefined, title: undefined });

    expect(controller.getEntries()[0]).toMatchObject({ level: "info", title: "Kept" });
  });

  it("unknown levels are shown as info", () => {
    const controller = createController();
    const handle = controller.api.notify({ level: "critical" as never, title: "From a newer module" });
    expect(controller.getEntries()[0].level).toBe("info");

    handle.update({ level: "fatal" as never });
    expect(controller.getEntries()[0].level).toBe("info");
  });

  it("keeps only the newest fifty entries in memory", () => {
    const storage = createFakeStorage();
    const controller = createController({ storage });
    controller.setScope("s");

    for (let index = 0; index < 55; index++) controller.api.notify({ level: "info", title: `n${index}` });

    expect(controller.getEntries()).toHaveLength(50);
    expect(controller.getEntries()[0].title).toBe("n54");
    expect(controller.getEntries()[49].title).toBe("n5");
    expect(storage.save).toHaveBeenLastCalledWith("s", controller.getEntries());
  });

  it("clearAction removes an action so it cannot run again", () => {
    const controller = createController();
    const handle = controller.api.notify({ level: "success", title: "Deleted", action: { label: "Restore", run: vi.fn() } });

    controller.clearAction(handle.id);

    expect(controller.getEntries()[0].action).toBeUndefined();
    expect(() => controller.clearAction("missing")).not.toThrow();
  });

  it("suppressing toasts hides them, keeps new entries quiet and does not resurrect them", () => {
    const controller = createController();
    const listener = vi.fn();
    controller.subscribe(listener);
    const shown = controller.api.notify({ level: "error", title: "Shown" });

    controller.setToastsSuppressed(true);
    expect(controller.getToastsSuppressed()).toBe(true);
    expect(controller.getEntries()[0]).toMatchObject({ id: shown.id, toasting: false });

    const quiet = controller.api.notify({ level: "success", title: "While open" });
    const working = controller.api.notify({ level: "progress", title: "Working" });
    working.update({ level: "error", title: "Failed" });
    expect(controller.getEntries()[0]).toMatchObject({ id: working.id, read: false, toasting: false });
    expect(controller.getEntries().find(e => e.id === quiet.id)).toMatchObject({ read: false, toasting: false });

    controller.setToastsSuppressed(false);
    expect(controller.getEntries().every(e => !e.toasting)).toBe(true);
    controller.api.notify({ level: "info", title: "After close" });
    expect(controller.getEntries()[0]).toMatchObject({ title: "After close", toasting: true });
  });

  it("toggling suppression notifies subscribers even without toasts", () => {
    const controller = createController();
    const listener = vi.fn();
    controller.subscribe(listener);

    controller.setToastsSuppressed(true);
    controller.setToastsSuppressed(true);
    controller.setToastsSuppressed(false);

    expect(listener).toHaveBeenCalledTimes(2);
  });

  it("update and dismiss after dismissal are no-ops", () => {
    const controller = createController();
    const handle = controller.api.notify({ level: "info", title: "gone" });
    handle.dismiss();

    handle.update({ title: "back?" });
    handle.dismiss();

    expect(controller.getEntries()).toEqual([]);
  });

  it("track resolves to success and returns the result", async () => {
    const controller = createController();

    const result = await controller.api.track(Promise.resolve(42), {
      progress: "Working",
      success: n => `Got ${n}`,
      error: "Failed"
    });

    expect(result).toBe(42);
    expect(controller.getEntries()).toHaveLength(1);
    expect(controller.getEntries()[0]).toMatchObject({ level: "success", title: "Got 42" });
  });

  it("track rejects with the original error and records error", async () => {
    const controller = createController();
    const err = new Error("boom");

    await expect(controller.api.track(Promise.reject(err), {
      progress: "Working",
      success: "Done",
      error: e => (e as Error).message
    })).rejects.toBe(err);

    expect(controller.getEntries()).toHaveLength(1);
    expect(controller.getEntries()[0]).toMatchObject({ level: "error", title: "boom" });
  });

  it("track keeps the result and falls back when the success formatter throws", async () => {
    const controller = createController();

    const result = await controller.api.track(Promise.resolve(42), {
      progress: "Working",
      success: () => { throw new Error("formatter"); },
      error: "Failed"
    });

    expect(result).toBe(42);
    expect(controller.getEntries()[0]).toMatchObject({ level: "success", title: "Operation finished" });
  });

  it("track keeps the original rejection and falls back when the error formatter throws", async () => {
    const controller = createController();
    const err = new Error("boom");

    await expect(controller.api.track(Promise.reject(err), {
      progress: "Working",
      success: "Done",
      error: () => { throw new Error("formatter"); }
    })).rejects.toBe(err);

    expect(controller.getEntries()[0]).toMatchObject({ level: "error", title: "Operation failed" });
  });

  it("entries published before a scope are kept when the scope is set", () => {
    const stored = entry();
    const storage = createFakeStorage({ s1: [stored] });
    const controller = createController({ storage });

    controller.api.notify({ level: "info", title: "early" });
    controller.setScope("s1");

    const entries = controller.getEntries();
    expect(entries.map(e => e.title)).toEqual(["early", "Stored"]);
    expect(storage.save).toHaveBeenLastCalledWith("s1", entries);
  });

  it("switching scope saves the old list and loads the new one", () => {
    const stored = entry({ id: "b-1", title: "From b" });
    const storage = createFakeStorage({ b: [stored] });
    const controller = createController({ storage });

    controller.setScope("a");
    controller.api.notify({ level: "info", title: "in a" });
    const inA = [...controller.getEntries()];
    controller.setScope("b");

    expect(storage.save).toHaveBeenCalledWith("a", inA);
    expect(inA).toHaveLength(1);
    expect(controller.getEntries()).toEqual([stored]);
  });

  it("clearing the scope saves and empties the list", () => {
    const storage = createFakeStorage();
    const controller = createController({ storage });
    controller.setScope("a");
    controller.api.notify({ level: "info", title: "mine" });
    const saved = [...controller.getEntries()];

    controller.setScope(null);

    expect(storage.save).toHaveBeenLastCalledWith("a", saved);
    expect(controller.getEntries()).toEqual([]);
  });

  it("saves after every mutation while a scope is set", () => {
    const storage = createFakeStorage();
    const controller = createController({ storage });
    controller.setScope("a");

    const handle = controller.api.notify({ level: "info", title: "one" });
    handle.update({ title: "two" });
    controller.markRead(handle.id);
    controller.markAllRead();
    controller.dismissAll();

    expect(storage.save.mock.calls.length).toBeGreaterThanOrEqual(4);
    expect(storage.save).toHaveBeenLastCalledWith("a", []);
  });

  it("getEntries is referentially stable without changes", () => {
    const controller = createController();
    controller.api.notify({ level: "info", title: "x" });

    expect(controller.getEntries()).toBe(controller.getEntries());
  });

  it("replaceEntries merges by id and keeps local actions", () => {
    const storage = createFakeStorage();
    const controller = createController({ storage });
    controller.setScope("a");
    const run = vi.fn();
    const handle = controller.api.notify({ level: "info", title: "old", action: { label: "Go", run } });
    storage.save.mockClear();

    const local = controller.getEntries()[0];
    controller.replaceEntries([
      { ...local, title: "changed", action: undefined },
      entry({ id: "remote-1", title: "Remote" })
    ]);

    const entries = controller.getEntries();
    expect(entries.find(e => e.id === handle.id)).toMatchObject({ title: "changed", action: { label: "Go", run } });
    expect(entries.map(e => e.id)).toContain("remote-1");
    expect(storage.save).not.toHaveBeenCalled();
  });

  it("subscribe notifies on change and unsubscribes", () => {
    const controller = createController();
    const listener = vi.fn();
    const unsubscribe = controller.subscribe(listener);

    controller.api.notify({ level: "info", title: "x" });
    expect(listener).toHaveBeenCalledTimes(1);

    unsubscribe();
    controller.api.notify({ level: "info", title: "y" });
    expect(listener).toHaveBeenCalledTimes(1);
  });
});
