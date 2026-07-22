import React, { Component, Suspense } from "react";

type WorkflowLazyBoundaryProps = {
  label: string;
  children: React.ReactNode;
};

type WorkflowLazyBoundaryState = {
  error: Error | null;
};

/**
 * Keeps every deferred Workflows surface operable for assistive technology while its code loads,
 * and turns a failed chunk request into an actionable error instead of a blank route.
 */
export class WorkflowLazyBoundary extends Component<WorkflowLazyBoundaryProps, WorkflowLazyBoundaryState> {
  state: WorkflowLazyBoundaryState = { error: null };

  static getDerivedStateFromError(error: unknown): WorkflowLazyBoundaryState {
    return { error: error instanceof Error ? error : new Error(String(error)) };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    console.error(`[workflow.lazy-boundary] ${this.props.label} failed.`, error, info);
  }

  render() {
    if (this.state.error) {
      const chunkFailure = isChunkLoadFailure(this.state.error);
      return (
        <div className="wf-lazy-state error" role="alert">
          <strong>Unable to load {this.props.label}</strong>
          <span>{chunkFailure
            ? "The Studio module may have been updated while this page was open."
            : "This workflow surface encountered an unexpected error."}</span>
          <button type="button" className="wf-lazy-reload" onClick={() => window.location.reload()}>
            Reload page
          </button>
        </div>
      );
    }

    return (
      <Suspense fallback={(
        // Skeleton rows matching the destination pages' own loading skeletons, so the route
        // transition renders one continuous loading treatment instead of a text flash followed
        // by a different-looking skeleton. Rows fade in delayed (see .wf-skeleton), so a chunk
        // that resolves quickly never flashes a fallback at all.
        <div
          className="wf-lazy-state wf-lazy-skeleton"
          role="status"
          aria-live="polite"
          aria-busy="true"
        >
          <span className="wf-visually-hidden">Loading {this.props.label}…</span>
          {Array.from({ length: 5 }).map((_, index) => (
            <div key={index} className="wf-skeleton wf-skeleton-row" aria-hidden="true" style={{ width: `${90 - (index % 3) * 12}%` }} />
          ))}
        </div>
      )}>
        {this.props.children}
      </Suspense>
    );
  }
}

function isChunkLoadFailure(error: Error) {
  return error.name === "ChunkLoadError" || /dynamically imported module|loading chunk|importing a module script/i.test(error.message);
}
