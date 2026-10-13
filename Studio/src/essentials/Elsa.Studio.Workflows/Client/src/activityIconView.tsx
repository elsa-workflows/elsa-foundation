import { lazy, Suspense } from "react";
import { loadActivityIconGlyphs } from "./activityIconLoad";
import type { WorkflowNodeIcon } from "./activityIcons";

/** Lightweight stand-in so the lucide table stays in a deferred chunk. */
function ActivityIconPlaceholder(_props?: { icon?: WorkflowNodeIcon }) {
  return (
    <svg width={15} height={15} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <rect x="3" y="3" width="8" height="8" rx="1" opacity="0.35" />
      <rect x="13" y="3" width="8" height="8" rx="1" opacity="0.35" />
      <rect x="3" y="13" width="8" height="8" rx="1" opacity="0.35" />
      <rect x="13" y="13" width="8" height="8" rx="1" opacity="0.35" />
    </svg>
  );
}

const LazyActivityIconGlyph = lazy(() =>
  loadActivityIconGlyphs()
    .then(module => ({ default: module.ActivityIconGlyph }))
    .catch(() => ({ default: ActivityIconPlaceholder }))
);

/**
 * Eager placeholder + deferred glyph table. Extensions call this, then
 * `registerActivityIconGlyphs` from the loaded module (any lucide name later).
 */
export function loadActivityIconRegistry() {
  return loadActivityIconGlyphs();
}

export function renderActivityIcon(icon: WorkflowNodeIcon | undefined) {
  return (
    <Suspense fallback={<ActivityIconPlaceholder />}>
      <LazyActivityIconGlyph icon={icon} />
    </Suspense>
  );
}
