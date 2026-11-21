import React, { Component, Suspense } from "react";

type WorkflowLazyBoundaryProps = {
  label: string;
  children: React.ReactNode;
  recoveryAction?: { label: string; onClick: () => void };
  backdropOptOut?: boolean;
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
      const chunkFailure = isWorkflowChunkLoadFailure(this.state.error);
      return (
        <div className="wf-lazy-state error" data-studio-workspace-backdrop={this.props.backdropOptOut ? "none" : undefined} role="alert">
          <strong>Unable to load {this.props.label}</strong>
          <span>{chunkFailure
            ? "The Studio module may have been updated while this page was open."
            : "This workflow surface encountered an unexpected error."}</span>
          <button type="button" className="wf-lazy-reload" onClick={() => window.location.reload()}>
            Reload page
          </button>
          {this.props.recoveryAction ? (
            <button type="button" className="wf-lazy-reload" onClick={this.props.recoveryAction.onClick}>
              {this.props.recoveryAction.label}
            </button>
          ) : null}
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
          data-studio-workspace-backdrop={this.props.backdropOptOut ? "none" : undefined}
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

export function isWorkflowChunkLoadFailure(error: unknown) {
  const failure = error instanceof Error ? error : new Error(String(error));
  return failure.name === "ChunkLoadError" || /dynamically imported module|loading chunk|importing a module script|failed to load module script|unable to preload css|does not provide an export named/i.test(failure.message);
}
