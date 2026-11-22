import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { formatStudioDate, getStudioTimeZonePreference, isStudioTimeZone, localDateTimeToIso, setStudioTimeZonePreference, studioLocalDateTime, studioTimeZoneStorageKey, useStudioTimeZone } from "../sdk/timeZone";
import { readAppearancePreferences, resetAppearancePreferences, writeAppearancePreferences } from "../app/themes/appearance";

beforeEach(() => { localStorage.clear(); setStudioTimeZonePreference("browser"); });
afterEach(() => { vi.restoreAllMocks(); setStudioTimeZonePreference("browser"); localStorage.clear(); });

describe("Studio date conversion", () => {
  it.each([
    ["2026-01-15T12:30", "Europe/Amsterdam", "2026-01-15T12:30+01:00"],
    ["2026-07-15T12:30", "Europe/Amsterdam", "2026-07-15T12:30+02:00"],
    ["2026-07-15T12:30", "UTC", "2026-07-15T12:30+00:00"],
    ["2026-10-25T02:30", "Europe/Amsterdam", "2026-10-25T02:30+02:00"],
    ["2026-07-15T12:30:01.1234567", "UTC", "2026-07-15T12:30:01.1234567+00:00"],
    ["2026-01-15T12:30+05:45", "Europe/Amsterdam", "2026-01-15T12:30+05:45"]
  ])("uses the offset for %s in %s", (value, zone, expected) => expect(localDateTimeToIso(value, zone)).toBe(expected));

  it.each(["2026-02-30T12:30", "2026-01-15T24:00", "0000-01-01T00:00Z", "2026-07-15T12:30+23:59", "2026-07-15T12:30+14:01", "0001-01-01T00:00+14:00", "9999-12-31T23:59:59-14:00"])("refuses an impossible or out-of-range date %s", value => expect(() => localDateTimeToIso(value, "UTC")).toThrow(RangeError));
  it("refuses a DST gap instead of moving the user's time", () => expect(() => localDateTimeToIso("2026-03-29T02:30", "Europe/Amsterdam")).toThrow("does not exist"));
  it("preserves precision and the later repeated-hour instant on unchanged edits", () => {
    const original = "2026-10-25T01:30:00.1234567Z";
    const wall = studioLocalDateTime(original, "Europe/Amsterdam");
    expect(wall).toBe("2026-10-25T02:30:00.1234567");
    expect(localDateTimeToIso(wall, "Europe/Amsterdam", original)).toBe("2026-10-25T02:30:00.1234567+01:00");
  });
  it("shows explicit offsets including historical second offsets", () => {
    expect(formatStudioDate("2026-01-15T12:30:00Z", "Europe/Amsterdam")).toContain("UTC+01:00");
    expect(formatStudioDate("2026-07-15T12:30:00Z", "Europe/Amsterdam")).toContain("UTC+02:00");
    expect(formatStudioDate("1900-01-01T00:00:00Z", "Europe/Paris")).toContain("UTC+00:09:21");
    expect(formatStudioDate("bad date", "UTC")).toBe("bad date");
  });
});

describe("Studio time zone preference", () => {
  it("validates persisted values, reloads a saved choice and resets to browser", () => {
    localStorage.setItem(studioTimeZoneStorageKey, "bad zone");
    expect(readAppearancePreferences().timeZone).toBe("browser");
    expect(isStudioTimeZone("+01:00")).toBe(false);
    writeAppearancePreferences({ timeZone: "Europe/Amsterdam" });
    expect(readAppearancePreferences().timeZone).toBe("Europe/Amsterdam");
    expect(getStudioTimeZonePreference()).toBe("Europe/Amsterdam");
    resetAppearancePreferences();
    expect(getStudioTimeZonePreference()).toBe("browser");
  });
  it("updates a mounted view without replacing its unsaved input", () => {
    const host = document.createElement("div"); document.body.append(host); const root = createRoot(host);
    function Probe() { useStudioTimeZone(); return <><input defaultValue="unsaved" /><time>{formatStudioDate("2026-07-15T12:30:00Z")}</time></>; }
    try {
      flushSync(() => { setStudioTimeZonePreference("UTC"); root.render(<Probe />); });
      const input = host.querySelector("input")!; input.value = "my unsaved edit";
      flushSync(() => setStudioTimeZonePreference("Europe/Amsterdam"));
      expect(host.querySelector("time")!.textContent).toContain("UTC+02:00");
      expect(host.querySelector("input")).toBe(input); expect(input.value).toBe("my unsaved edit");
      localStorage.setItem(studioTimeZoneStorageKey, "UTC");
      flushSync(() => window.dispatchEvent(new StorageEvent("storage", { key: studioTimeZoneStorageKey, storageArea: localStorage })));
      expect(host.querySelector("time")!.textContent).toContain("UTC+00:00");
    } finally { flushSync(() => root.unmount()); host.remove(); }
  });
  it("keeps the session choice during storage failure and unrelated storage events", () => {
    localStorage.setItem(studioTimeZoneStorageKey, "Europe/Amsterdam");
    const host = document.createElement("div"); const root = createRoot(host);
    function Probe() { return <span>{useStudioTimeZone()}</span>; }
    try {
      flushSync(() => root.render(<Probe />));
      vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("quota"); });
      flushSync(() => setStudioTimeZonePreference("UTC"));
      flushSync(() => window.dispatchEvent(new StorageEvent("storage", { key: "elsa-studio-theme", storageArea: localStorage })));
      expect(host.textContent).toBe("UTC"); expect(getStudioTimeZonePreference()).toBe("UTC");
    } finally { flushSync(() => root.unmount()); host.remove(); }
  });
});
