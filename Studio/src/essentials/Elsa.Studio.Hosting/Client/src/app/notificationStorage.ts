import { maxNotificationEntries, type AuthSession, type StudioNotificationEntry, type StudioNotificationLevel, type StudioNotificationLink, type StudioNotificationStorage } from "../sdk";

export const notificationStorageKeyPrefix = "elsa-studio:notifications:v1:";

const maxAgeMs = 7 * 24 * 60 * 60 * 1000;
const defaultDebounceMs = 250;
const restoredProgressMessage = "Status unknown — this operation was interrupted by a page reload";
const knownLevels: readonly StudioNotificationLevel[] = ["info", "success", "warning", "error", "progress"];

export interface LocalNotificationStorageOptions {
  storage?: () => Storage | null;
  now?: () => number;
  debounceMs?: number;
}

// 32-bit FNV-1a: not a security boundary, only keeps raw identity and backend values out of keys.
function hash(value: string): string {
  let result = 0x811c9dc5;
  for (let index = 0; index < value.length; index++) {
    result ^= value.charCodeAt(index);
    result = Math.imul(result, 0x01000193) >>> 0;
  }
  return result.toString(16).padStart(8, "0");
}

export function notificationScopeKey(session: { key: string; identity: AuthSession } | null | undefined): string | null {
  if (!session) return null;
  const { identity } = session;
  const user = `${identity.provider?.id ?? ""}|${identity.tenantId ?? ""}|${identity.subject ?? "anonymous"}`;
  return `${hash(user)}:${hash(session.key)}`;
}

function defaultStorage(): Storage | null {
  try { return window.localStorage; } catch { return null; }
}

function isLink(value: unknown): value is StudioNotificationLink {
  if (!value || typeof value !== "object") return false;
  const link = value as { label?: unknown; path?: unknown };
  return typeof link.label === "string" && typeof link.path === "string";
}

/**
 * Validates stored entries. `isInterrupted` decides which `progress` entries belong to an operation
 * that no longer runs: only a page load can know that, so cross-tab updates pass none.
 */
function restoreEntries(value: unknown, now: number, isInterrupted: (id: string) => boolean = () => false): StudioNotificationEntry[] {
  if (!Array.isArray(value)) return [];
  const oldest = now - maxAgeMs;
  const restored: StudioNotificationEntry[] = [];
  for (const raw of value) {
    if (!raw || typeof raw !== "object") continue;
    const item = raw as Partial<Record<keyof StudioNotificationEntry, unknown>>;
    if (typeof item.id !== "string" || typeof item.title !== "string"
      || !knownLevels.includes(item.level as StudioNotificationLevel)
      || typeof item.createdAt !== "number" || item.createdAt < oldest) continue;
    const level = item.level as StudioNotificationLevel;
    const interrupted = level === "progress" && isInterrupted(item.id);
    const entry: StudioNotificationEntry = {
      id: item.id,
      level: interrupted ? "warning" : level,
      title: item.title,
      source: typeof item.source === "string" ? item.source : "studio",
      createdAt: item.createdAt,
      updatedAt: typeof item.updatedAt === "number" ? item.updatedAt : item.createdAt,
      read: item.read === true,
      toasting: false
    };
    const message = interrupted ? restoredProgressMessage : item.message;
    if (typeof message === "string") entry.message = message;
    if (isLink(item.link)) entry.link = { label: item.link.label, path: item.link.path };
    restored.push(entry);
  }
  return restored.sort((a, b) => b.createdAt - a.createdAt).slice(0, maxNotificationEntries);
}

function parseEntries(json: string | null, now: number, isInterrupted?: (id: string) => boolean): StudioNotificationEntry[] {
  if (!json) return [];
  try { return restoreEntries(JSON.parse(json), now, isInterrupted); } catch { return []; }
}

/** What another tab can observe of an entry; the toast and the action belong to one tab. */
function signature(entry: StudioNotificationEntry): string {
  return JSON.stringify([entry.level, entry.title, entry.message ?? null, entry.link ?? null, entry.source, entry.updatedAt, entry.read]);
}

/**
 * Three-way merge of a list another tab wrote (`remote`) with this tab's unwritten list (`local`),
 * both derived from the last list this tab saw in storage (`base`). A side that changed an entry wins;
 * entries one side added survive; entries one side removed without the other changing them stay removed.
 */
function mergeEntries(remote: StudioNotificationEntry[], local: StudioNotificationEntry[], base: StudioNotificationEntry[]): StudioNotificationEntry[] {
  const remoteById = new Map(remote.map(entry => [entry.id, entry]));
  const localById = new Map(local.map(entry => [entry.id, entry]));
  const baseById = new Map(base.map(entry => [entry.id, entry]));
  const changedFromBase = (entry: StudioNotificationEntry) => {
    const known = baseById.get(entry.id);
    return !known || signature(known) !== signature(entry);
  };
  const merged: StudioNotificationEntry[] = [];
  for (const id of new Set([...localById.keys(), ...remoteById.keys()])) {
    const mine = localById.get(id);
    const theirs = remoteById.get(id);
    if (mine && theirs) merged.push(changedFromBase(mine) ? mine : theirs);
    else if (mine) { if (!baseById.has(id) || changedFromBase(mine)) merged.push(mine); }
    else if (theirs) { if (!baseById.has(id) || changedFromBase(theirs)) merged.push(theirs); }
  }
  return merged.sort((a, b) => b.createdAt - a.createdAt).slice(0, maxNotificationEntries);
}

export type LocalNotificationStorage = Required<StudioNotificationStorage> & { flush(): void };

/** Persists notifications in `localStorage`; every access is guarded so a blocked or full store only loses persistence. */
export function createLocalNotificationStorage(options: LocalNotificationStorageOptions = {}): LocalNotificationStorage {
  const getStorage = options.storage ?? defaultStorage;
  const now = options.now ?? Date.now;
  const debounceMs = options.debounceMs ?? defaultDebounceMs;
  /** Lists saved but not yet written, per scope. */
  const pending = new Map<string, StudioNotificationEntry[]>();
  /** The list this tab last wrote to or read from storage, per scope: the base of a cross-tab merge. */
  const lastKnown = new Map<string, StudioNotificationEntry[]>();
  /** Progress entries this page saved: their operations are still running, so a load must not call them interrupted. */
  const liveProgressIds = new Set<string>();
  let timer: ReturnType<typeof setTimeout> | undefined;
  let listeningForPageHide = false;

  const write = (scope: string, entries: StudioNotificationEntry[]) => {
    lastKnown.set(scope, entries);
    try {
      getStorage()?.setItem(notificationStorageKeyPrefix + scope, JSON.stringify(entries));
    } catch { /* storage blocked or full: notifications stay in memory only */ }
  };

  const flush = () => {
    if (timer !== undefined) { clearTimeout(timer); timer = undefined; }
    const writes = [...pending];
    pending.clear();
    for (const [scope, entries] of writes) write(scope, entries);
  };

  const readStored = (scope: string): string | null => {
    try {
      return getStorage()?.getItem(notificationStorageKeyPrefix + scope) ?? null;
    } catch { return null; }
  };

  return {
    load(scope) {
      try {
        const isInterrupted = (id: string) => !liveProgressIds.has(id);
        // A write still waiting for the debounce is newer than storage (for example after a quick A → B → A switch).
        const unwritten = pending.get(scope);
        if (unwritten) return restoreEntries(unwritten, now(), isInterrupted);
        const json = readStored(scope);
        lastKnown.set(scope, parseEntries(json, now()));
        return parseEntries(json, now(), isInterrupted);
      } catch { return []; }
    },
    save(scope, entries) {
      try {
        const persisted = entries.slice(0, maxNotificationEntries).map(({ action: _action, ...rest }) => rest);
        for (const entry of persisted) {
          if (entry.level === "progress") liveProgressIds.add(entry.id);
          else liveProgressIds.delete(entry.id);
        }
        pending.set(scope, persisted);
        if (!listeningForPageHide && typeof window !== "undefined") {
          window.addEventListener("pagehide", flush);
          listeningForPageHide = true;
        }
        if (timer !== undefined) clearTimeout(timer);
        timer = setTimeout(flush, debounceMs);
      } catch { /* never let persistence break a notification */ }
    },
    subscribe(scope, listener) {
      if (typeof window === "undefined") return () => undefined;
      const key = notificationStorageKeyPrefix + scope;
      const onStorage = (event: StorageEvent) => {
        if (event.key !== key) return;
        try {
          const remote = parseEntries(event.newValue, now());
          const unwritten = pending.get(scope);
          const base = lastKnown.get(scope) ?? [];
          lastKnown.set(scope, remote);
          if (!unwritten) {
            listener(remote);
            return;
          }
          // This tab has a write waiting: merge so neither tab's recent changes are lost, and write the merge.
          const merged = mergeEntries(remote, restoreEntries(unwritten, now()), base);
          pending.set(scope, merged);
          listener(merged);
        } catch { /* a faulty listener must not break the event loop */ }
      };
      window.addEventListener("storage", onStorage);
      return () => window.removeEventListener("storage", onStorage);
    },
    flush
  };
}
