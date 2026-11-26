import { Component, lazy, Suspense, type ReactNode } from "react";
import { loadActivityIconGlyphs } from "./activityIconLoad";
import type { WorkflowNodeIcon } from "./activityIcons";

/** Lightweight stand-in so the lucide table stays in a deferred chunk. */
export function ActivityIconPlaceholder(_props?: { icon?: WorkflowNodeIcon }) {
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

class ActivityIconErrorBoundaryInner extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  render() {
    return this.state.failed ? <ActivityIconPlaceholder /> : this.props.children;
  }
}

/**
 * Eager placeholder + deferred glyph table. Extensions register React icons with
 * `registerActivityIcons` from `@elsa-workflows/studio-sdk` (`ext:<name>`). Any lucide name,
 * `asset:<package>/<path>`, and failed loads stay in this lazy path so the entry budget is unchanged.
 */
export function loadActivityIconRegistry() {
  return loadActivityIconGlyphs();
}

export function renderActivityIcon(icon: WorkflowNodeIcon | undefined, fallback?: WorkflowNodeIcon) {
  return (
    <ActivityIconErrorBoundaryInner key={`${icon ?? ""}:${fallback ?? ""}`}>
      <Suspense fallback={<ActivityIconPlaceholder />}>
        <LazyActivityIconGlyph icon={icon} fallback={fallback} />
      </Suspense>
    </ActivityIconErrorBoundaryInner>
  );
}
