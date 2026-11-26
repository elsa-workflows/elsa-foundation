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
  /**
   * Changes the entry in place. A key absent from `patch` is left untouched; an optional key present
   * with the value `undefined` (for example `message: undefined`) clears that field. `level` and
   * `title` cannot be cleared. Changing the level into `success`, `error` or `warning` marks the
   * entry unread and raises its toast again. A no-op once the entry is dismissed or evicted.
   */
  update(patch: Partial<StudioNotificationInput>): void;
  /** Removes the entry; a no-op once it is gone. */
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

/**
 * Host-side view of the notification store. Modules only ever receive `api`; every other member is
 * host-only and is used by the shell, the toast stack and the Notification Center.
 */
export interface StudioNotificationController {
  readonly api: StudioNotificationApi;
  /** Host-only. Newest first, at most `maxNotificationEntries`. The array reference changes only when something changed. */
  getEntries(): readonly StudioNotificationEntry[];
  /** Host-only. Called after every change to the entries or to toast suppression. */
  subscribe(listener: () => void): () => void;
  /** Host-only. Switches the persisted list to the given identity/backend scope; `null` clears it (sign-out). */
  setScope(scope: string | null): void;
  /** Host-only. Applies a list written by another tab; never saved back. */
  replaceEntries(entries: StudioNotificationEntry[]): void;
  /** Host-only. Closes the entry's toast; the entry stays in the Notification Center. */
  hideToast(id: string): void;
  /** Host-only. Removes the entry's action so a one-shot action (for example Restore) cannot run twice. */
  clearAction(id: string): void;
  /**
   * Host-only. While on (the Notification Center is open), every toast is hidden and new or re-raised
   * entries do not toast; they still appear in the Center. Turning it off does not bring toasts back.
   */
  setToastsSuppressed(suppressed: boolean): void;
  /** Host-only. Whether toasts are currently suppressed. */
  getToastsSuppressed(): boolean;
  /** Host-only. */
  markRead(id: string): void;
  /** Host-only. */
  markAllRead(): void;
  /** Host-only. */
  dismiss(id: string): void;
  /** Host-only. */
  dismissAll(): void;
}

/** The store keeps at most this many entries, in memory and in storage. */
export const maxNotificationEntries = 50;

const notificationLevels: readonly StudioNotificationLevel[] = ["info", "success", "warning", "error", "progress"];

/** Unknown levels (for example from a module built against a newer SDK) are shown as `info`. */
export function normalizeNotificationLevel(level: unknown): StudioNotificationLevel {
  return notificationLevels.includes(level as StudioNotificationLevel) ? level as StudioNotificationLevel : "info";
}

/** Toast lifetime is host policy: `null` keeps the toast until it is dismissed or updated. */
export function notificationToastDurationMs(level: StudioNotificationLevel): number | null {
  switch (level) {
    case "info":
    case "success":
      return 8000;
    case "warning":
      return 12000;
    case "error":
    case "progress":
      return null;
    default: {
      const unknownLevel: never = level;
      return unknownLevel;
    }
  }
}

/** Levels that report an outcome; moving into one of them makes an entry news again. */
function isOutcomeLevel(level: StudioNotificationLevel) {
  return level === "success" || level === "error" || level === "warning";
}

/** Applies a patch with explicit field semantics: absent keys stay, optional keys present as `undefined` clear. */
function applyPatch(entry: StudioNotificationEntry, patch: Partial<StudioNotificationInput>): StudioNotificationEntry {
  const next: StudioNotificationEntry = { ...entry };
  if (patch.level !== undefined) next.level = normalizeNotificationLevel(patch.level);
  if (patch.title !== undefined) next.title = patch.title;
  if ("message" in patch) {
    if (patch.message === undefined) delete next.message;
    else next.message = patch.message;
  }
  if ("link" in patch) {
    if (patch.link === undefined) delete next.link;
    else next.link = patch.link;
  }
  if ("action" in patch) {
    if (patch.action === undefined) delete next.action;
    else next.action = patch.action;
  }
  if ("source" in patch) next.source = patch.source ?? "studio";
  return next;
}

function describeSettled(format: () => string, fallback: string): string {
  try {
    return format();
  } catch {
    return fallback;
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
  let toastsSuppressed = false;

  function emit() {
    for (const listener of listeners) listener();
  }

  /** Publishes a changed list (capped to the newest entries), persists it when a scope is set and notifies subscribers. */
  function commit(next: StudioNotificationEntry[]) {
    entries = next.slice(0, maxNotificationEntries);
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
      const merged = applyPatch(entry, patch);
      merged.updatedAt = now();
      if (patch.toast !== undefined) merged.toasting = patch.toast;
      // An outcome that differs from the previous level is news again: unread, with its toast raised.
      if (merged.level !== entry.level && isOutcomeLevel(merged.level)) {
        merged.read = false;
        merged.toasting = patch.toast !== false;
      }
      if (toastsSuppressed) merged.toasting = false;
      return merged;
    });
  }

  function dismiss(id: string) {
    if (!entries.some(entry => entry.id === id)) return;
    commit(entries.filter(entry => entry.id !== id));
  }

  const api: StudioNotificationApi = {
    notify(input) {
      const { toast, source, level, ...fields } = input;
      const timestamp = now();
      const id = createId();
      commit([{
        ...fields,
        id,
        level: normalizeNotificationLevel(level),
        source: source ?? "studio",
        createdAt: timestamp,
        updatedAt: timestamp,
        read: false,
        toasting: toast !== false && !toastsSuppressed
      }, ...entries]);
      return {
        id,
        update: patch => update(id, patch),
        dismiss: () => dismiss(id)
      };
    },
    track(work, messages) {
      const handle = api.notify({ level: "progress", title: messages.progress });
      // The caller gets the original result or rejection even when a message formatter throws.
      return work.then(
        result => {
          const { success } = messages;
          const title = describeSettled(() => (typeof success === "function" ? success(result) : success), "Operation finished");
          handle.update({ level: "success", title, message: undefined });
          return result;
        },
        (error: unknown) => {
          const { error: failure } = messages;
          const title = describeSettled(() => (typeof failure === "function" ? failure(error) : failure), "Operation failed");
          handle.update({ level: "error", title, message: undefined });
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
      entries = [...carried, ...loaded.filter(entry => !carriedIds.has(entry.id))].slice(0, maxNotificationEntries);
      if (next !== null && carried.length > 0) storage?.save(next, entries);
      emit();
    },
    replaceEntries(incoming) {
      const local = new Map(entries.map(entry => [entry.id, entry]));
      entries = incoming.slice(0, maxNotificationEntries).map(entry => {
        const existing = local.get(entry.id);
        // `action` is never persisted and the toast belongs to this tab, so both stay local.
        if (!existing) return entry;
        const merged = { ...entry, toasting: existing.toasting };
        if (existing.action) merged.action = existing.action;
        return merged;
      });
      emit();
    },
    hideToast(id) {
      if (entries.some(entry => entry.id === id && entry.toasting)) patchEntry(id, entry => ({ ...entry, toasting: false }));
    },
    clearAction(id) {
      if (entries.some(entry => entry.id === id && entry.action)) patchEntry(id, ({ action: _action, ...entry }) => entry);
    },
    setToastsSuppressed(suppressed) {
      if (suppressed === toastsSuppressed) return;
      toastsSuppressed = suppressed;
      if (suppressed && entries.some(entry => entry.toasting)) {
        commit(entries.map(entry => (entry.toasting ? { ...entry, toasting: false } : entry)));
      } else {
        emit();
      }
    },
    getToastsSuppressed: () => toastsSuppressed,
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
