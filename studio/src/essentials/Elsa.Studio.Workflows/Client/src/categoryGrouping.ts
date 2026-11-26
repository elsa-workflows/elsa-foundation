/**
 * Buckets items into alphabetically sorted category groups. Blank/missing categories fall back to
 * "Uncategorized". Item order within a group follows the input order. Shared by the designer's
 * activity palette and the activity availability page so category normalization stays in one place.
 */
export interface CategoryGroup<T> {
  category: string;
  items: T[];
}

export function groupByCategory<T>(items: T[], getCategory: (item: T) => string | null | undefined): CategoryGroup<T>[] {
  const groups = new Map<string, T[]>();
  for (const item of items) {
    const category = getCategory(item)?.trim() || "Uncategorized";
    const bucket = groups.get(category);
    if (bucket) bucket.push(item);
    else groups.set(category, [item]);
  }
  return [...groups.entries()]
    .map(([category, groupItems]) => ({ category, items: groupItems }))
    .sort((left, right) => left.category.localeCompare(right.category));
}
