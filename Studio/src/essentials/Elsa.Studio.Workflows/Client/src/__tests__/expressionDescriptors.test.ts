import { afterEach, describe, expect, it, vi } from "vitest";
import { listExpressionDescriptors } from "../api/workflows";
import { clearApiCapabilityCache } from "../api/capabilities";

function context(response: unknown) {
  const getJson = vi.fn()
    .mockResolvedValueOnce({ capabilities: [{
      id: "elsa.api.expressions",
      contractVersion: "1",
      links: [{ rel: "expression-descriptors", href: "expressions/descriptors" }]
    }] })
    .mockResolvedValueOnce(response);
  return { value: { baseUrl: `test://expressions-${Math.random()}`, http: { getJson } } as never, getJson };
}

describe("listExpressionDescriptors", () => {
  afterEach(clearApiCapabilityCache);

  it.each([
    ["an empty response", {}],
    ["an empty items collection", { items: [] }],
    ["an empty array response", []]
  ])("returns no descriptors for %s instead of inventing syntax contracts", async (_label, response) => {
    const api = context(response);

    await expect(listExpressionDescriptors(api.value)).resolves.toEqual([]);
    expect(api.getJson).toHaveBeenCalledTimes(2);
    expect(api.getJson).toHaveBeenLastCalledWith("/expressions/descriptors");
  });

  it("returns the backend descriptors unchanged", async () => {
    const descriptors = [{ type: "Python", displayName: "Python", editingMode: "text" }];
    const api = context({ expressionDescriptors: descriptors });

    await expect(listExpressionDescriptors(api.value)).resolves.toEqual(descriptors);
  });
});
