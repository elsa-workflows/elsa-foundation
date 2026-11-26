import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, expect, it } from "vitest";
import { setStudioTimeZonePreference } from "@elsa-workflows/studio-sdk";
import { ResultView } from "../ResultView";
import type { QueryResult } from "../types";

const instant = "2026-07-15T23:30:00Z";
const result: QueryResult = {
  outcome: "succeeded", generatedAt: instant,
  columns: [{ id: "instant", label: "Started", type: "instant" }, { id: "text", label: "Literal", type: "string" }, { id: "date", label: "Date", type: "date" }],
  rows: [{ key: "one", values: { instant: { type: "instant", value: instant }, text: { type: "string", value: instant }, date: { type: "date", value: "2026-07-15" } } }],
  periods: [{ role: "current", from: instant, to: "2026-07-16T23:30:00Z", timeZone: "America/New_York" }]
};
const cleanups: (() => void)[] = [];
afterEach(() => { cleanups.splice(0).forEach(cleanup => cleanup()); setStudioTimeZonePreference("browser"); });

it("converts typed instants, refresh and period dates live while retaining raw attributes, query zone, strings and date-only values", () => {
  const original = JSON.stringify(result);
  setStudioTimeZonePreference("Europe/Amsterdam");
  const container = document.createElement("div"); document.body.append(container);
  const root = createRoot(container);
  cleanups.push(() => { flushSync(() => root.unmount()); container.remove(); });
  flushSync(() => root.render(<ResultView result={result} visualization={{ kind: "table", y: [] }} />));
  const expected = (zone: string, offset: string) => `${new Intl.DateTimeFormat(undefined, { timeZone: zone, dateStyle: "medium", timeStyle: "medium" }).format(new Date(instant))} UTC${offset}`;
  // The refresh time, the period start and the typed instant cell render the instant; each keeps its raw value.
  const dates = [...container.querySelectorAll("time")].filter(date => date.dateTime === instant);
  expect(dates.length).toBeGreaterThanOrEqual(3);
  for (const date of dates) expect(date.textContent).toBe(expected("Europe/Amsterdam", "+02:00"));
  const cells = container.querySelectorAll("tbody td");
  expect(cells[0].textContent).toBe(expected("Europe/Amsterdam", "+02:00"));
  // A literal string stays as typed, and a date-only value names its calendar day in every zone (never shifted).
  expect(cells[1].textContent).toBe(instant);
  const dateOnly = cells[2].textContent;
  expect(dateOnly).toMatch(/15/);
  expect(container.textContent).toContain("query zone: America/New_York; end exclusive");
  flushSync(() => setStudioTimeZonePreference("UTC"));
  expect(cells[0].textContent).toBe(expected("UTC", "+00:00"));
  expect(cells[2].textContent).toBe(dateOnly);
  for (const date of dates) expect(date.textContent).toBe(expected("UTC", "+00:00"));
  expect(JSON.stringify(result)).toBe(original);
});
