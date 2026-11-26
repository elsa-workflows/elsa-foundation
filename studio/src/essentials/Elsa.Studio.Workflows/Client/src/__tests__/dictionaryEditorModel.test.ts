import { describe, expect, it } from "vitest";
import {
  defaultDictionaryValue,
  dictionaryEntries,
  formatDictionaryJson,
  parseDictionaryJson,
  validateDictionaryRows
} from "../dictionaryEditorModel";

describe("dictionary rows", () => {
  it("converts plain objects to rows and back without changing insertion order", () => {
    const rows = dictionaryEntries({ first: 1, second: 2 });
    expect(rows).toEqual([{ key: "first", value: 1 }, { key: "second", value: 2 }]);
    expect(validateDictionaryRows(rows, "ordinal", "System.Int32")).toEqual({
      valid: true,
      value: { first: 1, second: 2 }
    });
  });

  it("treats null and non-object values as an empty dictionary without legacy parsing", () => {
    expect(dictionaryEntries(null)).toEqual([]);
    expect(dictionaryEntries('{"legacy":true}')).toEqual([]);
    expect(defaultDictionaryValue("System.String")).toEqual({});
  });

  it("reports blank keys without trimming otherwise meaningful whitespace", () => {
    expect(validateDictionaryRows([
      { key: "   ", value: "invalid" },
      { key: " padded ", value: "valid" }
    ], "ordinal", "System.String")).toMatchObject({
      valid: false,
      errors: [expect.objectContaining({ index: 0, code: "blank-key" })]
    });
    expect(validateDictionaryRows([{ key: " padded ", value: "valid" }], "ordinal", "System.String"))
      .toEqual({ valid: true, value: { " padded ": "valid" } });
  });

  it("marks both duplicate rows under the configured comparison", () => {
    const exact = validateDictionaryRows([
      { key: "Content-Type", value: "a" },
      { key: "content-type", value: "b" }
    ], "ordinal", "System.String");
    expect(exact.valid).toBe(true);

    const insensitive = validateDictionaryRows([
      { key: "Content-Type", value: "a" },
      { key: "content-type", value: "b" }
    ], "ordinalIgnoreCase", "System.String");
    expect(insensitive).toMatchObject({
      valid: false,
      errors: [
        expect.objectContaining({ index: 0, code: "duplicate-key" }),
        expect.objectContaining({ index: 1, code: "duplicate-key" })
      ]
    });
  });

  it.each([
    ["System.String", 1],
    ["System.Boolean", "true"],
    ["System.Int32", 1.5],
    ["System.UInt32", -1],
    ["System.Double", "1.5"]
  ] as const)("rejects incompatible %s values", (valueTypeName, value) => {
    expect(validateDictionaryRows([{ key: "item", value }], "ordinal", valueTypeName)).toMatchObject({
      valid: false,
      errors: [expect.objectContaining({ index: 0, key: "item", code: "invalid-value" })]
    });
  });

  it("rejects null for known value types and accepts it for reference or unknown complex types", () => {
    expect(validateDictionaryRows([{ key: "count", value: null }], "ordinal", "System.Int32").valid).toBe(false);
    expect(validateDictionaryRows([{ key: "text", value: null }], "ordinal", "System.String").valid).toBe(true);
    expect(validateDictionaryRows([{ key: "payload", value: null }], "ordinal", "Company.Payload").valid).toBe(true);
  });
});

describe("dictionary JSON", () => {
  it("parses and formats strict object-root JSON in source order", () => {
    const parsed = parseDictionaryJson('{"second":2,"first":1}', "ordinal", "System.Int32");
    expect(parsed).toEqual({ valid: true, value: { second: 2, first: 1 } });
    if (parsed.valid) expect(formatDictionaryJson(parsed.value)).toBe('{\n  "second": 2,\n  "first": 1\n}');
  });

  it.each([
    '[{"key":"value"}]',
    'null',
    '"value"',
    '{"key":"value",}',
    '{// comment\n"key":"value"}'
  ])("rejects unsupported or non-strict JSON %s", text => {
    expect(parseDictionaryJson(text, "ordinal", "System.String").valid).toBe(false);
  });

  it("detects duplicate source keys before JSON parsing can discard one", () => {
    const parsed = parseDictionaryJson('{"key":"first","key":"second"}', "ordinal", "System.String");
    expect(parsed).toMatchObject({
      valid: false,
      errors: [
        expect.objectContaining({ index: 0, code: "duplicate-key" }),
        expect.objectContaining({ index: 1, code: "duplicate-key" })
      ]
    });
  });

  it("keeps values paired with integer-like keys despite JavaScript object ordering", () => {
    expect(parseDictionaryJson('{"2":"two","1":"one"}', "ordinal", "System.String"))
      .toEqual({ valid: true, value: { 1: "one", 2: "two" } });
  });

  it("applies case-insensitive duplicate comparison to escaped JSON keys", () => {
    const parsed = parseDictionaryJson('{"Header":"a","\\u0068eader":"b"}', "ordinalIgnoreCase", "System.String");
    expect(parsed.valid).toBe(false);
  });

  it("rejects duplicate keys nested inside complex dictionary values", () => {
    expect(parseDictionaryJson('{"payload":{"name":"first","name":"second"}}', "ordinal", "Company.Payload"))
      .toMatchObject({
        valid: false,
        errors: [
          expect.objectContaining({ index: 0, key: "payload.name", code: "duplicate-key" }),
          expect.objectContaining({ index: 0, key: "payload.name", code: "duplicate-key" })
        ]
      });
  });

  it("identifies the key containing an incompatible typed value", () => {
    expect(parseDictionaryJson('{"timeout":30,"retries":"three"}', "ordinal", "System.Int32"))
      .toMatchObject({
        valid: false,
        errors: [expect.objectContaining({ key: "retries", code: "invalid-value" })]
      });
  });
});
