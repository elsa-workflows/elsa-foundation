import { URL } from "node:url";

/** Compare parsed URLs, ignoring only query-key order; preserve each key's value sequence. */
export function hasExactUrlMultimap(actual, expected) {
  let left, right;
  try {
    left = new URL(actual instanceof URL ? actual.href : actual);
    right = new URL(expected instanceof URL ? expected.href : expected);
  } catch { return false; }
  if (left.origin !== right.origin || left.pathname !== right.pathname || left.hash !== right.hash ||
      left.href.includes("#") !== right.href.includes("#") ||
      left.username !== right.username || left.password !== right.password) return false;
  const keys = new Set(right.searchParams.keys());
  if (new Set(left.searchParams.keys()).size !== keys.size) return false;
  for (const key of keys) {
    const expectedValues = right.searchParams.getAll(key);
    const actualValues = left.searchParams.getAll(key);
    if (actualValues.length !== expectedValues.length ||
        actualValues.some((value, index) => value !== expectedValues[index])) return false;
  }
  return true;
}
