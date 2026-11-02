import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioNotificationEntry } from "../sdk";
import {
  createLocalNotificationStorage,
  notificationScopeKey,
  notificationStorageKeyPrefix
} from "../app/notificationStorage";

const DAY = 24 * 60 * 60 * 1000;
const now = 1_800_000_000_000;
const restoredMessage = "Status unknown — this operation was interrupted by a page reload";

function entry(overrides: Partial<StudioNotificationEntry> = {}): StudioNotificationEntry {
  return {
    id: "n-1",
    level: "info",
    title: "Saved",
    source: "studio",
    createdAt: now - 1000,
    updatedAt: now - 1000,
    read: false,
    toasting: false,
    ...overrides
  };
}

function createStorage() {
  return createLocalNotificationStorage({ now: () => now, debounceMs: 0 });
}

describe("notification storage adapter", () => {
  beforeEach(() => {
    window.localStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("scope key hashes identity and backend without raw values", () => {
    const identity = { status: "authenticated" as const, subject: "jane@example.com", roles: [], permissions: [] };
    const key = notificationScopeKey({ key: "https://a.example", identity });

    expect(key).toMatch(/^[0-9a-f]{8}:[0-9a-f]{8}$/);
    expect(key).not.toContain("jane");
    expect(notificationScopeKey({ key: "https://a.example", identity: { ...identity, subject: "john@example.com" } })).not.toBe(key);
    expect(notificationScopeKey({ key: "https://b.example", identity })).not.toBe(key);
    expect(notificationScopeKey(null)).toBeNull();
    expect(notificationScopeKey(undefined)).toBeNull();
  });

  it("save then load round-trips without actions", () => {
    const storage = createStorage();
    const withAction = entry({
      message: "Done",
      link: { label: "Open", path: "/workflows/1" },
      action: { label: "Restore", run: () => undefined },
      toasting: true
    });

    storage.save("s", [withAction]);
    storage.flush();

    expect(window.localStorage.getItem(notificationStorageKeyPrefix + "s")).not.toContain("Restore");
    const { action: _action, ...persisted } = withAction;
    expect(storage.load("s")).toEqual([{ ...persisted, toasting: false }]);
  });

  it("load drops entries older than seven days and keeps the newest fifty", () => {
    const entries = Array.from({ length: 60 }, (_, index) =>
      entry({ id: `n-${index}`, createdAt: now - index * 1000 - (index >= 55 ? 8 * DAY : 0) }));
    window.localStorage.setItem(notificationStorageKeyPrefix + "s", JSON.stringify(entries));

    const loaded = createStorage().load("s");

    expect(loaded).toHaveLength(50);
    expect(loaded[0].id).toBe("n-0");
    expect(loaded[49].id).toBe("n-49");
  });

  it("load restores progress as status unknown warning", () => {
    const progress = entry({
      level: "progress",
      message: "Restoring 3 of 10",
      link: { label: "Open", path: "/workflows/1" },
      toasting: true
    });
    window.localStorage.setItem(notificationStorageKeyPrefix + "s", JSON.stringify([progress]));

    const [restored] = createStorage().load("s");

    expect(restored.level).toBe("warning");
    expect(restored.message).toBe(restoredMessage);
    expect(restored.link).toEqual({ label: "Open", path: "/workflows/1" });
    expect(restored.toasting).toBe(false);
  });

  it("corrupt json loads as empty", () => {
    const storage = createStorage();

    window.localStorage.setItem(notificationStorageKeyPrefix + "s", "{not json");
    expect(storage.load("s")).toEqual([]);

    window.localStorage.setItem(notificationStorageKeyPrefix + "s", "{\"a\":1}");
    expect(storage.load("s")).toEqual([]);

    window.localStorage.setItem(notificationStorageKeyPrefix + "s", JSON.stringify([null, 3, { id: 1 }, entry({ level: "bogus" as never }), entry({ createdAt: "x" as never }), entry({ id: "ok" })]));
    expect(storage.load("s").map(e => e.id)).toEqual(["ok"]);
  });

  it("throwing storage degrades silently", () => {
    const storage = createLocalNotificationStorage({
      now: () => now,
      debounceMs: 0,
      storage: () => { throw new Error("denied"); }
    });
    const stopListening = storage.subscribe?.("s", () => undefined);

    expect(() => { storage.save("s", [entry()]); storage.flush(); }).not.toThrow();
    expect(() => storage.load("s")).not.toThrow();
    expect(storage.load("s")).toEqual([]);
    expect(() => stopListening?.()).not.toThrow();
  });

  it("write failures such as a full quota do not throw", () => {
    const storage = createLocalNotificationStorage({
      now: () => now,
      debounceMs: 0,
      storage: () => ({ setItem() { throw new Error("quota"); } }) as unknown as Storage
    });

    expect(() => { storage.save("s", [entry()]); storage.flush(); }).not.toThrow();
  });

  it("scopes are isolated", () => {
    const storage = createStorage();

    storage.save("a", [entry()]);
    storage.flush();

    expect(storage.load("a")).toHaveLength(1);
    expect(storage.load("b")).toEqual([]);
  });

  it("debounces writes until the delay elapses", () => {
    vi.useFakeTimers();
    try {
      const storage = createLocalNotificationStorage({ now: () => now });

      storage.save("s", [entry({ id: "first" })]);
      storage.save("s", [entry({ id: "second" })]);
      expect(window.localStorage.getItem(notificationStorageKeyPrefix + "s")).toBeNull();

      vi.advanceTimersByTime(250);

      expect(storage.load("s").map(e => e.id)).toEqual(["second"]);
    } finally {
      vi.useRealTimers();
    }
  });

  it("flushes pending writes on pagehide", () => {
    vi.useFakeTimers();
    try {
      const storage = createLocalNotificationStorage({ now: () => now });

      storage.save("s", [entry()]);
      window.dispatchEvent(new Event("pagehide"));

      expect(storage.load("s")).toHaveLength(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it("storage events from another tab reach subscribers", () => {
    const listener = vi.fn();
    const stop = createStorage().subscribe!("s", listener);
    const incoming = entry({ id: "remote", toasting: true });

    window.dispatchEvent(new StorageEvent("storage", {
      key: notificationStorageKeyPrefix + "s",
      newValue: JSON.stringify([incoming])
    }));
    expect(listener).toHaveBeenCalledTimes(1);
    expect(listener.mock.calls[0][0]).toEqual([{ ...incoming, toasting: false }]);

    window.dispatchEvent(new StorageEvent("storage", {
      key: notificationStorageKeyPrefix + "other",
      newValue: JSON.stringify([incoming])
    }));
    expect(listener).toHaveBeenCalledTimes(1);

    stop();
    window.dispatchEvent(new StorageEvent("storage", {
      key: notificationStorageKeyPrefix + "s",
      newValue: JSON.stringify([incoming])
    }));
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it("a cleared scope in another tab reaches subscribers as an empty list", () => {
    const listener = vi.fn();
    createStorage().subscribe!("s", listener);

    window.dispatchEvent(new StorageEvent("storage", { key: notificationStorageKeyPrefix + "s", newValue: null }));

    expect(listener).toHaveBeenCalledWith([]);
  });
});
