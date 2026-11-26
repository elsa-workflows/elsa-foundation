/** Isolated so tests can mock a rejected glyph-chunk import without hoisting issues. */
export function loadActivityIconGlyphs() {
  return import("./activityIconGlyphs");
}
