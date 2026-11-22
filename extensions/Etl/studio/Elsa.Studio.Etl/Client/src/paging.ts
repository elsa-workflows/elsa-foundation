/** Append a bounded response page without changing the provider's existing order. */
export function appendUnique<T>(current: readonly T[], incoming: readonly T[], key: (item: T) => string): T[] {
  const seen = new Set(current.map(key));
  const result = [...current];
  for (const item of incoming) {
    const itemKey = key(item);
    if (seen.has(itemKey)) continue;
    seen.add(itemKey);
    result.push(item);
  }
  return result;
}
