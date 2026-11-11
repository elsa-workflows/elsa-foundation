import { StudioPageHeader } from "@elsa-workflows/studio-ui";
import React, { lazy, useCallback, useEffect, useState } from "react";
import type { StudioWeaverRegistry, StudioEndpointContext, StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import type { WorkflowWeaverController } from "../workflowWeaver";
import { WorkflowLazyBoundary } from "../WorkflowLazyBoundary";
import {
  formatExecutableInspectorPageTitle,
  type ExecutableInspectorIdentity
} from "./executableInspectorIdentity";

const WorkflowExecutableInspectorWorkbench = lazy(() => import("./WorkflowExecutableInspector").then(module => ({ default: module.WorkflowExecutableInspectorWorkbench })));

export function WorkflowExecutableInspectorPage({ context, ai, weaver, runInputEditors }: { context: StudioEndpointContext; ai: StudioWeaverRegistry; weaver?: WorkflowWeaverController; runInputEditors: StudioWorkflowRunInputEditorContribution[] }) {
  const [location, setLocation] = useState(readExecutableInspectorLocation);
  const [identity, setIdentity] = useState<ExecutableInspectorIdentity | null>(null);

  useEffect(() => {
    const syncFromLocation = () => {
      setLocation(readExecutableInspectorLocation());
      setIdentity(null);
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

  const pageTitle = formatExecutableInspectorPageTitle(identity?.definitionName, location.artifactId, identity?.version);
  const resolvedName = identity?.definitionName?.trim();

  return (
    <section className="studio-page wf-page wf-page--run-workbench">
      <StudioPageHeader title={pageTitle} eyebrow={resolvedName ? "Inspector" : undefined} headingLevel={2} />
      <WorkflowLazyBoundary label="executable inspector">
        <WorkflowExecutableInspectorWorkbench
          context={context}
          ai={ai}
          weaver={weaver}
          runInputEditors={runInputEditors}
          artifactId={location.artifactId}
          sourceReferenceId={location.sourceReferenceId}
          onSelectReference={selectReference}
          onIdentityChange={setIdentity}
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
