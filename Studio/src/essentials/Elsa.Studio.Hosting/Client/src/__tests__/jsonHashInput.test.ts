import { afterEach, describe, expect, it, vi } from "vitest";
import { canonicalJsonText, hashJson, hashJsonText, hashJsonTextAsync } from "../sdk/jsonHashInput";
import { sha256 } from "../sdk/sha256";
import vectorsJson from "../../../../../../../specs/1207-json-data-integrity-baseline/evidence/json-hash-vectors.json?raw";

const vectors = JSON.parse(vectorsJson) as
  { name: string; json: string; canonical: string; sha256: string }[];
const freeze = "First-stable hash freeze: changes require an explicit identity/migration decision.";
afterEach(() => vi.unstubAllGlobals());

describe("the Core/SDK JsonHashInput contract", () => {
  for (const vector of vectors) it(`pins ${vector.name}`, async () => {
    expect(canonicalJsonText(vector.json), freeze).toBe(vector.canonical);
    expect(hashJsonText(vector.json), freeze).toBe(vector.sha256);
    vi.stubGlobal("crypto", { subtle: { digest: vi.fn(async (_algorithm: string, bytes: Uint8Array) => sha256(bytes).buffer) } });
    expect(await hashJsonTextAsync(vector.json), freeze).toBe(vector.sha256);
  });

  it("keeps missing/null, array order and exact large numbers separate", () => {
    expect(hashJson({ x: undefined })).toBe(hashJson({}));
    expect(hashJson({})).not.toBe(hashJson({ x: null }));
    expect(hashJson([1, 2])).not.toBe(hashJson([2, 1]));
    expect(hashJsonText("9007199254740993")).not.toBe(hashJsonText("9007199254740992"));
    expect(hashJson({ Z: 1, a: "é" })).toBe(hashJson({ a: "é", Z: 1 }));
  });

  it.each(["", "01", "1 trailing", "NaN", "[1,]", '{"a":1,}', '{"a":1,"a":2}', '"\\uD800"', '"\\uDC00"'])("refuses invalid or ambiguous projection text %s", value => {
    expect(() => hashJsonText(value)).toThrow();
  });

  it("retains the depth bound and the no-Web-Crypto path", async () => {
    expect(() => hashJsonText("[".repeat(1000) + "]".repeat(1000))).not.toThrow();
    expect(() => hashJsonText("[".repeat(1001) + "]".repeat(1001))).toThrow();
    expect(() => hashJsonText("[".repeat(1001) + "0" + "]".repeat(1001))).toThrow();
    expect(() => hashJson(undefined)).toThrow();
    vi.stubGlobal("crypto", undefined);
    expect(await hashJsonTextAsync(vectors[0].json), freeze).toBe(vectors[0].sha256);
  });

  it.each([NaN, Infinity, -Infinity])("refuses non-finite value projections %s", value => {
    for (const projection of [value, { x: value }, [value], { toJSON: () => value }, new Number(value)])
      expect(() => hashJson(projection)).toThrow(TypeError);
  });
});
