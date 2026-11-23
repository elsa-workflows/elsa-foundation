import { describe, expect, it, vi } from "vitest";
import { builtInTaskFieldControls, validateTaskFieldConfigurationShape, validateTaskFieldControlConfiguration } from "../fieldControls";
import { parseTaskFormSource, serializeTaskFormSource, validateTaskFields } from "../formDesignerModel";
import type { TaskField, TaskForm } from "../taskApi";

const nullableForm = JSON.parse(`{
  "id":"contact",
  "version":"1",
  "name":"Contact form",
  "provider":"elsa.basic-forms/1",
  "fields":[
    {"key":"accepted","label":"Accept terms","kind":"boolean","required":false,"readOnly":false,"help":null,"choices":null,"mustBeTrue":null,"presentation":{"display":"radio","trueLabel":null,"falseLabel":"No","unansweredLabel":null}},
    {"key":"amount","label":"Amount","kind":"number","required":false,"readOnly":false,"minimum":null,"maximum":10,"maxLength":null,"configuration":null},
    {"key":"rating","label":"Rating","kind":"number","required":false,"readOnly":false,"controlId":"acme.rating/1","configuration":{"note":null}}
  ]
}`) as TaskForm;

describe("nullable backend form settings", () => {
  it("opens and validates optional presentation labels serialized as null", () => {
    const control = { id: "acme.rating/1", label: "Rating", valueKind: "number", validateConfiguration: () => [] } as never;
    const fields = nullableForm.fields;
    expect(() => validateTaskFields(fields, [...builtInTaskFieldControls, control])).not.toThrow();
    expect(validateTaskFields(fields, [...builtInTaskFieldControls, control])).toEqual([[], [], []]);
  });

  it("normalizes null optional settings when applying source and preserves null configuration values", () => {
    const validateConfiguration = vi.fn(() => []);
    const control = {
      id: "acme.rating/1",
      label: "Rating",
      valueKind: "number",
      validateConfiguration
    } as never;
    const parsed = parseTaskFormSource(serializeTaskFormSource(nullableForm), nullableForm, [...builtInTaskFieldControls, control]);

    expect(parsed.fields[0]).toEqual({
      key: "accepted", label: "Accept terms", kind: "boolean", required: false, readOnly: false,
      presentation: { display: "radio", falseLabel: "No" }
    });
    expect(parsed.fields[1]).toEqual({ key: "amount", label: "Amount", kind: "number", required: false, readOnly: false, maximum: 10 });
    expect(parsed.fields[2].configuration).toEqual({ note: null });
    expect(validateConfiguration).toHaveBeenCalledWith({ note: null });
  });

  it("treats a null control configuration as absent but retains nulls inside configuration objects", () => {
    const validateConfiguration = vi.fn(() => []);
    const control = { id: "acme.rating/1", validateConfiguration } as never;

    expect(validateTaskFieldConfigurationShape(null)).toEqual([]);
    expect(validateTaskFieldControlConfiguration(control, null)).toEqual([]);
    expect(validateConfiguration).toHaveBeenCalledWith(undefined);
    expect(validateTaskFieldConfigurationShape({ answer: null })).toEqual([]);
  });

  it("continues rejecting malformed non-null optional settings", () => {
    const field = { key: "accepted", label: "Accept terms", kind: "boolean", required: false, readOnly: false, presentation: { display: "radio", trueLabel: 42 } } as unknown as TaskField;
    expect(validateTaskFields([field])[0]).toContain("Yes label must contain 1–256 characters.");
    const source = JSON.stringify({ ...nullableForm, fields: [field] });
    expect(() => parseTaskFormSource(source, nullableForm)).toThrow("trueLabel must be a string");
  });
});