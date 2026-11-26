export type ToastDurationPreference = "default" | "manual" | number;

export const toastDurationPreferenceStorageKey = "elsa-studio:toast-duration:v1";

const minimumToastDurationSeconds = 1;
const maximumToastDurationSeconds = 60;

type PreferenceStorage = Pick<Storage, "getItem" | "setItem">;

function localStorageOrNull(): PreferenceStorage | null {
  try { return window.localStorage; } catch { return null; }
}

function isValidSeconds(value: number): boolean {
  return Number.isInteger(value) && value >= minimumToastDurationSeconds && value <= maximumToastDurationSeconds;
}

function normalizePreference(preference: ToastDurationPreference): ToastDurationPreference {
  if (preference === "default" || preference === "manual") return preference;
  return isValidSeconds(preference) ? preference : "default";
}

/** Reads the browser-local override; missing or invalid values follow per-level defaults. */
export function readToastDurationPreference(storage: PreferenceStorage | null = localStorageOrNull()): ToastDurationPreference {
  if (!storage) return "default";
  try {
    const stored = storage.getItem(toastDurationPreferenceStorageKey);
    if (stored === "default" || stored === "manual") return stored;
    if (stored === null) return "default";
    const seconds = Number(stored);
    return isValidSeconds(seconds) ? seconds : "default";
  } catch {
    return "default";
  }
}

/** Saves the browser-local override without making notification delivery depend on storage access. */
export function saveToastDurationPreference(preference: ToastDurationPreference, storage: PreferenceStorage | null = localStorageOrNull()): void {
  if (!storage) return;
  try { storage.setItem(toastDurationPreferenceStorageKey, String(normalizePreference(preference))); } catch { /* Storage may be disabled or full. */ }
}