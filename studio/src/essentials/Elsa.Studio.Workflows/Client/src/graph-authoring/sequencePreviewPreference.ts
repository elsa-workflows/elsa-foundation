import { useSyncExternalStore } from "react";

export const sequencePreviewPreferenceStorageKey = "elsa.studio.sequence-activity-preview";

const listeners = new Set<() => void>();
let storageAccessFailed = false;
let previewEnabled = readStoredPreference() ?? false;
let storageWindow: Window | null = null;

export function useSequenceActivityPreviewPreference(): [boolean, (enabled: boolean) => void] {
  const enabled = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
  return [enabled, setSequenceActivityPreviewEnabled];
}

export function setSequenceActivityPreviewEnabled(enabled: boolean) {
  try {
    if (typeof window !== "undefined") {
      window.localStorage.setItem(sequencePreviewPreferenceStorageKey, String(enabled));
      storageAccessFailed = false;
    }
  } catch {
    storageAccessFailed = true;
    // Keep the preference usable for the current page when browser storage is unavailable.
  }

  updatePreference(enabled);
}

function subscribe(listener: () => void) {
  const storedPreference = readStoredPreference();
  if (storedPreference !== null) updatePreference(storedPreference);

  listeners.add(listener);
  if (listeners.size === 1 && typeof window !== "undefined") {
    storageWindow = window;
    storageWindow.addEventListener("storage", handleStorageChange);
  }

  return () => {
    listeners.delete(listener);
    if (listeners.size === 0 && storageWindow) {
      storageWindow.removeEventListener("storage", handleStorageChange);
      storageWindow = null;
    }
  };
}

function getSnapshot() {
  return previewEnabled;
}

function getServerSnapshot() {
  return false;
}

function handleStorageChange(event: StorageEvent) {
  if (event.key !== sequencePreviewPreferenceStorageKey && event.key !== null) return;
  updatePreference(event.key === sequencePreviewPreferenceStorageKey && event.newValue === "true");
}

function updatePreference(enabled: boolean) {
  if (previewEnabled === enabled) return;
  previewEnabled = enabled;
  for (const listener of listeners) listener();
}

function readStoredPreference() {
  if (typeof window === "undefined" || storageAccessFailed) return null;

  try {
    return window.localStorage.getItem(sequencePreviewPreferenceStorageKey) === "true";
  } catch {
    storageAccessFailed = true;
    return null;
  }
}