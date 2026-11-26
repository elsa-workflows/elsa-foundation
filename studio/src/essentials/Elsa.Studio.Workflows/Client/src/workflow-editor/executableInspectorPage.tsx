import React, { lazy, useCallback, useEffect, useState } from "react";
import type { StudioWeaverRegistry, StudioEndpointContext, StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import type { WorkflowWeaverController } from "../workflowWeaver";
import { WorkflowLazyBoundary } from "../WorkflowLazyBoundary";

const WorkflowExecutableInspectorWorkbench = lazy(() => import("./WorkflowExecutableInspector").then(module => ({ default: module.WorkflowExecutableInspectorWorkbench })));

export function WorkflowExecutableInspectorPage({ context, ai, weaver, runInputEditors }: { context: StudioEndpointContext; ai: StudioWeaverRegistry; weaver?: WorkflowWeaverController; runInputEditors: StudioWorkflowRunInputEditorContribution[] }) {
  const [location, setLocation] = useState(readExecutableInspectorLocation);

  useEffect(() => {
    const syncFromLocation = () => {
      setLocation(readExecutableInspectorLocation());
    };
    window.addEventListener("popstate", syncFromLocation);
    return () => window.removeEventListener("popstate", syncFromLocation);
  }, []);

  // Selecting a reference re-renders the same artifact from that reference's Layout Sidecar; the
  // choice is part of the page address (?ref=) so it survives reload and can be shared.
  const selectReference = useCallback((sourceReferenceId: string | null) => {
    const url = new URL(window.location.href);

    if (sourceReferenceId) {
      url.searchParams.set("ref", sourceReferenceId);
    } else {
      url.searchParams.delete("ref");
    }

    window.history.replaceState({}, "", `${url.pathname}${url.search}${url.hash}`);
    setLocation(readExecutableInspectorLocation());
  }, []);

  return (
    <section className="studio-page wf-page wf-page--run-workbench wf-page--executable-inspector wf-editor" data-studio-workspace-backdrop="none">
      <WorkflowLazyBoundary label="executable inspector">
        <WorkflowExecutableInspectorWorkbench
          context={context}
          ai={ai}
          weaver={weaver}
          runInputEditors={runInputEditors}
          artifactId={location.artifactId}
          sourceReferenceId={location.sourceReferenceId}
          onSelectReference={selectReference}
        />
      </WorkflowLazyBoundary>
    </section>
  );
}

function readExecutableInspectorLocation() {
  const match = /^\/workflows\/executables\/([^/]+)$/.exec(window.location.pathname);
  return {
    artifactId: match ? decodeURIComponent(match[1]) : "",
    sourceReferenceId: new URLSearchParams(window.location.search).get("ref")
  };
}
