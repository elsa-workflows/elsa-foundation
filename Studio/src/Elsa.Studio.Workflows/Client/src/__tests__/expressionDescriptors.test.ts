import { describe, expect, it, vi } from "vitest";
import { listExpressionDescriptors } from "../api/workflows";

function context(response: unknown) {
  const getJson = vi.fn().mockResolvedValue(response);
  return { value: { http: { getJson } } as never, getJson };
}

describe("listExpressionDescriptors", () => {
  it.each([
    ["an empty response", {}],
    ["an empty items collection", { items: [] }],
    ["an empty array response", []]
  ])("returns no descriptors for %s instead of inventing syntax contracts", async (_label, response) => {
    const api = context(response);

    await expect(listExpressionDescriptors(api.value)).resolves.toEqual([]);
    expect(api.getJson).toHaveBeenCalledTimes(1);
  });

  it("returns the backend descriptors unchanged", async () => {
    const descriptors = [{ type: "Python", displayName: "Python", editingMode: "text" }];
    const api = context({ expressionDescriptors: descriptors });

    await expect(listExpressionDescriptors(api.value)).resolves.toEqual(descriptors);
  });
});
