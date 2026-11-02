export type StudioNotificationLevel = "info" | "success" | "warning" | "error" | "progress";

export interface StudioNotificationLink {
  label: string;
  /** Studio route opened through host navigation. External URLs are not supported. */
  path: string;
}

export interface StudioNotificationAction {
  label: string;
  run(): void | Promise<void>;
}

export interface StudioNotificationInput {
  level: StudioNotificationLevel;
  title: string;
  message?: string;
  link?: StudioNotificationLink;
  /** Not persisted: after a reload the entry keeps its link but loses its action. */
  action?: StudioNotificationAction;
  /** Default true; false records in the Notification Center only. */
  toast?: boolean;
  /** Reserved for future server-pushed notifications; defaults to "studio". */
  source?: string;
}

export interface StudioNotificationHandle {
  readonly id: string;
  update(patch: Partial<StudioNotificationInput>): void;
  dismiss(): void;
}

export interface StudioNotificationApi {
  notify(input: StudioNotificationInput): StudioNotificationHandle;
  track<T>(work: Promise<T>, messages: {
    progress: string;
    success: string | ((result: T) => string);
    error: string | ((error: unknown) => string);
  }): Promise<T>;
}

export interface StudioNotificationEntry {
  id: string;
  level: StudioNotificationLevel;
  title: string;
  message?: string;
  link?: StudioNotificationLink;
  action?: StudioNotificationAction;
  source: string;
  createdAt: number;
  updatedAt: number;
  read: boolean;
  /** Whether the entry is currently shown (or queued) as a toast. */
  toasting: boolean;
}

export interface StudioNotificationStorage {
  load(scope: string): StudioNotificationEntry[];
  save(scope: string, entries: StudioNotificationEntry[]): void;
  /** Reports changes made to the scope by another Studio tab. */
  subscribe?(scope: string, listener: (entries: StudioNotificationEntry[]) => void): () => void;
}

export interface StudioNotificationController {
  readonly api: StudioNotificationApi;
  /** Newest first. The array reference changes only when something changed. */
  getEntries(): readonly StudioNotificationEntry[];
  subscribe(listener: () => void): () => void;
  setScope(scope: string | null): void;
  /** Applies a list written by another tab; never saved back. */
  replaceEntries(entries: StudioNotificationEntry[]): void;
  hideToast(id: string): void;
  markRead(id: string): void;
  markAllRead(): void;
  dismiss(id: string): void;
  dismissAll(): void;
}

/** Toast lifetime is host policy: `null` keeps the toast until it is dismissed or updated. */
export function notificationToastDurationMs(level: StudioNotificationLevel): number | null {
  switch (level) {
    case "info":
    case "success":
      return 8000;
    case "warning":
      return 12000;
    default:
      return null;
  }
}

export function createNotificationController(options: {
  storage?: StudioNotificationStorage;
  now?: () => number;
  createId?: () => string;
} = {}): StudioNotificationController {
  const { storage } = options;
  const now = options.now ?? Date.now;
  let idCounter = 0;
  const createId = options.createId ?? (() => globalThis.crypto?.randomUUID?.() ?? `notification-${Date.now()}-${++idCounter}`);

  const listeners = new Set<() => void>();
  let entries: StudioNotificationEntry[] = [];
  let scope: string | null = null;

  function emit() {
    for (const listener of listeners) listener();
  }

  /** Publishes a changed list, persists it when a scope is set and notifies subscribers. */
  function commit(next: StudioNotificationEntry[]) {
    entries = next;
    if (scope !== null) storage?.save(scope, entries);
    emit();
  }

  function patchEntry(id: string, change: (entry: StudioNotificationEntry) => StudioNotificationEntry) {
    const index = entries.findIndex(entry => entry.id === id);
    if (index < 0) return;
    commit(entries.map((entry, i) => (i === index ? change(entry) : entry)));
  }

  function update(id: string, patch: Partial<StudioNotificationInput>) {
    patchEntry(id, entry => {
      const { toast, ...fields } = patch;
      // Only defined values are merged, so a patch never erases a field by passing `undefined`.
      const defined = Object.fromEntries(Object.entries(fields).filter(([, value]) => value !== undefined));
      const merged: StudioNotificationEntry = { ...entry, ...defined, updatedAt: now() };
      if (toast !== undefined) merged.toasting = toast;
      // A finished operation is news again: unread, with its toast raised.
      if (entry.level === "progress" && (merged.level === "success" || merged.level === "error" || merged.level === "warning")) {
        merged.read = false;
        merged.toasting = toast !== false;
      }
      return merged;
    });
  }

  function dismiss(id: string) {
    if (!entries.some(entry => entry.id === id)) return;
    commit(entries.filter(entry => entry.id !== id));
  }

  const api: StudioNotificationApi = {
    notify(input) {
      const { toast, source, ...fields } = input;
      const timestamp = now();
      const id = createId();
      commit([{
        ...fields,
        id,
        source: source ?? "studio",
        createdAt: timestamp,
        updatedAt: timestamp,
        read: false,
        toasting: toast !== false
      }, ...entries]);
      return {
        id,
        update: patch => update(id, patch),
        dismiss: () => dismiss(id)
      };
    },
    track(work, messages) {
      const handle = api.notify({ level: "progress", title: messages.progress });
      return work.then(
        result => {
          handle.update({ level: "success", title: typeof messages.success === "function" ? messages.success(result) : messages.success });
          return result;
        },
        (error: unknown) => {
          handle.update({ level: "error", title: typeof messages.error === "function" ? messages.error(error) : messages.error });
          throw error;
        }
      );
    }
  };

  return {
    api,
    getEntries: () => entries,
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    setScope(next) {
      if (next === scope) return;
      // Entries published while no scope was set (module startup) belong to the first scope that arrives.
      const carried = scope === null ? entries : [];
      if (scope !== null) storage?.save(scope, entries);
      scope = next;
      const loaded = next === null ? [] : storage?.load(next) ?? [];
      const carriedIds = new Set(carried.map(entry => entry.id));
      entries = [...carried, ...loaded.filter(entry => !carriedIds.has(entry.id))];
      if (next !== null && carried.length > 0) storage?.save(next, entries);
      emit();
    },
    replaceEntries(incoming) {
      const local = new Map(entries.map(entry => [entry.id, entry]));
      entries = incoming.map(entry => {
        const existing = local.get(entry.id);
        // `action` is never persisted and the toast belongs to this tab, so both stay local.
        return existing ? { ...entry, action: existing.action, toasting: existing.toasting } : entry;
      });
      emit();
    },
    hideToast(id) {
      if (entries.some(entry => entry.id === id && entry.toasting)) patchEntry(id, entry => ({ ...entry, toasting: false }));
    },
    markRead(id) {
      if (entries.some(entry => entry.id === id && !entry.read)) patchEntry(id, entry => ({ ...entry, read: true }));
    },
    markAllRead() {
      if (entries.some(entry => !entry.read)) commit(entries.map(entry => (entry.read ? entry : { ...entry, read: true })));
    },
    dismiss,
    dismissAll() {
      if (entries.length > 0) commit([]);
    }
  };
}
