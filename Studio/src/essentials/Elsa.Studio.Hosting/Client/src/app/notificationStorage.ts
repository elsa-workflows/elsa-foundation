import type { AuthSession, StudioNotificationEntry, StudioNotificationLevel, StudioNotificationStorage } from "../sdk";

export const notificationStorageKeyPrefix = "elsa-studio:notifications:v1:";

const maxEntries = 50;
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

function restoreEntries(value: unknown, now: number): StudioNotificationEntry[] {
  if (!Array.isArray(value)) return [];
  const oldest = now - maxAgeMs;
  const restored: StudioNotificationEntry[] = [];
  for (const raw of value) {
    if (!raw || typeof raw !== "object") continue;
    const item = raw as Partial<StudioNotificationEntry>;
    if (typeof item.id !== "string" || typeof item.title !== "string"
      || !knownLevels.includes(item.level as StudioNotificationLevel)
      || typeof item.createdAt !== "number" || item.createdAt < oldest) continue;
    const { action: _action, ...rest } = item as StudioNotificationEntry;
    const interrupted = rest.level === "progress";
    restored.push({
      ...rest,
      level: interrupted ? "warning" : rest.level,
      message: interrupted ? restoredProgressMessage : rest.message,
      source: typeof rest.source === "string" ? rest.source : "studio",
      updatedAt: typeof rest.updatedAt === "number" ? rest.updatedAt : rest.createdAt,
      read: rest.read === true,
      toasting: false
    });
  }
  return restored.sort((a, b) => b.createdAt - a.createdAt).slice(0, maxEntries);
}

function parseEntries(json: string | null, now: number): StudioNotificationEntry[] {
  if (!json) return [];
  try { return restoreEntries(JSON.parse(json), now); } catch { return []; }
}

/** Persists notifications in `localStorage`; every access is guarded so a blocked or full store only loses persistence. */
export function createLocalNotificationStorage(options: LocalNotificationStorageOptions = {}): StudioNotificationStorage & { flush(): void } {
  const getStorage = options.storage ?? defaultStorage;
  const now = options.now ?? Date.now;
  const debounceMs = options.debounceMs ?? defaultDebounceMs;
  const pending = new Map<string, StudioNotificationEntry[]>();
  let timer: ReturnType<typeof setTimeout> | undefined;
  let listeningForPageHide = false;

  const write = (scope: string, entries: StudioNotificationEntry[]) => {
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

  return {
    load(scope) {
      try {
        return parseEntries(getStorage()?.getItem(notificationStorageKeyPrefix + scope) ?? null, now());
      } catch { return []; }
    },
    save(scope, entries) {
      try {
        pending.set(scope, entries.slice(0, maxEntries).map(({ action: _action, ...rest }) => rest));
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
        try { listener(parseEntries(event.newValue, now())); } catch { /* a faulty listener must not break the event loop */ }
      };
      window.addEventListener("storage", onStorage);
      return () => window.removeEventListener("storage", onStorage);
    },
    flush
  };
}
