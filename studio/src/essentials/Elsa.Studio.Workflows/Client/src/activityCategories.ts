/**
 * Builds category suggestions from the catalog the current author is allowed to see. Categories are
 * labels rather than managed resources, so normalization is intentionally limited to blank removal
 * and case-insensitive duplicate detection; the first visible spelling remains the display spelling.
 */
export function deriveActivityCategorySuggestions(items: Iterable<{ category?: unknown }>) {
  const suggestions: string[] = [];
  const seen = new Set<string>();

  for (const item of items) {
    const category = typeof item.category === "string" ? item.category.trim() : "";
    const key = category.toLocaleLowerCase();
    if (!category || seen.has(key)) continue;
    seen.add(key);
    suggestions.push(category);
  }

  return suggestions.sort((left, right) => left.localeCompare(right, undefined, { sensitivity: "base" }));
}

export function filterActivityCategorySuggestions(suggestions: string[], query: string) {
  const term = query.trim().toLocaleLowerCase();
  return term
    ? suggestions.filter(suggestion => suggestion.toLocaleLowerCase().includes(term))
    : suggestions;
}
