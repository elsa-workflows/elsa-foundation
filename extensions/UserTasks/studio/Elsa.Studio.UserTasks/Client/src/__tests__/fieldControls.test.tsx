import { describe, expect, it } from "vitest";
import { createContributionRegistry, studioSlots, type StudioUserTaskFieldControlContribution } from "@elsa-workflows/studio-sdk";
import { parseTaskFormSource, serializeTaskFormSource, validateTaskForm } from "../formDesignerModel";
import { builtInTaskFieldControls, resolveTaskFieldControl, validateTaskFieldControlConfiguration } from "../fieldControls";
import type { TaskField } from "../taskApi";

const customControl = (id = "example.rating/1"): StudioUserTaskFieldControlContribution => ({
  id,
  label: "Rating",
  valueKind: "number",
  component: ({ id, disabled, value, onChange }) => <input id={id} type="number" value={typeof value === "number" ? value : ""} disabled={disabled} onChange={event => onChange(Number(event.target.value))} />,
  createField: fields => ({ key: `rating${fields.length + 1}`, label: "Rating", kind: "number", required: false, readOnly: false, controlId: id }),
  validateConfiguration: () => []
});

describe("user-task field controls", () => {
  it("provides namespaced built-ins for all legacy field kinds", () => {
    expect(builtInTaskFieldControls.map(control => control.legacyKind)).toEqual(["text", "multiline", "number", "boolean", "date", "choice"]);
    expect(builtInTaskFieldControls.every(control => /^[a-z0-9.-]+\/[1-9]\d*$/.test(control.id))).toBe(true);
    expect(resolveTaskFieldControl({ kind: "text" } as TaskField, builtInTaskFieldControls)).toMatchObject({ status: "available", control: builtInTaskFieldControls[0] });
  });

  it("resolves custom controls by exact persisted identity and rejects duplicates", () => {
    const registry = createContributionRegistry<StudioUserTaskFieldControlContribution>({ slot: studioSlots.userTaskFieldControls });
    const control = customControl();
    registry.add(control);
    expect(resolveTaskFieldControl({ kind: "number", controlId: control.id } as TaskField, registry)).toMatchObject({ status: "available", control });

    registry.add(customControl());
    expect(resolveTaskFieldControl({ kind: "number", controlId: control.id } as TaskField, registry)).toMatchObject({ status: "ambiguous" });
  });

  it("rejects competing legacy-kind aliases instead of selecting by registration order", () => {
    const alias = { ...customControl("example.legacy-text/1"), legacyKind: "text" };
    expect(resolveTaskFieldControl({ kind: "text" } as TaskField, [builtInTaskFieldControls[0], alias])).toMatchObject({ status: "ambiguous" });
  });

  it("does not select host-policy-blocked controls", () => {
    const control = customControl();
    const registry = createContributionRegistry<StudioUserTaskFieldControlContribution>({
      slot: studioSlots.userTaskFieldControls,
      hostPolicy: () => ({ state: "unavailable", reason: "The control is blocked by host policy." })
    });
    registry.add(control);

    expect(resolveTaskFieldControl({ kind: "number", controlId: control.id } as TaskField, registry)).toMatchObject({
      status: "unavailable",
      reason: "The control is blocked by host policy."
    });
  });

  it("bounds custom configuration and runs the contribution validator", () => {
    const control = customControl();
    expect(validateTaskFieldControlConfiguration(control, { precision: 2 })).toEqual([]);
    expect(validateTaskFieldControlConfiguration(control, { text: "x".repeat(20_000) })).toContain("Control configuration exceeds the supported size limit.");
    expect(validateTaskFieldControlConfiguration(control, ["not", "an", "object"])).toContain("Control configuration must be a JSON object.");
  });

  it("round-trips custom identity/configuration and keeps missing controls unavailable without losing source", () => {
    const control = customControl();
    const form = { id: "custom", version: "1", name: "Custom", provider: "elsa.basic-forms/1", fields: [
      { key: "rating", label: "Rating", kind: "number", required: true, readOnly: false, controlId: control.id, configuration: { precision: 2 } }
    ] };
    const serialized = serializeTaskFormSource(form);
    expect(parseTaskFormSource(serialized, form, [control])).toEqual(form);

    const unavailable = parseTaskFormSource(serialized, form, builtInTaskFieldControls);
    expect(unavailable.fields[0]).toMatchObject({ controlId: control.id, configuration: { precision: 2 } });
    expect(validateTaskForm(unavailable, builtInTaskFieldControls).join(" ")).toContain("unavailable");
  });
});