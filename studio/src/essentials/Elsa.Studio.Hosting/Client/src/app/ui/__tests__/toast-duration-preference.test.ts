import { describe, expect, it } from "vitest";
import {
  readToastDurationPreference,
  saveToastDurationPreference,
  toastDurationPreferenceStorageKey
} from "../../toastDurationPreference";

function createStorage() {
  const values = new Map<string, string>();
  return {
    values,
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => { values.set(key, value); }
  };
}

describe("toast duration preference", () => {
  it("defaults safely and persists custom and manual-only choices", () => {
    const storage = createStorage();
    expect(readToastDurationPreference(storage)).toBe("default");

    saveToastDurationPreference(5, storage);
    expect(storage.values.get(toastDurationPreferenceStorageKey)).toBe("5");
    expect(readToastDurationPreference(storage)).toBe(5);

    saveToastDurationPreference("manual", storage);
    expect(readToastDurationPreference(storage)).toBe("manual");
  });

  it("ignores out-of-range values and unavailable storage", () => {
    const storage = createStorage();
    storage.values.set(toastDurationPreferenceStorageKey, "61");
    expect(readToastDurationPreference(storage)).toBe("default");
    storage.values.set(toastDurationPreferenceStorageKey, "not-a-number");
    expect(readToastDurationPreference(storage)).toBe("default");
    expect(readToastDurationPreference(null)).toBe("default");
    expect(() => saveToastDurationPreference(5, null)).not.toThrow();
  });
});