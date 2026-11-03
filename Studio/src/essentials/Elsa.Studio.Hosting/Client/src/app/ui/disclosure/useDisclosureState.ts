import { useCallback, useState } from "react";

export const disclosureStoragePrefix = "elsa-studio.ui.";

export function disclosureStorageKey(persistKey: string): string {
  return persistKey.startsWith(disclosureStoragePrefix)
    ? persistKey
    : `${disclosureStoragePrefix}${persistKey}`;
}

export function isDisclosureBoolean(value: unknown): value is boolean {
  return value === true || value === false;
}

export function isDisclosureString(value: unknown): value is string {
  return typeof value === "string" && value.length > 0;
}

function defaultStorage(): Storage | undefined {
  try {
    return typeof window === "undefined" ? undefined : window.localStorage;
  } catch {
    return undefined;
  }
}

function safely<T>(action: () => T, fallback: T): T {
  try {
    return action();
  } catch {
    return fallback;
  }
}

export function readDisclosureState<T>(
  persistKey: string | undefined,
  fallback: T,
  validate: (value: unknown) => value is T,
  storage: Storage | undefined = defaultStorage()
): T {
  if (!persistKey || !storage) return fallback;
  const raw = safely(() => storage.getItem(disclosureStorageKey(persistKey)), null);
  if (raw == null) return fallback;
  return safely(() => {
    const parsed: unknown = JSON.parse(raw);
    return validate(parsed) ? parsed : fallback;
  }, fallback);
}

export function writeDisclosureState<T>(
  persistKey: string | undefined,
  value: T,
  storage: Storage | undefined = defaultStorage()
) {
  if (!persistKey || !storage) return;
  safely(() => storage.setItem(disclosureStorageKey(persistKey), JSON.stringify(value)), undefined);
}

/**
 * Persist a disclosure value under `elsa-studio.ui.*`. Storage errors are swallowed
 * and unknown values fall back to `defaultValue`. This is the disclosure-scoped helper
 * the kit components use for `persistKey`; the broader Appearance-adjacent A1′ polish
 * can later promote the same namespace.
 */
export function useDisclosureState<T>(
  persistKey: string | undefined,
  defaultValue: T,
  validate: (value: unknown) => value is T
): [T, (value: T) => void] {
  const [value, setValue] = useState(() => readDisclosureState(persistKey, defaultValue, validate));

  const update = useCallback((next: T) => {
    setValue(next);
    writeDisclosureState(persistKey, next);
  }, [persistKey]);

  return [value, update];
}
