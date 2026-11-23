import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import { StudioTimestamp, formatStudioDateTime } from "../data-grid/StudioTimestamp";
import { formatStudioDateParts, setStudioTimeZonePreference } from "../../../sdk/timeZone";

const mounted: { root: Root; host: HTMLElement }[] = [];
afterEach(() => {
  for (const { root, host } of mounted.splice(0)) { flushSync(() => root.unmount()); host.remove(); }
  setStudioTimeZonePreference("browser");
});

describe("StudioTimestamp", () => {
  it("renders a localized date and time with a full timestamp tooltip", () => {
    const value = "2026-11-20T22:59:15.000Z";
    const date = new Date(value);
    const markup = renderToStaticMarkup(<StudioTimestamp value={value} />);

    expect(markup).toContain(`dateTime="${value}"`);
    const parts = formatStudioDateParts(date, "browser");
    expect(markup).toContain(`title="${parts.dateTime}"`);
    expect(markup).toContain(`<span class="studio-timestamp-date">${parts.date}</span>`);
    expect(markup).toContain(`<span class="studio-timestamp-time">${parts.time}</span>`);
    expect(markup).toMatch(/UTC[+-]\d{2}:\d{2}/);
  });

  it.each(["time", "date-time"] as const)("retains diagnostic seconds in the %s format", format => {
    const value = "2026-11-20T22:59:15.000Z";
    const parts = formatStudioDateParts(new Date(value), "browser");
    const formatted = format === "time" ? parts.time : parts.dateTime;
    const markup = renderToStaticMarkup(<StudioTimestamp value={value} format={format} />);
    expect(markup).toContain(`dateTime="${value}"`);
    expect(markup).toContain(`>${formatted}</time>`);
  });

  it("uses the caller fallback for missing or invalid values", () => {
    expect(renderToStaticMarkup(<StudioTimestamp fallback="Unavailable" />)).toBe("Unavailable");
    expect(renderToStaticMarkup(<StudioTimestamp value="not-a-date" fallback="Unavailable" />)).toBe("Unavailable");
  });

  it("preserves an invalid raw value when no fallback is supplied", () => {
    expect(renderToStaticMarkup(<StudioTimestamp value="not-a-date" />)).toBe("not-a-date");
  });

  it.each([
    ["2026-01-15T23:30:15Z", "Europe/Amsterdam", "UTC+01:00"],
    ["2026-07-15T23:30:15Z", "Europe/Amsterdam", "UTC+02:00"],
    ["2026-07-15T23:30:15Z", "UTC", "UTC+00:00"],
    ["2026-10-25T01:30:15Z", "Europe/Amsterdam", "UTC+01:00"]
  ])("keeps the instant and date-specific offset for %s in %s in every layout", (value, zone, offset) => {
    setStudioTimeZonePreference(zone);
    for (const format of ["stacked", "time", "date-time"] as const) {
      const host = document.createElement("div"); document.body.append(host);
      const root = createRoot(host); mounted.push({ root, host });
      flushSync(() => root.render(<StudioTimestamp value={value} format={format} />));
      const time = host.querySelector("time")!;
      expect(time.getAttribute("datetime")).toBe(value);
      expect(time.textContent).toContain(offset);
      expect(time.getAttribute("title")).toContain(offset);
      const clock = new Intl.DateTimeFormat(undefined, { timeZone: zone, timeStyle: "medium" }).format(new Date(value));
      expect(time.textContent).toContain(clock);
      if (format === "stacked") expect(time.querySelector(".studio-timestamp-date")?.textContent)
        .toBe(new Intl.DateTimeFormat(undefined, { timeZone: zone, dateStyle: "medium" }).format(new Date(value)));
    }
  });

  it("updates the mounted timestamp when the preference changes without changing its instant or node", () => {
    setStudioTimeZonePreference("UTC");
    const host = document.createElement("div"); document.body.append(host);
    const root = createRoot(host); mounted.push({ root, host });
    const value = "2026-07-15T23:30:15Z";
    flushSync(() => root.render(<StudioTimestamp value={value} />));
    const time = host.querySelector("time")!;
    expect(time.textContent).toContain("UTC+00:00");
    flushSync(() => setStudioTimeZonePreference("Europe/Amsterdam"));
    expect(host.querySelector("time")).toBe(time);
    expect(time.getAttribute("datetime")).toBe(value);
    expect(time.textContent).toContain("UTC+02:00");
  });

  it("formats inline confirmation dates in the same selected zone and retains caller fallbacks", () => {
    setStudioTimeZonePreference("UTC");
    expect(formatStudioDateTime("2026-07-15T23:30:15Z", "missing")).toContain("UTC+00:00");
    setStudioTimeZonePreference("Europe/Amsterdam");
    expect(formatStudioDateTime("2026-07-15T23:30:15Z", "missing")).toContain("UTC+02:00");
    expect(formatStudioDateTime("bad date", "missing")).toBe("missing");
    expect(formatStudioDateTime(null, "missing")).toBe("missing");
  });
});
