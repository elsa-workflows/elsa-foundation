import React, { useEffect, useMemo, useState } from "react";
import { createHttpClient, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getDefinition } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/api/workflowDesign";
import { WorkflowAlert } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowAlert";
import { describeWorkflowError } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorHelpers";
import type { WorkflowEditorError } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/editorTypes";

// The design API's exact wire bodies (spec 1193, story 5): what WorkflowDesignFaultRenderer writes when the owning
// authorizer cannot decide, and the existing 403 when it denies. The server tests pin these bytes.
const bodies: Record<string, { status: number; body: unknown }> = {
  unavailable: {
    status: 503,
    body: {
      errors: { generalErrors: ["Workflow definition authorization could not be completed."] },
      message: "One or more errors occurred!",
      statusCode: 503,
      errorCode: "workflow.authorization.unavailable"
    }
  },
  denied: {
    status: 403,
    body: {
      errors: { generalErrors: ["The requested workflow definition is not readable in the current application context."] },
      message: "One or more errors occurred!",
      statusCode: 403
    }
  }
};

/** Opens a definition through the editor's real load path and renders its real error banner. */
export function WorkflowAuthorizationUnavailableFixture() {
  const outcome = new URLSearchParams(window.location.search).get("outcome") ?? "unavailable";
  const context = useMemo(() => createContext(window.location.origin, outcome), [outcome]);
  const [error, setError] = useState<WorkflowEditorError | null>(null);

  useEffect(() => {
    // The same composition the designer uses when its definition load fails (useWorkflowEditorData).
    void getDefinition(context, "definition-browser").then(
      () => setError({ message: "Unexpected success" }),
      failure => setError(describeWorkflowError(failure, "Could not load the workflow editor.")));
  }, [context]);

  return (
    <main>
      <h1>Workflow authorization outcome</h1>
      {error ? <WorkflowAlert error={error} onDismiss={() => setError(null)} onCopied={() => undefined} onCopyFailed={() => undefined} /> : null}
    </main>
  );
}

function createContext(baseUrl: string, outcome: string): StudioEndpointContext {
  const http = createHttpClient(baseUrl, {
    fetch: async (input, init) => {
      const path = new URL(input instanceof Request ? input.url : String(input), baseUrl).pathname;
      const method = (init?.method ?? "GET").toUpperCase();
      if (method === "GET" && path === "/capabilities") {
        return json({ capabilities: [{ id: "elsa.api.workflow-design", contractVersion: "1",
          links: [{ rel: "workflow-definitions", href: "design/workflows/definitions" }] }] });
      }
      if (method === "GET" && path === "/design/workflows/definitions/definition-browser" && bodies[outcome]) {
        return new Response(JSON.stringify(bodies[outcome].body), {
          status: bodies[outcome].status, headers: { "Content-Type": "application/problem+json; charset=utf-8" } });
      }
      return json({ title: "Unexpected fixture API request", status: 404, detail: `${method} ${path}` }, 404);
    }
  });
  return { baseUrl, http };
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}
