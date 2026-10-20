import React, { Suspense, type ComponentProps } from "react";

const WorkflowTestDrivePanel = React.lazy(() =>
  import("./WorkflowTestDrivePanel").then(module => ({ default: module.WorkflowTestDrivePanel })));

const WorkflowTestRunControls = React.lazy(() =>
  import("./WorkflowTestRunControls").then(module => ({ default: module.WorkflowTestRunControls })));

export function LazyWorkflowTestDrivePanel(props: ComponentProps<typeof WorkflowTestDrivePanel>) {
  return (
    <Suspense fallback={
      <div className="wf-test-drive-panel">
        <p className="wf-test-drive-loading" role="status">Loading Test Run setup…</p>
      </div>
    }>
      <WorkflowTestDrivePanel {...props} />
    </Suspense>
  );
}

export function LazyWorkflowTestRunControls(props: ComponentProps<typeof WorkflowTestRunControls>) {
  return (
    <Suspense fallback={
      <section className="wf-test-run-controls" aria-label="Test Run controls">
        <p className="wf-test-drive-loading" role="status">Loading Test Drive controls…</p>
      </section>
    }>
      <WorkflowTestRunControls {...props} />
    </Suspense>
  );
}
