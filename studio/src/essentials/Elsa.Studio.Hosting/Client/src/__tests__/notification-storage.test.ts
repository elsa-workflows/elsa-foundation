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

  it("load drops a malformed message or link but keeps the entry", () => {
    window.localStorage.setItem(notificationStorageKeyPrefix + "s", JSON.stringify([
      { ...entry({ id: "bad-message" }), message: { text: "x" } },
      { ...entry({ id: "bad-link" }), link: "/workflows" },
      { ...entry({ id: "partial-link" }), link: { label: "Open" } },
      entry({ id: "good", message: "Fine", link: { label: "Open", path: "/workflows" } })
    ]));

    const loaded = createStorage().load("s");
    const byId = new Map(loaded.map(e => [e.id, e]));

    expect(loaded).toHaveLength(4);
    expect("message" in byId.get("bad-message")!).toBe(false);
    expect("link" in byId.get("bad-link")!).toBe(false);
    expect("link" in byId.get("partial-link")!).toBe(false);
    expect(byId.get("good")).toMatchObject({ message: "Fine", link: { label: "Open", path: "/workflows" } });
  });

  it("load prefers a write that is still waiting for the debounce", () => {
    vi.useFakeTimers();
    try {
      const storage = createLocalNotificationStorage({ now: () => now });
      window.localStorage.setItem(notificationStorageKeyPrefix + "a", JSON.stringify([entry({ id: "stale" })]));

      storage.save("a", [entry({ id: "fresh" }), entry({ id: "running", level: "progress" })]);
      const loaded = storage.load("a");

      expect(loaded.map(e => e.id)).toEqual(["fresh", "running"]);
      expect(loaded[1].level).toBe("progress");
    } finally {
      vi.useRealTimers();
    }
  });

  it("load keeps a progress entry this page saved as progress", () => {
    const storage = createStorage();

    storage.save("a", [entry({ id: "running", level: "progress", message: "3 of 10" })]);
    storage.flush();

    expect(storage.load("a")[0]).toMatchObject({ level: "progress", message: "3 of 10" });
    expect(createStorage().load("a")[0]).toMatchObject({ level: "warning", message: restoredMessage });
  });

  it("a progress entry from another tab arrives as progress", () => {
    const listener = vi.fn();
    createStorage().subscribe("s", listener);
    const running = entry({ id: "running", level: "progress", message: "3 of 10" });

    window.dispatchEvent(new StorageEvent("storage", {
      key: notificationStorageKeyPrefix + "s",
      newValue: JSON.stringify([running])
    }));

    expect(listener.mock.calls[0][0][0]).toMatchObject({ level: "progress", message: "3 of 10" });
  });

  it("a storage event during the debounce merges both tabs and the flush writes the merge", () => {
    const storage = createLocalNotificationStorage({ now: () => now, debounceMs: 60_000 });
    const shared = entry({ id: "shared", createdAt: now - 5000, updatedAt: now - 5000 });
    window.localStorage.setItem(notificationStorageKeyPrefix + "s", JSON.stringify([shared]));
    storage.load("s");
    const listener = vi.fn();
    storage.subscribe("s", listener);

    const mine = entry({ id: "mine", createdAt: now - 2000, updatedAt: now - 2000 });
    storage.save("s", [mine, shared]);
    const theirs = entry({ id: "theirs", createdAt: now - 1000, updatedAt: now - 1000 });
    const sharedReadElsewhere = { ...shared, read: true };
    const remote = JSON.stringify([theirs, sharedReadElsewhere]);
    window.localStorage.setItem(notificationStorageKeyPrefix + "s", remote);
    window.dispatchEvent(new StorageEvent("storage", { key: notificationStorageKeyPrefix + "s", newValue: remote }));

    expect(listener.mock.calls[0][0].map((e: StudioNotificationEntry) => e.id)).toEqual(["theirs", "mine", "shared"]);
    storage.flush();
    const written = JSON.parse(window.localStorage.getItem(notificationStorageKeyPrefix + "s")!) as StudioNotificationEntry[];
    expect(written.map(e => e.id)).toEqual(["theirs", "mine", "shared"]);
    expect(written.find(e => e.id === "shared")!.read).toBe(true);
  });

  it("a merge keeps this tab's changes and removals of entries the other tab left alone", () => {
    const storage = createLocalNotificationStorage({ now: () => now, debounceMs: 60_000 });
    const edited = entry({ id: "edited", createdAt: now - 3000 });
    const dismissed = entry({ id: "dismissed", createdAt: now - 4000 });
    window.localStorage.setItem(notificationStorageKeyPrefix + "s", JSON.stringify([edited, dismissed]));
    storage.load("s");
    const listener = vi.fn();
    storage.subscribe("s", listener);

    storage.save("s", [{ ...edited, read: true }]);
    const theirs = entry({ id: "theirs", createdAt: now - 1000 });
    window.dispatchEvent(new StorageEvent("storage", {
      key: notificationStorageKeyPrefix + "s",
      newValue: JSON.stringify([theirs, edited, dismissed])
    }));

    const merged = listener.mock.calls[0][0] as StudioNotificationEntry[];
    expect(merged.map(e => e.id)).toEqual(["theirs", "edited"]);
    expect(merged[1].read).toBe(true);
  });

  it("throwing storage degrades silently", () => {
    const storage = createLocalNotificationStorage({
      now: () => now,
      debounceMs: 0,
      storage: () => { throw new Error("denied"); }
    });
    const stopListening = storage.subscribe("s", () => undefined);

    expect(() => { storage.save("s", [entry()]); storage.flush(); }).not.toThrow();
    expect(() => storage.load("s")).not.toThrow();
    expect(storage.load("s")).toEqual([]);
    expect(() => stopListening()).not.toThrow();
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
    const stop = createStorage().subscribe("s", listener);
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
    createStorage().subscribe("s", listener);

    window.dispatchEvent(new StorageEvent("storage", { key: notificationStorageKeyPrefix + "s", newValue: null }));

    expect(listener).toHaveBeenCalledWith([]);
  });
});
