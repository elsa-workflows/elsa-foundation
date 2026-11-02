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
