import React, { type ReactNode } from "react";

/** A failed module presentation keeps the shared review controls and structured evidence usable. */
export class AgentResultBoundary extends React.Component<{ children: ReactNode; fallback: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() {
    return this.state.failed ? <div role="status"><p>Module presentation is unavailable. Structured evidence remains available.</p>{this.props.fallback}</div> : this.props.children;
  }
}
