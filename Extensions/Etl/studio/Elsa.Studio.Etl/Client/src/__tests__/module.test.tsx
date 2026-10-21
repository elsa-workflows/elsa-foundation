import { describe, expect, it, vi } from "vitest";
import { createContributionRegistry, createEndpointContext, studioSlots,
  type StudioActivityPropertyEditorContribution, type StudioFeatureAreaContribution } from "@elsa-workflows/studio-sdk";
import { register } from "../module";

// Real SDK registries prove this contribution uses the public slots and their ordering contracts.
describe("ETL module registration", () => {
  it("owns its routes and claims only the scalar ETL Pipeline property", () => {
    const api = {
      backend: createEndpointContext("/backend"),
      featureAreas: createContributionRegistry<StudioFeatureAreaContribution>({ slot: studioSlots.featureAreas }),
      propertyEditors: createContributionRegistry<StudioActivityPropertyEditorContribution>({ slot: studioSlots.propertyEditors }),
      dialogs: { confirm: vi.fn(async () => false), prompt: vi.fn(async () => null), alert: vi.fn(async () => {}) }
    };
    register(api);
    const area = api.featureAreas.list()[0];
    expect(area.ownedPaths).toEqual(["/etl/definitions", "/etl/runs"]);
    expect(area.routes.map(route => route.path)).toEqual(area.ownedPaths);
    const editor = api.propertyEditors.list()[0];
    const input = { name: "Pipeline", typeName: "System.String", uiHint: "etl-published-selection" };
    const context = { activity: {}, expressionDescriptors: [] };
    expect(editor.order).toBeLessThan(500);
    expect(editor.supports(input, context)).toBe(true);
    expect(editor.supports(input, { ...context, readOnly: true })).toBe(true);
    expect(editor.supports(input, { ...context, scope: "collection" })).toBe(false);
    expect(editor.supports({ ...input, uiHint: null }, context)).toBe(false);
    expect(editor.supports({ ...input, name: "AnotherInput" }, context)).toBe(false);
  });
});
