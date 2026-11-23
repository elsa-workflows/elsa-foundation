import { expect, test } from "@playwright/test";
import { canonicalDashboardDefinition as canonical } from "./dashboardJourneySupport";

test("native optional-null and UTC rendering preserve a validated saved definition", () => {
  const authored = { query: { time: { from: "2026-11-12T00:00:00.1234567Z", to: "2026-11-13T00:00:00Z" }, metrics: [{ metric: "started", alias: "total" }], filters: { value: { type: "null" } } }, parameterValues: {} };
  const stored = { query: { time: { from: "2026-11-12T00:00:00.1234567+00:00", to: "2026-11-13T00:00:00.0000000+00:00", bucket: null }, metrics: [{ metric: "started", alias: "total", field: null }], filters: { value: { type: "null", literal: null }, any: null }, comparison: null }, parameterValues: {} };
  expect(canonical(stored)).toEqual(canonical(authored));
  expect(canonical({ ...authored, query: { ...authored.query, time: { ...authored.query.time, from: "2026-11-12T00:00:00.1234568Z" } } })).not.toEqual(canonical(authored));
  expect(canonical({ ...authored, query: { ...authored.query, metrics: [{ metric: "failed", alias: "total" }] } })).not.toEqual(canonical(authored));
  expect(canonical({ ...authored, query: { ...authored.query, filters: { value: { type: "string", literal: "null" } } } })).not.toEqual(canonical(authored));
});
